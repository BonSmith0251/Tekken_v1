using System;

namespace Versus.Remaster.Arcade
{
    // Provisional cabinet defaults. No runtime behavior changes until a director opts in.
    [Serializable]
    public sealed class ArcadeSettings
    {
        public int schema = 1;
        public bool freePlay;
        public bool sharedCredits = true;
        public int coinsPerCredit1 = 1, coinsPerCredit2 = 1;
        public int creditsToStart = 1, creditsToContinue = 1, creditsToChallenge = 1;
        public int maxCredits = 9;
        public int coinLockoutMs = 80, coinJamMs = 2000;
        public int roundSeconds = 60, roundsToWin = 2, bossRoundsToWin = 2;
        public int selectSeconds = 20, continueSeconds = 10, nameEntrySeconds = 20;
        public int victoryMaxSeconds = 8, loadTimeoutSeconds = 30;
        public int ladderStages = 5, difficulty = 4;
        public bool joinIn = true, continueReselect = true, continueAcceleration = true;
        public string boss = "Kazuya";

        public ArcadeSettings Copy() => (ArcadeSettings)MemberwiseClone();

        public void Validate()
        {
            if (schema != 1) throw new ArgumentException("Unsupported arcade settings schema.");
            Range(maxCredits, 1, 999, nameof(maxCredits));
            Range(coinsPerCredit1, 1, 100, nameof(coinsPerCredit1));
            Range(coinsPerCredit2, 1, 100, nameof(coinsPerCredit2));
            Range(creditsToStart, 1, maxCredits, nameof(creditsToStart));
            Range(creditsToContinue, 1, maxCredits, nameof(creditsToContinue));
            Range(creditsToChallenge, 1, maxCredits, nameof(creditsToChallenge));
            Range(coinLockoutMs, 0, 1000, nameof(coinLockoutMs));
            Range(coinJamMs, Math.Max(1, coinLockoutMs), 60000, nameof(coinJamMs));
            Range(roundSeconds, 10, 300, nameof(roundSeconds));
            Range(roundsToWin, 1, 10, nameof(roundsToWin));
            Range(bossRoundsToWin, 1, 10, nameof(bossRoundsToWin));
            Range(selectSeconds, 1, 120, nameof(selectSeconds));
            Range(continueSeconds, 1, 60, nameof(continueSeconds));
            Range(nameEntrySeconds, 1, 120, nameof(nameEntrySeconds));
            Range(victoryMaxSeconds, 1, 60, nameof(victoryMaxSeconds));
            Range(loadTimeoutSeconds, 1, 300, nameof(loadTimeoutSeconds));
            Range(ladderStages, 1, 100, nameof(ladderStages));
            Range(difficulty, 1, 8, nameof(difficulty));
            if (string.IsNullOrWhiteSpace(boss) || boss.Length > 64)
                throw new ArgumentException("A boss fighter identifier is required.");
        }

        static void Range(int value, int min, int max, string name)
        {
            if (value < min || value > max) throw new ArgumentOutOfRangeException(name, value, $"Expected {min}..{max}.");
        }
    }
}
