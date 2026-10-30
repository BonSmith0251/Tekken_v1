using System;
using System.IO;
using System.Linq;

namespace Versus.Remaster.Arcade
{
    [Serializable] public sealed class ArcadeScore
    {
        public string runId, rules, initials, fighter;
        public int stagesCleared, cleanWins;
        public long fightTicks;
        public ArcadeScore Copy() => (ArcadeScore)MemberwiseClone();
        public void Validate()
        {
            if (!Guid.TryParseExact(runId, "N", out _) || string.IsNullOrEmpty(rules) || rules.Length > 256 ||
                initials == null || initials.Length != 3 || initials.Any(c => c < 'A' || c > 'Z') ||
                string.IsNullOrWhiteSpace(fighter) || fighter.Length > 64 || stagesCleared < 1 || stagesCleared > 100 ||
                cleanWins < 0 || cleanWins > stagesCleared || fightTicks < 1)
                throw new InvalidDataException("Invalid ranking entry.");
        }
    }
    [Serializable] public sealed class ArcadeRankingState
    {
        public int schema = 1;
        public ArcadeScore[] entries = Array.Empty<ArcadeScore>();
        public void Validate()
        {
            if (schema != 1 || entries == null || entries.Length > 1280) throw new InvalidDataException("Invalid ranking header.");
            foreach (var entry in entries) { if (entry == null) throw new InvalidDataException("Null ranking entry."); entry.Validate(); }
            if (entries.Select(e => e.runId).Distinct().Count() != entries.Length ||
                entries.GroupBy(e => e.rules).Any(g => g.Count() > 10) || entries.Select(e => e.rules).Distinct().Count() > 128)
                throw new InvalidDataException("Invalid ranking capacity or duplicate run.");
        }
    }
    // Rankings are independent of money: a damaged/unwritable score file disables scoring, never
    // resets it or interrupts a paid match. Preserve the failed file for operator reconciliation.
    public sealed class ArcadeRanking : IDisposable
    {
        readonly ArcadeAtomicFile<ArcadeRankingState> store;
        ArcadeRankingState state = new ArcadeRankingState();
        public string Error { get; private set; }
        public bool Available => Error == null;
        public ArcadeRanking(string path, Action<ArcadeWriteStage> fault = null)
        {
            try
            {
                store = new ArcadeAtomicFile<ArcadeRankingState>(path, s => s.Validate(), fault);
                state = store.Load();
                if (state == null) { state = new ArcadeRankingState(); store.Save(state); }
            }
            catch (Exception error) { Error = error.Message; state = new ArcadeRankingState(); store?.Dispose(); }
        }
        public static string Rules(ArcadeSettings s) => string.Join("|", "v1", s.difficulty, s.ladderStages,
            s.roundSeconds, s.roundsToWin, s.bossRoundsToWin, s.boss, s.continueReselect ? 1 : 0);
        static IOrderedEnumerable<ArcadeScore> Ordered(System.Collections.Generic.IEnumerable<ArcadeScore> scores) =>
            scores.OrderByDescending(e => e.stagesCleared).ThenBy(e => e.fightTicks).ThenByDescending(e => e.cleanWins);
        public ArcadeScore[] Table(string rules) => Ordered(state.entries.Where(e => e.rules == rules)).Select(e => e.Copy()).ToArray();
        public bool Qualifies(ArcadeScore candidate)
        {
            if (!Available) return false;
            candidate.Validate();
            if (state.entries.Any(e => e.runId == candidate.runId)) return false;
            var table = Table(candidate.rules);
            if (table.Length == 0 && state.entries.Select(e => e.rules).Distinct().Count() >= 128) return false;
            return table.Length < 10 || Better(candidate, table[table.Length - 1]);
        }
        static bool Better(ArcadeScore a, ArcadeScore b) => a.stagesCleared > b.stagesCleared ||
            a.stagesCleared == b.stagesCleared && (a.fightTicks < b.fightTicks || a.fightTicks == b.fightTicks && a.cleanWins > b.cleanWins);
        public bool Submit(ArcadeScore candidate)
        {
            if (!Available) return false;
            candidate.Validate();
            if (state.entries.Any(e => e.runId == candidate.runId)) return true; // Retried completion cannot rename/duplicate the original.
            if (!Qualifies(candidate)) return false;
            var table = Ordered(Table(candidate.rules).Concat(new[] { candidate.Copy() })).Take(10);
            return Commit(new ArcadeRankingState { entries = state.entries.Where(e => e.rules != candidate.rules).Concat(table).ToArray() });
        }
        public bool Clear(string rules) => Available && Commit(new ArcadeRankingState { entries = state.entries.Where(e => e.rules != rules).ToArray() });
        bool Commit(ArcadeRankingState next)
        {
            try { store.Save(next); state = next; return true; }
            catch (Exception error) { Error = error.Message; return false; }
        }
        public void Dispose() => store?.Dispose();
    }
    public sealed class ArcadeNameEntry
    {
        readonly char[] letters = { 'A', 'A', 'A' };
        public int Position { get; private set; }
        public string Initials => new string(letters);
        public void Change(int direction) => letters[Position] = (char)('A' + (letters[Position] - 'A' + Math.Sign(direction) + 26) % 26);
        public void Move(int direction) => Position = Math.Max(0, Math.Min(2, Position + Math.Sign(direction)));
        public bool Confirm() { if (Position == 2) return true; Position++; return false; }
    }
}
