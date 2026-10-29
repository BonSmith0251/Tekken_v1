using System;
using System.Collections.Generic;

namespace Versus.Remaster
{
    /// <summary>Match state the original requirement conditions read. Defaults describe a plain versus round.</summary>
    public sealed class CommandContext
    {
        public int characterId, opponentId;
        public int distance = 2000;                 // original units (millimetres)
        public bool rage, backturned, opponentDown, opponentCrouching, opponentAirborne, counterHit;
        public int playerSide;                      // 0 = left (P1), 1 = right
        public int health = 170, opponentHealth = 170, maxHealth = 170;
        public HitState hitState = HitState.Hit;    // result of the current move's attack
        public float bodyYaw;                       // set by the interpreter: degrees turned from the opponent
        public Random random = new Random(1);
        public enum HitState { Hit, Block, Whiff }
    }

    /// <summary>
    /// Runs a fighter through its original command graph at 60 Hz. Mirrors
    /// Tools/CommandGraph/command_reference.py, which is checked against the game's movelist demos:
    /// inputs are numpad directions relative to facing plus buttons 1-4 as bits 1, 2, 4, 8.
    /// Each frame the current move's cancels are scanned in order (a 0x800B entry scans its
    /// group-cancel list in place, up to 0x800C). A match inside its detection window is queued and
    /// taken on its starting frame. The list's 0x8000 entry recovers into its target from its
    /// start frame; otherwise the animation end takes the move's transition.
    /// </summary>
    public sealed class CommandInterpreter
    {
        const long End = 0x8000, DoubleForward = 0x8001, DoubleBack = 0x8002, Group = 0x800B, GroupEnd = 0x800C, SequenceBase = 0x800D;
        const long Press = 0x40000000, AnyOf = 0x20000000;
        const int Open = 32767, DoubleTapFrames = 12, HistoryLength = 120;
        /// <summary>Frames a press group stays usable until a cancel consumes it (fitted to the demos).</summary>
        public const int PressBuffer = 60;
        /// <summary>Presses this close together form one group (1+2).</summary>
        public const int PressMerge = 2;
        static readonly int[] DirectionBits = { 0, 0x2, 0x4, 0x8, 0x10, 0x20, 0x40, 0x80, 0x100, 0x200 };
        // Unlabeled boolean conditions in the Jin/Kazuya graphs, chosen by replaying the demos.
        // 289 gates down-tap sidesteps.
        static readonly HashSet<long> UnknownTrue = new HashSet<long> { 289 };

        /// <summary>The fighter's own graph.</summary>
        public readonly CommandGraph graph;
        public readonly CommandContext context;
        /// <summary>The graph the current move belongs to: the attacker's during a hit reaction.</summary>
        public CommandGraph Active { get; private set; }
        public bool Borrowed => Active != graph;
        /// <summary>
        /// Lets a held reaction continue: such reactions cancel into themselves without input
        /// (a hold kept by the attacker, as in Rage Art and throw hits); while set, those
        /// self-cancels are skipped.
        /// </summary>
        public bool ReleaseHolds;
        /// <summary>Degrees of hip yaw from facing the opponent beyond which a fighter is backturned.</summary>
        public const float BackturnedYaw = 90;
        const long HoldFacing = 0x81A3;
        float turn;
        bool holdFacing;
        /// <summary>
        /// Turn carried between moves (degrees, positive to the fighter's right). Moves rotate the body
        /// through the hip; while property 0x81A3 holds it, a backturned body stays turned.
        /// </summary>
        public float Turn => turn;
        public bool Backturned => Math.Abs(BodyYaw) > BackturnedYaw;
        const int BorrowLimit = 900;
        int borrowedFrames;
        public int Move { get; private set; }
        /// <summary>1-based frame of the current move; 0 on the tick the move was entered.</summary>
        public int Frame { get; private set; }
        /// <summary>True when the current move was entered through a button cancel.</summary>
        public bool EnteredByInput { get; private set; }
        public CommandGraph.Move Current => Active.moves[Move];
        /// <summary>
        /// The move and frame that played on the last step, before that frame's cancels took effect.
        /// Hits are checked against it: a cancel on an active frame still lets that frame hit.
        /// </summary>
        public CommandGraph ActedGraph { get; private set; }
        public int ActedMove { get; private set; }
        public int ActedFrame { get; private set; }
        public CommandGraph.Move Acted => ActedGraph.moves[ActedMove];

        readonly int[] historyDirection = new int[HistoryLength], historyHeld = new int[HistoryLength];
        int historyCount, historyHead;
        int pressButtons, pressDirection = 5, pressAge = 99;
        bool hasPending, pendingByInput;
        int pendingTarget, pendingFrame;
        readonly Dictionary<long, long> variables = new Dictionary<long, long>();

        public CommandInterpreter(CommandGraph graph, CommandContext context)
        {
            this.graph = graph; this.context = context;
            Reset();
        }

        /// <summary>
        /// Forgets every press and direction read so far and drops a queued input cancel (automatic
        /// cancels stay). A knocked-out fighter no longer reads input; without this a press made just
        /// before the KO stays buffered and fires a tech roll or getup during the death fall.
        /// </summary>
        public void DropInput()
        {
            historyCount = historyHead = 0; pressButtons = 0; pressDirection = 5; pressAge = 99;
            if (hasPending && pendingByInput) hasPending = false;
        }

        public void Reset()
        {
            Move = graph.stance; Active = graph; Frame = 0; EnteredByInput = false;
            ActedGraph = graph; ActedMove = Move; ActedFrame = 0;
            historyCount = historyHead = 0; pressButtons = 0; pressDirection = 5; pressAge = 99;
            hasPending = false; variables.Clear(); turn = 0; holdFacing = false;
        }

        /// <summary>Enter one of the fighter's own moves directly, as a scripted state does.</summary>
        public void Force(int move)
        {
            if (move < 0 || move >= graph.moves.Length) throw new ArgumentOutOfRangeException(nameof(move));
            Active = graph; turn = 0; holdFacing = false; Enter(move, false, false);
        }

        /// <summary>
        /// Run a move of another fighter's graph. Tekken 7 hit reactions live in the attacker's moveset:
        /// the defender plays them there, reading that graph's cancels (tech rolls, recoveries), and
        /// returns to its own moveset when the reaction reaches a standard state such as stance or crouch.
        /// </summary>
        public void Borrow(CommandGraph owner, int move)
        {
            if (owner == null || move < 0 || move >= owner.moves.Length) throw new ArgumentOutOfRangeException(nameof(move));
            Active = owner; borrowedFrames = 0; turn = 0; holdFacing = false; Enter(move, false, false); ReturnFromBorrow();
        }

        /// <summary>Advance one 60 Hz frame. Returns true when the move changed.</summary>
        public bool Step(int direction, int held)
        {
            if (direction < 1 || direction > 9) direction = 5;
            held &= 0xF;
            int previous = historyCount > 0 ? HeldAt(0) : 0;
            historyHead = (historyHead + 1) % HistoryLength;
            historyDirection[historyHead] = direction; historyHeld[historyHead] = held;
            if (historyCount < HistoryLength) historyCount++;
            int fresh = held & ~previous;
            if (fresh != 0) { pressButtons = pressAge < PressMerge ? pressButtons | fresh : fresh; pressDirection = direction; pressAge = 0; }
            else pressAge = Math.Min(99, pressAge + 1);

            Frame++;
            var move = Current;
            ActedGraph = Active; ActedMove = Move; ActedFrame = Frame;
            if (move.properties != null)
                for (int i = 0; i + 2 < move.properties.Length; i += 3)
                {
                    if ((move.properties[i] & 0x3FFF) == Frame && move.properties[i + 1] == HoldFacing)
                        holdFacing = move.properties[i + 2] != 0;
                    if (move.properties[i] == Frame && move.properties[i + 1] >= 0x8000)
                    {
                        long id = move.properties[i + 1], value = move.properties[i + 2];
                        if (id == 0x8415) variables[0x8414] = Variable(0x8414) + value;   // "Add to 0x8414"
                        else variables[id] = value;
                    }
                }
            context.bodyYaw = BodyYaw;
            // Once an input cancel is queued, later presses stay buffered for the next move.
            if (move.cancelStart >= 0 && !(hasPending && pendingByInput) &&
                Scan(Active.cancels, move.cancelStart, 0, out int target, out int starting, out bool byInput))
            {
                // A buffered input cancel is not replaced by an automatic one.
                if (byInput || !hasPending || !pendingByInput)
                {
                    hasPending = true; pendingTarget = target; pendingFrame = starting; pendingByInput = byInput;
                    if (byInput) { pressButtons = 0; pressDirection = 5; pressAge = 99; }
                }
            }
            if (hasPending && Frame >= pendingFrame) { Enter(pendingTarget, pendingByInput); ReturnFromBorrow(); return true; }
            if (move.frames > 0 && Frame >= move.frames) { Enter(move.transition, false); ReturnFromBorrow(); return true; }
            if (Borrowed && ++borrowedFrames > BorrowLimit) { Active = graph; turn = 0; holdFacing = false; Enter(graph.stance, false, false); return true; }
            return false;
        }

        // A borrowed reaction ends when it reaches a standard state (stance, crouch, downed...):
        // the defender continues from its own move in the same alias slot.
        void ReturnFromBorrow()
        {
            if (!Borrowed) return;
            int slot = Active.aliases != null ? Array.IndexOf(Active.aliases, Move) : -1;
            if (slot >= 0 && graph.aliases != null && slot < graph.aliases.Length && graph.aliases[slot] >= 0)
            { Active = graph; Move = graph.aliases[slot]; }
            else if (Move == Active.stance) { Active = graph; Move = graph.stance; }
        }

        int[] ClipYaws(CommandGraph g, int move)
        {
            int clip = g.moves[move].clip;
            return clip >= 0 && clip < g.clips.Length ? g.clips[clip].yaw : null;
        }

        // Backturned moves are animated already turned around, relative to a front-facing body.
        static bool AuthoredBackturned(int[] yaws) => yaws != null && yaws.Length > 0 && Math.Abs(yaws[0]) > BackturnedYaw;

        /// <summary>Body turn from facing the opponent (degrees): carried turn plus the current clip's turn.</summary>
        public float BodyYaw
        {
            get
            {
                var yaws = ClipYaws(Active, Move);
                if (yaws == null || yaws.Length == 0) return turn;
                int now = yaws[Math.Min(Math.Max(Frame - 1, 0), yaws.Length - 1)];
                int start = AuthoredBackturned(yaws) ? 0 : yaws[0];
                return Wrap(turn + now - start);
            }
        }

        static float Wrap(float degrees) => ((degrees + 180) % 360 + 360) % 360 - 180;

        // carryTurn is false when the graph was just switched (Force, Borrow): the current move id
        // belongs to the previous graph, and the turn was reset there.
        void Enter(int move, bool byInput, bool carryTurn = true)
        {
            // A move's rotation carries into the next only while the 0x81A3 hold keeps the fighter
            // turned away; facing front again (or no hold) re-faces the opponent.
            if (carryTurn)
            {
                move = OrientLying(move);
                float body = BodyYaw;
                if (holdFacing && Math.Abs(body) > BackturnedYaw) turn = body;
                else { turn = 0; holdFacing = false; }
            }
            if (AuthoredBackturned(ClipYaws(Active, move))) turn = 0;
            Move = move; Frame = 0; hasPending = false; EnteredByInput = byInput;
        }

        // Lying states come in face-up / face-down twins (alias slots 4/8, 6/9, 7/10: dwDWN_00U /
        // dwDWN_00D ...). A fall names one of them (sDm_ASI10LD ends face down but goes to dwDWN_00U),
        // and the body takes the twin that lies the way it landed, instead of turning end over end.
        static readonly int[][] LyingTwins = { new[] { 4, 8 }, new[] { 6, 9 }, new[] { 7, 10 } };
        const int LyingLine = 60;   // cm of head-to-feet length along the facing that counts as lying

        int OrientLying(int target)
        {
            var aliases = Active.aliases;
            if (aliases == null) return target;
            int slot = Array.IndexOf(aliases, target);
            foreach (var twin in LyingTwins)
            {
                int other = slot == twin[0] ? twin[1] : slot == twin[1] ? twin[0] : -1;
                if (other < 0 || other >= aliases.Length || aliases[other] < 0) continue;
                int now = BodyLine(Active, Move, Frame), wanted = BodyLine(Active, target, 1), swapped = BodyLine(Active, aliases[other], 1);
                if (Math.Abs(now) < LyingLine || Math.Abs(wanted) < LyingLine) return target;
                return (now > 0) != (wanted > 0) && (now > 0) == (swapped > 0) ? aliases[other] : target;
            }
            return target;
        }

        /// <summary>Head minus feet along the facing (cm) on a frame of the move's clip, 0 without a body track.</summary>
        static int BodyLine(CommandGraph g, int move, int frame)
        {
            if (move < 0 || move >= g.moves.Length) return 0;
            int clip = g.moves[move].clip;
            if (clip < 0 || clip >= g.clips.Length || g.bodyFields < 6) return 0;
            var body = g.clips[clip].body;
            if (body == null || body.Length < g.bodyFields) return 0;
            int k = Math.Min(Math.Max(frame - 1, 0) / Math.Max(1, g.bodyStep), body.Length / g.bodyFields - 1) * g.bodyFields;
            return body[k + 2] - body[k + 4];
        }

        int DirectionAt(int age) => historyDirection[(historyHead - age + HistoryLength) % HistoryLength];
        int HeldAt(int age) => historyHeld[(historyHead - age + HistoryLength) % HistoryLength];
        long Variable(long id) => variables.TryGetValue(id, out long v) ? v : 0;
        static bool Matches(long mask, int direction) => mask == 0 || (mask & DirectionBits[direction]) != 0;

        bool Scan(CommandGraph.Cancel[] table, int index, int depth, out int target, out int starting, out bool byInput)
        {
            target = starting = 0; byInput = false;
            while (index >= 0 && index < table.Length)
            {
                var c = table[index];
                long low = c.command & 0xFFFFFFFF;
                if (low == GroupEnd) return false;
                if (low == End)
                {
                    if (depth == 0 && c.start > 0 && c.start <= Frame && Frame <= Math.Min(c.end, Open))
                    { target = c.target; starting = c.startingFrame; return true; }
                    return false;
                }
                index++;
                if (c.reqCount > 0 && !Requirements(c.reqStart, c.reqCount)) continue;
                if (low == Group)
                {
                    if (depth < 4 && Scan(Active.groupCancels, c.target, depth + 1, out target, out starting, out byInput)) return true;
                    continue;
                }
                int end = c.end >= Open ? Open : c.end;
                if (Frame < c.start || Frame > end || !Command(c.command)) continue;
                if (ReleaseHolds && c.command == 0 && c.target == Move) continue;
                target = c.target; starting = c.startingFrame; byInput = ByInput(c.command);
                return true;
            }
            return false;
        }

        bool ByInput(long command)
        {
            long low = command & 0xFFFFFFFF;
            if (low >= SequenceBase && low - SequenceBase < Active.sequences.Length)
            {
                var s = Active.sequences[low - SequenceBase];
                if (s.inputCount > 0 && ((Active.inputs[s.inputStart + s.inputCount - 1] >> 32) & 0xF) != 0) return true;
            }
            return ((command >> 32) & 0x1F) != 0;
        }

        bool Command(long command)
        {
            long direction = command & 0xFFFFFFFF, word = command >> 32;
            int buttons = (int)(word & 0x1F);
            bool live = pressAge < PressBuffer;
            if (direction >= 0x8000)
            {
                bool ok = direction == DoubleForward ? DoubleTap(6) :
                          direction == DoubleBack ? DoubleTap(4) :
                          direction >= SequenceBase && direction - SequenceBase < Active.sequences.Length && SequenceMatches((int)(direction - SequenceBase));
                if (!ok) return false;
            }
            else if (!Matches(direction, buttons != 0 && (word & (Press | AnyOf)) != 0 ? pressDirection : DirectionAt(0)))
                return false;
            if ((word & Press) != 0) return live && pressButtons == buttons;
            if ((word & AnyOf) != 0) return live && (pressButtons & buttons) != 0;
            if (buttons != 0) return (HeldAt(0) & buttons) == buttons;
            return true;
        }

        bool DoubleTap(int numpad)
        {
            if (historyCount < 2 || DirectionAt(0) != numpad || DirectionAt(1) == numpad) return false;
            bool released = false;
            for (int age = 1; age <= DoubleTapFrames && age < historyCount; age++)
            {
                if (DirectionAt(age) != numpad) released = true;
                else if (released) return true;
            }
            return false;
        }

        bool SequenceMatches(int index)
        {
            var s = Active.sequences[index];
            if (s.inputCount == 0) return false;
            int window = Math.Min(Math.Max(1, s.window), historyCount);
            long last = Active.inputs[s.inputStart + s.inputCount - 1];
            int lastButtons = (int)((last >> 32) & 0xF);
            int newest = 0, direction = DirectionAt(0);
            if (lastButtons != 0)
            {
                if (pressAge >= PressBuffer || pressButtons != lastButtons || pressAge >= window) return false;
                direction = pressDirection;
                newest = pressAge;
            }
            if (!Matches(last & 0xFFFFFFFF, direction)) return false;
            int k = s.inputCount - 2;
            for (int age = newest + 1; age < window && k >= 0; age++)
            {
                long step = Active.inputs[s.inputStart + k];
                int b = (int)((step >> 32) & 0xF);
                if (Matches(step & 0xFFFFFFFF, DirectionAt(age)) && (HeldAt(age) & b) == b) k--;
            }
            return k < 0;
        }

        /// <summary>Evaluates a requirement list (hit conditions use this with the attacker's context).</summary>
        public bool RequirementsHold(int start, int count) => Requirements(start, count);

        bool Requirements(int start, int count)
        {
            for (int i = start; i < start + count; i++)
                if (!Requirement(Active.requirements[2 * i], Active.requirements[2 * i + 1])) return false;
            return true;
        }

        // Condition names follow the TKMovesets Tekken 7 editor labels.
        bool Requirement(long c, long p)
        {
            var x = context;
            if (c >= 0x8000) return Variable(c) == p;
            switch (c)
            {
                case 0: return true;
                case 35: return x.distance <= p;
                case 36: return x.distance >= p;
                case 43: case 44: case 51: return x.hitState == CommandContext.HitState.Hit;
                case 47: return x.hitState == CommandContext.HitState.Block;
                case 48: return x.hitState == CommandContext.HitState.Whiff;
                case 49: return x.hitState != CommandContext.HitState.Whiff;
                case 66: return !x.opponentCrouching;
                case 67: return x.opponentCrouching;
                case 72: return Backturned;
                case 73: return !Backturned;
                case 80: return Backturned && x.bodyYaw < 0;   // backturned, turned to one side (side fitted)
                case 81: return Backturned && x.bodyYaw >= 0;
                case 92: case 96: return true;              // opponent facing forward
                case 114: return Frame <= p;                // own move frame <= X (sidestep taps; fitted)
                case 150: return x.playerSide == 0;         // screen-left side: up steps to own left
                case 151: return x.playerSide == 1;
                case 126: return x.opponentDown;
                case 127: return !x.opponentDown;
                case 130: return x.counterHit;
                case 135: return x.health <= 0;             // on death: KO reactions fall into the death moves
                case 137: return x.opponentHealth * 2 < x.maxHealth;
                case 138: return x.health >= p;
                case 139: return x.health < p;
                case 140: return x.opponentHealth >= p;
                case 141: return x.opponentHealth > p;
                case 142: return x.health * 100L >= p * x.maxHealth;
                case 143: return x.health * 100L < p * x.maxHealth;
                case 144: return x.opponentHealth <= p;
                case 145: return x.health <= p;
                case 146: return x.playerSide == 0;
                case 147: return x.playerSide == 1;
                case 157: return x.rage;
                case 217: case 221: return x.characterId == p;
                case 218: case 222: return x.characterId != p;
                case 219: case 223: return x.opponentId == p;
                case 220: case 224: return x.opponentId != p;
                case 225: return p == 0;                    // 0 = human controller
                case 552: return x.random.NextDouble() * 100 < p;
                case 553: return x.random.NextDouble() * 1000 < p;
                case 579: return x.playerSide == p;
                case 583: return true;                      // male (Jin, Kazuya)
                case 607: return x.opponentAirborne;
                case 614: return x.opponentAirborne;        // juggle: hit conditions for an airborne opponent
                case 608: return !x.opponentAirborne;
                case 807: return Variable(0x8414) >= p;
                case 808: return Variable(0x8414) <= p;
                case 809: return Variable(0x8414) == p;
            }
            // Everything else is false in local versus: practice demos, throw outcomes (1-30),
            // incoming-attack and enemy-frame checks (68-71, 100-114), arcade/story/treasure/
            // intro/assist modes, items, juggle/screw, parries and power crush (813-874).
            return UnknownTrue.Contains(c);
        }
    }
}
