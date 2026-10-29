using UnityEngine;

namespace Versus.Remaster
{
    // The battle effect tuning panel (effectTuningPanel, F6; the BattleEffectTuning scene stores it on): sliders and
    // switches for the shared Resources/BattleEffectTuning.asset, which every battle reads live, and buttons that play
    // each effect on demand without changing the fight. Drawn inside the 1280 x 720 gameplay GUI.
    public partial class VersusGame
    {
        const float TuningPanelX = 880, TuningPanelWidth = 390;
        Vector2 tuningScroll;
        bool tuningDirty;
        GUIStyle tuningLabel, tuningSmallButton, tuningHeading, tuningToggle;

        void DrawTuningPanel()
        {
            if (tuningLabel == null)
            {
                tuningLabel = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleLeft };
                tuningHeading = new GUIStyle(tuningLabel) { fontSize = 14, fontStyle = FontStyle.Bold };
                tuningSmallButton = new GUIStyle(GUI.skin.button) { fontSize = 12 };
                // The skin's toggle style draws the check box; a label style would draw only the text.
                tuningToggle = new GUIStyle(GUI.skin.toggle) { fontSize = 13, alignment = TextAnchor.MiddleLeft };
            }
            var t = Tuning;
            var area = new Rect(TuningPanelX, 70, TuningPanelWidth, 630);
            Panel(area, new Color(0, 0, 0, .72f));
            GUILayout.BeginArea(new Rect(area.x + 8, area.y + 6, area.width - 16, area.height - 12));
            GUILayout.Label("EFFECT TUNING   (F6 hides; saved for every battle)", tuningHeading);
            tuningScroll = GUILayout.BeginScrollView(tuningScroll);

            Heading("Play an exchange (the fighters act it out; press again to replay)");
            bool fight = battle != null && battle.phase == MatchPhase.Fight && !paused && (entrance == null || !entrance.Active);
            bool automatic = automaticFight && effectDemo != null;
            effectDemosOnly = Toggle("Fighters idle between demonstrations", effectDemosOnly);
            GUI.enabled = fight && automatic;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("HIT", tuningSmallButton)) effectDemo.Start(BattleEffectDemo.Scenario.Hit);
            if (GUILayout.Button("COUNTER", tuningSmallButton)) effectDemo.Start(BattleEffectDemo.Scenario.Counter);
            if (GUILayout.Button("GUARD", tuningSmallButton)) effectDemo.Start(BattleEffectDemo.Scenario.Guard);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("TRADE", tuningSmallButton)) effectDemo.Start(BattleEffectDemo.Scenario.Trade);
            if (GUILayout.Button("KO", tuningSmallButton)) effectDemo.Start(BattleEffectDemo.Scenario.Ko);
            if (GUILayout.Button("RAGE DRIVE KO", tuningSmallButton)) effectDemo.Start(BattleEffectDemo.Scenario.RageDriveKo);
            GUILayout.EndHorizontal();
            GUI.enabled = true;
            if (effectDemo != null && effectDemo.Report.Length > 0) GUILayout.Label(effectDemo.Report, tuningLabel);
            if (!automatic) GUILayout.Label("Exchanges need auto fight (F2).", tuningLabel);
            Heading("Play the presentation only (no hit)");
            GUI.enabled = fight && effects;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("HIT FX", tuningSmallButton)) DemoHitEffect(CascadeEffects.Hit);
            if (GUILayout.Button("COUNTER FX", tuningSmallButton)) DemoHitEffect(CascadeEffects.Counter);
            if (GUILayout.Button("GUARD FX", tuningSmallButton)) DemoHitEffect(CascadeEffects.Guard);
            GUILayout.EndHorizontal();
            GUI.enabled = fight;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("HIT SHAKE", tuningSmallButton)) DemoShake(t.hitShakePreset);
            if (GUILayout.Button("COUNTER SHAKE", tuningSmallButton)) DemoShake(t.counterShakePreset);
            if (GUILayout.Button("KO SHAKE", tuningSmallButton)) DemoShake(BattleCameraEffects.Ko.shakePreset);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("TRADE CAMERA", tuningSmallButton)) StartTrade();
            if (GUILayout.Button("KO CAMERA", tuningSmallButton)) StartKo(false);
            if (GUILayout.Button("RAGE DRIVE CAMERA", tuningSmallButton)) StartKo(true);
            GUILayout.EndHorizontal();
            GUI.enabled = true;

            Heading("Common effects");
            t.commonBattleEffects = Toggle("Common hit / counter / guard / floor / Rage effects", t.commonBattleEffects);
            t.hitEffectScale = Slider("Hit effect size", t.hitEffectScale, .25f, 3, "0.00");
            t.glareGain = Slider("Glare", t.glareGain, 0, 3, "0.00");

            Heading("Hit shake (remaster choice; the original shakes only from move data)");
            t.hitShake = Toggle("Shake on hits", t.hitShake);
            t.hitShakePreset = Slider("Clean hit preset", t.hitShakePreset, 1, 10);
            t.counterShakePreset = Slider("Counter hit preset", t.counterShakePreset, 1, 10);
            t.shakeStrength = Slider("Shake strength (all shakes)", t.shakeStrength, 0, 3, "0.00");

            Heading("Fight camera");
            t.followRateInfluence = Slider("Follow-rate requests influence", t.followRateInfluence, 0, 2, "0.00");
            t.yawSmoothing = Slider("Angle smoothing (s)", t.yawSmoothing, .05f, 1, "0.00");
            t.distanceSmoothing = Slider("Distance smoothing (s)", t.distanceSmoothing, .05f, 1, "0.00");
            t.targetSmoothing = Slider("Centre smoothing (s)", t.targetSmoothing, .05f, 1, "0.00");

            Heading("Trade (both struck on one tick)");
            t.tradeDamageThreshold = Slider("Damage threshold", t.tradeDamageThreshold, 1, 60);
            t.tradeSlowPercent = Slider("Game speed %", t.tradeSlowPercent, 1, 100);
            t.tradeLength = Slider("Length x original", t.tradeLength, .5f, 4, "0.0");
            t.tradeFov = Slider("Close-up FOV", t.tradeFov, 15, 65, "0");
            t.smoothTrade = Toggle("Ease in and out", t.smoothTrade);
            t.centreTrade = Toggle("Keep both fighters centred", t.centreTrade);

            Heading("Knockout");
            t.koSlowPercent = Slider("Game speed %", t.koSlowPercent, 1, 100);
            t.koLength = Slider("Length x original", t.koLength, .25f, 3, "0.00");
            t.koFov = Slider("Close-up FOV", t.koFov, 15, 65, "0");
            t.smoothKo = Toggle("Ease in and out", t.smoothKo);
            t.centreKo = Toggle("Keep both fighters centred", t.centreKo);
            t.rageDriveKoShot = Toggle("Rage Drive KO: original orbit shot", t.rageDriveKoShot);
            t.koCollapseAtOnce = Toggle("Standing loser collapses at once", t.koCollapseAtOnce);
            t.koFreezeWinnerOnly = Toggle("Slow-down holds only the winner", t.koFreezeWinnerOnly);

            GUILayout.EndScrollView();
            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            GUI.enabled = tuningDirty;
            if (GUILayout.Button(tuningDirty ? "SAVE *" : "SAVED", tuningSmallButton)) { t.Save(); tuningDirty = false; }
            GUI.enabled = true;
            if (GUILayout.Button("ORIGINAL", tuningSmallButton)) { t.ResetToOriginal(); tuningDirty = true; }
            if (GUILayout.Button("DEFAULTS", tuningSmallButton)) { t.ResetToRemasterDefaults(); tuningDirty = true; }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        void Heading(string text) { GUILayout.Space(6); GUILayout.Label(text, tuningHeading); }

        bool Toggle(string label, bool value)
        {
            bool next = GUILayout.Toggle(value, " " + label, tuningToggle);
            if (next != value) tuningDirty = true;
            return next;
        }

        float Slider(string label, float value, float min, float max, string format)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, tuningLabel, GUILayout.Width(210));
            float next = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(100));
            GUILayout.Label(next.ToString(format), tuningLabel, GUILayout.Width(44));
            GUILayout.EndHorizontal();
            if (!Mathf.Approximately(next, value)) tuningDirty = true;
            return next;
        }

        int Slider(string label, int value, int min, int max) => Mathf.RoundToInt(Slider(label, value, min, max, "0"));

        // Demonstrations: the effect at the struck point between the fighters, or the shake, without any hit.
        void DemoHitEffect(string role)
        {
            if (!effects || !effects.Has(role)) return;
            Contact(0, 1, out var point, out var direction);
            effects.Play(role, point, Quaternion.LookRotation(direction, Vector3.up), Tuning.hitEffectScale);
        }

        void DemoShake(int preset)
        {
            shakePreset = Mathf.Clamp(preset, 1, 10); shakeFrame = 0; shakeStarted = true; shakeFromHit = false;
        }
    }
}
