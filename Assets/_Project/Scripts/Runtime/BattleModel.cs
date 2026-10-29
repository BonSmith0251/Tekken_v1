using System;

namespace Versus.Remaster
{
    public enum MatchPhase { Intro, Fight, RoundOver, MatchOver }
    public enum HitHeight { High, Mid, Low, SpecialLow }

    [Serializable]
    public class MoveSpec
    {
        public string name;
        public HitHeight height;
        public int damage, startup, active, recovery, stun;
        public float reach;
        public MoveSpec(string name, HitHeight height, int damage, int startup, int active, int recovery, int stun, float reach)
        { this.name = name; this.height = height; this.damage = damage; this.startup = startup;
          this.active = active; this.recovery = recovery; this.stun = stun; this.reach = reach; }
    }

    [Serializable]
    public class MatchRules
    {
        public int health = 100, seconds = 60, roundsToWin = 2;
        public float walkSpeed = 2.8f, arenaHalfWidth = 6f, arenaHalfDepth = 2.8f;
        // New prototype balance. These are NOT reconstructed Tekken frame data.
        public MoveSpec[] moves = {
            new MoveSpec("Jab", HitHeight.High, 7, 10, 3, 16, 18, 1.65f),
            new MoveSpec("Cross", HitHeight.Mid, 14, 18, 3, 23, 25, 1.8f),
            new MoveSpec("Sweep", HitHeight.Low, 11, 22, 4, 26, 24, 1.9f),
            new MoveSpec("Drive kick", HitHeight.Mid, 20, 28, 4, 32, 33, 2.1f)
        };
        public void Validate()
        {
            if (health < 1 || seconds < 1 || roundsToWin < 1 || walkSpeed <= 0 || arenaHalfWidth < 3 || arenaHalfDepth < 1)
                throw new ArgumentException("Invalid match rules");
            if (moves == null || moves.Length != 4) throw new ArgumentException("Four moves are required");
            foreach (var m in moves)
                if (m == null || m.damage < 0 || m.startup < 1 || m.active < 1 || m.recovery < 1 || m.stun < 1 || m.reach <= 0)
                    throw new ArgumentException("Invalid move definition");
        }
    }

    public struct PlayerCommand
    {
        // Prototype controls.
        public float x, z;
        public bool guard, crouch;
        public int attack;
        // Original command controls: numpad direction in screen space (6 = right) and buttons 1-4 as bits 1, 2, 4, 8.
        public int stick, buttons;
        public static PlayerCommand Neutral => new PlayerCommand { attack = -1, stick = 5 };
    }

    public class FighterState
    {
        public float x, z;
        public int health, wins, facing, attack = -1, attackFrame, stun, flash, buffer = -1, bufferTicks;
        public bool guard, crouch, connected;
        /// <summary>Original move id and 1-based frame when the battle runs command graphs; -1 otherwise.</summary>
        public int move = -1, moveFrame;
        /// <summary>Player whose moveset holds the move (the attacker's during a hit reaction) and its clip key.</summary>
        public int moveOwner;
        public string clipKey;
        public bool borrowed;
        /// <summary>Unit facing direction on the floor (x, z). Command battles track the opponent in 3D.</summary>
        public float dirX = 1, dirZ;
        public bool blocked, counterHit, reacting, attacking, lowAttack;
        /// <summary>Whether the move that played this tick (before its frame's cancels) already hit.</summary>
        public bool actedConnected;
        /// <summary>Ticks spent lying in the downed idle loop (alias slots 7/10), waiting for a getup input.</summary>
        public int downedTicks;
        /// <summary>From the current move's original vulnerability state and airborne window.</summary>
        public bool downed, airborne;
        /// <summary>Body turned away from the opponent, and the turn carried between moves (degrees).</summary>
        public bool backturned;
        public float turn;
        /// <summary>Head and feet on the floor (from the clip's body track): the body's floor line runs
        /// from the feet to the head (a lying body covers its whole length).</summary>
        public float headX, headZ, feetX, feetZ;
        /// <summary>Counts heading cuts: the battle turned a body on the floor at once so a new move's
        /// pose continues the old one (the scene shows such a turn without easing it).</summary>
        public int headingCuts;
        /// <summary>Shallow copy for presentation (render interpolation between 60 Hz ticks).</summary>
        public FighterState Snapshot() => (FighterState)MemberwiseClone();
    }

    public struct HitEvent
    {
        public int attacker, defender, damage, stun;
        public bool blocked, counterHit;
        /// <summary>Landed while the defender was recovering from an attack (Tekken 7's punish notice).</summary>
        public bool punish;
        public HitHeight height;
        /// <summary>Original reaction record in the attacker's moveset, or -1 (command graphs only).</summary>
        public int reaction;
        /// <summary>A throw (hit level bit 0x400000, the Thrmv* moves): armour does not absorb it.</summary>
        public bool throwHit;
        /// <summary>Power crush: the defender's armour (property 0x81BD) took the high or mid hit. The damage is
        /// dealt, but there is no reaction, pushback or turn, and the armoured move goes on.</summary>
        public bool absorbed;
    }

    /// <summary>An original timed property on the frame actually acted, before a cancel replaces
    /// the move. Graph ownership and raw frame flags are preserved for visual dispatch consumers.</summary>
    public struct ActedMovePropertyEvent
    {
        public int player, move, frame;
        public long rawFrame, id, value;
        public CommandGraph graph;
    }

    /// <summary>
    /// 60 Hz rules independent of rendering and input devices. Local play only. Built with command
    /// graphs it runs the original movesets: inputs go through each fighter's cancel graph, position
    /// follows the clips' root travel, attacks use the original active frames, damage, hit level and
    /// hit conditions, and hits put the defender into the original reaction move. Without graphs it
    /// runs the four-move prototype rules the editor tools test against.
    /// </summary>
    public class BattleModel
    {
        public const float StartingHalfSeparation = 1.1f;
        // Not recovered from the game: root-to-root strike reach and lateral tolerance in metres.
        public const float CommandReach = 1.15f, CommandLateralTolerance = .65f;
        const int FallbackStun = 18;
        public readonly MatchRules rules;
        public readonly MoveSpec[][] moves;
        public readonly CommandGraph[] graphs;
        public readonly CommandInterpreter[] interpreters;
        readonly CommandContext[] contexts;
        readonly int[] fallbackStun = new int[2];
        public readonly FighterState[] fighters = { new FighterState(), new FighterState() };
        public bool UsesCommandGraphs => graphs != null;
        /// <summary>
        /// Screen-right axis on the floor. It follows the line between the fighters continuously (never
        /// flipping), so the camera stays perpendicular to that line and left/right input stays stable.
        /// </summary>
        public float screenRightX { get; private set; } = 1;
        public float screenRightZ { get; private set; }
        public MatchPhase phase { get; private set; }
        public int ticksLeft { get; private set; }
        public int phaseTicks { get; private set; }
        public int round { get; private set; }
        public int winner { get; private set; } = -1;
        public string result { get; private set; } = "";
        public event Action<HitEvent> Hit;
        /// <summary>
        /// A move property reached its frame (command graphs): (player, property id, value). Frames
        /// carry flag bits above 0x3FFF; the low bits are the move frame. Camera shakes, preset zoom
        /// and cinematic cameras are presented by the scene from these original properties.
        /// </summary>
        public event Action<int, long, long> MoveProperty;
        /// <summary>Rage given by the scene's Rage Art button (VersusGame) regardless of health, until it
        /// clears it after the Rage Art. A demonstration aid, not an original rule.</summary>
        public readonly bool[] rageGranted = new bool[2];
        public event Action<ActedMovePropertyEvent> ActedMoveProperty;
        const long SlowDown = 0x828C, SlowDownImmunity = 0x828D;
        readonly int[] slowTicks = new int[2];
        // A hit reaction is held while the attacker stays in the move that landed it.
        readonly int[] heldBy = { -1, -1 }, heldMove = { -1, -1 };
        // Knockback in progress per fighter: the record, the frame reached, and the push direction.
        readonly CommandGraph.Pushback[] push = new CommandGraph.Pushback[2];
        readonly int[] pushFrame = new int[2];
        readonly float[] pushX = new float[2], pushZ = new float[2];
        // Getting up from the floor (getup, roll, getup kick): the body pivots toward the opponent at
        // most RisingTurn degrees a tick, instead of snapping round while it still lies on the floor,
        // until the fighter is back in its stance or crouch, or RisingLimit ticks have passed.
        readonly bool[] rising = new bool[2];
        readonly int[] risingTicks = new int[2];
        const int RisingLimit = 120;
        const float RisingTurn = 5;
        readonly float[] slowFactor = { 1, 1 }, slowClock = new float[2];
        /// <summary>Remaster choice (user request 2026-10-28), not established as original: a blow that knocks out a
        /// standing fighter plays that fighter's standing death collapse (fatal_s) at once. Off: the stagger plays out,
        /// the fighter returns to its stance, and the stance's "on death" cancel (condition 135) starts fatal_s
        /// 25-45 ticks later.</summary>
        public bool immediateKoCollapse = true;
        /// <summary>Slows one fighter for a number of ticks: it advances on <paramref name="factor"/> of them (the
        /// mechanism of the original slow-down 0x828C). Used by the scene for the KO near-freeze of the winner.</summary>
        public void SlowFighter(int player, int ticks, float factor)
        {
            slowTicks[player] = Math.Max(0, ticks); slowFactor[player] = Math.Max(.001f, Math.Min(1, factor)); slowClock[player] = 0;
        }
        public BattleModel(MatchRules rules, MoveSpec[][] moves = null) { rules.Validate(); this.rules = rules; this.moves = moves ?? new[] { rules.moves, rules.moves }; Restart(); }
        public BattleModel(MatchRules rules, CommandGraph[] graphs)
        {
            rules.Validate();
            if (graphs == null || graphs.Length != 2 || graphs[0] == null || graphs[1] == null) throw new ArgumentException("Two command graphs are required");
            this.rules = rules; this.graphs = graphs; moves = new[] { rules.moves, rules.moves };
            contexts = new CommandContext[2]; interpreters = new CommandInterpreter[2];
            for (int i = 0; i < 2; i++)
            {
                contexts[i] = new CommandContext { characterId = graphs[i].characterId, opponentId = graphs[1 - i].characterId,
                    playerSide = i, health = rules.health, opponentHealth = rules.health, maxHealth = rules.health, random = new Random(17 + i) };
                interpreters[i] = new CommandInterpreter(graphs[i], contexts[i]);
            }
            Restart();
        }
        public MoveSpec Move(int player, int index) => moves[player][index];
        public void Restart()
        {
            foreach (var f in fighters) f.wins = 0;
            round = 0;
            StartRound();
        }
        // Used after a scene-directed entrance. The round was already reset by Restart.
        public void BeginFight()
        {
            if (phase != MatchPhase.Intro) throw new InvalidOperationException("Fight can only begin from Intro");
            phase = MatchPhase.Fight;
            phaseTicks = 0;
        }
        void StartRound()
        {
            round++; winner = -1; result = "";
            screenRightX = 1; screenRightZ = 0;
            ticksLeft = rules.seconds * 60;
            phase = MatchPhase.Intro; phaseTicks = 150;
            for (int i = 0; i < 2; i++)
            {
                var f = fighters[i];
                f.x = i == 0 ? -StartingHalfSeparation : StartingHalfSeparation; f.z = 0; f.facing = i == 0 ? 1 : -1;
                f.dirX = f.facing; f.dirZ = 0;
                f.health = rules.health; f.attack = -1; f.downedTicks = 0; f.attackFrame = f.stun = f.flash = f.bufferTicks = 0;
                f.buffer = -1; f.guard = f.crouch = f.connected = f.blocked = f.counterHit = f.reacting = false;
                slowTicks[i] = 0; slowClock[i] = 0; heldBy[i] = heldMove[i] = -1; push[i] = null; rising[i] = false;
                if (interpreters != null)
                { interpreters[i].Reset(); f.move = interpreters[i].Move; f.moveFrame = 0; f.moveOwner = i; f.borrowed = false; f.clipKey = null; fallbackStun[i] = 0; }
            }
        }
        static float Clamp(float n, float min, float max) => Math.Max(min, Math.Min(max, n));
        public void Tick(PlayerCommand p1, PlayerCommand p2)
        {
            // After a KO the original knockdown reactions keep playing, without further hits.
            if (UsesCommandGraphs && (phase == MatchPhase.RoundOver || phase == MatchPhase.MatchOver))
            {
                UpdateCommandFighter(0, PlayerCommand.Neutral); UpdateCommandFighter(1, PlayerCommand.Neutral);
                // Bodies stay apart while the death fall plays, so the winner never ends up on the loser.
                SeparateBodies(); SeparateTorsos();
            }
            if (phase == MatchPhase.MatchOver) return;
            if (phase != MatchPhase.Fight)
            {
                if (--phaseTicks <= 0)
                { if (phase == MatchPhase.Intro) phase = MatchPhase.Fight; else StartRound(); }
                return;
            }
            var a = fighters[0]; var b = fighters[1];
            if (UsesCommandGraphs) { UpdateScreenAxis(); UpdateCommandFighter(0, p1); UpdateCommandFighter(1, p2); SeparateBodies(); SeparateTorsos(); }
            else { UpdateFighter(0, p1); UpdateFighter(1, p2); }
            // Resolve body overlap without preventing sidesteps or changing arena bounds.
            if (!UsesCommandGraphs && Math.Abs(a.z - b.z) < .55f && Math.Abs(a.x - b.x) < .8f)
            {
                float center = Clamp((a.x + b.x) * .5f, -rules.arenaHalfWidth + .4f, rules.arenaHalfWidth - .4f);
                int direction = a.x <= b.x ? 1 : -1;
                a.x = center - direction * .4f; b.x = center + direction * .4f;
            }
            // Snapshot both contacts before applying damage: equal-frame strikes can trade.
            HitEvent? hitA = UsesCommandGraphs ? CommandContact(0) : Contact(0);
            HitEvent? hitB = UsesCommandGraphs ? CommandContact(1) : Contact(1);
            if (hitA.HasValue) Apply(hitA.Value);
            if (hitB.HasValue) Apply(hitB.Value);
            ticksLeft--;
            if (a.health <= 0 || b.health <= 0 || ticksLeft <= 0)
            {
                winner = a.health == b.health ? -1 : a.health > b.health ? 0 : 1;
                result = ticksLeft <= 0 ? "TIME UP" : winner < 0 ? "DOUBLE KO" : "KNOCKOUT";
                if (winner >= 0) fighters[winner].wins++;
                // The knocked-out fighter stops reading input, including presses still buffered.
                if (UsesCommandGraphs)
                    for (int i = 0; i < 2; i++) if (fighters[i].health <= 0) interpreters[i].DropInput();
                phase = winner >= 0 && fighters[winner].wins >= rules.roundsToWin ? MatchPhase.MatchOver : MatchPhase.RoundOver;
                // Command battles let the original death fall (about 70 frames) finish before the next round.
                phaseTicks = UsesCommandGraphs ? 240 : 150;
            }
        }

        // ---- Original command graphs -------------------------------------------------------

        /// <summary>Screen-space numpad direction to the fighter's own (6 = toward the facing side).</summary>
        public static int Relative(int stick, int facing)
        {
            if (stick < 1 || stick > 9) return 5;
            if (facing >= 0) return stick;
            int column = (stick - 1) % 3, row = (stick - 1) / 3;
            return row * 3 + (2 - column) + 1;
        }

        static bool Attacking(CommandGraph.Move m) => m.firstActive > 0;
        // Downed: the original vulnerability state, or a falling body (vuln low byte 0x02) past its
        // airborne window, i.e. landing on the floor.
        static bool Downed(CommandGraph.Move m, int frame) =>
            (m.vuln & 0x80) != 0 || ((m.vuln & 0xFF) == 0x02 && m.airEnd > 0 && frame > m.airEnd);
        static bool Airborne(CommandGraph.Move m, int frame) => m.airEnd > 0 && frame >= m.airStart && frame <= m.airEnd;
        public static HitHeight Height(int hitlevel)
        {
            switch (hitlevel & 0xFF)
            {
                case 0x12: return HitHeight.High;
                case 0x0F: return HitHeight.Low;
                case 0x1F: return HitHeight.SpecialLow;
                default: return HitHeight.Mid;
            }
        }

        void UpdateCommandFighter(int i, PlayerCommand c)
        {
            var f = fighters[i]; var other = fighters[1 - i];
            var interpreter = interpreters[i]; var context = contexts[i];
            if (f.flash > 0) f.flash--;
            // Original slow-down (0x828C): the slowed fighter advances on a fraction of the ticks.
            if (slowTicks[i] > 0)
            {
                slowTicks[i]--; slowClock[i] += slowFactor[i];
                if (slowClock[i] < 1) return;
                slowClock[i] -= 1;
            }
            int direction = Relative(c.stick, f.facing);
            if (fallbackStun[i] > 0)
            {
                // A reaction the defender's moveset does not name: hold the stance briefly.
                fallbackStun[i]--; direction = 5; c.buttons = 0;
            }
            float dx = other.x - f.x, dz = other.z - f.z;
            context.distance = (int)(Math.Sqrt(dx * dx + dz * dz) * 1000);
            context.health = f.health; context.opponentHealth = other.health; context.maxHealth = rules.health;
            // Tekken 7 Rage starts at a quarter of full health or less.
            context.rage = f.health * 4 <= rules.health || rageGranted[i];
            context.opponentCrouching = other.crouch;
            context.opponentDown = other.downed;
            context.opponentAirborne = other.airborne;
            // "Standing on left/right side" (146/147, 579) follow the current screen side.
            context.playerSide = f.facing > 0 ? 0 : 1;
            context.counterHit = f.counterHit;
            context.hitState = !f.connected ? CommandContext.HitState.Whiff : f.blocked ? CommandContext.HitState.Block : CommandContext.HitState.Hit;
            bool wasBorrowed = interpreter.Borrowed;
            bool connectedBefore = f.connected;
            if (heldBy[i] >= 0)
            {
                interpreter.ReleaseHolds = interpreters[heldBy[i]].Move != heldMove[i] || interpreters[heldBy[i]].Borrowed;
                if (!interpreter.Borrowed) { heldBy[i] = heldMove[i] = -1; interpreter.ReleaseHolds = false; }
            }
            bool changed = interpreter.Step(direction, c.buttons);
            PublishActedProperties(i, interpreter);
            f.actedConnected = changed ? connectedBefore : f.connected;
            if (changed)
            {
                f.connected = f.blocked = f.counterHit = false;
                // A reaction ends when it hands back to the fighter's own moveset (or, for the
                // fallback stun, when the fighter acts or settles into stance again).
                if (!interpreter.Borrowed && (wasBorrowed || interpreter.EnteredByInput || interpreter.Move == interpreter.graph.stance)) f.reacting = false;
            }
            var move = interpreter.Current;
            var active = interpreter.Active;
            f.move = interpreter.Move; f.moveFrame = interpreter.Frame; f.moveOwner = interpreter.Borrowed ? 1 - i : i; f.borrowed = interpreter.Borrowed;
            RaiseProperties(i, move, f.moveFrame);
            f.clipKey = move.clip >= 0 && move.clip < active.clips.Length ? active.clips[move.clip].key : null;
            // Original root travel of the move's clip, applied in the fighter's facing frame.
            if (move.clip >= 0 && move.clip < active.clips.Length && f.moveFrame >= 2)
            {
                var clip = active.clips[move.clip];
                int s = Math.Min(f.moveFrame - 1, clip.forward.Length - 1);
                if (s >= 1 && f.moveFrame - 1 < clip.forward.Length)
                {
                    float forward = (clip.forward[s] - clip.forward[s - 1]) / 1000f, lateral = (clip.lateral[s] - clip.lateral[s - 1]) / 1000f;
                    // Forward along the body's facing (the opponent direction turned by the carried turn,
                    // positive to the right), lateral toward the body's left.
                    double a = f.turn * Math.PI / 180;
                    float bx = (float)(f.dirX * Math.Cos(a) - f.dirZ * Math.Sin(a)), bz = (float)(f.dirX * Math.Sin(a) + f.dirZ * Math.Cos(a));
                    f.x = Clamp(f.x + forward * bx - lateral * bz, -rules.arenaHalfWidth, rules.arenaHalfWidth);
                    f.z = Clamp(f.z + forward * bz + lateral * bx, -rules.arenaHalfDepth, rules.arenaHalfDepth);
                }
            }
            ApplyPushback(i, f);
            // Fighters turn to face each other outside attacks and reactions, and an attack keeps
            // tracking its target through its startup (until the first active frame). Bodies on the
            // floor or in the air, and a knocked-out fighter, keep their heading.
            bool grounded = Downed(move, f.moveFrame), inAir = Airborne(move, f.moveFrame);
            bool tracking = !Attacking(move) || f.moveFrame < move.firstActive;
            if (grounded) { rising[i] = true; risingTicks[i] = 0; }
            else if (rising[i] && (++risingTicks[i] > RisingLimit || (!interpreter.Borrowed &&
                     (f.move == interpreter.graph.stance || IsAlias(interpreter.graph, f.move, 2)) && AngleTo(f, other) <= RisingTurn)))
                rising[i] = false;
            if (tracking && !f.reacting && !grounded && !inAir && f.health > 0)
            {
                if (rising[i]) TurnToward(f, other, RisingTurn); else FaceOpponent(f, other);
            }
            f.facing = ScreenSide(f, other);
            bool down = direction == 1 || direction == 2 || direction == 3;
            f.crouch = down && !f.downed && !f.airborne;
            // Fighters on the ground or in the air cannot guard.
            f.guard = !Attacking(move) && !f.reacting && fallbackStun[i] == 0 && !f.downed && !f.airborne && (direction == 4 || direction == 1);
            f.stun = f.reacting || fallbackStun[i] > 0 ? 1 : 0;
            f.attacking = Attacking(move);
            f.downed = Downed(move, f.moveFrame);
            f.backturned = interpreter.Backturned; f.turn = interpreter.Turn;
            float lineX = f.headX - f.feetX, lineZ = f.headZ - f.feetZ;
            PlaceHead(f, move, active);
            if (changed && f.downed) KeepLyingLine(f, move, active, lineX, lineZ);
            f.airborne = Airborne(move, f.moveFrame);
            f.downedTicks = !interpreter.Borrowed && IsDownedIdle(interpreter.graph, f.move) ? f.downedTicks + 1 : 0;
            f.lowAttack = f.attacking && Height(move.hitlevel) == HitHeight.Low;
            f.attack = -1;
        }

        void PublishActedProperties(int player, CommandInterpreter interpreter)
        {
            if (ActedMoveProperty == null || interpreter.ActedFrame < 1) return;
            var properties = interpreter.Acted.properties;
            if (properties == null) return;
            for (int i = 0; i + 2 < properties.Length; i += 3)
                if ((properties[i] & 0x3FFF) == interpreter.ActedFrame)
                    ActedMoveProperty.Invoke(new ActedMovePropertyEvent {
                        player = player, graph = interpreter.ActedGraph, move = interpreter.ActedMove,
                        frame = interpreter.ActedFrame, rawFrame = properties[i], id = properties[i + 1], value = properties[i + 2]
                    });
        }

        void RaiseProperties(int i, CommandGraph.Move move, int frame)
        {
            var p = move.properties;
            if (p == null || frame < 1) return;
            for (int k = 0; k + 2 < p.Length; k += 3)
            {
                if ((p[k] & 0x3FFF) != frame) continue;
                long id = p[k + 1], value = p[k + 2];
                if (id == SlowDown && !HasProperty(interpreters[1 - i].Current, SlowDownImmunity))
                {
                    // Value: level in the high word, duration in ticks in the low word (inferred).
                    int level = (int)(value >> 16) & 0xFFFF, ticks = (int)(value & 0xFFFF);
                    slowTicks[1 - i] = ticks; slowFactor[1 - i] = 1f / (Math.Max(1, level) + 1); slowClock[1 - i] = 0;
                }
                MoveProperty?.Invoke(i, id, value);
            }
        }

        // Alias slots 7 and 10 are the face-up and face-down lying loops (dwKAM_00U/D); every getup
        // from them is an input cancel (stand, roll, getup kick), so an idle fighter stays down.
        static bool IsAlias(CommandGraph graph, int move, int slot) =>
            graph.aliases != null && graph.aliases.Length > slot && graph.aliases[slot] == move;

        static bool IsDownedIdle(CommandGraph graph, int move) =>
            graph.aliases != null && graph.aliases.Length > 10 && (move == graph.aliases[7] || move == graph.aliases[10]);

        static bool HasProperty(CommandGraph.Move move, long id)
        {
            var p = move.properties;
            if (p != null) for (int k = 0; k + 2 < p.Length; k += 3) if (p[k + 1] == id) return true;
            return false;
        }

        /// <summary>True while the fighter's current move carries the property (on any frame).</summary>
        public bool CurrentMoveHas(int player, long id) => UsesCommandGraphs && HasProperty(interpreters[player].Current, id);

        static float AngleTo(FighterState f, FighterState other) => (float)Math.Abs(Math.IEEERemainder(
            Math.Atan2(other.z - f.z, other.x - f.x) - Math.Atan2(f.dirZ, f.dirX), 2 * Math.PI) * 180 / Math.PI);

        static void TurnToward(FighterState f, FighterState other, float maxDegrees)
        {
            double to = Math.Atan2(other.z - f.z, other.x - f.x), from = Math.Atan2(f.dirZ, f.dirX);
            double delta = Math.IEEERemainder(to - from, 2 * Math.PI), limit = maxDegrees * Math.PI / 180;
            double heading = from + Math.Max(-limit, Math.Min(limit, delta));
            f.dirX = (float)Math.Cos(heading); f.dirZ = (float)Math.Sin(heading);
        }

        static void FaceOpponent(FighterState f, FighterState other)
        {
            float dx = other.x - f.x, dz = other.z - f.z, length = (float)Math.Sqrt(dx * dx + dz * dz);
            if (length > 1e-4f) { f.dirX = dx / length; f.dirZ = dz / length; }
        }

        int ScreenSide(FighterState f, FighterState other)
        {
            float side = (other.x - f.x) * screenRightX + (other.z - f.z) * screenRightZ;
            return side > 0 ? 1 : side < 0 ? -1 : f.facing;
        }

        void UpdateScreenAxis()
        {
            var a = fighters[0]; var b = fighters[1];
            float dx = b.x - a.x, dz = b.z - a.z, length = (float)Math.Sqrt(dx * dx + dz * dz);
            if (length < .05f) return;
            dx /= length; dz /= length;
            // Keep the orientation closest to the previous axis so the view never flips.
            if (dx * screenRightX + dz * screenRightZ < 0) { dx = -dx; dz = -dz; }
            screenRightX = dx; screenRightZ = dz;
        }

        // Standing bodies keep apart; a downed or airborne body is never pushed (it moves only by its
        // own animation), the standing fighter is moved off it instead, and two non-standing bodies
        // pass each other.
        // The body on the floor: from the feet to the head, as the clip poses it (a lunge leans it
        // forward, a lying body stretches it along the floor from feet to head).
        static void PlaceHead(FighterState f, CommandGraph.Move move, CommandGraph graph)
        {
            f.headX = f.feetX = f.x; f.headZ = f.feetZ = f.z;
            if (move.clip < 0 || move.clip >= graph.clips.Length) return;
            var body = graph.clips[move.clip].body;
            int step = Math.Max(1, graph.bodyStep), fields = graph.bodyFields > 0 ? graph.bodyFields : 4;
            if (body == null || body.Length < fields) return;
            int k = Math.Min(Math.Max(f.moveFrame - 1, 0) / step, body.Length / fields - 1) * fields;
            double a = f.turn * Math.PI / 180;
            float bx = (float)(f.dirX * Math.Cos(a) - f.dirZ * Math.Sin(a)), bz = (float)(f.dirX * Math.Sin(a) + f.dirZ * Math.Cos(a));
            float forward = body[k + 2] / 100f, left = body[k + 3] / 100f;
            f.headX = f.x + forward * bx - left * bz; f.headZ = f.z + forward * bz + left * bx;
            if (fields < 6) return;
            forward = body[k + 4] / 100f; left = body[k + 5] / 100f;
            f.feetX = f.x + forward * bx - left * bz; f.feetZ = f.z + forward * bz + left * bx;
        }

        // A body on the floor that changes move (a fall ending in a lying loop, a roll, a bounce) keeps
        // its feet-to-head line: some falls end lying across the facing (sDm_ROL00RD rolls to the side)
        // while the lying loop they lead to lies along it, which would swing the body round in one tick.
        // The heading turns instead; once the fighter gets up, the rising pivot faces the opponent again.
        const float LyingLength = .6f, LyingCut = 30;

        static void KeepLyingLine(FighterState f, CommandGraph.Move move, CommandGraph graph, float lineX, float lineZ)
        {
            float nx = f.headX - f.feetX, nz = f.headZ - f.feetZ;
            if (lineX * lineX + lineZ * lineZ < LyingLength * LyingLength || nx * nx + nz * nz < LyingLength * LyingLength) return;
            double angle = Math.Atan2(nx * lineZ - nz * lineX, nx * lineX + nz * lineZ);
            if (Math.Abs(angle) * 180 / Math.PI < LyingCut) return;
            float c = (float)Math.Cos(angle), s = (float)Math.Sin(angle);
            float dx = f.dirX * c - f.dirZ * s, dz = f.dirX * s + f.dirZ * c;
            f.dirX = dx; f.dirZ = dz; f.headingCuts++;
            PlaceHead(f, move, graph);
        }

        // Bodies (feet-to-head lines on the floor) stay this far apart, besides the 0.8 m between
        // positions: a fighting stance keeps about 0.56 m, so it only acts when a body leans into the
        // other or a standing fighter steps onto any part of a lying body.
        const float TorsoClearance = .5f;

        void SeparateTorsos()
        {
            var a = fighters[0]; var b = fighters[1];
            bool aStands = !a.downed && !a.airborne, bStands = !b.downed && !b.airborne;
            if (!aStands && !bStands) return;
            ClosestPoints(a.feetX, a.feetZ, a.headX, a.headZ, b.feetX, b.feetZ, b.headX, b.headZ, out float pax, out float paz, out float pbx, out float pbz);
            float dx = pbx - pax, dz = pbz - paz, length = (float)Math.Sqrt(dx * dx + dz * dz);
            if (length >= TorsoClearance) return;
            if (length < 1e-4f) { dx = b.x - a.x; dz = b.z - a.z; length = (float)Math.Sqrt(dx * dx + dz * dz); }
            if (length < 1e-4f) { dx = screenRightX; dz = screenRightZ; length = 1; }
            float push = TorsoClearance - (pbx - pax) * dx / length - (pbz - paz) * dz / length;
            if (push <= 0) return;
            dx /= length; dz /= length;
            float aShare = aStands && bStands ? .5f : aStands ? 1 : 0, bShare = aStands && bStands ? .5f : bStands ? 1 : 0;
            Move(a, -dx * push * aShare, -dz * push * aShare);
            Move(b, dx * push * bShare, dz * push * bShare);
        }

        void Move(FighterState f, float dx, float dz)
        {
            float nx = Clamp(f.x + dx, -rules.arenaHalfWidth, rules.arenaHalfWidth), nz = Clamp(f.z + dz, -rules.arenaHalfDepth, rules.arenaHalfDepth);
            f.headX += nx - f.x; f.headZ += nz - f.z; f.feetX += nx - f.x; f.feetZ += nz - f.z; f.x = nx; f.z = nz;
        }

        // Closest points between floor segments p0-p1 and q0-q1.
        static void ClosestPoints(float p0x, float p0z, float p1x, float p1z, float q0x, float q0z, float q1x, float q1z,
            out float px, out float pz, out float qx, out float qz)
        {
            float ux = p1x - p0x, uz = p1z - p0z, vx = q1x - q0x, vz = q1z - q0z, wx = p0x - q0x, wz = p0z - q0z;
            float a = ux * ux + uz * uz, b = ux * vx + uz * vz, c = vx * vx + vz * vz, d = ux * wx + uz * wz, e = vx * wx + vz * wz;
            float den = a * c - b * b, s, t;
            if (a < 1e-8f && c < 1e-8f) { s = t = 0; }
            else if (a < 1e-8f) { s = 0; t = Clamp01(e / c); }
            else if (c < 1e-8f) { t = 0; s = Clamp01(-d / a); }
            else
            {
                s = den > 1e-8f ? Clamp01((b * e - c * d) / den) : 0;
                t = (b * s + e) / c;
                if (t < 0) { t = 0; s = Clamp01(-d / a); }
                else if (t > 1) { t = 1; s = Clamp01((b - d) / a); }
            }
            px = p0x + ux * s; pz = p0z + uz * s; qx = q0x + vx * t; qz = q0z + vz * t;
        }

        static float Clamp01(float v) => v < 0 ? 0 : v > 1 ? 1 : v;

        void SeparateBodies()
        {
            var a = fighters[0]; var b = fighters[1];
            float dx = b.x - a.x, dz = b.z - a.z, length = (float)Math.Sqrt(dx * dx + dz * dz);
            const float minimum = .8f;
            if (length >= minimum) return;
            bool aStands = !a.downed && !a.airborne, bStands = !b.downed && !b.airborne;
            if (!aStands && !bStands) return;
            if (length < 1e-4f) { dx = screenRightX; dz = screenRightZ; length = 1; }
            float overlap = (minimum - length) / length;
            float aShare = aStands && bStands ? .5f : aStands ? 1 : 0, bShare = aStands && bStands ? .5f : bStands ? 1 : 0;
            Move(a, -dx * overlap * aShare, -dz * overlap * aShare);
            Move(b, dx * overlap * bShare, dz * overlap * bShare);
        }

        HitEvent? CommandContact(int i)
        {
            var a = fighters[i]; var d = fighters[1 - i];
            // The move and frame that just played: a cancel on an active frame still lets it hit.
            var move = interpreters[i].Acted;
            int frame = interpreters[i].ActedFrame;
            if (a.actedConnected || !Attacking(move) || frame < move.firstActive || frame > Math.Max(move.firstActive, move.lastActive)) return null;
            float rx = d.x - a.x, rz = d.z - a.z;
            float forward = rx * a.dirX + rz * a.dirZ, lateral = Math.Abs(rz * a.dirX - rx * a.dirZ);
            if (forward < 0 || forward > CommandReach || lateral > CommandLateralTolerance) return null;
            var height = Height(move.hitlevel);
            bool low = height == HitHeight.Low || height == HitHeight.SpecialLow;
            // A downed fighter is only reached by lows; lows pass under a jump (not under a juggled body).
            if (d.downed && !low) return null;
            if (d.airborne && !d.reacting && low) return null;
            if (height == HitHeight.High && d.crouch) return null;
            bool blocked = d.guard && (height == HitHeight.Low ? d.crouch : height == HitHeight.Mid ? !d.crouch : true);
            // Tekken 7: a counter hit lands during the defender's startup or active frames; a hit in
            // the recovery is a normal hit, a punish.
            var defending = interpreters[1 - i].Current;
            bool defenderAttacking = !blocked && Attacking(defending) && !d.reacting;
            bool counter = defenderAttacking && d.moveFrame <= Math.Max(defending.firstActive, defending.lastActive);
            bool punish = defenderAttacking && !counter;
            contexts[i].counterHit = counter;
            // Juggle (614) and opponent-downed (126) hit conditions read the defender's state.
            contexts[i].opponentAirborne = d.airborne; contexts[i].opponentDown = d.downed;
            var condition = HitConditionFor(interpreters[i].ActedGraph, i, move);
            return new HitEvent { attacker = i, defender = 1 - i, damage = blocked ? 0 : condition?.damage ?? move.damage,
                reaction = condition?.reaction ?? -1, blocked = blocked, counterHit = counter, punish = punish, height = height,
                throwHit = (move.hitlevel & 0x400000) != 0 };
        }

        CommandGraph.HitCondition HitConditionFor(CommandGraph graph, int player, CommandGraph.Move move)
        {
            if (move.hitConditionStart < 0) return null;
            // The first entry whose requirements hold applies; an unconditional entry closes the list.
            for (int k = move.hitConditionStart; k < graph.hitConditions.Length && k < move.hitConditionStart + 32; k++)
            {
                var h = graph.hitConditions[k];
                if (h.reqCount == 0 || interpreters[player].RequirementsHold(h.reqStart, h.reqCount)) return h;
            }
            return null;
        }

        // Power crush armour (original property 0x81BD): a non-zero value from its frame on arms the move, a later
        // zero disarms it; frame 0x8001 is the first frame. It is on every Rage Art startup (RageArts01) and attack
        // (RageArts02, until just after its active frame), each fighter's power crush (Jz_lrk, Kz_tukirpsp, Ft_BodyAttack,
        // ...; their *arm variants from frame 1) and Kazuya's transform. Highs and mids are absorbed; lows and throws are not.
        public const long Armour = 0x81BD;
        static bool Armoured(CommandGraph.Move move, int frame)
        {
            var p = move.properties; bool on = false; int at = -1;
            if (p != null)
                for (int k = 0; k + 2 < p.Length; k += 3)
                {
                    int from = (int)(p[k] & 0x3FFF);
                    if (p[k + 1] == Armour && from <= frame && from >= at) { on = p[k + 2] != 0; at = from; }
                }
            return on;
        }

        /// <summary>True while the fighter's current move frame is armoured (power crush, Rage Art).</summary>
        public bool IsArmoured(int player) => UsesCommandGraphs && Armoured(interpreters[player].Current, fighters[player].moveFrame);

        void ApplyCommandHit(HitEvent hit)
        {
            var a = fighters[hit.attacker]; var d = fighters[hit.defender];
            if (!hit.blocked && !hit.throwHit && (hit.height == HitHeight.High || hit.height == HitHeight.Mid) &&
                d.health > hit.damage && !d.reacting && Armoured(interpreters[hit.defender].Current, d.moveFrame))
            {
                // Absorbed: the attack connects and deals its damage, the defender keeps its move and place.
                a.connected = a.actedConnected = true; a.blocked = false; a.counterHit = false;
                d.health -= hit.damage; d.flash = 9;
                hit.absorbed = true; hit.counterHit = hit.punish = false; hit.reaction = -1;
                Hit?.Invoke(hit);
                return;
            }
            // A struck or guarding body is turned square to the attacker, so its reaction (stagger,
            // knockback, launch, fall) plays away from them. A body lying on the floor, or still rolling or
            // getting up from it (rising), keeps its heading: turning it would spin it round on the floor.
            if (!d.downed && !rising[hit.defender]) FaceOpponent(d, a);
            // The hit also counts for a follow-through the move cancelled into on that frame.
            a.connected = a.actedConnected = true; a.blocked = hit.blocked; a.counterHit = hit.counterHit;
            d.health = Math.Max(0, d.health - hit.damage); d.flash = hit.blocked ? 4 : 9;
            StartPushback(hit, a, d);
            // Reaction moves live in the attacker's moveset; the defender runs them there (as Tekken 7 does).
            int reaction = ReactionMove(hit);
            if (reaction >= 0)
            {
                interpreters[hit.defender].Borrow(graphs[hit.attacker], reaction); fallbackStun[hit.defender] = 0;
                heldBy[hit.defender] = hit.attacker; heldMove[hit.defender] = interpreters[hit.attacker].Move;
                interpreters[hit.defender].ReleaseHolds = false;
            }
            else { interpreters[hit.defender].Force(graphs[hit.defender].stance); fallbackStun[hit.defender] = hit.blocked ? 9 : FallbackStun; }
            if (immediateKoCollapse && d.health <= 0 && !hit.blocked && StandingReaction(reaction >= 0 ? graphs[hit.attacker] : graphs[hit.defender], interpreters[hit.defender].Current))
            {
                int fatal = FatalMove(hit.defender);
                if (fatal >= 0)
                {
                    interpreters[hit.defender].Force(fatal); fallbackStun[hit.defender] = 0;
                    heldBy[hit.defender] = heldMove[hit.defender] = -1;
                }
            }
            d.reacting = true; d.move = interpreters[hit.defender].Move; d.moveFrame = 0;
            d.borrowed = interpreters[hit.defender].Borrowed; d.moveOwner = d.borrowed ? hit.attacker : hit.defender;
            d.connected = d.blocked = d.counterHit = false;
            Hit?.Invoke(hit);
        }

        // A reaction that keeps the body on its feet: never airborne and not a downed state. A short hop that
        // lands on its feet and goes straight back to the stance (sDw_JACKup, airborne frames 1-24) counts
        // too: knocked out by one, the loser otherwise landed, stood in its stance and only then collapsed.
        static bool StandingReaction(CommandGraph graph, CommandGraph.Move move) => (move.vuln & 0x80) == 0 &&
            (move.airStart <= 0 && move.airEnd <= 0 || move.airEnd <= 30 && move.transition == graph.stance);
        // The defender's standing death collapse, which its stance reaches through condition 135 (on death).
        readonly int[] fatalMove = { -2, -2 };
        int FatalMove(int player)
        {
            if (fatalMove[player] == -2) fatalMove[player] = Array.FindIndex(graphs[player].moves, m => m.name == "fatal_s");
            return fatalMove[player];
        }

        // The original's knockback: the reaction names a pushback record for the side that was struck
        // (front, backturned, counter hit, downed, block). It carries the defender away from the
        // attacker over its duration; a new hit replaces the push in progress.
        void StartPushback(HitEvent hit, FighterState a, FighterState d)
        {
            int i = hit.defender; push[i] = null;
            var graph = graphs[hit.attacker];
            // A body off its feet (lying or juggled) takes the "downed" pushback: the long slide that
            // carries juggles and floor hits away; the front one is only a short nudge for most moves.
            // The knock-out blow throws the body the same long way, so it falls clear of the winner.
            bool knockOut = !hit.blocked && d.health <= 0;
            bool hasReaction = graph.pushbacks != null && hit.reaction >= 0 && hit.reaction < graph.reactions.Length;
            var refs = hasReaction ? graph.reactions[hit.reaction].pushbacks : null;
            int side = hit.blocked ? 6 : d.downed || d.airborne || knockOut ? 5 : d.backturned ? 1 : hit.counterHit ? 4 : 0;
            CommandGraph.Pushback record = null;
            if (refs != null && side < refs.Length)
            {
                int index = refs[side] >= 0 ? refs[side] : refs[0];
                if (index >= 0 && index < graph.pushbacks.Length) record = graph.pushbacks[index];
            }
            // Some knock-out reactions carry no long slide; those still throw the body at least
            // KnockOutSlide (our choice, not original data) so the death fall lands clear.
            if (knockOut && (record == null || PushbackTravel(record) < KnockOutSlide)) record = KnockOutPush;
            if (record == null || PushbackFrames(record) == 0) return;
            float dx = d.x - a.x, dz = d.z - a.z, length = (float)Math.Sqrt(dx * dx + dz * dz);
            if (length < 1e-4f) { dx = a.dirX; dz = a.dirZ; length = 1; }
            push[i] = record; pushFrame[i] = 0; pushX[i] = dx / length; pushZ[i] = dz / length;
        }

        void ApplyPushback(int i, FighterState f)
        {
            var record = push[i];
            if (record == null) return;
            int frame = pushFrame[i]++;
            if (frame >= PushbackFrames(record)) { push[i] = null; return; }
            float step = PushbackStep(record, frame);
            f.x = Clamp(f.x + pushX[i] * step, -rules.arenaHalfWidth, rules.arenaHalfWidth);
            f.z = Clamp(f.z + pushZ[i] * step, -rules.arenaHalfDepth, rules.arenaHalfDepth);
        }

        const float KnockOutSlide = 1f;
        static readonly CommandGraph.Pushback KnockOutPush = new CommandGraph.Pushback { duration = 20, displacement = 50 };

        static float PushbackTravel(CommandGraph.Pushback record)
        {
            float total = 0;
            for (int k = PushbackFrames(record) - 1; k >= 0; k--) total += PushbackStep(record, k);
            return Math.Abs(total);
        }

        static int PushbackFrames(CommandGraph.Pushback record)
        {
            int curve = 0;
            if (record.curve != null) for (int k = 0; k < record.curve.Length; k++) if (record.curve[k] != 0) curve = k + 1;
            return Math.Max(curve, record.displacement != 0 ? record.duration : 0);
        }

        /// <summary>Metres pushed on this frame: the curve's millimetres plus the displacement's.</summary>
        static float PushbackStep(CommandGraph.Pushback record, int frame)
        {
            float mm = record.curve != null && frame < record.curve.Length ? record.curve[frame] : 0;
            if (frame < record.duration) mm += record.displacement;
            return mm / 1000f;
        }

        int ReactionMove(HitEvent hit)
        {
            var graph = graphs[hit.attacker];
            if (hit.reaction < 0 || hit.reaction >= graph.reactions.Length) return -1;
            var slots = graph.reactions[hit.reaction].moves;
            bool crouch = fighters[hit.defender].crouch;
            // Slots: 1 standing/default, 2 crouch, 3 counter hit, 4 crouching counter hit, 11 block,
            // 12 crouch block, 9/10 backturned (standing/crouching), 13/14 downed lying face up / face down
            // (dDw_OUC00_ / dDwrOUC00_: their clips lie head away from / toward the attacker, so the
            // wrong one turns the body end over end). Airborne hits use the juggle hit condition's default reaction.
            bool turned = fighters[hit.defender].backturned;
            int slot = fighters[hit.defender].downed ? (LyingHeadForward(hit.defender) ? 14 : 13) :
                turned && !hit.blocked ? (crouch ? 10 : 9) :
                hit.blocked ? (crouch ? 12 : 11) : hit.counterHit ? (crouch ? 4 : 3) : crouch ? 2 : 1;
            int move = slots[slot] > 0 ? slots[slot] : slot == 13 && slots[14] > 0 ? slots[14] : slots[1];
            if (move <= 0 || move >= graph.moves.Length) return -1;
            // A juggled body hit by a move without a juggle-specific reaction plays the standard
            // airborne hit (alias slot 30, sDw_AIR00_) instead of a standing one.
            if (fighters[hit.defender].airborne && !hit.blocked && (graph.moves[move].vuln & 0xFF) != 0x02 &&
                graph.aliases != null && graph.aliases.Length > 30 && graph.aliases[30] > 0)
                move = graph.aliases[30];
            return move;
        }

        /// <summary>Whether a body on the floor lies with its head toward its facing (face down, as
        /// dwKAM_00D) rather than its feet (face up, as dwKAM_00U): from the clip's body track.</summary>
        bool LyingHeadForward(int player)
        {
            var f = fighters[player]; var graph = graphs[f.moveOwner];
            var move = graph.moves[f.move];
            if (move.clip < 0 || move.clip >= graph.clips.Length) return false;
            var body = graph.clips[move.clip].body;
            int step = Math.Max(1, graph.bodyStep), fields = graph.bodyFields;
            if (body == null || fields < 6 || body.Length < fields) return false;
            int k = Math.Min(Math.Max(f.moveFrame - 1, 0) / step, body.Length / fields - 1) * fields;
            return body[k + 2] > body[k + 4];
        }

        // ---- Prototype rules ----------------------------------------------------------------

        void UpdateFighter(int i, PlayerCommand c)
        {
            var f = fighters[i]; var other = fighters[1 - i];
            if (f.flash > 0) f.flash--;
            if (c.attack >= 0 && c.attack < moves[i].Length) { f.buffer = c.attack; f.bufferTicks = 8; }
            else if (f.bufferTicks > 0) f.bufferTicks--;
            if (f.bufferTicks == 0) f.buffer = -1;
            f.guard = false;
            if (f.stun > 0) { f.stun--; return; }
            if (f.attack >= 0)
            {
                f.attackFrame++;
                var move = Move(i, f.attack);
                if (f.attackFrame >= move.startup + move.active + move.recovery) f.attack = -1;
                else return;
            }
            f.facing = other.x >= f.x ? 1 : -1;
            f.crouch = c.crouch;
            f.guard = c.guard || c.x * f.facing < -.3f;
            if (f.buffer >= 0)
            {
                f.attack = f.buffer; f.buffer = -1; f.bufferTicks = 0;
                f.attackFrame = 0; f.connected = false; f.guard = false;
                return;
            }
            float x = Clamp(c.x, -1, 1), z = Clamp(c.z, -1, 1);
            float magnitude = (float)Math.Sqrt(x * x + z * z);
            if (magnitude > 1) { x /= magnitude; z /= magnitude; }
            float speed = rules.walkSpeed / 60f * (c.guard || c.crouch ? .35f : 1);
            f.x = Clamp(f.x + x * speed, -rules.arenaHalfWidth, rules.arenaHalfWidth);
            f.z = Clamp(f.z + z * speed, -rules.arenaHalfDepth, rules.arenaHalfDepth);
        }
        HitEvent? Contact(int i)
        {
            var a = fighters[i]; var d = fighters[1 - i];
            if (a.attack < 0 || a.connected) return null;
            var move = Move(i, a.attack);
            if (a.attackFrame < move.startup || a.attackFrame >= move.startup + move.active) return null;
            float forward = (d.x - a.x) * a.facing;
            if (forward < 0 || forward > move.reach || Math.Abs(a.z - d.z) > .65f) return null;
            if (move.height == HitHeight.High && d.crouch) return null;
            bool blocked = d.guard && d.stun == 0 && d.attack < 0 && (move.height == HitHeight.Low ? d.crouch : !d.crouch);
            return new HitEvent { attacker = i, defender = 1 - i, damage = blocked ? 0 : move.damage,
                stun = blocked ? 9 : move.stun, blocked = blocked, reaction = -1, height = move.height };
        }
        void Apply(HitEvent hit)
        {
            if (UsesCommandGraphs) { ApplyCommandHit(hit); return; }
            var a = fighters[hit.attacker]; var d = fighters[hit.defender];
            a.connected = true;
            d.health = Math.Max(0, d.health - hit.damage); d.flash = hit.blocked ? 4 : 9;
            d.stun = Math.Max(d.stun, hit.stun);
            d.attack = -1;
            d.x = Clamp(d.x + a.facing * (hit.blocked ? .12f : .23f), -rules.arenaHalfWidth, rules.arenaHalfWidth);
            Hit?.Invoke(hit);
        }
    }
}
