using System;
using System.IO;
using System.Runtime.Serialization;

namespace Versus.Remaster.Arcade
{
    public enum ArcadePaidAction { Start, Continue, Challenge }
    public enum ArcadeLedgerResult { Accepted, Duplicate, InsufficientCredit, SideAlreadyActive }

    [Serializable]
    public sealed class ArcadeSession
    {
        public long id;
        public int side, pool, cost;
        public ArcadePaidAction action;
        public bool matchReady;
        public ArcadeSession Copy() => (ArcadeSession)MemberwiseClone();
    }

    [Serializable]
    public sealed class ArcadeLedgerState
    {
        public int schema = 1;
        public long generation, lastEventId;
        public bool sharedCredits;
        public int[] coinsPerCredit = new int[2];
        // Total balances retain overflow beyond the display cap; spending makes it available again.
        public long[] credits = new long[2], coins = new long[2];
        public int[] partialCoins = new int[2];
        public long serviceCredits, starts, continues, challenges, refundedCredits;
        // Serializable fields are required by DataContractJsonSerializer unless explicitly optional.
        // Pre-M5 schema-1 ledgers omit these additions; keep original money fields required.
        [OptionalField(VersionAdded = 2)] public ArcadeSettings operatorSettings;
        [OptionalField(VersionAdded = 2)] public long bookkeepingClears, creditClears;
        public ArcadeSession[] sessions = new ArcadeSession[2];

        public ArcadeLedgerState Copy()
        {
            var copy = (ArcadeLedgerState)MemberwiseClone();
            copy.coinsPerCredit = (int[])coinsPerCredit.Clone();
            copy.credits = (long[])credits.Clone(); copy.coins = (long[])coins.Clone();
            copy.partialCoins = (int[])partialCoins.Clone();
            copy.operatorSettings = operatorSettings?.Copy();
            copy.sessions = new[] { sessions[0]?.Copy(), sessions[1]?.Copy() };
            return copy;
        }

        public void Validate()
        {
            if (operatorSettings != null)
            {
                operatorSettings.Validate();
                if (operatorSettings.sharedCredits != sharedCredits || coinsPerCredit == null || coinsPerCredit.Length != 2 ||
                    operatorSettings.coinsPerCredit1 != coinsPerCredit[0] || operatorSettings.coinsPerCredit2 != coinsPerCredit[1])
                    throw new InvalidDataException("Operator configuration and ledger pricing disagree.");
            }
            if (bookkeepingClears < 0 || creditClears < 0) throw new InvalidDataException("Invalid clear counters.");
            if (schema != 1 || generation < 0 || lastEventId < 0 || serviceCredits < 0 || starts < 0 ||
                continues < 0 || challenges < 0 || refundedCredits < 0 ||
                coinsPerCredit == null || coinsPerCredit.Length != 2 || credits == null || credits.Length != 2 ||
                coins == null || coins.Length != 2 || partialCoins == null || partialCoins.Length != 2 ||
                sessions == null || sessions.Length != 2)
                throw new InvalidDataException("Invalid arcade ledger header/arrays.");
            for (int i = 0; i < 2; i++)
            {
                if (coinsPerCredit[i] < 1 || coinsPerCredit[i] > 100 || credits[i] < 0 || coins[i] < 0 ||
                    partialCoins[i] < 0 || partialCoins[i] >= coinsPerCredit[i])
                    throw new InvalidDataException("Invalid arcade balance/coin remainder.");
                var session = sessions[i];
                if (session != null && (session.id <= 0 || session.id > lastEventId || session.side != i ||
                    session.pool != (sharedCredits ? 0 : i) || session.cost < 0 || session.cost > 999 ||
                    !Enum.IsDefined(typeof(ArcadePaidAction), session.action)))
                    throw new InvalidDataException("Invalid arcade paid-session record.");
            }
            if (sharedCredits && credits[1] != 0) throw new InvalidDataException("Shared ledger has an unexpected second balance.");
            if (sessions[0] != null && sessions[1] != null && sessions[0].id == sessions[1].id)
                throw new InvalidDataException("Paid-session IDs must be unique.");
        }
    }

    // All external money events use ONE ordered sequence, resumed at Snapshot.lastEventId + 1.
    // The caller drains ArcadeInput in order; do not feed independent per-device counters here.
    // Older IDs are acknowledged as duplicates, never as permission to start a new match.
    public sealed class ArcadeLedger : IDisposable
    {
        readonly object gate = new object();
        readonly ArcadeAtomicFile<ArcadeLedgerState> store;
        ArcadeSettings settings;
        ArcadeLedgerState state;
        bool faulted, disposed;

        public ArcadeLedger(string path, ArcadeSettings settings, Action<ArcadeWriteStage> faultInjection = null)
        {
            settings.Validate();
            this.settings = settings.Copy();
            store = new ArcadeAtomicFile<ArcadeLedgerState>(path, s => s.Validate(), faultInjection);
            try
            {
                state = store.Load();
                if (state == null)
                {
                    state = new ArcadeLedgerState { sharedCredits = settings.sharedCredits,
                        coinsPerCredit = new[] { settings.coinsPerCredit1, settings.coinsPerCredit2 } };
                    store.Save(state); // Establish a committed empty store before accepting events.
                }
                if (state.operatorSettings != null) this.settings = settings = state.operatorSettings.Copy();
                if (state.sharedCredits != settings.sharedCredits || state.coinsPerCredit[0] != settings.coinsPerCredit1 ||
                    state.coinsPerCredit[1] != settings.coinsPerCredit2)
                    throw new InvalidOperationException("Coin pricing/pool changes require explicit ledger migration; balances and partial coins were preserved.");
            }
            catch { store.Dispose(); throw; }
        }

        public ArcadeSettings Settings { get { lock (gate) return settings.Copy(); } }
        // Configuration and its balance migration have one commit boundary. Legacy settings remain
        // the initial defaults; once Apply commits, this embedded configuration is authoritative.
        public void ApplyOperatorSettings(ArcadeSettings value)
        {
            value.Validate();
            lock (gate)
            {
                EnsureUsable();
                if (state.sessions[0] != null || state.sessions[1] != null) throw new InvalidOperationException("Finish the paid game before changing settings.");
                if ((value.coinsPerCredit1 != settings.coinsPerCredit1 && state.partialCoins[0] != 0) ||
                    (value.coinsPerCredit2 != settings.coinsPerCredit2 && state.partialCoins[1] != 0))
                    throw new InvalidOperationException("Complete the partial coin payment before changing that chute's ratio.");
                var next = state.Copy();
                if (value.sharedCredits && !settings.sharedCredits)
                { next.credits[0] = checked(next.credits[0] + next.credits[1]); next.credits[1] = 0; }
                // Shared -> separate: the existing shared balance stays in P1. Shown before Apply.
                next.sharedCredits = value.sharedCredits;
                next.coinsPerCredit = new[] { value.coinsPerCredit1, value.coinsPerCredit2 };
                next.operatorSettings = value.Copy(); Commit(next); settings = value.Copy();
            }
        }
        public void ClearBookkeeping()
        {
            lock (gate)
            {
                EnsureUsable(); var next = state.Copy();
                next.coins[0] = next.coins[1] = next.serviceCredits = next.starts = next.continues = next.challenges = next.refundedCredits = 0;
                next.bookkeepingClears = checked(next.bookkeepingClears + 1); Commit(next);
            }
        }
        public void ClearCredits()
        {
            lock (gate)
            {
                EnsureUsable();
                if (state.sessions[0] != null || state.sessions[1] != null) throw new InvalidOperationException("Finish the paid game before clearing credits.");
                var next = state.Copy(); next.credits[0] = next.credits[1] = 0; next.partialCoins[0] = next.partialCoins[1] = 0;
                next.creditClears = checked(next.creditClears + 1); Commit(next);
            }
        }

        public ArcadeLedgerState Snapshot { get { lock (gate) return state.Copy(); } }
        public bool Faulted { get { lock (gate) return faulted; } }
        public long VisibleCredits(int side) { Side(side); lock (gate) return Math.Min(settings.maxCredits, state.credits[Pool(side)]); }
        public long OverflowCredits(int side) { Side(side); lock (gate) return Math.Max(0, state.credits[Pool(side)] - settings.maxCredits); }
        public bool ShouldInhibitCoin(int chute) => VisibleCredits(chute) >= settings.maxCredits;

        public ArcadeLedgerResult Coin(long eventId, int chute)
        {
            Side(chute);
            lock (gate)
            {
                EnsureUsable();
                if (Duplicate(eventId)) return ArcadeLedgerResult.Duplicate;
                var next = state.Copy(); next.lastEventId = eventId;
                checked
                {
                    next.coins[chute]++;
                    next.partialCoins[chute]++;
                    if (next.partialCoins[chute] == next.coinsPerCredit[chute])
                    { next.partialCoins[chute] = 0; next.credits[Pool(chute)]++; }
                }
                Commit(next); return ArcadeLedgerResult.Accepted;
            }
        }

        public ArcadeLedgerResult Service(long eventId, int side)
        {
            Side(side);
            lock (gate)
            {
                EnsureUsable();
                if (Duplicate(eventId)) return ArcadeLedgerResult.Duplicate;
                var next = state.Copy(); next.lastEventId = eventId;
                checked { next.credits[Pool(side)]++; next.serviceCredits++; }
                Commit(next); return ArcadeLedgerResult.Accepted;
            }
        }

        public ArcadeLedgerResult Begin(long eventId, int side, ArcadePaidAction action, out long sessionId)
        {
            Side(side);
            if (!Enum.IsDefined(typeof(ArcadePaidAction), action)) throw new ArgumentOutOfRangeException(nameof(action));
            lock (gate)
            {
                EnsureUsable(); sessionId = 0;
                if (Duplicate(eventId)) return ArcadeLedgerResult.Duplicate;
                int cost = settings.freePlay ? 0 : action == ArcadePaidAction.Start ? settings.creditsToStart :
                    action == ArcadePaidAction.Continue ? settings.creditsToContinue : settings.creditsToChallenge;
                var next = state.Copy(); next.lastEventId = eventId;
                var result = next.sessions[side] != null ? ArcadeLedgerResult.SideAlreadyActive :
                    next.credits[Pool(side)] < cost ? ArcadeLedgerResult.InsufficientCredit : ArcadeLedgerResult.Accepted;
                if (result == ArcadeLedgerResult.Accepted)
                {
                    next.credits[Pool(side)] -= cost;
                    next.sessions[side] = new ArcadeSession { id = eventId, side = side, pool = Pool(side), cost = cost, action = action };
                    checked
                    {
                        if (action == ArcadePaidAction.Start) next.starts++;
                        else if (action == ArcadePaidAction.Continue) next.continues++;
                        else next.challenges++;
                    }
                }
                // Persist even a denied Start so its retry cannot spend a later coin.
                Commit(next);
                if (result == ArcadeLedgerResult.Accepted) sessionId = eventId;
                return result;
            }
        }

        public bool MarkMatchReady(long sessionId) => ChangeSession(sessionId, false);
        // Close only when the paid entitlement ends; not at every ladder match or challenger interruption.
        public bool Complete(long sessionId) => ChangeSession(sessionId, true);

        bool ChangeSession(long id, bool complete)
        {
            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
            lock (gate)
            {
                EnsureUsable();
                int side = Array.FindIndex(state.sessions, s => s != null && s.id == id);
                if (side < 0 || (!complete && state.sessions[side].matchReady)) return false;
                var next = state.Copy();
                if (complete) next.sessions[side] = null; else next.sessions[side].matchReady = true;
                Commit(next); return true;
            }
        }

        // Proposed recovery policy: refund all still-open entitlements once. Call at boot before
        // accepting new starts, or for an explicitly abandoned session; not on ordinary scene loads.
        public long RecoverInterruptedSessions()
        {
            lock (gate)
            {
                EnsureUsable();
                if (state.sessions[0] == null && state.sessions[1] == null) return 0;
                var next = state.Copy(); long refund = 0;
                checked
                {
                    for (int i = 0; i < 2; i++)
                    {
                        var session = next.sessions[i]; if (session == null) continue;
                        next.credits[session.pool] += session.cost; refund += session.cost; next.sessions[i] = null;
                    }
                    next.refundedCredits += refund;
                }
                Commit(next); return refund;
            }
        }

        void Commit(ArcadeLedgerState next)
        {
            next.generation = checked(state.generation + 1);
            try { store.Save(next); state = next; }
            catch { faulted = true; throw; }
        }

        bool Duplicate(long id)
        {
            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
            return id <= state.lastEventId;
        }
        int Pool(int side) => settings.sharedCredits ? 0 : side;
        static void Side(int side) { if (side < 0 || side > 1) throw new ArgumentOutOfRangeException(nameof(side)); }
        void EnsureUsable()
        {
            if (disposed) throw new ObjectDisposedException(nameof(ArcadeLedger));
            if (faulted) throw new InvalidOperationException("Ledger fault: reopen and recover before accepting more events.");
        }
        public void Dispose() { lock (gate) { if (disposed) return; disposed = true; store.Dispose(); } }
    }
}
