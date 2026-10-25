using UnityEngine;

namespace Versus.Remaster
{
    // Tekken 7's VS screen (SW_UI_Makuai), shown between the character select and the entrances while
    // the match scene reloads and VersusGame builds the fighters. It survives the scene load, holds the
    // match (Holding) until VersusGame reports it is ready and a minimum time has passed, then closes
    // with the lightning sweep and a flash into the first entrance shot. Layout is measured from a
    // capture at 1440x810; the original's fire-burst flipbook is additive and is not reproduced.
    public sealed class VersusLoadingScreen : MonoBehaviour
    {
        static VersusLoadingScreen current;
        // VersusGame waits while this is true: no entrance or battle ticks behind the screen.
        public static bool Holding => current && !current.revealing;

        const float MinSeconds = 3.2f, IntroSeconds = .5f, SweepSeconds = .35f, BlackSeconds = .25f, RevealSeconds = .4f, GiveUpSeconds = 20;
        // Measured layout in the 1280x720 GUI space: panel centres, panel scale, VS centre.
        const float PanelScale = .66f;
        static readonly Vector2 LeftCentre = new Vector2(366, 348), RightCentre = new Vector2(911, 342), VsCentre = new Vector2(653, 351);

        Texture2D[] art = new Texture2D[2];
        Texture2D baseL, baseR, frameL, frameR, logo, glow, lightning, lightningFlare, flareRed, flareBlue, warm;
        float time, readyTime = -1, outroTime = -1;
        bool revealing;

        // Called by the character select as it reloads the scene with the picks.
        public static void Show(string playerOneCode, string playerTwoCode)
        {
            if (current) Destroy(current.gameObject);
            var host = new GameObject("VS screen");
            DontDestroyOnLoad(host);
            current = host.AddComponent<VersusLoadingScreen>();
            current.art[0] = Load("VsArt_L_" + playerOneCode);
            current.art[1] = Load("VsArt_R_" + playerTwoCode);
        }

        // Called by VersusGame once the fighters are built.
        public static void MatchReady()
        {
            if (current && current.readyTime < 0) current.readyTime = current.time;
        }

        static Texture2D Load(string name) => Resources.Load<Texture2D>("CharacterSelect/" + name);

        void Awake()
        {
            baseL = Load("VsBaseL"); baseR = Load("VsBaseR"); frameL = Load("VsFrameL"); frameR = Load("VsFrameR");
            logo = Load("VsLogo"); glow = Load("VsGlow"); lightning = Load("VsLightning"); lightningFlare = Load("VsLightningFlare");
            flareRed = Load("FlareRed"); flareBlue = Load("FlareBlue"); warm = Load("FlareRedVolume");
        }

        void Update()
        {
            // Scene loading can stall a frame for seconds; don't let that skip the intro.
            time += Mathf.Min(Time.unscaledDeltaTime, .1f);
            bool ready = readyTime >= 0 || time > GiveUpSeconds;
            // One extra second after setup lets the fighters' first poses and the lighting settle.
            if (outroTime < 0 && ready && time >= MinSeconds && (readyTime < 0 || time >= readyTime + 1)) outroTime = time;
            if (outroTime >= 0)
            {
                float t = time - outroTime;
                if (t >= SweepSeconds + BlackSeconds) revealing = true;
                if (t >= SweepSeconds + BlackSeconds + RevealSeconds) { if (current == this) current = null; Destroy(gameObject); }
            }
        }

        void OnDestroy() { if (current == this) current = null; }

        void OnGUI()
        {
            GUI.depth = -100; // over the versus HUD and entrance captions
            if (Event.current.type != EventType.Repaint) return;
            var color = GUI.color;
            float outro = outroTime < 0 ? -1 : time - outroTime;
            if (revealing)
            {
                // Black lifts off the first entrance shot.
                GUI.color = new Color(0, 0, 0, 1 - Mathf.Clamp01((outro - SweepSeconds - BlackSeconds) / RevealSeconds));
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
                GUI.color = color;
                return;
            }
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = color;
            float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
            var previous = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - 1280 * scale) / 2, (Screen.height - 720 * scale) / 2, 0), Quaternion.identity, new Vector3(scale, scale, 1));
            try { Draw(outro); }
            finally { GUI.matrix = previous; GUI.color = color; }
        }

        void Draw(float outro)
        {
            var color = GUI.color;
            float intro = Mathf.SmoothStep(0, 1, Mathf.Clamp01(time / IntroSeconds));
            // Background: near black with a warm glow from below and drifting red/blue glass flares.
            GUI.color = new Color(.02f, .025f, .035f); GUI.DrawTexture(new Rect(0, 0, 1280, 720), Texture2D.whiteTexture);
            GUI.color = new Color(1, .55f, .35f, .55f * intro); GUI.DrawTexture(new Rect(-200, 420, 1680, 520), warm);
            for (int k = 0; k < 4; k++)
            {
                float drift = Mathf.Repeat(time * (40 + k * 9) + k * 300, 1700) - 420;
                GUI.color = new Color(1, 1, 1, .45f * intro);
                GUI.DrawTexture(new Rect(k % 2 == 0 ? drift : 1280 - drift - 420, 90 + k * 160, 420, 24), k % 2 == 0 ? flareRed : flareBlue);
            }
            // The two panels slide in from their sides, art inside, framed red (P1) and blue (P2).
            float slide = (1 - intro) * 70;
            Panel(LeftCentre + new Vector2(-slide, 0), baseL, art[0], frameL, intro);
            Panel(RightCentre + new Vector2(slide, 0), baseR, art[1], frameR, intro);
            // VS pops in a little after the panels, with its glow.
            float vsTime = Mathf.Clamp01((time - .25f) / .25f);
            float pop = Mathf.Lerp(1.5f, 1, Mathf.SmoothStep(0, 1, vsTime));
            GUI.color = new Color(1, 1, 1, vsTime * (.6f + .25f * Mathf.Sin(time * 3)));
            Centred(glow, VsCentre, PanelScale * pop * 1.02f);
            GUI.color = new Color(1, 1, 1, vsTime);
            Centred(logo, VsCentre, PanelScale * pop);
            if (outro >= 0)
            {
                // Close: lightning sweeps across the VS, then the screen flashes and cuts to black.
                float sweep = Mathf.Clamp01(outro / SweepSeconds);
                GUI.color = new Color(1, 1, 1, Mathf.Sin(sweep * Mathf.PI));
                GUI.DrawTexture(new Rect(-200 + sweep * 200, VsCentre.y - 120, 1680, 240), lightning);
                Centred(lightningFlare, VsCentre, 1.6f);
                GUI.color = new Color(1, 1, 1, Mathf.Clamp01((outro - SweepSeconds * .6f) / (SweepSeconds * .4f)) * .9f);
                GUI.DrawTexture(new Rect(0, 0, 1280, 720), Texture2D.whiteTexture);
                GUI.color = new Color(0, 0, 0, Mathf.Clamp01((outro - SweepSeconds) / BlackSeconds));
                GUI.DrawTexture(new Rect(0, 0, 1280, 720), Texture2D.whiteTexture);
            }
            GUI.color = color;
        }

        static void Panel(Vector2 centre, Texture2D backing, Texture2D picture, Texture2D frame, float alpha)
        {
            var color = GUI.color;
            GUI.color = new Color(1, 1, 1, alpha);
            Centred(backing, centre, PanelScale);
            Centred(picture, centre, PanelScale);
            Centred(frame, centre, PanelScale);
            GUI.color = color;
        }

        static void Centred(Texture2D texture, Vector2 centre, float scale)
        {
            if (!texture) return;
            float w = texture.width * scale, h = texture.height * scale;
            GUI.DrawTexture(new Rect(centre.x - w / 2, centre.y - h / 2, w, h), texture);
        }
    }
}
