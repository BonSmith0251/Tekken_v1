using System;
using UnityEngine;

namespace Versus.Remaster
{
    // Original dispatch selects preset IDs; the presets themselves are the original per-frame pitch tables
    // (Tools/CameraEffects/export_shake_presets.py, evaluator 0x140701FD0 of the inspected executable).
    public static class BattleCameraEffects
    {
        [Serializable] public sealed class ShakeBinding { public int propertyId, preset; }
        [Serializable] public sealed class ShakePreset { public int preset, frames; public float[] values; }
        [Serializable] public sealed class Clip { public string code, resource, moveName; public int moveId, frame, frames, track; }
        [Serializable] public sealed class FollowScale { public int request; public float scale; }
        [Serializable] public sealed class FollowRate
        {
            public float baseRate, risePerFrame, fallPerFrame, minimumMatch; public int requestProperty;
            public FollowScale[] scales;
        }
        [Serializable] public sealed class KoCamera
        {
            public int slowSpeedPercent, slowFrames, rageDriveSlowFrames, blendInFrames, holdFrames, blendOutFrames;
            public float horizontalFov; public int shakePreset, rageDriveProperty;
            public RageDriveCamera rageDrive;
        }
        [Serializable] public sealed class RageDriveCamera
        {
            public int frames; public float distanceMm, heightMm, startFov, rollDegrees, easeDegrees;
            public RageDriveShot[] variants;
        }
        [Serializable] public sealed class RageDriveShot { public string centre; public float startAngle, endAngle, endFov; }
        [Serializable] public sealed class TradeCamera
        {
            public int slowSpeedPercent, slowFrames, damageThreshold, shakePreset, blendInFrames, holdFrames, blendOutFrames;
            public float horizontalFov;
        }
        [Serializable] public sealed class Catalog
        {
            public string executableSha256; public ShakeBinding[] shakeDispatch; public Clip[] clips;
            public float shakeUnitRadians; public ShakePreset[] shakePresets;
            public FollowRate followRate; public KoCamera koCamera; public TradeCamera tradeCamera;
        }
        static Catalog catalog;
        public static Catalog Data
        {
            get
            {
                if (catalog == null)
                {
                    var asset = Resources.Load<TextAsset>("CameraEffects/catalog");
                    if (!asset) throw new InvalidOperationException("Original camera evidence catalog missing.");
                    catalog = JsonUtility.FromJson<Catalog>(asset.text);
                }
                return catalog;
            }
        }
        /// <summary>Shake preset a move property selects, including 0x8005 / 0x800A, which pass the property
        /// value as the preset (min(value, 9); 0 stops a running shake; interpreter case 0x14071999B).</summary>
        public static bool TryShakePreset(long property, long value, out int preset)
        {
            if (property == 0x8005 || property == 0x800A) { preset = (int)Math.Min(value & 0xFFFF, 9); return true; }
            return TryShakePreset(property, out preset);
        }
        public static bool TryShakePreset(long property, out int preset)
        {
            foreach (var row in Data.shakeDispatch)
                if (row.propertyId == property) { preset = row.preset; return true; }
            preset = -1; return false;
        }
        static ShakePreset Find(int preset)
        {
            if (Data.shakePresets != null)
                foreach (var row in Data.shakePresets) if (row.preset == preset) return row;
            return null;
        }
        /// <summary>Length of a preset in 60 Hz battle ticks; the shake ends at this frame (0 if unknown).</summary>
        public static int ShakeFrames(int preset) => Find(preset)?.frames ?? 0;
        /// <summary>The original's pitch offset in degrees (positive looks up) for a preset at a battle frame
        /// counted from 0 when the shake starts. As in the evaluator, the last table entry is never applied.
        /// The original shakes pitch only: no lateral or positional motion.</summary>
        public static float OriginalShakePitch(int preset, int frame)
        {
            var row = Find(preset);
            if (row == null || frame < 0 || frame >= row.frames - 1) return 0;
            return row.values[frame] * Data.shakeUnitRadians * Mathf.Rad2Deg;
        }
        /// <summary>The original fight camera's follow rate with no request: the fraction of the way to its
        /// framing goal the camera moves each 60 Hz frame (Tools/CameraEffects/export_follow_rates.py).</summary>
        public static float BaseFollowRate => Data.followRate.baseRate;
        /// <summary>Camera request a move property sets (0x84CC: the value's low 16 bits), as in interpreter
        /// case 0x140719875. 0x84CC is not a zoom: it only selects how fast the camera follows.</summary>
        public static bool TryCameraRequest(long property, long value, out int request)
        {
            request = (int)(value & 0xFFFF);
            return property == Data.followRate.requestProperty;
        }
        /// <summary>Target follow rate for both fighters' requests: the base rate times the largest scale either
        /// request selects, or the base rate when neither is in the table (view update 0x1406EE020).</summary>
        public static float TargetFollowRate(int requestA, int requestB)
        {
            var data = Data.followRate; float best = 0;
            foreach (var row in data.scales)
                if (row.request == requestA || row.request == requestB) best = Mathf.Max(best, data.baseRate * row.scale);
            return best < data.minimumMatch ? data.baseRate : best;
        }
        /// <summary>One frame of the original's rate easing: a fixed step up or down, never a jump.</summary>
        public static float StepFollowRate(float current, float target)
        {
            var data = Data.followRate;
            if (current < target) return current + data.risePerFrame;
            if (current > target) return current - data.fallPerFrame;
            return current;
        }
        /// <summary>The original knockout (fight state 0x1406DE720, Tools/CameraEffects/export_ko_camera.py): game speed
        /// slowSpeedPercent (a near freeze) for slowFrames (rageDriveSlowFrames when the winner's Rage Drive, property
        /// 0x801B, is active), shake preset 6, and camera mode 7: the view at the KO, its FOV replaced, held over the
        /// live view.</summary>
        public static KoCamera Ko => Data.koCamera;
        /// <summary>The original trade (hit resolution 0x140713DD0): both fighters struck on the same frame, either for at
        /// least damageThreshold, slows the game to slowSpeedPercent for slowFrames, plays shakePreset and holds the view
        /// with camera mode 7 like the KO, with its own blend frames and FOV.</summary>
        public static TradeCamera Trade => Data.tradeCamera;
        /// <summary>Weight of a held view (camera mode 7) against the live view at a camera frame counted from its start, as
        /// mode 7's update (0x1406EF580): rising over blendIn, 1 through hold, falling over blendOut.</summary>
        public static float HoldWeight(float frame, int blendIn, int hold, int blendOut)
        {
            if (frame <= 0 || frame >= blendIn + hold + blendOut) return 0;
            if (frame < blendIn) return frame / blendIn;
            if (frame < blendIn + hold) return 1;
            return 1 - (frame - blendIn - hold) / blendOut;
        }
        /// <summary>The Rage Drive KO shot (camera mode 8, update 0x1406EF2C0 / 0x1406F3BF0) at a camera frame counted from
        /// the KO. It replaces the live view outright for its frames (no blend). It is anchored on the centre fighter:
        /// along the floor line to the opponent, swung by the variant's angle, (that distance + distanceMm) out and heightMm
        /// up, looking at the live camera's target; FOV and angle ease by sin(easeDegrees * t) / sin(easeDegrees), and the
        /// view is rolled by rollDegrees, signed by the centre's screen side as in the original.
        /// Interpretation (unverified against footage): the original's right-handed y-up rotation with that side sign is
        /// taken to keep the camera on the viewer's side of the fight line, so the swing goes toward liveCamera.</summary>
        public static void RageDriveKoShot(int variant, float frame, Vector3 centre, Vector3 opponent, Vector3 liveCamera,
            Vector3 liveTarget, Vector3 screenRight, out Vector3 position, out Quaternion rotation, out float horizontalFov)
        {
            var shot = Ko.rageDrive; var v = shot.variants[variant];
            float t = Mathf.Clamp01(frame / shot.frames);
            float s = Mathf.Sin(shot.easeDegrees * t * Mathf.Deg2Rad) / Mathf.Sin(shot.easeDegrees * Mathf.Deg2Rad);
            horizontalFov = Mathf.Lerp(shot.startFov, v.endFov, s);
            float angle = Mathf.Abs(Mathf.Lerp(v.startAngle, v.endAngle, s)) * Mathf.Deg2Rad;
            var line = opponent - centre; line.y = 0;
            float length = line.magnitude;
            var along = length > 1e-4f ? line / length : Vector3.right;
            var across = liveCamera - centre; across.y = 0; across -= Vector3.Dot(across, along) * along;
            across = across.sqrMagnitude > 1e-6f ? across.normalized : Vector3.Cross(along, Vector3.up);
            position = centre + (along * Mathf.Cos(angle) + across * Mathf.Sin(angle)) * (length + shot.distanceMm * .001f);
            position.y = centre.y + shot.heightMm * .001f;
            rotation = Quaternion.LookRotation(liveTarget - position);
            float side = Vector3.Dot(centre - opponent, screenRight) > 0 ? -1 : 1;
            rotation = Quaternion.AngleAxis(side * shot.rollDegrees, rotation * Vector3.forward) * rotation;
        }
        /// <summary>Applies a pitch offset (degrees, positive up) about the camera's own right axis.</summary>
        public static Quaternion Pitched(Quaternion rotation, float degrees) =>
            degrees == 0 ? rotation : Quaternion.AngleAxis(-degrees, rotation * Vector3.right) * rotation;
    }
}
