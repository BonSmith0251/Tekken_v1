using System;
using System.Collections.Generic;

namespace Versus.Remaster.Arcade
{
    [Flags]
    public enum ArcadeControls : uint
    {
        None = 0,
        P1Up = 1u << 0, P1Down = 1u << 1, P1Left = 1u << 2, P1Right = 1u << 3,
        P1B1 = 1u << 4, P1B2 = 1u << 5, P1B3 = 1u << 6, P1B4 = 1u << 7,
        P2Up = 1u << 8, P2Down = 1u << 9, P2Left = 1u << 10, P2Right = 1u << 11,
        P2B1 = 1u << 12, P2B2 = 1u << 13, P2B3 = 1u << 14, P2B4 = 1u << 15,
        Start1 = 1u << 16, Start2 = 1u << 17, Coin1 = 1u << 18, Coin2 = 1u << 19,
        Service = 1u << 20, Test = 1u << 21, VolumeUp = 1u << 22, VolumeDown = 1u << 23
    }

    public enum ArcadeEventKind { Coin, Service, Start, Test, VolumeUp, VolumeDown, CoinJam }

    public readonly struct ArcadeInputEvent
    {
        public readonly long id;
        public readonly double time;
        public readonly ArcadeEventKind kind;
        public readonly int side;
        public ArcadeInputEvent(long id, double time, ArcadeEventKind kind, int side)
        { this.id = id; this.time = time; this.kind = kind; this.side = side; }
    }

    public readonly struct ArcadeStickButtons
    {
        public readonly int stick, buttons;
        public ArcadeStickButtons(int stick, int buttons) { this.stick = stick; this.buttons = buttons; }
    }

    // Device-independent, single-consumer core. Sample from an unscaled adapter on the main thread;
    // a background device reader must queue observations for that adapter, not call this concurrently.
    public sealed class ArcadeInput
    {
        readonly List<ArcadeInputEvent> pending = new List<ArcadeInputEvent>();
        double debounceSeconds, jamSeconds;
        readonly double[] lastCoin = { double.NegativeInfinity, double.NegativeInfinity };
        readonly double[] coinHeldSince = { double.NaN, double.NaN };
        readonly bool[] jamReported = new bool[2];
        readonly int[] pressed = new int[2];
        ArcadeControls held;
        long sequence;
        double lastTime = double.NegativeInfinity;
        bool suppressUntilRelease;

        // Initialize from the ledger's persisted lastEventId, after restart recovery.
        public ArcadeInput(ArcadeSettings settings, long lastEventId = 0)
        {
            settings.Validate();
            if (lastEventId < 0) throw new ArgumentOutOfRangeException(nameof(lastEventId));
            sequence = lastEventId; debounceSeconds = settings.coinLockoutMs / 1000.0;
            jamSeconds = settings.coinJamMs / 1000.0;
        }

        public bool OperatorPaused { get; private set; }
        public void SetOperatorPaused(bool value) { OperatorPaused = value; ResetGameplayForTransition(); }
        public void ApplySettings(ArcadeSettings settings)
        { settings.Validate(); debounceSeconds = settings.coinLockoutMs / 1000.0; jamSeconds = settings.coinJamMs / 1000.0; }
        // Rebinding must not turn a currently held key into a new coin/Start edge.
        public void RebaseHeldControls(ArcadeControls controls)
        {
            held = controls; ResetGameplayForTransition();
            for (int i = 0; i < 2; i++)
            { coinHeldSince[i] = (controls & (i == 0 ? ArcadeControls.Coin1 : ArcadeControls.Coin2)) != 0 ? lastTime : double.NaN; jamReported[i] = false; }
        }

        public void Sample(ArcadeControls controls, double unscaledTime)
        {
            if (double.IsNaN(unscaledTime) || double.IsInfinity(unscaledTime) || unscaledTime < 0 || unscaledTime < lastTime)
                throw new ArgumentOutOfRangeException(nameof(unscaledTime), "Use a finite monotonic unscaled clock.");
            lastTime = unscaledTime;
            var rising = controls & ~held;
            for (int chute = 0; chute < 2; chute++)
            {
                var bit = chute == 0 ? ArcadeControls.Coin1 : ArcadeControls.Coin2;
                if ((rising & bit) != 0)
                {
                    coinHeldSince[chute] = unscaledTime;
                    if (unscaledTime - lastCoin[chute] >= debounceSeconds)
                    { lastCoin[chute] = unscaledTime; Queue(unscaledTime, ArcadeEventKind.Coin, chute); }
                }
                if ((controls & bit) == 0) { coinHeldSince[chute] = double.NaN; jamReported[chute] = false; }
                else if (!jamReported[chute] && unscaledTime - coinHeldSince[chute] >= jamSeconds)
                { jamReported[chute] = true; Queue(unscaledTime, ArcadeEventKind.CoinJam, chute); }
            }
            Edge(rising, ArcadeControls.Service, unscaledTime, ArcadeEventKind.Service, 0);
            Edge(rising, ArcadeControls.Start1, unscaledTime, ArcadeEventKind.Start, 0);
            Edge(rising, ArcadeControls.Start2, unscaledTime, ArcadeEventKind.Start, 1);
            Edge(rising, ArcadeControls.Test, unscaledTime, ArcadeEventKind.Test, 0);
            Edge(rising, ArcadeControls.VolumeUp, unscaledTime, ArcadeEventKind.VolumeUp, 0);
            Edge(rising, ArcadeControls.VolumeDown, unscaledTime, ArcadeEventKind.VolumeDown, 0);
            if (suppressUntilRelease && (((uint)controls & 0xFFFFu) == 0)) suppressUntilRelease = false;
            if (!suppressUntilRelease)
                for (int side = 0; side < 2; side++) pressed[side] |= ((int)rising >> (side * 8 + 4)) & 15;
            held = controls;
        }

        // Drain ALL events in this order. The adapter decides whether Start is eligible before Begin.
        // Same-time money events precede Start, and P1 wins simultaneous shared-credit contention.
        // IDs are assigned after sorting so the ledger watermark never drops a reordered coin.
        public ArcadeInputEvent[] DrainEvents()
        {
            pending.Sort((a, b) =>
            {
                int value = a.time.CompareTo(b.time); if (value != 0) return value;
                value = Priority(a.kind).CompareTo(Priority(b.kind)); if (value != 0) return value;
                return a.side.CompareTo(b.side);
            });
            var result = new ArcadeInputEvent[pending.Count];
            for (int i = 0; i < result.Length; i++)
            { var item = pending[i]; result[i] = new ArcadeInputEvent(checked(++sequence), item.time, item.kind, item.side); }
            pending.Clear(); return result;
        }

        public ArcadeStickButtons ConsumeGameplay(int side)
        {
            if (side < 0 || side > 1) throw new ArgumentOutOfRangeException(nameof(side));
            if (OperatorPaused || suppressUntilRelease) return new ArcadeStickButtons(5, 0);
            int bits = (int)((uint)held >> (side * 8)) & 255;
            int x = ((bits & 8) != 0 ? 1 : 0) - ((bits & 4) != 0 ? 1 : 0);
            int y = ((bits & 1) != 0 ? 1 : 0) - ((bits & 2) != 0 ? 1 : 0);
            int buttons = (bits >> 4) | pressed[side]; pressed[side] = 0;
            return new ArcadeStickButtons(5 + x + y * 3, buttons);
        }

        // Presentation reads held buttons without stealing a short gameplay tap from the next tick.
        public int HeldButtons(int side)
        {
            if (side < 0 || side > 1) throw new ArgumentOutOfRangeException(nameof(side));
            return OperatorPaused || suppressUntilRelease ? 0 : ((int)held >> (side * 8 + 4)) & 15;
        }

        // Do not clear cabinet money events when scenes change. Require gameplay release instead.
        public void ResetGameplayForTransition()
        { pressed[0] = pressed[1] = 0; suppressUntilRelease = true; }

        void Edge(ArcadeControls rising, ArcadeControls bit, double time, ArcadeEventKind kind, int side)
        { if ((rising & bit) != 0) Queue(time, kind, side); }
        void Queue(double time, ArcadeEventKind kind, int side) => pending.Add(new ArcadeInputEvent(0, time, kind, side));
        static int Priority(ArcadeEventKind kind) => kind == ArcadeEventKind.Test ? -1 : kind == ArcadeEventKind.Coin ? 0 : kind == ArcadeEventKind.Service ? 1 :
            kind == ArcadeEventKind.Start ? 2 : 3 + (int)kind;
    }
}
