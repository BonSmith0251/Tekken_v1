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
    // Player one's take plays first, then player two's; each fighter's own staging, camera and
    // hand/face tracks are selected from the staging catalog by its character code.
    public sealed class LocalVersusEntrance
    {
        public enum Shot { PlayerOne, PlayerCut, PlayerTwo, RoundCut, RoundStart, Ready, Fight, Complete }
        public Shot Current { get; private set; }
        public bool Active => Current != Shot.Complete;
        public int Frame { get; private set; }
        public float SampleFrame => Frame + frameRemainder;
        public bool ShowsBattleHud => Current == Shot.RoundStart || Current == Shot.Ready || Current == Shot.Fight;
        // The centre call during those shots: ROUND 1 on reveal frames 15-65, then READY and FIGHT.
        public BattleHud.Call Cue => Current == Shot.RoundStart ? (Frame >= 15 && Frame < 66 ? BattleHud.Call.Round : BattleHud.Call.None) :
            Current == Shot.Ready ? BattleHud.Call.Ready : Current == Shot.Fight ? BattleHud.Call.Fight : BattleHud.Call.None;
        public float CueSeconds => (Current == Shot.RoundStart ? SampleFrame - 15 : SampleFrame) / 60f;
        public float DurationSeconds => (twoIntroFrames + oneIntroFrames + 2 * CutFrames + RoundFrames + ReadyFrames + FightFrames) / 60f;
        const int CutFrames = 2, RoundFrames = 90, ReadyFrames = 45, FightFrames = 45;
        const int CameraSettleFrames = 24;

        readonly Transform playerOne, playerTwo;
        readonly Transform twoHead, oneHead;
        readonly Vector3 twoFaceAxis, oneFaceAxis;
        readonly Transform[] oneShotBones;
        readonly Renderer[] twoRenderers, oneRenderers;
        readonly bool[] twoRendererEnabled, oneRendererEnabled;
        readonly Camera cameraRig;
        readonly BattleModel battle;
        readonly FighterActionMotion twoMotion, oneMotion;
        readonly int twoIntro, oneIntro;
        readonly StagingAction twoStaging, oneStaging;
        readonly System.Func<int,int> oneFrameMap;
        readonly Transform[] twoShotBones;
        public int TwoStagingMove => twoStaging.moveId;
        public int OneStagingMove => oneStaging.moveId;
        /// <summary>The entrance take's animation (e.g. jz_s00(DVD)), which names its voice line.</summary>
        public string TwoStagingAnimation => twoStaging?.animation;
        public string OneStagingAnimation => oneStaging?.animation;
        // Names kept for the editor flow validation written for Kazuya (P1) versus Jin (P2).
        public int JinStagingMove => TwoStagingMove;
        public int KazuyaStagingMove => OneStagingMove;
        readonly string oneName, twoName;
        readonly bool twoCloseUp;
        readonly int twoIntroFrames, oneIntroFrames;
        readonly EntranceCameraTrack twoCamera, oneCamera;
        readonly SecondaryMotionTrack twoSecondary, oneSecondary;
        readonly EntranceCameraLens lens;
        readonly float gameplayFov;
        static readonly Vector3 P1Ready = new Vector3(-BattleModel.StartingHalfSeparation, 0, 0);
        static readonly Vector3 P2Ready = new Vector3(BattleModel.StartingHalfSeparation, 0, 0);
        float frameRemainder;
        bool clockStarted;
        Vector3 oneFront;
        GUIStyle heading, caption;

        public LocalVersusEntrance(Transform p1, Transform p2, Camera camera, BattleModel model,
            FighterActionMotion two, MuseumCharacter twoCharacter,
            FighterActionMotion one, MuseumCharacter oneCharacter, EntranceCameraLens sharedLens,
            string oneCode = "kaz", string twoCode = "jin")
        {
            oneName = oneCharacter.displayName.ToUpperInvariant(); twoName = twoCharacter.displayName.ToUpperInvariant();
            playerOne = p1; playerTwo = p2; cameraRig = camera; battle = model;
            twoHead = System.Array.Find(p2.GetComponentsInChildren<Transform>(), t => Is(t, "Head"));
            oneHead = System.Array.Find(p1.GetComponentsInChildren<Transform>(), t => Is(t, "Head"));
            if (!twoHead || !oneHead) throw new System.InvalidOperationException("Entrance cameras require both head bones.");
            twoFaceAxis = twoHead.InverseTransformDirection(p2.forward);
            oneFaceAxis = oneHead.InverseTransformDirection(p1.forward);
            oneShotBones = System.Array.FindAll(p1.GetComponentsInChildren<Transform>(),
                t => Is(t, "Head") || Is(t, "L_Foot") || Is(t, "R_Foot") || Is(t, "L_Hand") || Is(t, "R_Hand"));
            twoRenderers = p2.GetComponentsInChildren<Renderer>(); oneRenderers = p1.GetComponentsInChildren<Renderer>();
            twoRendererEnabled = System.Array.ConvertAll(twoRenderers, r => r.enabled);
            oneRendererEnabled = System.Array.ConvertAll(oneRenderers, r => r.enabled);
            twoMotion = two; oneMotion = one;
            twoStaging = StagingCatalog.Select(twoCode, twoCharacter);
            oneStaging = StagingCatalog.Select(oneCode, oneCharacter);
            twoIntro = System.Array.FindIndex(twoCharacter.actions, a => a.moveId == twoStaging.moveId && a.motion);
            oneIntro = System.Array.FindIndex(oneCharacter.actions, a => a.moveId == oneStaging.moveId && a.motion);
            twoIntroFrames = System.BitConverter.ToInt32(twoCharacter.actions[twoIntro].motion.bytes, 4);
            oneIntroFrames = System.BitConverter.ToInt32(oneCharacter.actions[oneIntro].motion.bytes, 4);
            twoCamera = new EntranceCameraTrack(twoStaging.camera, twoStaging.moveId, twoIntroFrames);
            oneCamera = new EntranceCameraTrack(oneStaging.camera, oneStaging.moveId, oneIntroFrames);
            twoSecondary = new SecondaryMotionTrack(playerTwo, twoStaging.secondary, twoIntroFrames);
            oneSecondary = new SecondaryMotionTrack(playerOne, oneStaging.secondary, oneIntroFrames);
            if (oneStaging.landingStitch)
            {
                oneFrameMap = KazuyaEntranceTimeline.SourceFrame;
                oneIntroFrames -= KazuyaEntranceTimeline.RemovedFrames;
            }
            // Jin's jz_s02 take is a close-up throughout (tuned against the reference recording).
            twoCloseUp = twoCode == "jin" && twoStaging.moveId == 2220;
            twoShotBones = System.Array.FindAll(p2.GetComponentsInChildren<Transform>(),
                t => Is(t, "Head") || Is(t, "L_Foot") || Is(t, "R_Foot") || Is(t, "L_Hand") || Is(t, "R_Hand"));
            gameplayFov = camera.fieldOfView;
            lens = sharedLens;
            Current = Shot.PlayerOne;
        }

        public void PrepareMotion()
        {
            if (twoMotion && Current == Shot.PlayerTwo)
                twoMotion.SetPresentation(twoIntro, SampleFrame, false, true);
            else if (twoMotion) twoMotion.SetPresentation(-1, 0, false);

            if (oneMotion && Current == Shot.PlayerOne)
                oneMotion.SetPresentation(oneIntro, SampleFrame, false, true, oneFrameMap);
            else if (oneMotion) oneMotion.SetPresentation(-1, 0, false);
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
                int duration = Current == Shot.PlayerOne ? oneIntroFrames :
                    Current == Shot.PlayerTwo ? twoIntroFrames :
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
            twoSecondary.Restore(); oneSecondary.Restore();
            SetVisibility(true, true);
            if (twoMotion) twoMotion.SetPresentation(-1, 0, false);
            if (oneMotion) oneMotion.SetPresentation(-1, 0, false);
            playerOne.position = P1Ready; playerTwo.position = P2Ready;
            SetGameplayCamera(1);
            battle.BeginFight();
        }

        public void Present()
        {
            if (!Active) return;
            SetVisibility(Current != Shot.PlayerOne, Current != Shot.PlayerTwo);
            // Apply after the body renderer so native hands replace its procedural fallback.
            if (Current == Shot.PlayerOne) oneSecondary.Apply(SampleFrame, oneFrameMap);
            if (Current == Shot.PlayerTwo) twoSecondary.Apply(SampleFrame);
            // Original clips supply their own motion relative to these camera/actor anchors.
            playerOne.position = P1Ready;
            playerTwo.position = P2Ready;
            playerOne.rotation = Quaternion.Euler(0, 90, 0);
            playerTwo.rotation = Quaternion.Euler(0, -90, 0);
            if (Current == Shot.PlayerOne)
            {
                // Hold the opening frontal azimuth through the dive. Projecting a
                // downward-pointing head each frame flips sides as it crosses vertical.
                if (oneFront.sqrMagnitude < .001f)
                {
                    oneFront = Vector3.ProjectOnPlane(oneHead.TransformDirection(oneFaceAxis), Vector3.up).normalized;
                    if (oneFront.sqrMagnitude < .001f) oneFront = playerOne.forward;
                }
                Bounds close = SubjectBounds(oneHead, null);
                Bounds wide = SubjectBounds(oneHead, oneShotBones);
                float sourceFrame = (oneFrameMap != null ? oneFrameMap(Frame) : Frame) + (SampleFrame - Frame);
                float closeBlend = Mathf.SmoothStep(0, 1, (sourceFrame - 110) / 30f);
                float wideBlend = Mathf.SmoothStep(0, 1, (sourceFrame - 30) / 20f) * (1 - closeBlend);
                Bounds focus = !oneStaging.landingStitch ? wide : new Bounds(
                    Vector3.Lerp(close.center, wide.center, wideBlend),
                    Vector3.Lerp(close.size, wide.size, wideBlend));
                oneCamera.Apply(cameraRig, playerOne, SampleFrame, lens, focus,
                    oneFront, oneFrameMap, oneHead.position);
            }
            else if (Current == Shot.PlayerTwo)
                twoCamera.Apply(cameraRig, playerTwo, SampleFrame, lens, SubjectBounds(twoHead, twoCloseUp ? null : twoShotBones),
                    twoHead.TransformDirection(twoFaceAxis), null, twoHead.position);
            else
                SetGameplayCamera(Current == Shot.RoundStart
                    ? Mathf.SmoothStep(0, 1, SampleFrame / (float)(CameraSettleFrames - 1)) : 1);
        }

        // Rigs differ in case (Xiaoyu: HEAD).
        static bool Is(Transform t, string name) => string.Equals(t.name, name, System.StringComparison.OrdinalIgnoreCase);

        static Bounds SubjectBounds(Transform head, Transform[] landmarks)
        {
            // Head transform is near the base of the skull, not the hair silhouette.
            var bounds = new Bounds(head.position + Vector3.up * .10f, Vector3.one * .44f);
            if (landmarks != null) foreach (var bone in landmarks)
                bounds.Encapsulate(new Bounds(bone.position, Vector3.one * .20f));
            return bounds;
        }

        void SetVisibility(bool twoVisible, bool oneVisible)
        {
            for (int i = 0; i < twoRenderers.Length; i++) twoRenderers[i].enabled = twoVisible && twoRendererEnabled[i];
            for (int i = 0; i < oneRenderers.Length; i++) oneRenderers[i].enabled = oneVisible && oneRendererEnabled[i];
        }

        void SetGameplayCamera(float blend)
        {
            var a = battle.fighters[0]; var b = battle.fighters[1];
            GameplayCameraFraming.Evaluate(new Vector3(a.x, 0, a.z), new Vector3(b.x, 0, b.z),
                out Vector3 position, out Vector3 target);
            // Cut to a separate, elevated two-shot, then settle to the combat camera.
            // Do not interpolate across the arena from Two's last close-up.
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
            // VersusGame draws the battle HUD and these cues (Cue) with the original lettering.
            if (ShowsBattleHud) return;
            var old = GUI.color;
            GUI.color = new Color(.025f, .035f, .055f, .82f);
            GUI.DrawTexture(new Rect(0, 0, 1280, 90), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0, 624, 1280, 96), Texture2D.whiteTexture);
            GUI.color = old;
            string headline = Current == Shot.PlayerOne ? oneName :
                Current == Shot.PlayerTwo ? twoName :
                Current == Shot.Ready ? "READY" : "FIGHT";
            GUI.Label(new Rect(90, 10, 1100, 75), headline, heading);
            GUI.Label(new Rect(90, 641, 1100, 55), "LOCAL VERSUS  /  ROUND 1     •     ENTER TO SKIP", caption);
        }
    }
}
