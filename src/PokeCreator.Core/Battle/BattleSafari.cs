using System;
using System.Collections.Generic;
using System.Linq;

namespace PokeCreator.Battles
{
    /// <summary>
    /// Safari Zone battles: the player throws Safari Balls, bait or rocks instead of fighting. Bait makes the Pokémon
    /// harder to catch but less likely to run; rocks make it easier to catch but more likely to run. It never attacks.
    /// </summary>
    public partial class Battle
    {
        public bool Safari;
        int _catchFactor, _escapeFactor, _bait, _rock;
        int? _catchRateOverride;

        public enum SafariAction { Ball, Bait, Rock, Run }

        void SafariSetup()
        {
            var foe = Parties[1].FirstOrDefault();
            var fd = foe != null ? foe.Data(P) : null;
            int rate = fd != null && fd.CatchRate > 0 ? fd.CatchRate : 45;
            int speed = fd != null && fd.BaseStats != null && fd.BaseStats.Count > 3 ? fd.BaseStats[3] : 50;
            _catchFactor = Math.Max(3, Math.Min(20, rate * 100 / 1275));
            _escapeFactor = Math.Max(2, Math.Min(20, speed * 100 / 1275 * 2));
        }

        /// <summary>One Safari turn. ballsLeft = Safari Balls left after this throw (0 ends the game if it misses).</summary>
        public List<BattleEvent> SafariTurn(SafariAction action, int ballsLeft)
        {
            _ev = new List<BattleEvent>();
            Turn++;
            var foe = Live(1).FirstOrDefault();
            if (foe == null) return _ev;
            if (_catchFactor == 0) SafariSetup();
            switch (action)
            {
                case SafariAction.Ball:
                    _catchRateOverride = Math.Max(1, _catchFactor * 1275 / 100);
                    ThrowBall("SAFARIBALL");
                    _catchRateOverride = null;
                    if (Outcome != BattleOutcome.Ongoing) return _ev;
                    if (ballsLeft <= 0)
                    {
                        Say("PA: You have no Safari Balls left! Game over!");
                        Outcome = BattleOutcome.Escaped;
                        return _ev;
                    }
                    break;
                case SafariAction.Bait:
                    Say(PlayerName + " threw some bait at the " + foe.Pk.Name(P) + "!");
                    _bait += 2 + Rng.Next(4); _rock = 0;
                    _catchFactor = Math.Max(3, _catchFactor / 2);
                    _escapeFactor = Math.Max(2, _escapeFactor / 2);
                    break;
                case SafariAction.Rock:
                    Say(PlayerName + " threw a rock at the " + foe.Pk.Name(P) + "!");
                    _rock += 2 + Rng.Next(4); _bait = 0;
                    _catchFactor = Math.Min(20, _catchFactor * 2);
                    _escapeFactor = Math.Min(20, _escapeFactor * 2);
                    break;
                case SafariAction.Run:
                    Say("You got away safely!");
                    Outcome = BattleOutcome.Escaped;
                    _ev.Add(new BattleEvent { Kind = BattleEventKind.Flee });
                    return _ev;
            }
            // The Pokémon's turn: it eats, gets angry, watches, or runs away
            if (Rng.Next(100) < 5 * _escapeFactor)
            {
                Say("The wild " + foe.Pk.Name(P) + " fled!");
                Outcome = BattleOutcome.Escaped;
                _ev.Add(new BattleEvent { Kind = BattleEventKind.Flee });
                return _ev;
            }
            if (_bait > 0) { _bait--; Say("The wild " + foe.Pk.Name(P) + " is eating!"); }
            else if (_rock > 0) { _rock--; Say("The wild " + foe.Pk.Name(P) + " is angry!"); }
            else Say("The wild " + foe.Pk.Name(P) + " is watching carefully!");
            return _ev;
        }
    }
}
