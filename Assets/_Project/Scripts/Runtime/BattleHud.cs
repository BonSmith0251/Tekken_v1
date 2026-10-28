using System.Collections.Generic;
using UnityEngine;

namespace Versus.Remaster
{
    // Tekken 7's in-fight HUD rebuilt from its original Scaleform textures (Resources/BattleHud,
    // exported by Tools/BattleHud/export_battle_hud.py). Element placement was measured from gameplay
    // captures at 1440x810 and is drawn here in the versus scene's 1280x720 GUI space. The life gauge's
    // behaviour is ported from SW_UI_HUD_Gauge itself (its SWF timelines and ActionScript, decoded
    // 2026-10-26; see Docs/BATTLE_HUD.txt): Main.SetHealth, StartGaugeAnim, onRedScale and onDamage,
    // run on the movie's 60 fps clock.
    public sealed class BattleHud
    {
        public enum Call { None, Round, Ready, Fight, KO, DoubleKO, TimeUp, Draw }

        // Reference capture coordinates (1440 wide) -> GUI space (1280 wide).
        const float Ref = 1280f / 1440f;
        const float GaugeScale = .69f, DigitScale = .54f, CallScale = .7f;
        static readonly Vector2 FrameOrigin = new Vector2(126.3f, 35.3f), FillOrigin = new Vector2(144.6f, 53.9f);
        // Life fill textures are 752x76; their opaque band spans x 62..705 on the middle row (37) and both
        // ends lean 0.82 px right per px down.
        // Rows 16..60 hold the opaque band and its soft edges.
        const float FillWidth = 752, FillHeight = 76, FillMid0 = 62, FillMid1 = 705, FillMidRow = 37, FillSlant = .82f;
        const float FillBandTop = 16, FillBandBottom = 60, FillStrip = 2;
        static readonly Rect Portrait = new Rect(70, 27, 107, 111), Lightning = new Rect(100, 45, 236, 100),
            NamePlate = new Rect(182, 101, 330, 25), Name = new Rect(200, 98.5f, 190, 26);
        const float PipX = 641, PipSpacing = 29, PipY = 113, PipSize = 23;
        // The red trail drains linearly over DENOMINATOR_OF_SPEED frames. The game sets it natively
        // (SetGaugeAnimFrames), so it is measured: 11.5 frames at 30 fps in TEKKEN 7 2026-10-23 08-04-08.mp4.
        const int RedDrainFrames = 24;
        const float GlowDelay = .85f;   // Main.GLOW_DELAY_MAGNIFICATION: the redgrow glow trails the red edge.
        // GAU_Life_red_L_mc is 80% opaque but adds a second, additive copy of the red at 20% (49% as a drain
        // starts): about 96% of a fully opaque red. IMGUI can't add, so the red is drawn at that opacity.
        const float RedAlpha = .96f, GrowAlpha = .35f;   // GAU_Life_redgrow_L_mc is 35%.
        // Shake amplitude in movie (1920-wide stage) pixels and the stage -> reference-capture scale.
        const float ShakeX = 15, ShakeY = 5;

        readonly Texture2D frameNormal, frameRage, frameDamage, fill, damage, hit, grow, pipOff, pipOn, pipFlash;
        readonly Texture2D[] digits = new Texture2D[10], roundNumbers = new Texture2D[10], roundGlows = new Texture2D[10];
        readonly Texture2D[] icons = new Texture2D[2], names = new Texture2D[2], plates = new Texture2D[2], thunder = new Texture2D[2];
        readonly Gauge[] gauges = { new Gauge(), new Gauge() };
        readonly int[] lastWins = new int[2];
        readonly float[] pipAge = { 9, 9 };
        float clock;
        int frameCount;

        // One side's Scaleform state (Main's per-player vectors and the gauge clips' playheads).
        sealed class Gauge
        {
            public float shown = 1, red = 1, glow = 1, redTarget, redSpeed;
            public bool redAnim, held;
            // HUD_GAU_LIFE_FRAME_DAMAGE_Anim playhead (0 Off, 1..2 Start then hold, 3..14 End) and
            // GAU_Life_eff_sub playhead (0 Off, 1..49 playing).
            public int damageFrame, effFrame, rageFrame;
            // Main.onDamage: life_damage_frame / perFrame / count and life_damage_pos.
            public int shakeFrames, shakePerFrame, shakeCount;
            public float shakeX, shakeY, offsetX, offsetY;
        }

        public BattleHud(string playerOneCode, string playerTwoCode)
        {
            frameNormal = Load("FrameNormal"); frameRage = Load("FrameRage"); frameDamage = Load("FrameDamage");
            fill = Load("LifeFill"); damage = Load("LifeDamage"); hit = Load("LifeHit"); grow = Load("LifeGrow");
            pipOff = Load("RoundOff"); pipOn = Load("RoundOn"); pipFlash = Load("RoundFlash");
            for (int d = 0; d < 10; d++) { digits[d] = Load("Digit" + d); roundNumbers[d] = Load("RoundNum" + d); roundGlows[d] = Load("RoundNum" + d + "_Glow"); }
            string[] codes = { playerOneCode.ToUpperInvariant(), playerTwoCode.ToUpperInvariant() };
            for (int i = 0; i < 2; i++)
            {
                string side = i == 0 ? "L" : "R";
                icons[i] = Load("Icon_" + side + "_" + codes[i]); names[i] = Load("Name_" + side + "_" + codes[i]);
                plates[i] = Load("NameBase_" + side); thunder[i] = Load("Thunder_" + side);
            }
        }

        static Texture2D Load(string name) => Resources.Load<Texture2D>("BattleHud/" + name);
        static Rect R(float x, float y, float w, float h) => new Rect(x * Ref, y * Ref, w * Ref, h * Ref);
        // Mirror a left-side reference rect onto the right side of the screen.
        static Rect Side(Rect r, int side) => side == 0 ? R(r.x, r.y, r.width, r.height) : R(1440 - r.x - r.width, r.y, r.width, r.height);
        static Rect Shift(Rect r, Vector2 by) => new Rect(r.x + by.x, r.y + by.y, r.width, r.height);

        // Call once per rendered frame before drawing, with unscaled time so KO slow motion doesn't stall
        // the gauges. The movie runs at 60 fps, so its logic steps on a 60 Hz clock.
        public void Advance(BattleModel battle, float deltaTime)
        {
            for (int i = 0; i < 2; i++)
            {
                var f = battle.fighters[i];
                if (f.wins != lastWins[i]) { pipAge[i] = f.wins > lastWins[i] ? 0 : 9; lastWins[i] = f.wins; }
                pipAge[i] += deltaTime;
            }
            clock = Mathf.Min(clock + deltaTime, .25f);
            while (clock >= 1f / 60)
            {
                clock -= 1f / 60; frameCount++;
                for (int i = 0; i < 2; i++) Step(gauges[i], battle.fighters[i], battle.rules.health);
            }
        }

        void Step(Gauge g, FighterState f, int maxHealth)
        {
            // Timeline playheads advance first; the calls below then jump them to a label for this frame.
            if (g.damageFrame == 1) g.damageFrame = 2;                                // Start plays into its stop()
            else if (g.damageFrame >= 3 && g.damageFrame < 14) g.damageFrame++;       // End plays to its stop()
            if (g.effFrame > 0) g.effFrame = g.effFrame < 49 ? g.effFrame + 1 : 0;    // frame 50 ('Wait') is empty
            bool rage = f.health > 0 && f.health * 4 <= maxHealth;
            // RageOn plays 'Rage' (frames 1..18; frame 19's script jumps back to 'Rage'); RageOff stops on 'Normal'.
            g.rageFrame = !rage ? 0 : g.rageFrame == 0 || g.rageFrame >= 18 ? 1 : g.rageFrame + 1;

            float health = Mathf.Clamp01(f.health / (float)maxHealth);
            if (health < g.shown - 1e-4f)
            {
                // Main.SetHealth, health fell: the green cuts at once and the red stays; the gauge starts
                // its damage frame, its hit glow (unless this was the KO) and the shake (life_damage_enable).
                g.damageFrame = 1;
                g.effFrame = health > 0 ? 1 : 0;
                g.shakeFrames = 10; g.shakePerFrame = 3; g.shakeCount = 0; g.shakeX = ShakeX; g.shakeY = ShakeY;
            }
            else if (health > g.shown + 1e-4f)
            {
                // Health rose (a new round): red and glow snap to it and the hit glow switches off.
                g.red = g.glow = health; g.redAnim = false; g.effFrame = 0;
            }
            g.shown = health;

            // StopGaugeAnim/StartGaugeAnim come from native code. In the reference captures the red damage
            // stays while the struck fighter is still reeling and drains once the reaction ends, so the
            // hit reaction stands in for the native hold. On release the red slides to the new level over
            // a fixed number of frames whatever the amount (StartGaugeAnim + onRedScale).
            bool held = f.reacting && f.health > 0;
            if (!held && health < g.red - 1e-4f && (!g.redAnim || health < g.redTarget - 1e-4f))
            {
                g.redTarget = health; g.redSpeed = (g.red - health) / RedDrainFrames; g.redAnim = true;
            }
            if (g.redAnim)
            {
                g.red -= g.redSpeed; g.glow -= g.redSpeed * GlowDelay;
                if (g.red <= g.redTarget) { g.red = g.redTarget; g.effFrame = 0; }       // life_eff_mc 'End'
                if (g.glow <= g.redTarget) { g.glow = g.redTarget; g.redAnim = false; }
            }
            Shake(g);
        }

        // Main.onDamage, called every frame: the gauge jumps to alternate sides on every perFrame-th frame,
        // each jump perFrame/10 smaller; jumps come every 3 frames, then 2, then every frame, and the third
        // stage also plays the damage frame's 'End'.
        void Shake(Gauge g)
        {
            if (g.shakeFrames == 0 || g.shakePerFrame == 0) return;
            if ((int)(g.shakeFrames * .4f) == g.shakeCount) g.shakePerFrame--;
            else if ((int)(g.shakeFrames * .7f) == g.shakeCount)
            {
                g.shakePerFrame--;
                if (g.damageFrame > 0 && g.damageFrame < 3) g.damageFrame = 3;
            }
            if (g.shakePerFrame == 0) g.shakePerFrame = 1;
            if (frameCount % g.shakePerFrame == 0)
            {
                float sign = frameCount % 2 == 0 ? 1 : -1, steps = g.shakeFrames / (float)g.shakePerFrame;
                if (g.shakeX != 0) { g.offsetX = g.shakeX * sign; g.shakeX -= g.shakeX / steps; }
                if (g.shakeY != 0) { g.offsetY = g.shakeY * sign; g.shakeY -= g.shakeY / steps; }
                if (g.shakeX < .3f) { g.shakeX = 0; g.offsetX = 0; }
                if (g.shakeY < .3f) { g.shakeY = 0; g.offsetY = 0; }
                if (g.shakeX == 0 && g.shakeY == 0) g.shakeFrames = g.shakePerFrame = 0;
            }
            g.shakeCount++;
        }

        public void DrawGauges(BattleModel battle, float time)
        {
            var color = GUI.color;
            for (int i = 0; i < 2; i++)
            {
                var f = battle.fighters[i];
                bool rage = f.health > 0 && f.health * 4 <= battle.rules.health;
                // Lightning behind the portrait flickers constantly (pink for P1, blue for P2), brighter in Rage.
                float flicker = .55f + .45f * Mathf.PerlinNoise(time * 9f, i * 7.3f);
                GUI.color = new Color(1, 1, 1, rage ? flicker : flicker * .7f);
                if (thunder[i]) GUI.DrawTexture(Side(Lightning, i), thunder[i]);
                GUI.color = color;
                DrawGauge(i, rage);
                if (icons[i]) GUI.DrawTexture(Side(Portrait, i), icons[i]);
                if (plates[i]) GUI.DrawTexture(Side(NamePlate, i), plates[i]);
                if (names[i]) GUI.DrawTexture(Side(Name, i), names[i]);
                for (int p = 0; p < battle.rules.roundsToWin; p++)
                {
                    float cx = PipX - p * PipSpacing;
                    var pip = Side(new Rect(cx - PipSize / 2, PipY - PipSize / 2, PipSize, PipSize), i);
                    bool won = p < f.wins;
                    GUI.DrawTexture(pip, won ? pipOn : pipOff);
                    if (won && p == f.wins - 1 && pipAge[i] < .6f && pipFlash)
                    {
                        // The newest point pops with a ring, as the counter lights up.
                        float t = pipAge[i] / .6f, size = PipSize * Mathf.Lerp(1, 3, t);
                        GUI.color = new Color(1, 1, 1, 1 - t);
                        GUI.DrawTexture(Side(new Rect(cx - size / 2, PipY - size / 2, size, size), i), pipFlash);
                        GUI.color = color;
                    }
                }
            }
            DrawTimer(battle);
            GUI.color = color;
        }

        // Layer alphas from SW_UI_HUD_Gauge's timelines (60 fps frames).
        // GAU_Life_Frame 'Rage' loop, frames 1..18: the rage outline (depth 3) pulses 0 -> 1 -> 0 and a
        // second copy (depth 5) 0.1 -> 0.3 -> 0.1; on 'Normal' they rest at 0 and 0.1.
        static float RageOuter(int frame) => frame == 0 ? 0 : 1 - Mathf.Abs(frame - 10) / 9f;
        static float RageInner(int frame) => frame == 0 ? .1f : .1f + .2f * (1 - Mathf.Abs(frame - 10) / 9f);
        // HUD_GAU_LIFE_FRAME_DAMAGE_Anim frames 1..14 for its two copies of the damage frame.
        static readonly float[] DamageBack = { 0, .2f, .33f, .46f, .6f, .73f, .87f, 1, .86f, .71f, .57f, .43f, .29f, .14f, 0 };
        static readonly float[] DamageFront = { 0, .2f, .6f, 1, .91f, .82f, .73f, .64f, .55f, .45f, .36f, .27f, .18f, .09f, 0 };

        void DrawGauge(int side, bool rage)
        {
            var g = gauges[side];
            // The shake moves the whole gauge clip (frame, fills and effects), in movie pixels.
            var shake = new Vector2(g.offsetX, g.offsetY) * GaugeScale;
            var frameRect = new Rect(FrameOrigin.x + shake.x, FrameOrigin.y + shake.y, 796 * GaugeScale, 124 * GaugeScale);
            var color = GUI.color;
            // The normal frame (with the gauge's dark interior) always shows; Rage pulses its red outline over it.
            Mirrored(frameRect, 0, 1, frameNormal, side);
            DrawLayer(frameRect, frameRage, side, RageOuter(g.rageFrame));
            DrawLayer(frameRect, frameRage, side, RageInner(g.rageFrame));
            // The fill is anchored at the timer end: health h shows the inner h of its span. Under the red trail,
            // a thinner red glow (redgrow, 35%) follows the draining edge at 85% of its speed.
            DrawGrow(g, side, shake);
            GUI.color = new Color(1, 1, 1, RedAlpha);
            DrawFill(damage, g.red, g.shown, side, shake);
            GUI.color = color;
            DrawFill(fill, g.shown, 0, side, shake);
            DrawLayer(frameRect, frameDamage, side, DamageBack[g.damageFrame]);
            DrawLayer(frameRect, frameDamage, side, DamageFront[g.damageFrame]);
            DrawGlow(g, side, shake);
            GUI.color = color;
        }

        void DrawLayer(Rect reference, Texture texture, int side, float alpha)
        {
            if (alpha <= 0) return;
            var color = GUI.color;
            GUI.color = new Color(color.r, color.g, color.b, color.a * alpha);
            Mirrored(reference, 0, 1, texture, side);
            GUI.color = color;
        }

        // GAU_Life_redgrow: a 640x40 red strip centred on the fill, from the timer end to the glow level.
        void DrawGrow(Gauge g, int side, Vector2 shake)
        {
            if (!grow || g.glow <= g.shown + 1e-4f) return;
            float x0 = CutX(g.glow), x1 = FillMid1 - 5;
            var rect = new Rect(FillOrigin.x + x0 * GaugeScale, FillOrigin.y + (FillMidRow - 20) * GaugeScale, (x1 - x0) * GaugeScale, 40 * GaugeScale);
            var color = GUI.color;
            GUI.color = new Color(1, 1, 1, GrowAlpha);
            Mirrored(Shift(rect, shake), 0, 1, grow, side);
            GUI.color = color;
        }

        // GAU_Life_eff_sub: the hit glow (eff_damage, 84x240) on the cut, frames 1..49. It widens from 25% to
        // 78% in 8 frames and narrows back by frame 49, at 20-40% alpha, sheared with the gauge's lean
        // (skew 0.157) and tilting as it widens (skew -0.81 x width). IMGUI draws the shear through GUI.matrix;
        // the right side conjugates it by the mirror and flips the texture, so the matrix keeps a positive
        // determinant.
        void DrawGlow(Gauge g, int side, Vector2 shake)
        {
            if (!hit || g.effFrame < 1 || g.shown <= 0) return;
            float t = g.effFrame <= 9 ? (g.effFrame - 1) / 8f : 1 - (g.effFrame - 9) / 40f;
            float sx = Mathf.Lerp(.251f, .777f, t), alpha = Mathf.Lerp(.199f, .398f, t), r0 = -.8095f * sx;
            const float sy = .194f, r1 = .157f;
            var center = new Vector2(FillOrigin.x + CutX(g.shown) * GaugeScale, FillOrigin.y + FillMidRow * GaugeScale) + shake;
            if (side == 1) center.x = 1440 - center.x;
            float k = GaugeScale * Ref, mirror = side == 0 ? 1 : -1;
            var matrix = Matrix4x4.identity;
            matrix.m00 = sx * k; matrix.m01 = r1 * k * mirror; matrix.m03 = center.x * Ref;
            matrix.m10 = r0 * k * mirror; matrix.m11 = sy * k; matrix.m13 = center.y * Ref;
            var oldMatrix = GUI.matrix; var color = GUI.color;
            GUI.matrix = oldMatrix * matrix;
            GUI.color = new Color(1, 1, 1, alpha);
            GUI.DrawTextureWithTexCoords(new Rect(-42, -120, 84, 240), hit, side == 0 ? new Rect(0, 0, 1, 1) : new Rect(1, 0, -1, 1));
            GUI.matrix = oldMatrix; GUI.color = color;
        }

        // Fill texture x (pixels, on its middle row) where health h ends; the cut leans with the gauge's ends.
        static float CutX(float h) => FillMid1 - h * (FillMid1 - FillMid0);

        // Draws the part of a fill texture between health fractions `outer` and `inner`, with cuts parallel to
        // the gauge's slanted ends. IMGUI only cuts rectangles, so the band is drawn as thin horizontal strips,
        // each cut at its own row's offset (a strip is about one screen pixel tall).
        void DrawFill(Texture2D texture, float outer, float inner, int side, Vector2 shake)
        {
            if (!texture || outer <= inner) return;
            float xo = outer >= 1 ? -60 : CutX(outer), xi = inner <= 0 ? 812 : CutX(inner); // full ends keep their tips
            for (float y = FillBandTop; y < FillBandBottom; y += FillStrip)
            {
                float shift = (y + FillStrip / 2 - FillMidRow) * FillSlant;
                float x0 = Mathf.Clamp(xo + shift, 0, FillWidth), x1 = Mathf.Clamp(xi + shift, 0, FillWidth);
                if (x1 <= x0) continue;
                var strip = new Rect(FillOrigin.x + x0 * GaugeScale + shake.x, FillOrigin.y + y * GaugeScale + shake.y, (x1 - x0) * GaugeScale, FillStrip * GaugeScale);
                var uv = new Rect(x0 / FillWidth, 1 - (y + FillStrip) / FillHeight, (x1 - x0) / FillWidth, FillStrip / FillHeight);
                var rect = Side(strip, side);
                GUI.DrawTextureWithTexCoords(rect, texture, side == 0 ? uv : new Rect(uv.xMax, uv.y, -uv.width, uv.height));
            }
        }

        // Draws the u0..u1 slice of a left-side texture; on the right side the rect and the slice are mirrored.
        static void Mirrored(Rect reference, float u0, float u1, Texture texture, int side)
        {
            if (!texture) return;
            var rect = Side(reference, side);
            GUI.DrawTextureWithTexCoords(rect, texture, side == 0 ? new Rect(u0, 0, u1 - u0, 1) : new Rect(u1, 0, u0 - u1, 1));
        }

        void DrawTimer(BattleModel battle)
        {
            int seconds = Mathf.Clamp(Mathf.CeilToInt(battle.ticksLeft / 60f), 0, 99);
            float w = 124 * DigitScale, h = 84 * DigitScale, advance = 64, top = 56;
            int tens = seconds / 10, ones = seconds % 10;
            GUI.DrawTexture(R(720 - advance / 2 - w / 2, top, w, h), digits[tens]);
            GUI.DrawTexture(R(720 + advance / 2 - w / 2, top, w, h), digits[ones]);
        }

        // Centre-screen calls with the original lettering: a short pop-in, with the glow layer fading out.
        public void DrawCall(Call call, int round, bool finalRound, float age)
        {
            if (call == Call.None || age < 0) return;
            float pop = Mathf.SmoothStep(1.25f, 1, Mathf.Clamp01(age / .15f)), alpha = Mathf.Clamp01(age / .08f);
            float glow = Mathf.Clamp01(1 - age / .6f);
            switch (call)
            {
                case Call.Round: RoundClip(round, finalRound).Draw(age, StageToGui); break;
                // READY, FIGHT, K.O. and DOUBLE K.O. replay the original SW_UI_GameMessage timelines.
                case Call.Ready: Message(ref readyClip, "ReadyFight_mc", "Ready", "Fight", -1).Draw(age, StageToGui); break;
                case Call.Fight: Message(ref fightClip, "ReadyFight_mc", "Fight", null, 0).Draw(age, StageToGui); break;
                case Call.KO: Message(ref koClip, "KO_mc", "KO", null, 0).Draw(age, StageToGui); break;
                case Call.DoubleKO: Message(ref doubleKoClip, "WKO_mc", "WKO", null, 0).Draw(age, StageToGui); break;
                case Call.TimeUp: Message(ref timeUpClip, "TimeUp_mc", "TimeUp", null, 0).Draw(age, StageToGui); break;
                case Call.Draw: Message(ref drawClip, "Draw_mc", "Draw", null, 0).Draw(age, StageToGui); break;
            }
        }

        GfxClip readyClip, fightClip, koClip, doubleKoClip, timeUpClip, drawClip, perfectClip, finalRoundClip;
        readonly Dictionary<int, GfxClip> roundClips = new Dictionary<int, GfxClip>();

        // Round_mc.ChangeRound(n), ported from the movie's ActionScript: per last digit d it places the word
        // and the number (RoundLoc / NumLoc, glow copies RoundGlowLoc / NumGlowLoc), slides each digit strip
        // to show its numeral (x = d * -200 - 100; glow d * -220 - 110), shows the tens digit from round 10,
        // stops the digit mask on frame n, then plays from RD_Num. FINAL ROUND plays RD_Final instead.
        static readonly Vector2[] RoundLoc = { new Vector2(-165, 2), new Vector2(-81, 2), new Vector2(-100, -1), new Vector2(-100, 0), new Vector2(-102, -6),
            new Vector2(-101, 0), new Vector2(-100, 1), new Vector2(-98, -1), new Vector2(-100, 1), new Vector2(-99, 2) };
        static readonly Vector2[] NumLoc = { new Vector2(531, 0), new Vector2(485, 0), new Vector2(462, 0), new Vector2(467, 0), new Vector2(465, -4),
            new Vector2(469, -1), new Vector2(467, 0), new Vector2(469, 1), new Vector2(465, 0), new Vector2(465, 0) };
        static readonly Vector2[] RoundGlowLoc = { new Vector2(-636, -72), new Vector2(-552, -74), new Vector2(-570, -75), new Vector2(-571, -73), new Vector2(-572, -79),
            new Vector2(-571, -73), new Vector2(-570, -72), new Vector2(-569, -74), new Vector2(-573, -72), new Vector2(-571, -72) };
        static readonly Vector2[] NumGlowLoc = { new Vector2(531, 0), new Vector2(485, 0), new Vector2(463, -2), new Vector2(465, 0), new Vector2(464, -5),
            new Vector2(467, 0), new Vector2(466, 0), new Vector2(470, 0), new Vector2(465, 0), new Vector2(466, 0) };

        GfxClip RoundClip(int round, bool final)
        {
            // TEKKEN 7 shows ROUND from the movie's 12th fly-in frame (gameplay capture, 2026-10-23 08-04-08 at 35.9 s).
            const int RoundLead = 12;
            if (final) return finalRoundClip ??= new GfxClip("Round_mc", "RD_Final", "RD_Fina_Finl") { LeadFrames = RoundLead };
            round = Mathf.Clamp(round, 1, 10);
            if (roundClips.TryGetValue(round, out var clip)) return clip;
            clip = new GfxClip("Round_mc", "RD_Num", "RD_Num_Fin") { LeadFrames = RoundLead };
            int d = round % 10, tens = round / 10;
            const string Anim = "Moji_RoundAll_Anim_mc.";
            foreach (var word in new[] { Anim + "Moji_RoundAll_mc", Anim + "Moji_RoundAll_Shadow_mc", "Moji_RoundAll_mc" })
            {
                clip.SetPosition(word + ".Moji_Round_Base_mc", RoundLoc[d].x, RoundLoc[d].y)
                    .SetPosition(word + ".Num1", NumLoc[d].x, NumLoc[d].y)
                    .SetVisible(word + ".Num10", round > 9)
                    .SetPosition(word + ".Num1.Moji_RoundNum_Base_mc", d * -200 - 100, null)
                    .SetPosition(word + ".Num10.Moji_RoundNum_Base_mc", tens * -200 - 100, null);
            }
            const string Glow = Anim + "Moji_RoundAll_Glow_Anim_mc.Moji_RoundAll_Glow_mc";
            clip.SetPosition(Glow + ".Moji_Round_Glow_Base_mc", RoundGlowLoc[d].x, RoundGlowLoc[d].y)
                .SetPosition(Glow + ".Num1", NumGlowLoc[d].x, NumGlowLoc[d].y)
                .SetVisible(Glow + ".Num10", round > 9)
                .SetPosition(Glow + ".Num1.Moji_RoundNum_Glow_Base_mc", d * -220 - 110, null)
                .SetPosition(Glow + ".Num10.Moji_RoundNum_Glow_Base_mc", tens * -220 - 110, null)
                .GotoAndStop(Anim + "Moji_RoundAll_Mask_mc", round);
            return roundClips[round] = clip;
        }
        // The message movie's 1920x1080 stage in this HUD's 1280x720 GUI space.
        static Matrix4x4 StageToGui => Matrix4x4.Scale(Vector3.one * (1280f / GfxClip.StageWidth));
        static GfxClip Message(ref GfxClip clip, string entry, string start, string stop, int stopOffset) =>
            clip ??= new GfxClip(entry, start, stop, stopOffset);
        // How long the original FIGHT call runs (its Fight segment), so it can finish after control is given.
        public float FightCallSeconds => Message(ref fightClip, "ReadyFight_mc", "Fight", null, 0).Length / 60f;

        // PERFECT appears under the K.O. call when the winner took no damage.
        public void DrawPerfect(float age)
        {
            if (age < 0) return;
            Message(ref perfectClip, "Perfect_mc", "Perfect", null, 0).Draw(age, StageToGui);
        }

        void Word(string name, float cx, float cy, float pop, float alpha, float glow) => Draw(Load(name), Load(name + "_Glow"), cx, cy, pop, alpha, glow);

        static void Draw(Texture2D texture, Texture2D glowTexture, float cx, float cy, float pop, float alpha, float glow)
        {
            if (!texture) return;
            var color = GUI.color;
            if (glowTexture && glow > 0)
            {
                float gs = CallScale * pop * Mathf.Lerp(1, 1.08f, 1 - glow);
                GUI.color = new Color(1, 1, 1, glow);
                GUI.DrawTexture(new Rect(cx - glowTexture.width * gs / 2, cy - glowTexture.height * gs / 2, glowTexture.width * gs, glowTexture.height * gs), glowTexture);
            }
            float s = CallScale * pop;
            GUI.color = new Color(1, 1, 1, alpha);
            GUI.DrawTexture(new Rect(cx - texture.width * s / 2, cy - texture.height * s / 2, texture.width * s, texture.height * s), texture);
            GUI.color = color;
        }
    }
}
