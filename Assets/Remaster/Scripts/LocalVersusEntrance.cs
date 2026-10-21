using UnityEngine;

namespace Versus.Remaster
{
    // A new Unity presentation sequence informed by the original's separate
    // character-entrance and battle-begin events. Exact native timings are unavailable.
    public sealed class LocalVersusEntrance
    {
        public enum Shot { PlayerOne, PlayerTwo, FaceOff, Ready, Fight, Complete }
        public Shot Current { get; private set; }
        public bool Active => Current != Shot.Complete;
        public int Frame { get; private set; }
        public float DurationSeconds => (60 + jinIntroFrames + 60 + kazuyaIntroFrames + 45 + 60 + 35) / 60f;

        readonly Transform playerOne, playerTwo;
        readonly Camera cameraRig;
        readonly BattleModel battle;
        readonly FighterActionMotion jinMotion, kazuyaMotion;
        readonly int jinWalk, jinIntro, kazuyaWalk, kazuyaIntro;
        readonly int jinIntroFrames, kazuyaIntroFrames;
        readonly EntranceCameraTrack jinCamera, kazuyaCamera;
        readonly float gameplayFov;
        Vector3 faceOffPosition;
        Quaternion faceOffRotation;
        float faceOffFov;
        Shot lastPresented = Shot.Complete;
        readonly Vector3 p1Start = new Vector3(-4.4f, 0, 0);
        readonly Vector3 p2Start = new Vector3(4.4f, 0, 0);
        static readonly Vector3 P1Ready = new Vector3(-2, 0, 0);
        static readonly Vector3 P2Ready = new Vector3(2, 0, 0);
        float frameRemainder;
        GUIStyle heading, caption;

        public LocalVersusEntrance(Transform p1, Transform p2, Camera camera, BattleModel model,
            FighterActionMotion jin, MuseumCharacter jinCharacter,
            FighterActionMotion kazuya, MuseumCharacter kazuyaCharacter)
        {
            playerOne = p1; playerTwo = p2; cameraRig = camera; battle = model;
            jinMotion = jin;
            jinWalk = System.Array.FindIndex(jinCharacter.actions, a => a.moveId == 1444 && a.motion);
            jinIntro = System.Array.FindIndex(jinCharacter.actions, a => a.moveId == 2218 && a.motion);
            if (jinIntro < 0)
                throw new System.InvalidOperationException("Jin entrance requires jz_s00 (move 2218).");
            jinIntroFrames = System.BitConverter.ToInt32(jinCharacter.actions[jinIntro].motion.bytes, 4);
            kazuyaMotion = kazuya;
            kazuyaWalk = System.Array.FindIndex(kazuyaCharacter.actions, a => a.moveId == 1436 && a.motion);
            kazuyaIntro = System.Array.FindIndex(kazuyaCharacter.actions, a => a.moveId == 2247 && a.motion);
            if (kazuyaIntro < 0)
                throw new System.InvalidOperationException("Kazuya entrance requires kz_s06_7CS (move 2247).");
            // Use the complete imported sequence at its native 60 fps, not a fixed idle beat.
            kazuyaIntroFrames = System.BitConverter.ToInt32(kazuyaCharacter.actions[kazuyaIntro].motion.bytes, 4);
            jinCamera = new EntranceCameraTrack("Jin_s00", 2218, jinIntroFrames);
            kazuyaCamera = new EntranceCameraTrack("Kazuya_s06", 2247, kazuyaIntroFrames);
            gameplayFov = camera.fieldOfView;
            Current = Shot.PlayerOne;
        }

        public void PrepareMotion()
        {
            if (jinMotion && Current == Shot.PlayerOne && Frame < 60 && jinWalk >= 0)
                jinMotion.SetPresentation(jinWalk, Frame, true);
            else if (jinMotion && Current == Shot.PlayerOne)
                jinMotion.SetPresentation(jinIntro, Frame - 60, false, true);
            else if (jinMotion) jinMotion.SetPresentation(-1, 0, false);

            if (kazuyaMotion && Current == Shot.PlayerTwo && Frame < 60 && kazuyaWalk >= 0)
                kazuyaMotion.SetPresentation(kazuyaWalk, Frame, true);
            else if (kazuyaMotion && Current == Shot.PlayerTwo)
                kazuyaMotion.SetPresentation(kazuyaIntro, Frame - 60, false, true);
            else if (kazuyaMotion) kazuyaMotion.SetPresentation(-1, 0, false);
        }

        public void Advance(float deltaTime, bool skip)
        {
            if (!Active) return;
            if (skip) { Finish(); return; }
            frameRemainder += Mathf.Min(deltaTime, .1f) * 60;
            while (frameRemainder >= 1 && Active)
            {
                frameRemainder -= 1;
                Frame++;
                int duration = Current == Shot.PlayerOne ? 60 + jinIntroFrames :
                    Current == Shot.PlayerTwo ? 60 + kazuyaIntroFrames :
                    Current == Shot.FaceOff ? 45 : Current == Shot.Ready ? 60 : 35;
                if (Frame < duration) continue;
                Frame = 0;
                if (Current == Shot.Fight) Finish();
                else Current++;
            }
        }

        void Finish()
        {
            Current = Shot.Complete; Frame = 0;
            if (jinMotion) jinMotion.SetPresentation(-1, 0, false);
            if (kazuyaMotion) kazuyaMotion.SetPresentation(-1, 0, false);
            playerOne.position = P1Ready; playerTwo.position = P2Ready;
            SetGameplayCamera(1);
            battle.BeginFight();
        }

        public void Present()
        {
            if (!Active) return;
            float p1 = Current == Shot.PlayerOne ? Mathf.SmoothStep(0, 1, Frame / 60f) : 1;
            float p2 = Current < Shot.PlayerTwo ? 0 : Current == Shot.PlayerTwo ?
                Mathf.SmoothStep(0, 1, Frame / 60f) : 1;
            playerOne.position = Vector3.Lerp(p1Start, P1Ready, p1);
            playerTwo.position = Vector3.Lerp(p2Start, P2Ready, p2);
            playerOne.rotation = Quaternion.Euler(0, 90, 0);
            playerTwo.rotation = Quaternion.Euler(0, -90, 0);
            if (Current == Shot.PlayerOne)
                jinCamera.Apply(cameraRig, playerOne, Mathf.Max(0, Frame - 60));
            else if (Current == Shot.PlayerTwo)
                kazuyaCamera.Apply(cameraRig, playerTwo, Mathf.Max(0, Frame - 60));
            else
            {
                if (Current == Shot.FaceOff && lastPresented != Current)
                {
                    faceOffPosition = cameraRig.transform.position;
                    faceOffRotation = cameraRig.transform.rotation;
                    faceOffFov = cameraRig.fieldOfView;
                }
                SetGameplayCamera(Current == Shot.FaceOff ? Mathf.SmoothStep(0, 1, Frame / 44f) : 1);
            }
            lastPresented = Current;
        }

        void SetGameplayCamera(float blend)
        {
            var a = battle.fighters[0]; var b = battle.fighters[1];
            float distance = Mathf.Clamp(9 + Mathf.Abs(a.x - b.x) * .6f, 11, 17);
            Vector3 target = new Vector3((a.x + b.x) / 2, 1, (a.z + b.z) / 2);
            Vector3 position = target + new Vector3(0, distance * .27f, -distance);
            Quaternion rotation = Quaternion.LookRotation(target - position);
            cameraRig.transform.SetPositionAndRotation(Vector3.Lerp(faceOffPosition, position, blend),
                blend >= 1 ? rotation : Quaternion.Slerp(faceOffRotation, rotation, blend));
            cameraRig.fieldOfView = Mathf.Lerp(faceOffFov, gameplayFov, blend);
        }

        public void Draw()
        {
            if (!Active) return;
            if (heading == null)
            {
                heading = new GUIStyle(GUI.skin.label) { fontSize = 56, fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter };
                heading.normal.textColor = Color.white;
                caption = new GUIStyle(heading) { fontSize = 23 };
            }
            var old = GUI.color;
            GUI.color = new Color(.025f, .035f, .055f, .82f);
            GUI.DrawTexture(new Rect(0, 0, 1280, 90), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0, 624, 1280, 96), Texture2D.whiteTexture);
            GUI.color = old;
            string headline = Current == Shot.PlayerOne ? "JIN KAZAMA" :
                Current == Shot.PlayerTwo ? "KAZUYA MISHIMA" :
                Current == Shot.FaceOff ? "JIN  VS  KAZUYA" :
                Current == Shot.Ready ? "READY" : "FIGHT";
            GUI.Label(new Rect(90, 10, 1100, 75), headline, heading);
            GUI.Label(new Rect(90, 641, 1100, 55), "LOCAL VERSUS  /  ROUND 1     •     ENTER TO SKIP", caption);
        }
    }
}
