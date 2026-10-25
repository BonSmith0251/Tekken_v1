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
        /// <summary>Shallow copy for presentation (render interpolation between 60 Hz ticks).</summary>
        public FighterState Snapshot() => (FighterState)MemberwiseClone();
    }

    public struct HitEvent
    {
        public int attacker, defender, damage, stun;
        public bool blocked, counterHit;
        /// <summary>Original reaction record in the attacker's moveset, or -1 (command graphs only).</summary>
        public int reaction;
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
        const long SlowDown = 0x828C, SlowDownImmunity = 0x828D;
        readonly int[] slowTicks = new int[2];
        // A hit reaction is held while the attacker stays in the move that landed it.
        readonly int[] heldBy = { -1, -1 }, heldMove = { -1, -1 };
        readonly float[] slowFactor = { 1, 1 }, slowClock = new float[2];
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
                slowTicks[i] = 0; slowClock[i] = 0; heldBy[i] = heldMove[i] = -1;
                if (interpreters != null)
                { interpreters[i].Reset(); f.move = interpreters[i].Move; f.moveFrame = 0; f.moveOwner = i; f.borrowed = false; f.clipKey = null; fallbackStun[i] = 0; }
            }
        }
        static float Clamp(float n, float min, float max) => Math.Max(min, Math.Min(max, n));
        public void Tick(PlayerCommand p1, PlayerCommand p2)
        {
            // After a KO the original knockdown reactions keep playing, without further hits.
            if (UsesCommandGraphs && (phase == MatchPhase.RoundOver || phase == MatchPhase.MatchOver))
            { UpdateCommandFighter(0, PlayerCommand.Neutral); UpdateCommandFighter(1, PlayerCommand.Neutral); }
            if (phase == MatchPhase.MatchOver) return;
            if (phase != MatchPhase.Fight)
            {
                if (--phaseTicks <= 0)
                { if (phase == MatchPhase.Intro) phase = MatchPhase.Fight; else StartRound(); }
                return;
            }
            var a = fighters[0]; var b = fighters[1];
            if (UsesCommandGraphs) { UpdateScreenAxis(); UpdateCommandFighter(0, p1); UpdateCommandFighter(1, p2); SeparateBodies(); }
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
            context.rage = f.health * 4 <= rules.health;
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
            // Fighters turn to face each other outside attacks and reactions, and an attack keeps
            // tracking its target through its startup (until the first active frame). Bodies on the
            // floor or in the air, and a knocked-out fighter, keep their heading.
            bool grounded = Downed(move, f.moveFrame), inAir = Airborne(move, f.moveFrame);
            bool tracking = !Attacking(move) || f.moveFrame < move.firstActive;
            if (tracking && !f.reacting && !grounded && !inAir && f.health > 0) FaceOpponent(f, other);
            f.facing = ScreenSide(f, other);
            bool down = direction == 1 || direction == 2 || direction == 3;
            f.crouch = down && !f.downed && !f.airborne;
            // Fighters on the ground or in the air cannot guard.
            f.guard = !Attacking(move) && !f.reacting && fallbackStun[i] == 0 && !f.downed && !f.airborne && (direction == 4 || direction == 1);
            f.stun = f.reacting || fallbackStun[i] > 0 ? 1 : 0;
            f.attacking = Attacking(move);
            f.downed = Downed(move, f.moveFrame);
            f.backturned = interpreter.Backturned; f.turn = interpreter.Turn;
            f.airborne = Airborne(move, f.moveFrame);
            f.downedTicks = !interpreter.Borrowed && IsDownedIdle(interpreter.graph, f.move) ? f.downedTicks + 1 : 0;
            f.lowAttack = f.attacking && Height(move.hitlevel) == HitHeight.Low;
            f.attack = -1;
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
            a.x = Clamp(a.x - dx * overlap * aShare, -rules.arenaHalfWidth, rules.arenaHalfWidth); a.z = Clamp(a.z - dz * overlap * aShare, -rules.arenaHalfDepth, rules.arenaHalfDepth);
            b.x = Clamp(b.x + dx * overlap * bShare, -rules.arenaHalfWidth, rules.arenaHalfWidth); b.z = Clamp(b.z + dz * overlap * bShare, -rules.arenaHalfDepth, rules.arenaHalfDepth);
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
            bool counter = !blocked && Attacking(interpreters[1 - i].Current) && !d.reacting;
            contexts[i].counterHit = counter;
            // Juggle (614) and opponent-downed (126) hit conditions read the defender's state.
            contexts[i].opponentAirborne = d.airborne; contexts[i].opponentDown = d.downed;
            var condition = HitConditionFor(interpreters[i].ActedGraph, i, move);
            return new HitEvent { attacker = i, defender = 1 - i, damage = blocked ? 0 : condition?.damage ?? move.damage,
                reaction = condition?.reaction ?? -1, blocked = blocked, counterHit = counter };
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

        void ApplyCommandHit(HitEvent hit)
        {
            var a = fighters[hit.attacker]; var d = fighters[hit.defender];
            // A struck or guarding body is turned square to the attacker, so its reaction (stagger,
            // knockback, launch, fall) plays away from them; a body lying on the floor keeps its heading.
            if (!d.downed) FaceOpponent(d, a);
            // The hit also counts for a follow-through the move cancelled into on that frame.
            a.connected = a.actedConnected = true; a.blocked = hit.blocked; a.counterHit = hit.counterHit;
            d.health = Math.Max(0, d.health - hit.damage); d.flash = hit.blocked ? 4 : 9;
            // Reaction moves live in the attacker's moveset; the defender runs them there (as Tekken 7 does).
            int reaction = ReactionMove(hit);
            if (reaction >= 0)
            {
                interpreters[hit.defender].Borrow(graphs[hit.attacker], reaction); fallbackStun[hit.defender] = 0;
                heldBy[hit.defender] = hit.attacker; heldMove[hit.defender] = interpreters[hit.attacker].Move;
                interpreters[hit.defender].ReleaseHolds = false;
            }
            else { interpreters[hit.defender].Force(graphs[hit.defender].stance); fallbackStun[hit.defender] = hit.blocked ? 9 : FallbackStun; }
            d.reacting = true; d.move = interpreters[hit.defender].Move; d.moveFrame = 0;
            d.borrowed = interpreters[hit.defender].Borrowed; d.moveOwner = d.borrowed ? hit.attacker : hit.defender;
            d.connected = d.blocked = d.counterHit = false;
            Hit?.Invoke(hit);
        }

        int ReactionMove(HitEvent hit)
        {
            var graph = graphs[hit.attacker];
            if (hit.reaction < 0 || hit.reaction >= graph.reactions.Length) return -1;
            var slots = graph.reactions[hit.reaction].moves;
            bool crouch = fighters[hit.defender].crouch;
            // Slots: 1 standing/default, 2 crouch, 3 counter hit, 4 crouching counter hit, 11 block,
            // 12 crouch block, 9/10 backturned (standing/crouching), 14 downed. Airborne hits use the juggle hit condition's default reaction.
            bool turned = fighters[hit.defender].backturned;
            int slot = fighters[hit.defender].downed ? 14 :
                turned && !hit.blocked ? (crouch ? 10 : 9) :
                hit.blocked ? (crouch ? 12 : 11) : hit.counterHit ? (crouch ? 4 : 3) : crouch ? 2 : 1;
            int move = slots[slot] > 0 ? slots[slot] : slots[1];
            if (move <= 0 || move >= graph.moves.Length) return -1;
            // A juggled body hit by a move without a juggle-specific reaction plays the standard
            // airborne hit (alias slot 30, sDw_AIR00_) instead of a standing one.
            if (fighters[hit.defender].airborne && !hit.blocked && (graph.moves[move].vuln & 0xFF) != 0x02 &&
                graph.aliases != null && graph.aliases.Length > 30 && graph.aliases[30] > 0)
                move = graph.aliases[30];
            return move;
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
                stun = blocked ? 9 : move.stun, blocked = blocked, reaction = -1 };
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
