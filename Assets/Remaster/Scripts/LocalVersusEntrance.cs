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
        public float DurationSeconds => (jinIntroFrames + kazuyaIntroFrames + 45 + 60 + 35) / 60f;

        readonly Transform playerOne, playerTwo;
        readonly Camera cameraRig;
        readonly BattleModel battle;
        readonly FighterActionMotion jinMotion, kazuyaMotion;
        readonly int jinIntro, kazuyaIntro;
        readonly int jinIntroFrames, kazuyaIntroFrames;
        readonly EntranceCameraTrack jinCamera, kazuyaCamera;
        readonly EntranceCameraLens lens;
        readonly float gameplayFov;
        Vector3 faceOffPosition;
        Quaternion faceOffRotation;
        float faceOffFov;
        Shot lastPresented = Shot.Complete;
        static readonly Vector3 P1Ready = new Vector3(-2, 0, 0);
        static readonly Vector3 P2Ready = new Vector3(2, 0, 0);
        float frameRemainder;
        GUIStyle heading, caption;

        public LocalVersusEntrance(Transform p1, Transform p2, Camera camera, BattleModel model,
            FighterActionMotion jin, MuseumCharacter jinCharacter,
            FighterActionMotion kazuya, MuseumCharacter kazuyaCharacter, EntranceCameraLens sharedLens)
        {
            playerOne = p1; playerTwo = p2; cameraRig = camera; battle = model;
            jinMotion = jin;
            jinIntro = System.Array.FindIndex(jinCharacter.actions, a => a.moveId == 2218 && a.motion);
            if (jinIntro < 0)
                throw new System.InvalidOperationException("Jin entrance requires jz_s00 (move 2218).");
            jinIntroFrames = System.BitConverter.ToInt32(jinCharacter.actions[jinIntro].motion.bytes, 4);
            kazuyaMotion = kazuya;
            kazuyaIntro = System.Array.FindIndex(kazuyaCharacter.actions, a => a.moveId == 2247 && a.motion);
            if (kazuyaIntro < 0)
                throw new System.InvalidOperationException("Kazuya entrance requires kz_s06_7CS (move 2247).");
            // Use the complete imported sequence at its native 60 fps, not a fixed idle beat.
            kazuyaIntroFrames = System.BitConverter.ToInt32(kazuyaCharacter.actions[kazuyaIntro].motion.bytes, 4);
            jinCamera = new EntranceCameraTrack("Jin_s00", 2218, jinIntroFrames);
            kazuyaCamera = new EntranceCameraTrack("Kazuya_s06", 2247, kazuyaIntroFrames);
            gameplayFov = camera.fieldOfView;
            lens = sharedLens;
            Current = Shot.PlayerOne;
        }

        public void PrepareMotion()
        {
            if (jinMotion && Current == Shot.PlayerTwo)
                jinMotion.SetPresentation(jinIntro, Frame, false, true);
            else if (jinMotion) jinMotion.SetPresentation(-1, 0, false);

            if (kazuyaMotion && Current == Shot.PlayerOne)
                kazuyaMotion.SetPresentation(kazuyaIntro, Frame, false, true);
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
                int duration = Current == Shot.PlayerOne ? kazuyaIntroFrames :
                    Current == Shot.PlayerTwo ? jinIntroFrames :
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
            // Original clips supply their own motion relative to these camera/actor anchors.
            playerOne.position = P1Ready;
            playerTwo.position = P2Ready;
            playerOne.rotation = Quaternion.Euler(0, 90, 0);
            playerTwo.rotation = Quaternion.Euler(0, -90, 0);
            if (Current == Shot.PlayerOne)
                kazuyaCamera.Apply(cameraRig, playerOne, Frame, lens);
            else if (Current == Shot.PlayerTwo)
                jinCamera.Apply(cameraRig, playerTwo, Frame, lens);
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
            GameplayCameraFraming.Evaluate(new Vector3(a.x, 0, a.z), new Vector3(b.x, 0, b.z),
                out Vector3 position, out Vector3 target);
            Quaternion rotation = Quaternion.LookRotation(target - position);
            cameraRig.transform.SetPositionAndRotation(Vector3.Lerp(faceOffPosition, position, blend),
                blend >= 1 ? rotation : Quaternion.Slerp(faceOffRotation, rotation, blend));
            lens.SetVerticalFov(Mathf.Lerp(faceOffFov, gameplayFov, blend));
        }

        public void Draw()
        {
            if (!Active) return;
            var previousMatrix = GUI.matrix;
            var previousColor = GUI.color;
            GUI.matrix = Matrix4x4.identity;
            Rect viewport = cameraRig.pixelRect;
            float top = Screen.height - viewport.yMax;
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, top), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0, top + viewport.height, Screen.width, viewport.y), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0, top, viewport.x, viewport.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(viewport.xMax, top, Screen.width - viewport.xMax, viewport.height), Texture2D.whiteTexture);
            GUI.color = previousColor;
            GUI.matrix = Matrix4x4.TRS(new Vector3(viewport.x, top, 0), Quaternion.identity,
                new Vector3(viewport.width / 1280f, viewport.height / 720f, 1));
            try { DrawContent(); }
            finally { GUI.matrix = previousMatrix; }
        }

        void DrawContent()
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
            string headline = Current == Shot.PlayerOne ? "KAZUYA MISHIMA" :
                Current == Shot.PlayerTwo ? "JIN KAZAMA" :
                Current == Shot.FaceOff ? "KAZUYA  VS  JIN" :
                Current == Shot.Ready ? "READY" : "FIGHT";
            GUI.Label(new Rect(90, 10, 1100, 75), headline, heading);
            GUI.Label(new Rect(90, 641, 1100, 55), "LOCAL VERSUS  /  ROUND 1     •     ENTER TO SKIP", caption);
        }
    }
}
