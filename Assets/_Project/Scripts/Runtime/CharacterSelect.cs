using System;
using UnityEngine;

namespace Versus.Remaster
{
    // Tekken 7's versus character select, rebuilt from SW_UI_CharacterSelect and the ReplaceImages
    // character art (Resources/CharacterSelect, exported by Tools/BattleHud/export_character_select.py).
    // VersusGame shows it before spawning anything; once both players confirm, the picks are kept here
    // and the scene reloads so VersusGame builds the match with them. Layout follows the original
    // (large art for each side, name lettering, a row of portrait slots with the random slot in the
    // middle); the animated shard background is a movie in the original and is approximated.
    public sealed class CharacterSelect : MonoBehaviour
    {
        // The imported, versus-ready fighters in slot order; null is the random (?) slot.
        static readonly (VersusGame.Fighter fighter, string code)?[] Roster = {
            (VersusGame.Fighter.Jin, "JIN"), (VersusGame.Fighter.Kazuya, "KAZ"), (VersusGame.Fighter.Xiaoyu, "XIA"), null,
            (VersusGame.Fighter.Alisa, "ASA"), (VersusGame.Fighter.Katarina, "LTN") };

        public static bool Chosen { get; private set; }
        public static VersusGame.Fighter PlayerOne { get; private set; }
        public static VersusGame.Fighter PlayerTwo { get; private set; }

        // Enter Play Mode without a domain reload keeps statics; always start at the select screen.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetSelection() => Chosen = false;

        // Back to the select screen from a match (the scene reloads without its fighters).
        public static void Return()
        {
            Chosen = false;
            ReloadScene();
        }

        static void ReloadScene()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
#if UNITY_EDITOR
            // LocalVersus2 is not in the build settings, so the editor loads it by path.
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(scene.path,
                new UnityEngine.SceneManagement.LoadSceneParameters(UnityEngine.SceneManagement.LoadSceneMode.Single));
#else
            UnityEngine.SceneManagement.SceneManager.LoadScene(scene.buildIndex);
#endif
        }

        const float SlotWidth = 92, SlotHeight = 142, SlotStep = 100, SlotTop = 470;
        const float ConfirmDelay = 1.1f;
        readonly int[] cursor = { 0, Roster.Length - 1 };
        readonly bool[] locked = new bool[2];
        readonly int[] picked = new int[2];
        readonly float[] lockTime = new float[2];
        float doneTime = -1, time;
        readonly System.Random random = new System.Random();
        Texture2D light, flashLight, flareRed, flareBlue, randomNormal, tag1, tag2;
        Texture2D slotNormal, slotRed, slotBlue, slotPurple, hoverRed, hoverBlue, hoverPurple;
        Texture2D[] panels, artLeft, artRight, names;
        GUIStyle hint;

        // P1 / P2 keys, as in the fight: move left/right; button 1 (or Enter / pad A) confirms; button 2 (or pad B) goes back.
        static readonly KeyCode[][] Left = { new[] { KeyCode.A }, new[] { KeyCode.LeftArrow } };
        static readonly KeyCode[][] Right = { new[] { KeyCode.D }, new[] { KeyCode.RightArrow } };
        static readonly KeyCode[][] Confirm = { new[] { KeyCode.U, KeyCode.Return, KeyCode.Joystick1Button0 },
            new[] { KeyCode.Keypad4, KeyCode.O, KeyCode.KeypadEnter, KeyCode.Joystick2Button0 } };
        static readonly KeyCode[][] Back = { new[] { KeyCode.I, KeyCode.Joystick1Button1 },
            new[] { KeyCode.Keypad5, KeyCode.P, KeyCode.Joystick2Button1 } };

        static Texture2D Load(string name) => Resources.Load<Texture2D>("CharacterSelect/" + name);

        void Awake()
        {
            light = Load("Light"); flashLight = Load("FlashLight"); flareRed = Load("FlareRed"); flareBlue = Load("FlareBlue");
            randomNormal = Load("Random"); tag1 = Load("Tag1"); tag2 = Load("Tag2");
            slotNormal = Load("SlotNormal"); slotRed = Load("SlotRed"); slotBlue = Load("SlotBlue"); slotPurple = Load("SlotPurple");
            hoverRed = Load("SlotRedHover"); hoverBlue = Load("SlotBlueHover"); hoverPurple = Load("SlotPurpleHover");
            panels = new Texture2D[Roster.Length]; artLeft = new Texture2D[Roster.Length]; artRight = new Texture2D[Roster.Length]; names = new Texture2D[Roster.Length];
            for (int i = 0; i < Roster.Length; i++)
            {
                if (Roster[i] == null) continue;
                string code = Roster[i].Value.code;
                panels[i] = Load("Panel_" + code); artLeft[i] = Load("Art_L_" + code); artRight[i] = Load("Art_R_" + code); names[i] = Load("Name_" + code);
            }
        }

        static bool Pressed(KeyCode[] keys) => Array.Exists(keys, Input.GetKeyDown);

        void Update()
        {
            time += Time.unscaledDeltaTime;
            if (doneTime >= 0)
            {
                if (time - doneTime >= ConfirmDelay)
                {
                    PlayerOne = Roster[picked[0]].Value.fighter; PlayerTwo = Roster[picked[1]].Value.fighter;
                    Chosen = true;
                    // The VS screen covers the reload and the fighters' setup.
                    VersusLoadingScreen.Show(Roster[picked[0]].Value.code, Roster[picked[1]].Value.code);
                    ReloadScene();
                    enabled = false;
                }
                return;
            }
            for (int p = 0; p < 2; p++)
            {
                if (locked[p])
                {
                    if (Pressed(Back[p])) locked[p] = false;
                    continue;
                }
                if (Pressed(Left[p])) cursor[p] = (cursor[p] + Roster.Length - 1) % Roster.Length;
                if (Pressed(Right[p])) cursor[p] = (cursor[p] + 1) % Roster.Length;
                if (Pressed(Confirm[p]))
                {
                    // The random slot resolves to a fighter on confirm.
                    int pick = cursor[p];
                    while (Roster[pick] == null) pick = random.Next(Roster.Length);
                    picked[p] = pick; locked[p] = true; lockTime[p] = time;
                }
            }
            if (locked[0] && locked[1]) doneTime = time;
        }

        void OnGUI()
        {
            if (hint == null)
            {
                hint = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleLeft };
                hint.normal.textColor = new Color(.85f, .88f, .92f);
            }
            // Letterbox to 16:9 and draw in 1280x720, as the versus HUD does.
            float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
            float ox = (Screen.width - 1280 * scale) / 2, oy = (Screen.height - 720 * scale) / 2;
            var color = GUI.color;
            GUI.color = Color.black; GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture); GUI.color = color;
            var previous = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(new Vector3(ox, oy, 0), Quaternion.identity, new Vector3(scale, scale, 1));
            try { Draw(); }
            finally { GUI.matrix = previous; GUI.color = color; }
        }

        void Draw()
        {
            var color = GUI.color;
            // Background: deep blue with the screen's central light and red/blue glass flares.
            GUI.color = new Color(.035f, .07f, .13f); GUI.DrawTexture(new Rect(0, 0, 1280, 720), Texture2D.whiteTexture);
            GUI.color = new Color(1, 1, 1, .85f + .1f * Mathf.Sin(time * 1.3f)); GUI.DrawTexture(new Rect(0, 0, 1280, 720), light);
            for (int k = 0; k < 6; k++)
            {
                float y = 60 + k * 105 + 18 * Mathf.Sin(time * .4f + k), drift = Mathf.Repeat(time * (30 + k * 7) + k * 260, 1700) - 420;
                GUI.color = new Color(1, 1, 1, .55f);
                GUI.DrawTexture(new Rect(k % 2 == 0 ? drift : 1280 - drift - 420, y, 420, 24), k % 2 == 0 ? flareRed : flareBlue);
            }
            GUI.color = color;
            // Each player's art fills their side (the art is authored for 1920x1080, anchored left/right).
            for (int p = 1; p >= 0; p--)
            {
                int index = locked[p] ? picked[p] : cursor[p];
                var art = p == 0 ? artLeft[index] : artRight[index];
                if (!art) continue;
                float w = art.width * 2 / 3f, h = art.height * 2 / 3f;
                float pop = locked[p] ? Mathf.SmoothStep(1.04f, 1, Mathf.Clamp01((time - lockTime[p]) / .2f)) : 1;
                var rect = new Rect(p == 0 ? 0 : 1280 - w, 0, w, h);
                rect = new Rect(rect.center.x - rect.width * pop / 2, rect.y, rect.width * pop, rect.height * pop);
                GUI.DrawTexture(rect, art);
                if (locked[p] && time - lockTime[p] < .35f)
                {
                    // Confirm flash over the chosen fighter.
                    GUI.color = new Color(1, 1, 1, 1 - (time - lockTime[p]) / .35f);
                    GUI.DrawTexture(new Rect(p == 0 ? 60 : 660, 120, 560, 300), flashLight);
                    GUI.color = color;
                }
            }
            for (int p = 0; p < 2; p++)
            {
                int index = locked[p] ? picked[p] : cursor[p];
                var name = names[index];
                float cx = p == 0 ? 347 : 933;
                if (name) GUI.DrawTexture(new Rect(cx - 230, 344, 460, 56), name);
                else if (Roster[index] == null) GUI.DrawTexture(new Rect(cx - 28, 344, 56, 56), randomNormal);
            }
            DrawSlots();
            GUI.Label(new Rect(40, 676, 600, 26), "P1   A / D  select     U  confirm     I  back", hint);
            GUI.Label(new Rect(700, 676, 560, 26), "P2   ← / →  select     NUM 4  confirm     NUM 5  back", hint);
        }

        void DrawSlots()
        {
            float left = 640 - (Roster.Length * SlotStep - (SlotStep - SlotWidth)) / 2;
            var color = GUI.color;
            for (int i = 0; i < Roster.Length; i++)
            {
                var rect = new Rect(left + i * SlotStep, SlotTop, SlotWidth, SlotHeight);
                bool one = (locked[0] ? picked[0] : cursor[0]) == i, two = (locked[1] ? picked[1] : cursor[1]) == i;
                bool lockedHere = (one && locked[0]) || (two && locked[1]);
                GUI.DrawTexture(Grow(rect, 6), one && two ? (lockedHere ? slotPurple : hoverPurple) : one ? (locked[0] ? slotRed : hoverRed) :
                    two ? (locked[1] ? slotBlue : hoverBlue) : slotNormal);
                if (panels[i]) GUI.DrawTexture(Grow(rect, -3), panels[i]);
                else GUI.DrawTexture(new Rect(rect.center.x - 30, rect.center.y - 30, 60, 60), randomNormal);
                if (!one && !two)
                {
                    // Slots nobody points at sit back slightly.
                    GUI.color = new Color(0, 0, 0, .35f); GUI.DrawTexture(Grow(rect, -3), Texture2D.whiteTexture); GUI.color = color;
                }
                // Player tags above the slot: 1 left, 2 right.
                if (one) GUI.DrawTexture(new Rect(rect.x - 2, rect.y - 30, 24, 24), tag1);
                if (two) GUI.DrawTexture(new Rect(rect.xMax - 30, rect.y - 30, 32, 24), tag2);
            }
        }

        static Rect Grow(Rect r, float by) => new Rect(r.x - by, r.y - by, r.width + by * 2, r.height + by * 2);
    }
}
