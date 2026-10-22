using UnityEngine;

namespace Versus.Remaster
{
    public static class KazuyaEntranceTimeline
    {
        // Source shot edits repeat 47..57 and 88..96. Join 46->58 and 87->97
        // to keep the descent advancing across both cuts, including fractional poses.
        public const int FirstCut = 47, FirstOverlap = 11;
        public const int SecondCut = 77, SecondOverlap = 9;
        public const int RemovedFrames = FirstOverlap + SecondOverlap;
        public static int SourceFrame(int frame) => frame +
            (frame >= FirstCut ? FirstOverlap : 0) +
            (frame >= SecondCut ? SecondOverlap : 0);
    }

    // Native entrance variants and a round-start transition calibrated to the reference video.
    public sealed class LocalVersusEntrance
    {
        public enum Shot { PlayerOne, PlayerCut, PlayerTwo, RoundCut, RoundStart, Ready, Fight, Complete }
        public Shot Current { get; private set; }
        public bool Active => Current != Shot.Complete;
        public int Frame { get; private set; }
        public float SampleFrame => Frame + frameRemainder;
        public bool ShowsBattleHud => Current == Shot.RoundStart || Current == Shot.Ready || Current == Shot.Fight;
        public float DurationSeconds => (jinIntroFrames + kazuyaIntroFrames + 2 * CutFrames + RoundFrames + ReadyFrames + FightFrames) / 60f;
        const int CutFrames = 2, RoundFrames = 90, ReadyFrames = 45, FightFrames = 45;
        const int CameraSettleFrames = 24;

        readonly Transform playerOne, playerTwo;
        readonly Transform jinHead, kazuyaHead;
        readonly Vector3 jinFaceAxis, kazuyaFaceAxis;
        readonly Transform[] kazuyaShotBones;
        readonly Renderer[] jinRenderers, kazuyaRenderers;
        readonly bool[] jinRendererEnabled, kazuyaRendererEnabled;
        readonly Camera cameraRig;
        readonly BattleModel battle;
        readonly FighterActionMotion jinMotion, kazuyaMotion;
        readonly int jinIntro, kazuyaIntro;
        readonly int jinIntroFrames, kazuyaIntroFrames;
        readonly EntranceCameraTrack jinCamera, kazuyaCamera;
        readonly EntranceCameraLens lens;
        readonly float gameplayFov;
        static readonly Vector3 P1Ready = new Vector3(-BattleModel.StartingHalfSeparation, 0, 0);
        static readonly Vector3 P2Ready = new Vector3(BattleModel.StartingHalfSeparation, 0, 0);
        float frameRemainder;
        bool clockStarted;
        Vector3 kazuyaFront;
        GUIStyle heading, caption;

        public LocalVersusEntrance(Transform p1, Transform p2, Camera camera, BattleModel model,
            FighterActionMotion jin, MuseumCharacter jinCharacter,
            FighterActionMotion kazuya, MuseumCharacter kazuyaCharacter, EntranceCameraLens sharedLens)
        {
            playerOne = p1; playerTwo = p2; cameraRig = camera; battle = model;
            jinHead = System.Array.Find(p2.GetComponentsInChildren<Transform>(), t => t.name == "Head");
            kazuyaHead = System.Array.Find(p1.GetComponentsInChildren<Transform>(), t => t.name == "Head");
            if (!jinHead || !kazuyaHead) throw new System.InvalidOperationException("Entrance cameras require both head bones.");
            jinFaceAxis = jinHead.InverseTransformDirection(p2.forward);
            kazuyaFaceAxis = kazuyaHead.InverseTransformDirection(p1.forward);
            kazuyaShotBones = System.Array.FindAll(p1.GetComponentsInChildren<Transform>(),
                t => t.name == "Head" || t.name == "L_Foot" || t.name == "R_Foot" || t.name == "L_Hand" || t.name == "R_Hand");
            jinRenderers = p2.GetComponentsInChildren<Renderer>(); kazuyaRenderers = p1.GetComponentsInChildren<Renderer>();
            jinRendererEnabled = System.Array.ConvertAll(jinRenderers, r => r.enabled);
            kazuyaRendererEnabled = System.Array.ConvertAll(kazuyaRenderers, r => r.enabled);
            jinMotion = jin;
            jinIntro = System.Array.FindIndex(jinCharacter.actions, a => a.moveId == 2220 && a.motion);
            if (jinIntro < 0)
                throw new System.InvalidOperationException("Jin entrance requires jz_s02 (move 2220).");
            jinIntroFrames = System.BitConverter.ToInt32(jinCharacter.actions[jinIntro].motion.bytes, 4);
            kazuyaMotion = kazuya;
            kazuyaIntro = System.Array.FindIndex(kazuyaCharacter.actions, a => a.moveId == 2326 && a.motion);
            if (kazuyaIntro < 0)
                throw new System.InvalidOperationException("Kazuya entrance requires kz_s03 (move 2326).");
            // Keep native assets intact; omit repeated descent frames in presentation.
            kazuyaIntroFrames = System.BitConverter.ToInt32(kazuyaCharacter.actions[kazuyaIntro].motion.bytes, 4);
            jinCamera = new EntranceCameraTrack("Jin_s02", 2220, jinIntroFrames);
            kazuyaCamera = new EntranceCameraTrack("Kazuya_s03", 2326, kazuyaIntroFrames);
            kazuyaIntroFrames -= KazuyaEntranceTimeline.RemovedFrames;
            gameplayFov = camera.fieldOfView;
            lens = sharedLens;
            Current = Shot.PlayerOne;
        }

        public void PrepareMotion()
        {
            if (jinMotion && Current == Shot.PlayerTwo)
                jinMotion.SetPresentation(jinIntro, SampleFrame, false, true);
            else if (jinMotion) jinMotion.SetPresentation(-1, 0, false);

            if (kazuyaMotion && Current == Shot.PlayerOne)
                kazuyaMotion.SetPresentation(kazuyaIntro, SampleFrame, false, true, KazuyaEntranceTimeline.SourceFrame);
            else if (kazuyaMotion) kazuyaMotion.SetPresentation(-1, 0, false);
        }

        public void Advance(float deltaTime, bool skip)
        {
            if (!Active) return;
            if (skip) { Finish(); return; }
            // The first unscaled delta can include scene/catalog loading time.
            // Present frame zero before starting the cinematic clock.
            if (!clockStarted) { clockStarted = true; return; }
            // Do not fast-forward through shots after an import/render hitch.
            frameRemainder += Mathf.Clamp(deltaTime, 0, 1f / 15f) * 60;
            while (frameRemainder >= 1 && Active)
            {
                frameRemainder -= 1;
                Frame++;
                int duration = Current == Shot.PlayerOne ? kazuyaIntroFrames :
                    Current == Shot.PlayerTwo ? jinIntroFrames :
                    Current == Shot.PlayerCut || Current == Shot.RoundCut ? CutFrames :
                    Current == Shot.RoundStart ? RoundFrames : Current == Shot.Ready ? ReadyFrames : FightFrames;
                if (Frame < duration) continue;
                Frame = 0;
                if (Current == Shot.Fight) Finish();
                else Current++;
            }
        }

        void Finish()
        {
            Current = Shot.Complete; Frame = 0;
            SetVisibility(true, true);
            if (jinMotion) jinMotion.SetPresentation(-1, 0, false);
            if (kazuyaMotion) kazuyaMotion.SetPresentation(-1, 0, false);
            playerOne.position = P1Ready; playerTwo.position = P2Ready;
            SetGameplayCamera(1);
            battle.BeginFight();
        }

        public void Present()
        {
            if (!Active) return;
            SetVisibility(Current != Shot.PlayerOne, Current != Shot.PlayerTwo);
            // Original clips supply their own motion relative to these camera/actor anchors.
            playerOne.position = P1Ready;
            playerTwo.position = P2Ready;
            playerOne.rotation = Quaternion.Euler(0, 90, 0);
            playerTwo.rotation = Quaternion.Euler(0, -90, 0);
            if (Current == Shot.PlayerOne)
            {
                // Hold the opening frontal azimuth through the dive. Projecting a
                // downward-pointing head each frame flips sides as it crosses vertical.
                if (kazuyaFront.sqrMagnitude < .001f)
                {
                    kazuyaFront = Vector3.ProjectOnPlane(kazuyaHead.TransformDirection(kazuyaFaceAxis), Vector3.up).normalized;
                    if (kazuyaFront.sqrMagnitude < .001f) kazuyaFront = playerOne.forward;
                }
                Bounds close = SubjectBounds(kazuyaHead, null);
                Bounds wide = SubjectBounds(kazuyaHead, kazuyaShotBones);
                float sourceFrame = KazuyaEntranceTimeline.SourceFrame(Frame) + (SampleFrame - Frame);
                float closeBlend = Mathf.SmoothStep(0, 1, (sourceFrame - 110) / 30f);
                Bounds focus = Frame < 47 ? close : new Bounds(
                    Vector3.Lerp(wide.center, close.center, closeBlend),
                    Vector3.Lerp(wide.size, close.size, closeBlend));
                kazuyaCamera.Apply(cameraRig, playerOne, SampleFrame, lens, focus,
                    kazuyaFront, KazuyaEntranceTimeline.SourceFrame);
            }
            else if (Current == Shot.PlayerTwo)
                jinCamera.Apply(cameraRig, playerTwo, SampleFrame, lens, SubjectBounds(jinHead, null),
                    jinHead.TransformDirection(jinFaceAxis));
            else
                SetGameplayCamera(Current == Shot.RoundStart
                    ? Mathf.SmoothStep(0, 1, SampleFrame / (float)(CameraSettleFrames - 1)) : 1);
        }

        static Bounds SubjectBounds(Transform head, Transform[] landmarks)
        {
            // Head transform is near the base of the skull, not the hair silhouette.
            var bounds = new Bounds(head.position + Vector3.up * .10f, Vector3.one * .44f);
            if (landmarks != null) foreach (var bone in landmarks)
                bounds.Encapsulate(new Bounds(bone.position, Vector3.one * .20f));
            return bounds;
        }

        void SetVisibility(bool jinVisible, bool kazuyaVisible)
        {
            for (int i = 0; i < jinRenderers.Length; i++) jinRenderers[i].enabled = jinVisible && jinRendererEnabled[i];
            for (int i = 0; i < kazuyaRenderers.Length; i++) kazuyaRenderers[i].enabled = kazuyaVisible && kazuyaRendererEnabled[i];
        }

        void SetGameplayCamera(float blend)
        {
            var a = battle.fighters[0]; var b = battle.fighters[1];
            GameplayCameraFraming.Evaluate(new Vector3(a.x, 0, a.z), new Vector3(b.x, 0, b.z),
                out Vector3 position, out Vector3 target);
            // Cut to a separate, elevated two-shot, then settle to the combat camera.
            // Do not interpolate across the arena from Jin's last close-up.
            // Use a fixed reveal angle so a closer fight camera does not create a steeper tilt.
            float revealHeight = Vector3.Distance(position, target) * Mathf.Tan(25f * Mathf.Deg2Rad);
            Vector3 overhead = position + Vector3.up * revealHeight;
            cameraRig.transform.position = Vector3.Lerp(overhead, position, blend);
            cameraRig.transform.rotation = Quaternion.LookRotation(target - cameraRig.transform.position);
            lens.SetVerticalFov(gameplayFov);
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
            if (Current == Shot.PlayerCut || Current == Shot.RoundCut)
            {
                var color = GUI.color; GUI.color = Color.black;
                GUI.DrawTexture(new Rect(0, 0, 1280, 720), Texture2D.whiteTexture);
                GUI.color = color; return;
            }
            if (ShowsBattleHud)
            {
                string cue = Current == Shot.RoundStart ? (Frame >= 15 && Frame < 66 ? "ROUND 1" : "") :
                    Current == Shot.Ready ? "READY" : "FIGHT";
                GUI.Label(new Rect(90, 270, 1100, 150), cue, heading);
                return;
            }
            var old = GUI.color;
            GUI.color = new Color(.025f, .035f, .055f, .82f);
            GUI.DrawTexture(new Rect(0, 0, 1280, 90), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0, 624, 1280, 96), Texture2D.whiteTexture);
            GUI.color = old;
            string headline = Current == Shot.PlayerOne ? "KAZUYA MISHIMA" :
                Current == Shot.PlayerTwo ? "JIN KAZAMA" :
                Current == Shot.Ready ? "READY" : "FIGHT";
            GUI.Label(new Rect(90, 10, 1100, 75), headline, heading);
            GUI.Label(new Rect(90, 641, 1100, 55), "LOCAL VERSUS  /  ROUND 1     •     ENTER TO SKIP", caption);
        }
    }
}
