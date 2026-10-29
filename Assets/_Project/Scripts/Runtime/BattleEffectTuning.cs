using System;
using System.IO;
using UnityEngine;

namespace Versus.Remaster
{
    /// <summary>The fine-tuned battle effects every versus battle plays (Resources/BattleEffectTuning.asset): the
    /// remaster's choices on top of the original data in Resources/CameraEffects/catalog.json. Tuned live in the
    /// BattleEffectTuning scene (VersusGame.effectTuningPanel) and read by every VersusGame. "Original" in the
    /// tooltips is the inspected executable's behaviour; ResetToOriginal restores it where the original has one.</summary>
    [CreateAssetMenu(menuName = "Remaster/Battle Effect Tuning")]
    public sealed class BattleEffectTuning : ScriptableObject
    {
        [Header("Common effects")]
        [Tooltip("Play the common hit/counter/guard/floor/Rage effects (BP_CommonEffectManager roles) and the fallback impact sphere. Off: only the characters' own wired effects.")]
        public bool commonBattleEffects = true;
        [Tooltip("Size of the hit, counter and guard effects (1 = as exported).")]
        [Range(.25f, 3)] public float hitEffectScale = 1;
        [Tooltip("Brightness of the effects' glare (CascadeEffects.glareGain).")]
        [Range(0, 3)] public float glareGain = 1;

        [Header("Hit shake (remaster choice: the original shakes only from move data)")]
        [Tooltip("Shake the camera on clean and counter hits. Original data shakes always take precedence.")]
        public bool hitShake = true;
        [Tooltip("Original shake preset on a clean hit (1-10; 3 = 12 frames, 0.7 deg).")]
        [Range(1, 10)] public int hitShakePreset = 3;
        [Tooltip("Original shake preset on a counter hit (4 = 18 frames, 1.05 deg).")]
        [Range(1, 10)] public int counterShakePreset = 4;
        [Tooltip("Multiplies every shake's pitch amplitude (1 = the original tables).")]
        [Range(0, 3)] public float shakeStrength = 1;

        [Header("Fight camera")]
        [Tooltip("How much the original follow-rate requests (0x84CC: Rider x3, shoryu x2, rolls x1.5) change the camera's speed. 1 = the original ratio, 0 = ignore them.")]
        [Range(0, 2)] public float followRateInfluence = 1;
        [Tooltip("Smoothing time of the fight-line angle (seconds, at the base follow rate).")]
        [Range(.05f, 1)] public float yawSmoothing = .38f;
        [Tooltip("Smoothing time of the camera distance (seconds).")]
        [Range(.05f, 1)] public float distanceSmoothing = .32f;
        [Tooltip("Smoothing time of the framing centre (seconds).")]
        [Range(.05f, 1)] public float targetSmoothing = .2f;

        [Header("Trade (both fighters struck on one tick)")]
        [Tooltip("Lowest damage either hit must do for the trade effect (original 17).")]
        [Range(1, 60)] public int tradeDamageThreshold = 17;
        [Tooltip("Game speed during the trade slow-down, percent (original 10).")]
        [Range(1, 100)] public int tradeSlowPercent = 10;
        [Tooltip("Length of the trade's slow-down and close-up as a multiple of the original's (24 slow frames, 4/20/30 camera frames).")]
        [Range(.5f, 4)] public float tradeLength = 2.5f;
        [Tooltip("Ease the trade's game speed and close-up in and out (the original switches at once and blends linearly).")]
        public bool smoothTrade = true;
        [Tooltip("Keep both fighters centred in the trade close-up, widening past its FOV only to fit them (the original holds a fixed view).")]
        public bool centreTrade = true;
        [Tooltip("Horizontal FOV of the trade close-up, degrees (original 35; the fight camera is 65).")]
        [Range(15, 65)] public float tradeFov = 35;

        [Header("Knockout")]
        [Tooltip("Game speed during the KO near-freeze, percent (original 1).")]
        [Range(1, 100)] public int koSlowPercent = 1;
        [Tooltip("Length of the KO's freeze and close-up as a multiple of the original's (90 slow frames, 150 after a Rage Drive; 5/90/20 camera frames).")]
        [Range(.25f, 3)] public float koLength = 1;
        [Tooltip("Horizontal FOV of the KO close-up, degrees (original 35).")]
        [Range(15, 65)] public float koFov = 35;
        [Tooltip("Ease the KO's game speed and close-up (the original switches at once).")]
        public bool smoothKo;
        [Tooltip("Keep both fighters centred in the KO close-up (the original holds a fixed view).")]
        public bool centreKo;
        [Tooltip("Play the original's Rage Drive KO shot (camera mode 8) instead of the close-up when the winner's Rage Drive is active.")]
        public bool rageDriveKoShot = true;
        [Tooltip("A blow that knocks out a standing fighter plays its standing death collapse (fatal_s) at once. Off: the stagger plays out, the fighter returns to its stance and collapses 25-45 ticks later (the move data's route).")]
        public bool koCollapseAtOnce = true;
        [Tooltip("The KO slow-down holds only the winner, so the loser's KO action plays at full speed. Off: the whole game slows, as the original's game speed does.")]
        public bool koFreezeWinnerOnly = true;

        static BattleEffectTuning current;
        static string PlayerFile => Path.Combine(Application.persistentDataPath, "battle-effect-tuning.json");

        /// <summary>The tuning every battle reads. In the editor, the asset (created on first use); in a player, the
        /// asset with the last saved overrides applied.</summary>
        public static BattleEffectTuning Current
        {
            get
            {
                if (current) return current;
                var asset = Resources.Load<BattleEffectTuning>("BattleEffectTuning");
                current = asset ? asset : CreateInstance<BattleEffectTuning>();
#if UNITY_EDITOR
                if (!asset) current.Save();
#else
                try { if (File.Exists(PlayerFile)) { current = Instantiate(current); JsonUtility.FromJsonOverwrite(File.ReadAllText(PlayerFile), current); } }
                catch (Exception e) { Debug.LogWarning("Battle effect tuning not read: " + e.Message); }
#endif
                return current;
            }
        }

        public void Save()
        {
#if UNITY_EDITOR
            if (!UnityEditor.AssetDatabase.Contains(this))
                UnityEditor.AssetDatabase.CreateAsset(this, "Assets/_Project/Resources/BattleEffectTuning.asset");
            UnityEditor.EditorUtility.SetDirty(this);
            UnityEditor.AssetDatabase.SaveAssetIfDirty(this);
#else
            File.WriteAllText(PlayerFile, JsonUtility.ToJson(this));
#endif
        }

        /// <summary>The inspected executable's behaviour where it has one: no hit shake, original rates, the trade and
        /// KO at their recovered values with no easing or re-framing.</summary>
        public void ResetToOriginal()
        {
            var trade = BattleCameraEffects.Trade; var ko = BattleCameraEffects.Ko;
            hitEffectScale = 1; glareGain = 1;
            hitShake = false; shakeStrength = 1;
            followRateInfluence = 1; yawSmoothing = .38f; distanceSmoothing = .32f; targetSmoothing = .2f;
            tradeDamageThreshold = trade.damageThreshold; tradeSlowPercent = trade.slowSpeedPercent; tradeLength = 1;
            smoothTrade = false; centreTrade = false; tradeFov = trade.horizontalFov;
            koSlowPercent = ko.slowSpeedPercent; koLength = 1; koFov = ko.horizontalFov; smoothKo = false; centreKo = false;
            rageDriveKoShot = true; koCollapseAtOnce = false; koFreezeWinnerOnly = false;
        }

        /// <summary>The remaster's defaults as first tuned (hit shake on, the longer eased centred trade).</summary>
        public void ResetToRemasterDefaults()
        {
            ResetToOriginal();
            hitShake = true; hitShakePreset = 3; counterShakePreset = 4;
            tradeLength = 2.5f; smoothTrade = true; centreTrade = true;
            commonBattleEffects = true; koCollapseAtOnce = true; koFreezeWinnerOnly = true;
        }
    }
}
