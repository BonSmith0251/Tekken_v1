using System;
using UnityEngine;

namespace Versus.Remaster.Arcade
{
    public sealed partial class ArcadeDirector
    {
        static string ScoreTime(long ticks)
        {
            long seconds = ticks / 60;
            return (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00") + "." + (ticks % 60 * 100 / 60).ToString("00");
        }
        void DrawNameEntry(int remaining)
        {
            Text(70, "NEW HIGH SCORE", title);
            Text(165, "PLAYER " + (Flow.NameSide + 1) + " - ENTER YOUR INITIALS - " + remaining + " SECONDS", body);
            for (int i = 0; i < 3; i++)
            {
                var rect = new Rect(380 + i * 180, 290, 160, 170);
                Fill(rect, i == Flow.NameEntry.Position ? new Color(.16f, .36f, .65f) : new Color(.07f, .11f, .19f));
                GUI.Label(rect, Flow.NameEntry.Initials[i].ToString(), new GUIStyle(title) { fontSize = 100 });
            }
            bool one = Flow.NameSide == 0;
            Text(500, bindings.Label(one ? ArcadeControls.P1Up : ArcadeControls.P2Up) + " / " +
                bindings.Label(one ? ArcadeControls.P1Down : ArcadeControls.P2Down) + " LETTER    " +
                bindings.Label(one ? ArcadeControls.P1Left : ArcadeControls.P2Left) + " / " +
                bindings.Label(one ? ArcadeControls.P1Right : ArcadeControls.P2Right) + " POSITION", body, 60);
            Text(560, bindings.Label(one ? ArcadeControls.P1B1 : ArcadeControls.P2B1) + " CONFIRM    " +
                bindings.Label(one ? ArcadeControls.P1B2 : ArcadeControls.P2B2) + " BACK", detail, 50);
        }
        void DrawRankings()
        {
            Text(25, "ARCADE RANKINGS", new GUIStyle(title) { fontSize = 48 }, 80);
            Text(102, "LEVEL " + settings.difficulty + "  |  " + settings.ladderStages + " STAGES  |  " + settings.roundsToWin +
                " ROUNDS  |  " + settings.roundSeconds + " SECONDS", detail, 32);
            var entries = Rankings.Table(Flow.RankingRules);
            GUI.Label(new Rect(90, 150, 65, 35), "RANK", detail);
            GUI.Label(new Rect(165, 150, 380, 35), "NAME / FIGHTER", detail);
            GUI.Label(new Rect(570, 150, 140, 35), "STAGES", detail);
            GUI.Label(new Rect(745, 150, 210, 35), "TIME", detail);
            GUI.Label(new Rect(1010, 150, 140, 35), "CLEAN WINS", detail);
            if (entries.Length == 0) Text(280, Rankings.Available ? "CLEAR A CPU STAGE TO SET A SCORE" : "RANKINGS UNAVAILABLE", body);
            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i]; float y = 195 + i * 39;
                if (entry.runId == Flow.HighlightRunId) Fill(new Rect(85, y, 1110, 37), new Color(.12f, .28f, .45f));
                GUI.Label(new Rect(90, y, 65, 36), (i + 1).ToString("00"), body);
                GUI.Label(new Rect(165, y, 380, 36), entry.initials + "   " + entry.fighter.ToUpperInvariant(), body);
                GUI.Label(new Rect(570, y, 140, 36), entry.stagesCleared.ToString(), body);
                GUI.Label(new Rect(745, y, 210, 36), ScoreTime(entry.fightTicks), body);
                GUI.Label(new Rect(1010, y, 140, 36), entry.cleanWins.ToString(), body);
            }
            Text(597, Flow.RankingResumesWinner ? "THE WINNER'S CPU LADDER RESUMES AFTER THIS TABLE" : "STAGES CLEARED, THEN FASTEST CPU FIGHT TIME, THEN CLEAN WINS", detail, 40);
        }
    }
}
