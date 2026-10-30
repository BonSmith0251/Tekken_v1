using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Versus.Remaster.Arcade
{
    public sealed partial class ArcadeDirector
    {
        enum OperatorPage { Home, Coins, Game, Input, Bookkeeping, Diagnostics, Confirm, Rankings }
        enum OperatorAction { Apply, ClearBookkeeping, ClearCredits, ResetBindings, ClearRankings }
        public bool OperatorOpen { get; private set; }
        public ArcadeBindings Bindings => bindings.Copy();
        public ArcadeSettings CurrentSettings => settings.Copy();
        ArcadeBindings bindings;
        ArcadeSettings draft;
        OperatorPage operatorPage, returnPage;
        OperatorAction operatorAction;
        int operatorRow, returnRow;
        string operatorMessage = "";
        ArcadeControls captureControl;
        bool captureArmed, operatorReleaseRequired;
        string[] priorTestKeys = Array.Empty<string>();
        bool RawHeld(KeyCode key)
        {
#if UNITY_EDITOR
            if (AutomatedTest) return TestKeys != null && TestKeys.Contains(key.ToString());
#endif
            return Input.GetKey(key);
        }
        bool RawPressed(KeyCode key)
        {
#if UNITY_EDITOR
            if (AutomatedTest) return RawHeld(key) && !priorTestKeys.Contains(key.ToString());
#endif
            return Input.GetKeyDown(key);
        }
        void RememberRawKeys()
        {
#if UNITY_EDITOR
            if (AutomatedTest) priorTestKeys = TestKeys == null ? Array.Empty<string>() : (string[])TestKeys.Clone();
#endif
        }

        static readonly string[] CoinFields = { "freePlay", "sharedCredits", "coinsPerCredit1", "coinsPerCredit2", "creditsToStart", "creditsToContinue", "creditsToChallenge", "maxCredits", "coinLockoutMs", "coinJamMs" };
        static readonly string[] CoinLabels = { "Free play", "Shared credits", "Coins per credit - chute 1", "Coins per credit - chute 2", "Start price", "Continue price", "Challenge price", "Visible credit cap", "Coin debounce (ms)", "Coin jam threshold (ms)" };
        static readonly string[] GameFields = { "difficulty", "roundSeconds", "roundsToWin", "bossRoundsToWin", "ladderStages", "boss", "selectSeconds", "continueSeconds", "joinIn", "continueReselect", "continueAcceleration", "victoryMaxSeconds", "loadTimeoutSeconds", "nameEntrySeconds" };
        static readonly string[] GameLabels = { "CPU difficulty", "Round seconds", "Rounds to win", "Boss rounds to win", "Ladder stages", "Boss fighter", "Select seconds", "Continue seconds", "Allow challengers", "Reselect on continue", "Accelerate continue", "Victory timeout", "Loading timeout", "Name entry seconds" };
        static readonly KeyCode[] AllKeys = ((KeyCode[])Enum.GetValues(typeof(KeyCode))).Distinct().Where(k => ValidKey(k.ToString())).ToArray();
        static bool ValidKey(string name) => Enum.TryParse(name, out KeyCode key) && Enum.IsDefined(typeof(KeyCode), key) &&
            key != KeyCode.None && key != KeyCode.Escape && !name.StartsWith("Mouse", StringComparison.Ordinal);
        string BindingPath => Path.Combine(DataDirectory, "arcade-input.json");
        string SelectionHelp(int side) => bindings.Label(side == 0 ? ArcadeControls.P1Left : ArcadeControls.P2Left) + " / " +
            bindings.Label(side == 0 ? ArcadeControls.P1Right : ArcadeControls.P2Right) + " SELECT   |   " +
            bindings.Label(side == 0 ? ArcadeControls.P1B1 : ArcadeControls.P2B1) + " CONFIRM";

        void LoadBindings()
        {
            using (var file = new ArcadeAtomicFile<ArcadeBindings>(BindingPath, ValidateKeys))
            { bindings = file.Load(); if (bindings == null) { bindings = ArcadeBindings.Defaults(); file.Save(bindings); } }
        }
        static void ValidateKeys(ArcadeBindings value)
        {
            value.Validate();
            foreach (var row in value.keys)
                if (!ValidKey(row.key) || row.alternate != null && !ValidKey(row.alternate)) throw new ArgumentException("Unknown or unsupported input key.");
        }
        public void SaveBindings(ArcadeBindings value)
        {
            if (!OperatorOpen) throw new InvalidOperationException("Open the operator menu first.");
            ValidateKeys(value);
            using (var file = new ArcadeAtomicFile<ArcadeBindings>(BindingPath, ValidateKeys)) file.Save(value);
            bindings = value.Copy(); CabinetInput.RebaseHeldControls(bindings.Read(key => RawHeld((KeyCode)Enum.Parse(typeof(KeyCode), key))));
            operatorReleaseRequired = true;
        }
        void ToggleOperator(double now)
        {
            if (OperatorOpen) { CloseOperator(now); return; }
            OperatorOpen = true; draft = settings.Copy(); operatorPage = OperatorPage.Home; operatorRow = 0;
            captureControl = 0; operatorMessage = "";
            Flow.SetOperatorPaused(true, now); CabinetInput.SetOperatorPaused(true);
        }
        void CloseOperator(double now)
        {
            captureControl = 0; OperatorOpen = false;
            Flow.SetOperatorPaused(false, now); CabinetInput.SetOperatorPaused(false);
        }
        void ApplyOperatorSettings(ArcadeSettings value, double now)
        {
            if (!OperatorOpen || Flow.HumanSide >= 0) throw new InvalidOperationException("Return to the idle title before applying game or coin settings.");
            if (!Fighters.Contains(value.boss)) throw new ArgumentException("The boss must be in the imported roster.");
            Ledger.ApplyOperatorSettings(value); settings = Ledger.Settings; CabinetInput.ApplySettings(settings);
            Flow = new ArcadeFlow(settings, Ledger, Fighters, stagePool, Environment.TickCount, now, Rankings);
            Flow.SetOperatorPaused(true, now); previousState = Flow.State; draft = settings.Copy();
        }
        void UpdateOperator(ArcadeControls controls, ArcadeControls rising, double now)
        {
            // Saving changes the meaning of the key still held from capture. Do not let that
            // new logical edge re-open capture, activate a row, or accept a confirmation.
            if (operatorReleaseRequired)
            {
                if (controls == 0 && !AllKeys.Any(RawHeld)) operatorReleaseRequired = false;
                return;
            }
            if (captureControl != 0)
            {
                if (RawPressed(KeyCode.Escape)) { captureControl = 0; return; }
                if (!captureArmed) { if (!AllKeys.Any(RawHeld)) captureArmed = true; return; }
                foreach (var key in AllKeys)
                {
                    if (!RawPressed(key)) continue;
                    try { SaveBindings(bindings.Rebind(captureControl, key.ToString())); operatorMessage = captureControl + " = " + key; captureControl = 0; }
                    catch (ArgumentException error) { operatorMessage = error.Message; captureArmed = false; }
                    return;
                }
                return;
            }
            bool back = (rising & (ArcadeControls.P1B2 | ArcadeControls.P2B2)) != 0 || RawPressed(KeyCode.Escape);
            bool accept = (rising & (ArcadeControls.P1B1 | ArcadeControls.P2B1)) != 0 || RawPressed(KeyCode.Return);
            if (back)
            {
                if (operatorPage == OperatorPage.Home) CloseOperator(now);
                else if (operatorPage == OperatorPage.Confirm) { operatorPage = returnPage; operatorRow = returnRow; }
                else { operatorPage = OperatorPage.Home; operatorRow = 0; }
                operatorMessage = ""; return;
            }
            int count = OperatorRows().Length;
            if ((rising & (ArcadeControls.P1Up | ArcadeControls.P2Up)) != 0) operatorRow = (operatorRow + count - 1) % count;
            if ((rising & (ArcadeControls.P1Down | ArcadeControls.P2Down)) != 0) operatorRow = (operatorRow + 1) % count;
            int delta = (rising & (ArcadeControls.P1Right | ArcadeControls.P2Right)) != 0 ? 1 :
                (rising & (ArcadeControls.P1Left | ArcadeControls.P2Left)) != 0 ? -1 : 0;
            if (operatorPage == OperatorPage.Coins || operatorPage == OperatorPage.Game)
            {
                if (accept || delta != 0) EditSetting(delta == 0 ? 1 : delta);
                return;
            }
            if (!accept) return;
            operatorMessage = "";
            try
            {
                switch (operatorPage)
                {
                    case OperatorPage.Home:
                        if (operatorRow == 0) CloseOperator(now);
                        else if (operatorRow == 6) ConfirmOperator(OperatorAction.Apply);
                        else if (operatorRow == 7) { operatorPage = OperatorPage.Rankings; operatorRow = 0; }
                        else { operatorPage = (OperatorPage)operatorRow; operatorRow = 0; }
                        break;
                    case OperatorPage.Input:
                        if (operatorRow == 22) ConfirmOperator(OperatorAction.ResetBindings);
                        else { captureControl = bindings.keys[operatorRow].control; captureArmed = false; }
                        break;
                    case OperatorPage.Rankings:
                        if (operatorRow == Rankings.Table(Flow.RankingRules).Length + 1) ConfirmOperator(OperatorAction.ClearRankings);
                        break;
                    case OperatorPage.Bookkeeping:
                        if (operatorRow == 10) ConfirmOperator(OperatorAction.ClearBookkeeping);
                        if (operatorRow == 11) ConfirmOperator(OperatorAction.ClearCredits);
                        break;
                    case OperatorPage.Confirm:
                        if (operatorRow == 1)
                        {
                            switch (operatorAction)
                            {
                                case OperatorAction.Apply: ApplyOperatorSettings(draft, now); CloseOperator(now); break;
                                case OperatorAction.ClearBookkeeping: Ledger.ClearBookkeeping(); break;
                                case OperatorAction.ClearCredits: Ledger.ClearCredits(); break;
                                case OperatorAction.ResetBindings: SaveBindings(ArcadeBindings.Defaults()); break;
                                case OperatorAction.ClearRankings:
                                    if (Flow.State == ArcadeFlowState.NameEntry) throw new InvalidOperationException("Finish the pending name entry before clearing rankings.");
                                    if (!Rankings.Clear(Flow.RankingRules)) throw new InvalidOperationException(Rankings.Error ?? "Rankings unavailable.");
                                    returnRow = 0; break;
                            }
                            operatorMessage = "Saved.";
                        }
                        operatorPage = returnPage; operatorRow = returnRow; break;
                }
            }
            catch (ArgumentException error) { operatorMessage = error.Message; }
            catch (InvalidOperationException error) { if (Ledger.Faulted) throw; operatorMessage = error.Message; }
        }
        void ConfirmOperator(OperatorAction action)
        { operatorAction = action; returnPage = operatorPage; returnRow = operatorRow; operatorPage = OperatorPage.Confirm; operatorRow = 0; }
        void EditSetting(int delta)
        {
            var field = typeof(ArcadeSettings).GetField((operatorPage == OperatorPage.Coins ? CoinFields : GameFields)[operatorRow]);
            var next = draft.Copy();
            if (field.FieldType == typeof(bool)) field.SetValue(next, !(bool)field.GetValue(next));
            else if (field.FieldType == typeof(string)) { int index = Array.IndexOf(Fighters, next.boss); next.boss = Fighters[(index + delta + Fighters.Length) % Fighters.Length]; }
            else field.SetValue(next, (int)field.GetValue(next) + delta);
            try { next.Validate(); draft = next; operatorMessage = "Draft changed. Back to menu > Apply settings and return."; }
            catch (ArgumentException error) { operatorMessage = error.Message.Split('\n')[0]; }
        }
        string[] OperatorRows()
        {
            switch (operatorPage)
            {
                case OperatorPage.Home: return new[] { "Resume game (discard settings draft)", "Coin settings", "Game settings", "Input test / remap", "Bookkeeping", "System diagnostics", "Apply settings and return", "Rankings" };
                case OperatorPage.Coins: case OperatorPage.Game:
                    var fields = operatorPage == OperatorPage.Coins ? CoinFields : GameFields; var labels = operatorPage == OperatorPage.Coins ? CoinLabels : GameLabels;
                    return fields.Select((f, i) => labels[i] + " : " + typeof(ArcadeSettings).GetField(f).GetValue(draft)).ToArray();
                case OperatorPage.Input: return bindings.keys.Select(k => k.control + " : " + bindings.Label(k.control)).Concat(new[] { "Restore default bindings..." }).ToArray();
                case OperatorPage.Bookkeeping:
                    var s = Ledger.Snapshot;
                    return new[] { "Coins: chute 1 " + s.coins[0] + " / chute 2 " + s.coins[1], "Service credits: " + s.serviceCredits,
                        "Starts: " + s.starts, "Continues: " + s.continues, "Challenges: " + s.challenges, "Refunded credits: " + s.refundedCredits,
                        "Whole credits: " + s.credits[0] + " / " + s.credits[1], "Partial coins: " + s.partialCoins[0] + "/" + s.coinsPerCredit[0] + " / " + s.partialCoins[1] + "/" + s.coinsPerCredit[1],
                        "Active paid sessions: " + s.sessions.Count(x => x != null), "Counter clears: " + s.bookkeepingClears + " / credit clears: " + s.creditClears,
                        "Clear bookkeeping only...", "Clear credits and partial coins..." };
                case OperatorPage.Rankings:
                    return new[] { "LEVEL " + settings.difficulty + " / " + settings.ladderStages + " STAGES / " + settings.roundsToWin + " ROUNDS / " + settings.roundSeconds + " SECONDS" }
                        .Concat(Rankings.Table(Flow.RankingRules).Select((e, i) => (i + 1) + ".  " + e.initials + "  " + e.fighter + "  -  " + e.stagesCleared + " stages  /  " + ScoreTime(e.fightTicks)))
                        .Concat(new[] { "Clear this rules table..." }).ToArray();
                case OperatorPage.Diagnostics: return new[] { "Unity " + Application.unityVersion, "Application " + Application.version,
                    "Uptime: " + (int)Time.realtimeSinceStartup + " seconds", "Suspended screen: " + Flow.State,
                    "Ledger generation: " + Ledger.Snapshot.generation, "Ledger event: " + Ledger.Snapshot.lastEventId,
                    "Rankings: " + (Rankings.Available ? "Available" : Rankings.Error), "Data: " + DataDirectory, "F2 always opens operator controls", "Keyboard / joystick-button mapping; axes are not configured" };
                case OperatorPage.Confirm: return new[] { "Cancel", operatorAction == OperatorAction.Apply ? "Apply settings" :
                    operatorAction == OperatorAction.ClearBookkeeping ? "Clear bookkeeping" :
                    operatorAction == OperatorAction.ClearCredits ? "Clear credits and partial coins" :
                    operatorAction == OperatorAction.ClearRankings ? "Clear this ranking table" : "Restore default bindings" };
                default: return new[] { "Back" };
            }
        }
        void DrawOperator()
        {
            Fill(new Rect(0, 0, 1280, 720), new Color(.025f, .035f, .065f));
            Text(18, "OPERATOR - " + operatorPage.ToString().ToUpperInvariant(), new GUIStyle(title) { fontSize = 38 }, 65);
            string[] rows = OperatorRows(); int first = operatorRow / 10 * 10;
            if (rows.Length > 10) Text(78, "PAGE " + (first / 10 + 1) + " / " + ((rows.Length + 9) / 10) + " - UP / DOWN TO SCROLL", detail, 22);
            for (int i = first; i < Math.Min(rows.Length, first + 10); i++)
            {
                var rect = new Rect(70, 102 + (i - first) * 44, 1140, 41);
                if (i == operatorRow) Fill(rect, new Color(.12f, .27f, .45f));
                GUI.Label(rect, rows[i], new GUIStyle(body) { alignment = TextAnchor.MiddleLeft, fontSize = 23 });
            }
            string note = operatorPage == OperatorPage.Confirm ? operatorAction == OperatorAction.Apply ?
                "Apply at idle only. Separate pools merge; switching to separate assigns the shared balance to P1." :
                operatorAction == OperatorAction.ClearBookkeeping ? "Clear counters only. Paid sessions, credits and partial coins are preserved." :
                operatorAction == OperatorAction.ClearCredits ? "Permanently remove whole credits AND partial coin payments. Counters are preserved." : operatorAction == OperatorAction.ClearRankings ? "Erase scores for the current difficulty and round rules only. Credits and other tables are preserved." : "Restore the original keyboard profile." :
                operatorPage == OperatorPage.Input ? "Held: " + ReadKeyboard() + "   |   Real coin inputs still count." :
                "Arcade countdowns and gameplay are paused. Coin / service inputs still count; Start is ignored.";
            Text(550, captureControl != 0 ? "Release keys, then press the new key for " + captureControl + ". Escape cancels." : note, detail, 48);
            Text(601, operatorMessage, detail, 45);
            Text(653, "UP / DOWN: choose   LEFT / RIGHT: change   B1: select   B2: back   F2: resume", detail, 40);
        }
    }
}
