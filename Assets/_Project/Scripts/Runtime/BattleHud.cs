using UnityEngine;

namespace Versus.Remaster
{
    // Tekken 7's in-fight HUD rebuilt from its original Scaleform textures (Resources/BattleHud,
    // exported by Tools/BattleHud/export_battle_hud.py). The GFx timelines are compiled, so element
    // placement was measured from gameplay captures at 1440x810 and is drawn here in the versus
    // scene's 1280x720 GUI space. Gauges drain from the outer end toward the timer, keep a red
    // trail of recent damage, flash their frame on a hit and switch to the red frame in Rage.
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
        // Measured at 15 fps: the red trail holds ~0.6 s and drains in ~0.45 s; the gold frame burns 0.2 s, fades 0.1 s.
        const float TrailHold = .6f, TrailRate = .4f, FlashSeconds = .3f, FlashFade = .1f;

        readonly Texture2D frameNormal, frameRage, frameDamage, fill, damage, hit, pipOff, pipOn, pipFlash;
        readonly Texture2D[] digits = new Texture2D[10], roundNumbers = new Texture2D[10], roundGlows = new Texture2D[10];
        readonly Texture2D[] icons = new Texture2D[2], names = new Texture2D[2], plates = new Texture2D[2], thunder = new Texture2D[2];
        readonly float[] trail = { 1, 1 }, hold = new float[2], flash = new float[2], shown = { 1, 1 };
        readonly int[] lastWins = new int[2];
        readonly float[] pipAge = { 9, 9 };

        public BattleHud(string playerOneCode, string playerTwoCode)
        {
            frameNormal = Load("FrameNormal"); frameRage = Load("FrameRage"); frameDamage = Load("FrameDamage");
            fill = Load("LifeFill"); damage = Load("LifeDamage"); hit = Load("LifeHit");
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

        // Call once per rendered frame before drawing, with unscaled time so KO slow motion doesn't stall the trail.
        public void Advance(BattleModel battle, float deltaTime)
        {
            for (int i = 0; i < 2; i++)
            {
                var f = battle.fighters[i];
                float health = Mathf.Clamp01(f.health / (float)battle.rules.health);
                if (health < shown[i] - 1e-4f) { hold[i] = TrailHold; flash[i] = FlashSeconds; }
                if (health > shown[i] + 1e-4f || health >= trail[i]) trail[i] = health; // round reset or refill
                shown[i] = health;
                if ((hold[i] -= deltaTime) <= 0) trail[i] = Mathf.MoveTowards(trail[i], health, TrailRate * deltaTime);
                flash[i] = Mathf.Max(0, flash[i] - deltaTime);
                if (f.wins != lastWins[i]) { pipAge[i] = f.wins > lastWins[i] ? 0 : 9; lastWins[i] = f.wins; }
                pipAge[i] += deltaTime;
            }
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

        void DrawGauge(int side, bool rage)
        {
            var frameRect = new Rect(FrameOrigin.x, FrameOrigin.y, 796 * GaugeScale, 124 * GaugeScale);
            Mirrored(frameRect, 0, 1, rage ? frameRage : frameNormal, side);
            // The fill is anchored at the timer end: health h shows the inner h of its span.
            DrawFill(damage, trail[side], shown[side], side);
            DrawFill(fill, shown[side], 0, side);
            if (flash[side] <= 0) return;
            // On a hit the frame burns gold for 0.2 s, then fades over 0.1 s.
            var color = GUI.color;
            GUI.color = new Color(1, 1, 1, Mathf.Clamp01(flash[side] / FlashFade));
            Mirrored(frameRect, 0, 1, frameDamage, side);
            if (hit && shown[side] > 0)
            {
                float edge = FillOrigin.x + CutX(shown[side]) * GaugeScale;
                Mirrored(new Rect(edge - 42 * GaugeScale, FillOrigin.y - 82 * GaugeScale, 84 * GaugeScale, 240 * GaugeScale), 0, 1, hit, side);
            }
            GUI.color = color;
        }

        // Fill texture x (pixels, on its middle row) where health h ends; the cut leans with the gauge's ends.
        static float CutX(float h) => FillMid1 - h * (FillMid1 - FillMid0);

        // Draws the part of a fill texture between health fractions `outer` and `inner`, with cuts parallel to
        // the gauge's slanted ends. IMGUI only cuts rectangles, so the band is drawn as thin horizontal strips,
        // each cut at its own row's offset (a strip is about one screen pixel tall).
        void DrawFill(Texture2D texture, float outer, float inner, int side)
        {
            if (!texture || outer <= inner) return;
            float xo = outer >= 1 ? -60 : CutX(outer), xi = inner <= 0 ? 812 : CutX(inner); // full ends keep their tips
            for (float y = FillBandTop; y < FillBandBottom; y += FillStrip)
            {
                float shift = (y + FillStrip / 2 - FillMidRow) * FillSlant;
                float x0 = Mathf.Clamp(xo + shift, 0, FillWidth), x1 = Mathf.Clamp(xi + shift, 0, FillWidth);
                if (x1 <= x0) continue;
                var strip = new Rect(FillOrigin.x + x0 * GaugeScale, FillOrigin.y + y * GaugeScale, (x1 - x0) * GaugeScale, FillStrip * GaugeScale);
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
                case Call.Round:
                    if (finalRound) { Word("Call_Final", 640, 300, pop, alpha, glow); Word("Call_Round", 640, 400, pop, alpha, glow); }
                    else
                    {
                        var word = Load("Call_Round"); var number = roundNumbers[Mathf.Clamp(round, 0, 9)];
                        float gap = 24, total = word.width + gap + number.width, left = 640 / CallScale - total / 2;
                        Word("Call_Round", (left + word.width / 2) * CallScale, 345, pop, alpha, glow);
                        Draw(number, roundGlows[Mathf.Clamp(round, 0, 9)], (left + word.width + gap + number.width / 2) * CallScale, 345, pop, alpha, glow);
                    }
                    break;
                case Call.Ready: Word("Call_Ready", 640, 345, pop, alpha, glow); break;
                case Call.Fight: Word("Call_Fight", 640, 345, pop, alpha, glow); break;
                case Call.KO: Word("Call_KO", 640, 345, pop, alpha, glow); break;
                case Call.DoubleKO: Word("Call_Double", 640, 280, pop, alpha, glow); Word("Call_KO", 640, 400, pop, alpha, glow); break;
                case Call.TimeUp: Word("Call_TimeUp", 640, 345, pop, alpha, glow); break;
                case Call.Draw: Word("Call_Draw", 640, 345, pop, alpha, glow); break;
            }
        }

        // PERFECT appears under the K.O. call when the winner took no damage.
        public void DrawPerfect(float age)
        {
            if (age < 0) return;
            Word("Call_Perfect", 640, 470, Mathf.SmoothStep(1.25f, 1, Mathf.Clamp01(age / .15f)), Mathf.Clamp01(age / .08f), Mathf.Clamp01(1 - age / .6f));
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
