using System;
using System.Collections.Generic;
using System.Linq;

namespace PokeCreator.Runtime.World
{
    public class SafariState
    {
        public bool InProgress { get; set; }
        public int Steps { get; set; }
        public int Balls { get; set; }
        public int Captures { get; set; }
        /// <summary>Why it ended: 0 still going / quit, 1 out of steps or balls.</summary>
        public int Decision { get; set; }
        /// <summary>Where the game started (the reception): map, x, y, direction.</summary>
        public int StartMap { get; set; }
        public int StartX { get; set; }
        public int StartY { get; set; }
        public int StartDir { get; set; }
    }

    /// <summary>
    /// The Safari Zone (pbSafariState): a limited number of steps and Safari Balls on maps marked "Safari Zone".
    /// Wild battles there are Safari battles (Ball / Bait / Rock / Run). When the steps or balls run out the
    /// player is sent back to the reception.
    /// </summary>
    public static class Safari
    {
        public static SafariState State(Game g)
        {
            if (g.State.Extras.Safari == null) g.State.Extras.Safari = new SafariState();
            return g.State.Extras.Safari;
        }

        public static bool InProgress(Game g) { return g.State != null && g.State.Extras.Safari != null && g.State.Extras.Safari.InProgress; }

        static bool SafariMap(Game g)
        {
            var p = g.Map != null ? g.Map.Data.Properties : null;
            return p != null && p.SafariMap;
        }

        /// <summary>pbInSafari?: a Safari game is on and the player is in the zone or at the reception.</summary>
        public static bool InSafari(Game g)
        {
            if (!InProgress(g) || g.Map == null) return false;
            return SafariMap(g) || g.Map.Id == State(g).StartMap;
        }

        /// <summary>Wild battles here are Safari battles.</summary>
        public static bool InZone(Game g) { return InProgress(g) && SafariMap(g); }

        public static void Start(Game g, int balls)
        {
            var s = State(g);
            s.InProgress = true; s.Decision = 0; s.Captures = 0;
            s.Balls = balls > 0 ? balls : Math.Max(1, g.Project.Settings.SafariBalls);
            s.Steps = Math.Max(0, g.Project.Settings.SafariSteps);
            s.StartMap = g.Map.Id; s.StartX = g.Player.X; s.StartY = g.Player.Y; s.StartDir = g.Player.Direction;
        }

        public static void End(Game g)
        {
            if (g.State.Extras.Safari != null) g.State.Extras.Safari.InProgress = false;
        }

        public static void GoToStart(Game g)
        {
            var s = State(g);
            if (s.StartMap > 0) g.RequestTransfer(s.StartMap, s.StartX, s.StartY, s.StartDir > 0 ? s.StartDir : 2, true);
        }

        /// <summary>The game is over (out of steps or balls): announce it and go back to the reception.</summary>
        public static void TimeUp(Game g, string announcement)
        {
            var s = State(g);
            s.Decision = 1;
            g.Message.Show(announcement, null, -1, _ =>
                g.Message.Show("Your Safari Game is over!", null, -1, __ => { GoToStart(g); End(g); }));
        }

        /// <summary>Counts a step in the zone (0 = unlimited steps).</summary>
        public static void OnStep(Game g)
        {
            if (!InZone(g)) return;
            var s = State(g);
            if (g.Project.Settings.SafariSteps <= 0 || s.Steps <= 0) return;
            s.Steps--;
            if (s.Steps == 0) TimeUp(g, "PA: Ding-dong! Time's up!");
        }

        /// <summary>After a Safari battle: a catch is counted, and running out of balls ends the game.</summary>
        public static void AfterBattle(Game g, PokeCreator.Battles.BattleOutcome o)
        {
            var s = State(g);
            if (o == PokeCreator.Battles.BattleOutcome.Caught) s.Captures++;
            if (s.Balls <= 0 && s.InProgress) { s.Decision = 1; GoToStart(g); End(g); }
        }
    }
}
