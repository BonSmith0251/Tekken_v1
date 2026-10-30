using System;
using System.Collections.Generic;
using UnityEngine;

namespace Versus.Remaster.Arcade
{
    // The cabinet screens drawn with the original game's art: the title movie's arcade operation
    // messages (TIT_OPERATIONMSG_*, left over from the arcade version), the TEKKEN 7 logo and copyright
    // plate, the information bar strip, the character select's slots, tags, name lettering and art,
    // and the HUD timer digits. Textures come from Tools/BattleHud/export_arcade_ui.py and
    // export_character_select.py. "CONTINUE?", "GAME OVER" and the challenger call have no original
    // art in the console data, so they stay text over the original backdrop.
    public sealed partial class ArcadeDirector
    {
        const float SlotWidth = 92, SlotHeight = 142, SlotStep = 100, SlotTop = 470; // as CharacterSelect
        readonly Dictionary<string, Texture2D> uiArt = new Dictionary<string, Texture2D>();
        Texture2D[] digits;
        GUIStyle shadowed;

        Texture2D Ui(string name)
        {
            if (!uiArt.TryGetValue(name, out var texture)) uiArt[name] = texture = Resources.Load<Texture2D>(name);
            return texture;
        }
        Texture2D ArcadeArt(string name) => Ui("BattleHud/Arcade/" + name);
        Texture2D Select(string name) => Ui("CharacterSelect/" + name);

        // The message the original title shows for the current credit state.
        string OperationMessage()
        {
            if (settings.freePlay) return "PressP1orP2StartButton";
            bool one = Ledger.VisibleCredits(0) >= settings.creditsToStart, two = Ledger.VisibleCredits(1) >= settings.creditsToStart;
            if (settings.sharedCredits)
            {
                long credits = Ledger.VisibleCredits(0);
                if (credits >= settings.creditsToStart * 2L) return "PressP1AndorP2StartButton";
                if (one) return "PressP1orP2StartButton";
            }
            else if (one && two) return "PressP1AndorP2StartButton";
            else if (one) return "PressP1StartButton";
            else if (two) return "PressP2StartButton";
            var ledger = Ledger.Snapshot;
            bool paid = Ledger.VisibleCredits(0) > 0 || Ledger.VisibleCredits(1) > 0 || ledger.partialCoins[0] > 0 || ledger.partialCoins[1] > 0;
            return paid ? "InsertMoreCoins" : "InsertCoins";
        }

        // Original blink: on most of the cycle, briefly off.
        static bool BlinkOn => Mathf.Repeat(Time.unscaledTime, 1.2f) < .85f;

        void DrawMessage(string message, float centreY, float scale = 1)
        {
            var texture = ArcadeArt("Msg_" + message);
            if (!texture || !BlinkOn) return;
            float w = texture.width * scale, h = texture.height * scale;
            GUI.DrawTexture(new Rect(640 - w / 2, centreY - h / 2, w, h), texture);
        }

        // Deep blue with the select screen's central light and drifting red/blue glass flares.
        void DrawBackdrop(float alpha = 1)
        {
            var color = GUI.color;
            float time = Time.unscaledTime;
            GUI.color = new Color(.035f, .07f, .13f, alpha); GUI.DrawTexture(new Rect(0, 0, 1280, 720), Texture2D.whiteTexture);
            var light = Select("Light");
            if (light) { GUI.color = new Color(1, 1, 1, (.85f + .1f * Mathf.Sin(time * 1.3f)) * alpha); GUI.DrawTexture(new Rect(0, 0, 1280, 720), light); }
            for (int k = 0; k < 6; k++)
            {
                var flare = Select(k % 2 == 0 ? "FlareRed" : "FlareBlue");
                if (!flare) continue;
                float y = 60 + k * 105 + 18 * Mathf.Sin(time * .4f + k), drift = Mathf.Repeat(time * (30 + k * 7) + k * 260, 1700) - 420;
                GUI.color = new Color(1, 1, 1, .55f * alpha);
                GUI.DrawTexture(new Rect(k % 2 == 0 ? drift : 1280 - drift - 420, y, 420, 24), flare);
            }
            GUI.color = color;
        }

        void DrawTitle()
        {
            // Backdrop: drawn by OnGUI for every full-screen page.
            var logo = ArcadeArt("Logo");
            float pulse = 1 + .015f * Mathf.Sin(Time.unscaledTime * 1.6f);
            if (logo) Centred(logo, new Vector2(640, 215), 1.45f * pulse);
            DrawMessage(OperationMessage(), 455);
            var copyright = ArcadeArt("Copyright");
            if (copyright) Centred(copyright, new Vector2(640, 596), .55f);
        }

        // Large slanted HUD digits (the battle timer's), centred.
        void DrawNumber(int value, Vector2 centre, float scale)
        {
            if (digits == null) { digits = new Texture2D[10]; for (int i = 0; i < 10; i++) digits[i] = Ui("BattleHud/Digit" + i); }
            string text = Math.Max(0, value).ToString();
            float w = 124 * scale, h = 84 * scale, advance = 64 * scale;
            float left = centre.x - advance * (text.Length - 1) / 2;
            for (int i = 0; i < text.Length; i++)
            {
                var texture = digits[text[i] - '0'];
                if (texture) GUI.DrawTexture(new Rect(left + i * advance - w / 2, centre.y - h / 2, w, h), texture);
            }
        }

        void DrawContinue(int remaining)
        {
            Shadowed(new Rect(0, 120, 1280, 90), "CONTINUE?", 64);
            DrawNumber(remaining, new Vector2(640, 330), 2.2f);
            int side = Flow.ContinueSide;
            bool affordable = settings.freePlay || Ledger.VisibleCredits(side) >= settings.creditsToContinue;
            DrawMessage(affordable ? (side == 0 ? "PressP1StartButton" : "PressP2StartButton") : OperationMessage(), 500);
        }

        void DrawArcadeSelection(int remaining)
        {
            // Backdrop: drawn by OnGUI for every full-screen page.
            var color = GUI.color;
            bool two = Flow.TwoPlayers;
            for (int side = 1; side >= 0; side--)
            {
                bool human = two || side == Flow.HumanSide;
                if (!human) continue;
                var art = Select((side == 0 ? "Art_L_" : "Art_R_") + Code(Flow.Fighter(side)));
                if (!art) continue;
                float w = art.width * 2 / 3f, h = art.height * 2 / 3f;
                GUI.DrawTexture(new Rect(side == 0 ? 0 : 1280 - w, 0, w, h), art);
            }
            for (int side = 0; side < 2; side++)
            {
                float cx = side == 0 ? 347 : 933;
                bool human = two || side == Flow.HumanSide;
                if (human)
                {
                    var name = Select("Name_" + Code(Flow.Fighter(side)));
                    if (name) GUI.DrawTexture(new Rect(cx - 230, 344, 460, 56), name);
                    if (two && Flow.SelectionConfirmed(side)) Shadowed(new Rect(cx - 230, 300, 460, 40), "READY", 26);
                }
                else
                {
                    // The ladder chooses the CPU opponent after the pick: a mystery panel with the CPU tag.
                    var random = Select("Random"); var cpu = Select("TagCpu");
                    if (random) GUI.DrawTexture(new Rect(cx - 60, 200, 120, 120), random);
                    if (cpu) GUI.DrawTexture(new Rect(cx - 63, 344, 126, 36), cpu);
                }
            }
            DrawSlots(two);
            DrawNumber(remaining, new Vector2(640, 70), .62f);
            GUI.color = color;
            Shadowed(new Rect(0, 640, 1280, 26), two ? SelectionHelp(0) + "        " + SelectionHelp(1) : SelectionHelp(Flow.HumanSide), 16);
        }

        void DrawSlots(bool two)
        {
            float left = 640 - (Fighters.Length * SlotStep - (SlotStep - SlotWidth)) / 2;
            var color = GUI.color;
            int a = two || Flow.HumanSide == 0 ? Flow.SelectionCursor(0) : -1, b = two || Flow.HumanSide == 1 ? Flow.SelectionCursor(1) : -1;
            for (int i = 0; i < Fighters.Length; i++)
            {
                var rect = new Rect(left + i * SlotStep, SlotTop, SlotWidth, SlotHeight);
                bool one = a == i, other = b == i;
                bool lockedOne = one && two && Flow.SelectionConfirmed(0), lockedOther = other && two && Flow.SelectionConfirmed(1);
                string frame = one && other ? (lockedOne || lockedOther ? "SlotPurple" : "SlotPurpleHover") : one ? (lockedOne ? "SlotRed" : "SlotRedHover") :
                    other ? (lockedOther ? "SlotBlue" : "SlotBlueHover") : "SlotNormal";
                var slot = Select(frame); if (slot) GUI.DrawTexture(Grow(rect, 6), slot);
                var panel = Select("Panel_" + Codes[i]); if (panel) GUI.DrawTexture(Grow(rect, -3), panel);
                if (!one && !other) { GUI.color = new Color(0, 0, 0, .35f); GUI.DrawTexture(Grow(rect, -3), Texture2D.whiteTexture); GUI.color = color; }
                var tag1 = Select("Tag1"); var tag2 = Select("Tag2");
                if (one && tag1) GUI.DrawTexture(new Rect(rect.x - 2, rect.y - 30, 24, 24), tag1);
                if (other && tag2) GUI.DrawTexture(new Rect(rect.xMax - 30, rect.y - 30, 32, 24), tag2);
            }
        }

        // Credits on the information bar strip; during play only the text, as on the cabinet.
        void DrawCreditBar(ArcadeFlowState state)
        {
            bool playing = state == ArcadeFlowState.Fight || state == ArcadeFlowState.Victory || state == ArcadeFlowState.Demo;
            string credits = settings.freePlay ? "FREE PLAY" : settings.sharedCredits ? "CREDIT " + Ledger.VisibleCredits(0) :
                "CREDIT " + Ledger.VisibleCredits(0) + "      CREDIT " + Ledger.VisibleCredits(1);
            var bar = ArcadeArt("InfoBar");
            if (!playing && bar) GUI.DrawTexture(new Rect(0, 690, 1280, 30), bar);
            Shadowed(new Rect(0, 690, 1280, 30), credits, 18);
            if (state == ArcadeFlowState.Fight && !Flow.TwoPlayers && settings.joinIn && Flow.HumanSide >= 0)
            {
                // A waiting challenger sees the other side's start prompt, as on the cabinet.
                int other = 1 - Flow.HumanSide;
                if (settings.freePlay || Ledger.VisibleCredits(other) >= settings.creditsToChallenge)
                    DrawMessage(other == 0 ? "PressP1StartButton" : "PressP2StartButton", 664, .5f);
            }
            if (Application.isEditor)
                GUI.Label(new Rect(10, 4, 1260, 20), (playing && Flow.HumanSide >= 0 ? (Flow.TwoPlayers ? "PLAYER VS PLAYER" : "STAGE " + (Flow.StageIndex + 1) + " / " + Flow.StageCount) + "   |   " : "") +
                    "KEYBOARD TEST   COIN " + bindings.Label(ArcadeControls.Coin1) + " / " + bindings.Label(ArcadeControls.Coin2) + "   START " +
                    bindings.Label(ArcadeControls.Start1) + " / " + bindings.Label(ArcadeControls.Start2) + "   SERVICE " + bindings.Label(ArcadeControls.Service) +
                    "   F2 OPERATOR   STOP PLAY TO EXIT", hint);
        }

        GUIStyle hint => shadowed ?? (shadowed = MakeHint());
        GUIStyle MakeHint()
        {
            var style = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleLeft };
            style.normal.textColor = new Color(.85f, .88f, .92f, .8f);
            return style;
        }

        // White bold text with a soft dark drop shadow, matching the operation messages' lettering.
        void Shadowed(Rect rect, string value, int size)
        {
            var style = new GUIStyle(title) { fontSize = size };
            var old = style.normal.textColor;
            style.normal.textColor = new Color(0, 0, 0, .75f);
            GUI.Label(new Rect(rect.x + 2, rect.y + 3, rect.width, rect.height), value, style);
            style.normal.textColor = old;
            GUI.Label(rect, value, style);
        }

        static void Centred(Texture2D texture, Vector2 centre, float scale)
        {
            float w = texture.width * scale, h = texture.height * scale;
            GUI.DrawTexture(new Rect(centre.x - w / 2, centre.y - h / 2, w, h), texture);
        }
        static Rect Grow(Rect r, float by) => new Rect(r.x - by, r.y - by, r.width + by * 2, r.height + by * 2);
    }
}
