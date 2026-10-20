using System;

namespace Versus.Remaster
{
    public enum MatchPhase { Intro, Fight, RoundOver, MatchOver }
    public enum HitHeight { High, Mid, Low }

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
        public float x, z;
        public bool guard, crouch;
        public int attack;
        public static PlayerCommand Neutral => new PlayerCommand { attack = -1 };
    }

    public class FighterState
    {
        public float x, z;
        public int health, wins, facing, attack = -1, attackFrame, stun, flash, buffer = -1, bufferTicks;
        public bool guard, crouch, connected;
    }

    public struct HitEvent
    {
        public int attacker, defender, damage, stun;
        public bool blocked;
    }

    /// <summary>60 Hz rules independent of rendering and input devices. Local play only.</summary>
    public class BattleModel
    {
        public readonly MatchRules rules;
        public readonly MoveSpec[][] moves;
        public readonly FighterState[] fighters = { new FighterState(), new FighterState() };
        public MatchPhase phase { get; private set; }
        public int ticksLeft { get; private set; }
        public int phaseTicks { get; private set; }
        public int round { get; private set; }
        public int winner { get; private set; } = -1;
        public string result { get; private set; } = "";
        public event Action<HitEvent> Hit;
        public BattleModel(MatchRules rules, MoveSpec[][] moves = null) { rules.Validate(); this.rules = rules; this.moves = moves ?? new[] { rules.moves, rules.moves }; Restart(); }
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
            ticksLeft = rules.seconds * 60;
            phase = MatchPhase.Intro; phaseTicks = 150;
            for (int i = 0; i < 2; i++)
            {
                var f = fighters[i];
                f.x = i == 0 ? -2 : 2; f.z = 0; f.facing = i == 0 ? 1 : -1;
                f.health = rules.health; f.attack = -1; f.attackFrame = f.stun = f.flash = f.bufferTicks = 0;
                f.buffer = -1; f.guard = f.crouch = f.connected = false;
            }
        }
        static float Clamp(float n, float min, float max) => Math.Max(min, Math.Min(max, n));
        public void Tick(PlayerCommand p1, PlayerCommand p2)
        {
            if (phase == MatchPhase.MatchOver) return;
            if (phase != MatchPhase.Fight)
            {
                if (--phaseTicks <= 0)
                { if (phase == MatchPhase.Intro) phase = MatchPhase.Fight; else StartRound(); }
                return;
            }
            UpdateFighter(0, p1); UpdateFighter(1, p2);
            // Resolve body overlap without preventing sidesteps or changing arena bounds.
            var a = fighters[0]; var b = fighters[1];
            if (Math.Abs(a.z - b.z) < .55f && Math.Abs(a.x - b.x) < .8f)
            {
                float center = Clamp((a.x + b.x) * .5f, -rules.arenaHalfWidth + .4f, rules.arenaHalfWidth - .4f);
                int direction = a.x <= b.x ? 1 : -1;
                a.x = center - direction * .4f; b.x = center + direction * .4f;
            }
            // Snapshot both contacts before applying damage: equal-frame strikes can trade.
            HitEvent? hitA = Contact(0), hitB = Contact(1);
            if (hitA.HasValue) Apply(hitA.Value);
            if (hitB.HasValue) Apply(hitB.Value);
            ticksLeft--;
            if (a.health <= 0 || b.health <= 0 || ticksLeft <= 0)
            {
                winner = a.health == b.health ? -1 : a.health > b.health ? 0 : 1;
                result = ticksLeft <= 0 ? "TIME UP" : winner < 0 ? "DOUBLE KO" : "KNOCKOUT";
                if (winner >= 0) fighters[winner].wins++;
                phase = winner >= 0 && fighters[winner].wins >= rules.roundsToWin ? MatchPhase.MatchOver : MatchPhase.RoundOver;
                phaseTicks = 150;
            }
        }
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
                stun = blocked ? 9 : move.stun, blocked = blocked };
        }
        void Apply(HitEvent hit)
        {
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
