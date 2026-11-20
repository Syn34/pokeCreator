using System;
using System.Collections.Generic;
using System.Linq;
using PokeCreator.Data;
using PokeCreator.Pkmn;

namespace PokeCreator.Battles
{
    /// <summary>Battle weather and abilities (the commonly seen ones), hooked into the main battle code.</summary>
    public partial class Battle
    {
        // ------------------------------------------------------------------ weather
        public const string NoWeather = "", Sun = "Sun", Rain = "Rain", Sandstorm = "Sandstorm", Hail = "Hail";
        /// <summary>Current battle weather (Sun, Rain, Sandstorm, Hail or "").</summary>
        public string Weather = NoWeather;
        /// <summary>Turns left; 0 = lasts all battle (weather from the overworld).</summary>
        public int WeatherTurns;

        /// <summary>Battle weather for an overworld weather name (Rain, Storm, HeavyRain, Snow, Blizzard, Sandstorm, Sun).</summary>
        public static string FromFieldWeather(string field)
        {
            switch ((field ?? "").ToLowerInvariant())
            {
                case "rain": case "storm": case "heavyrain": return Rain;
                case "snow": case "blizzard": return Hail;
                case "sandstorm": return Sandstorm;
                case "sun": return Sun;
                default: return NoWeather;
            }
        }

        /// <summary>Weather that counts for effects: Cloud Nine / Air Lock on the field cancel it.</summary>
        string EffWeather
        {
            get
            {
                foreach (var b in OnField) if (!b.Pk.Fainted && (HasAbility(b, "CLOUDNINE") || HasAbility(b, "AIRLOCK"))) return NoWeather;
                return Weather;
            }
        }

        static string WeatherAnimName(string w)
        {
            switch (w) { case Sun: return "Sun"; case Rain: return "Rain"; case Sandstorm: return "Sandstorm"; case Hail: return "Hail"; default: return ""; }
        }

        static string WeatherStartText(string w)
        {
            switch (w) { case Sun: return "The sunlight turned harsh!"; case Rain: return "It started to rain!"; case Sandstorm: return "A sandstorm kicked up!"; case Hail: return "It started to hail!"; default: return ""; }
        }
        static string WeatherOngoingText(string w)
        {
            switch (w) { case Sun: return "The sunlight is strong."; case Rain: return "Rain continues to fall."; case Sandstorm: return "The sandstorm is raging."; case Hail: return "Hail continues to fall."; default: return ""; }
        }
        static string WeatherEndText(string w)
        {
            switch (w) { case Sun: return "The harsh sunlight faded."; case Rain: return "The rain stopped."; case Sandstorm: return "The sandstorm subsided."; case Hail: return "The hail stopped."; default: return ""; }
        }
        static string WeatherFieldText(string w)
        {
            switch (w) { case Sun: return "The sunlight is strong."; case Rain: return "It is raining."; case Sandstorm: return "A sandstorm is raging."; case Hail: return "Hail is falling."; default: return ""; }
        }

        /// <summary>Starts weather from a move or ability: 5 turns (8 with the matching rock).</summary>
        bool StartWeather(string w, Battler by)
        {
            if (Weather == w && WeatherTurns == 0 && w != NoWeather) { Say("But it failed!"); return false; }
            if (Weather == w) { Say("But it failed!"); return false; }
            Weather = w;
            string rock = w == Sun ? "HEATROCK" : w == Rain ? "DAMPROCK" : w == Sandstorm ? "SMOOTHROCK" : "ICYROCK";
            WeatherTurns = by != null && by.Pk.Item == rock ? 8 : 5;
            CommonAnim(WeatherAnimName(w), null);
            Say(WeatherStartText(w));
            return true;
        }

        /// <summary>Weather damage and countdown at the end of a turn.</summary>
        void WeatherEndOfTurn()
        {
            if (Weather == NoWeather) return;
            if (WeatherTurns > 0 && --WeatherTurns == 0) { Say(WeatherEndText(Weather)); Weather = NoWeather; return; }
            CommonAnim(WeatherAnimName(Weather), null);
            Say(WeatherOngoingText(Weather));
            if (EffWeather != Sandstorm && EffWeather != Hail) return;
            foreach (var b in OnField.Where(x => !x.Pk.Fainted).OrderByDescending(x => Speed(x)).ToList())
            {
                if (b.Pk.Fainted || b.Invulnerable) continue;
                var types = TypesOf(b);
                if (HasAbility(b, "MAGICGUARD") || HasAbility(b, "OVERCOAT") || b.Pk.Item == "SAFETYGOGGLES") continue;
                if (EffWeather == Sandstorm)
                {
                    if (types.Contains("ROCK") || types.Contains("GROUND") || types.Contains("STEEL") || HasAbility(b, "SANDVEIL") || HasAbility(b, "SANDRUSH") || HasAbility(b, "SANDFORCE")) continue;
                    Residual(b, Math.Max(1, b.Pk.TotalHP(P) / 16), Name(b) + " is buffeted by the sandstorm!");
                }
                else
                {
                    if (types.Contains("ICE") || HasAbility(b, "ICEBODY") || HasAbility(b, "SNOWCLOAK") || HasAbility(b, "SLUSHRUSH")) continue;
                    Residual(b, Math.Max(1, b.Pk.TotalHP(P) / 16), Name(b) + " is buffeted by the hail!");
                }
            }
        }

        // ------------------------------------------------------------------ abilities
        public string AbilityOf(Battler b) { return b == null || b.Pk == null ? "" : (b.AbilityOverride ?? b.Pk.AbilityId(P) ?? ""); }
        bool HasAbility(Battler b, string id) { return b != null && !b.AbilitySuppressed && AbilityOf(b) == id; }
        string AbilityName(Battler b)
        {
            string id = AbilityOf(b);
            var a = P.Abilities.FirstOrDefault(x => x.Id == id);
            return a != null && !string.IsNullOrEmpty(a.Name) ? a.Name : id;
        }
        /// <summary>"Gyarados's Intimidate!" (Essentials shows this as the ability splash).</summary>
        void Splash(Battler b) { Say(Name(b) + "'s " + AbilityName(b) + "!"); }

        bool MoldBreaker(Battler user) { return _ignoreAbilityMove || HasAbility(user, "MOLDBREAKER") || HasAbility(user, "TERAVOLT") || HasAbility(user, "TURBOBLAZE"); }
        /// <summary>Moongeist Beam, Sunsteel Strike, Photon Geyser: the current move ignores the target's ability.</summary>
        bool _ignoreAbilityMove;
        /// <summary>The target's ability counts against this attacker (Mold Breaker ignores most defensive abilities).</summary>
        bool TargetHas(Battler user, Battler target, string id) { return HasAbility(target, id) && !MoldBreaker(user); }

        /// <summary>Abilities that act when a Pokémon comes out: Intimidate, weather setters, Pressure...</summary>
        void SwitchInAbility(Battler b)
        {
            if (b == null || b.Pk.Fainted) return;
            EntryHazards(b);
            if (b.Pk.Fainted) return;
            var foe = Live(1 - b.Side).FirstOrDefault();
            switch (AbilityOf(b))
            {
                case "INTIMIDATE":
                    if (foe == null) break;
                    Splash(b);
                    foreach (var f in Live(1 - b.Side))
                    {
                        if (HasAbility(f, "CLEARBODY") || HasAbility(f, "WHITESMOKE") || HasAbility(f, "HYPERCUTTER") || HasAbility(f, "FULLMETALBODY")
                            || HasAbility(f, "INNERFOCUS") || HasAbility(f, "OBLIVIOUS") || HasAbility(f, "OWNTEMPO") || HasAbility(f, "SCRAPPY"))
                            Say(Name(f) + "'s " + AbilityName(f) + " prevents its Attack from being lowered!");
                        else ChangeStat(f, PokeCreator.Pkmn.Stat.Attack, -1, b);
                    }
                    break;
                case "DRIZZLE": if (Weather != Rain) { Splash(b); StartWeather(Rain, b); } break;
                case "DROUGHT": if (Weather != Sun) { Splash(b); StartWeather(Sun, b); } break;
                case "SANDSTREAM": if (Weather != Sandstorm) { Splash(b); StartWeather(Sandstorm, b); } break;
                case "SNOWWARNING": if (Weather != Hail) { Splash(b); StartWeather(Hail, b); } break;
                case "PRESSURE": Say(Name(b) + " is exerting its pressure!"); break;
                case "MOLDBREAKER": Say(Name(b) + " breaks the mold!"); break;
                case "CLOUDNINE": case "AIRLOCK": if (Weather != NoWeather) { Splash(b); Say("The effects of the weather disappeared."); } break;
                case "DOWNLOAD":
                    if (foe == null || foe.Pk.Fainted) break;
                    Splash(b);
                    ChangeStat(b, Stat(foe, PokeCreator.Pkmn.Stat.Defense) < Stat(foe, PokeCreator.Pkmn.Stat.SpDef) ? PokeCreator.Pkmn.Stat.Attack : PokeCreator.Pkmn.Stat.SpAtk, 1);
                    break;
            }
            MoreSwitchInAbilities(b);
        }

        /// <summary>Natural Cure and Regenerator when a Pokémon is withdrawn.</summary>
        void SwitchOutAbility(Battler b)
        {
            if (b == null || b.Pk.Fainted) return;
            if (HasAbility(b, "NATURALCURE") && !string.IsNullOrEmpty(b.Pk.Status)) b.Pk.Status = "";
            if (HasAbility(b, "REGENERATOR")) { int max = b.Pk.TotalHP(P); b.Pk.HP = Math.Min(max, b.Pk.HP + max / 3); }
        }

        /// <summary>Speed multipliers from abilities and weather.</summary>
        double AbilitySpeed(Battler b)
        {
            double extra = 1;
            if (HasAbility(b, "SLOWSTART") && b.TurnsOut < 5) extra *= 0.5;
            if (HasAbility(b, "UNBURDEN") && !string.IsNullOrEmpty(b.StartItem) && string.IsNullOrEmpty(b.Pk.Item)) extra *= 2;
            if (HasAbility(b, "SURGESURFER") && Terrain == "Electric") extra *= 2;
            if (BoostedStat(b) == PokeCreator.Pkmn.Stat.Speed) extra *= 1.5;
            return extra * BaseAbilitySpeed(b);
        }

        double BaseAbilitySpeed(Battler b)
        {
            string w = EffWeather;
            if (HasAbility(b, "SWIFTSWIM") && w == Rain) return 2;
            if (HasAbility(b, "CHLOROPHYLL") && w == Sun) return 2;
            if (HasAbility(b, "SANDRUSH") && w == Sandstorm) return 2;
            if (HasAbility(b, "SLUSHRUSH") && w == Hail) return 2;
            if (HasAbility(b, "QUICKFEET") && !string.IsNullOrEmpty(b.Pk.Status)) return 1.5;
            if (b.Pk.Item == "CHOICESCARF") return 1.5;
            return 1;
        }

        /// <summary>The move's type after Weather Ball, Normalize and similar.</summary>
        string MoveType(Battler user, MoveData md, string code)
        {
            if (code == "TypeAndPowerDependOnWeather")
            {
                switch (EffWeather) { case Sun: return "FIRE"; case Rain: return "WATER"; case Sandstorm: return "ROCK"; case Hail: return "ICE"; }
            }
            var special = SpecialType(user, md, code);
            if (special != null) return special;
            if (HasAbility(user, "NORMALIZE")) return "NORMAL";
            return AbilityMoveType(user, md, md.Type);
        }

        /// <summary>
        /// Defensive abilities that stop a move outright (Levitate, Volt Absorb, Flash Fire...). Returns true if the move
        /// is used up by the ability (its messages are already shown).
        /// </summary>
        bool AbilityAbsorbs(Battler user, Battler target, MoveData md, string type)
        {
            if (target == user || target.Pk.Fainted) return false;
            Func<string, bool> has = id => TargetHas(user, target, id);
            int max = target.Pk.TotalHP(P);
            Action<string> heal = why =>
            {
                Splash(target);
                if (target.Pk.HP >= max) { Say(Name(target) + "'s HP is full!"); return; }
                int f = target.Pk.HP; target.Pk.HP = Math.Min(max, target.Pk.HP + max / 4); Hp(target, f);
                Say(Name(target) + " restored HP using its " + AbilityName(target) + "!");
            };
            if (md.Category != "Status" || md.Target != "User")
            {
                if (type == "GROUND" && md.Category != "Status" && (has("LEVITATE") || target.Pk.Item == "AIRBALLOON")) { Say(Name(target) + (has("LEVITATE") ? " avoided the attack with Levitate!" : " is floating on its Air Balloon!")); return true; }
                if (type == "ELECTRIC" && (has("VOLTABSORB"))) { heal(null); return true; }
                if (type == "WATER" && (has("WATERABSORB") || has("DRYSKIN"))) { heal(null); return true; }
                if (type == "ELECTRIC" && has("MOTORDRIVE")) { Splash(target); ChangeStat(target, PokeCreator.Pkmn.Stat.Speed, 1); return true; }
                if (type == "ELECTRIC" && has("LIGHTNINGROD")) { Splash(target); ChangeStat(target, PokeCreator.Pkmn.Stat.SpAtk, 1); return true; }
                if (type == "WATER" && has("STORMDRAIN")) { Splash(target); ChangeStat(target, PokeCreator.Pkmn.Stat.SpAtk, 1); return true; }
                if (type == "GRASS" && has("SAPSIPPER")) { Splash(target); ChangeStat(target, PokeCreator.Pkmn.Stat.Attack, 1); return true; }
                if (type == "FIRE" && has("FLASHFIRE"))
                {
                    Splash(target);
                    if (!target.FlashFire) { target.FlashFire = true; Say("The power of " + NameL(target) + "'s Fire-type moves rose!"); }
                    else Say("It doesn't affect " + NameL(target) + "...");
                    return true;
                }
                if (md.Flags.Contains("Sound") && has("SOUNDPROOF")) { Splash(target); Say("It doesn't affect " + NameL(target) + "..."); return true; }
                if (md.Flags.Contains("Bomb") && has("BULLETPROOF")) { Splash(target); Say("It doesn't affect " + NameL(target) + "..."); return true; }
            }
            return false;
        }

        /// <summary>Power changes from abilities, items and weather (Technician, Iron Fist, Solar Beam in rain...).</summary>
        double PowerMod(Battler user, Battler target, MoveData md, string type, string code, int power)
        {
            double m = 1;
            if (HasAbility(user, "TECHNICIAN") && power <= 60) m *= 1.5;
            if (HasAbility(user, "IRONFIST") && md.Flags.Contains("Punching")) m *= 1.2;
            if (HasAbility(user, "RECKLESS") && (code.StartsWith("Recoil") || code.Contains("CrashDamage"))) m *= 1.2;
            if (HasAbility(user, "STRONGJAW") && md.Flags.Contains("Biting")) m *= 1.5;
            if (HasAbility(user, "MEGALAUNCHER") && md.Flags.Contains("Pulse")) m *= 1.5;
            if (HasAbility(user, "TOUGHCLAWS") && md.Flags.Contains("Contact")) m *= 1.3;
            if (HasAbility(user, "SHEERFORCE") && md.EffectChance > 0) m *= 1.3;
            if (HasAbility(user, "SANDFORCE") && EffWeather == Sandstorm && (type == "ROCK" || type == "GROUND" || type == "STEEL")) m *= 1.3;
            if (HasAbility(user, "ANALYTIC") && user.MovedLast) m *= 1.3;
            if (TargetHas(user, target, "HEATPROOF") && type == "FIRE") m *= 0.5;
            if (TargetHas(user, target, "DRYSKIN") && type == "FIRE") m *= 1.25;
            if (code == "TypeAndPowerDependOnWeather" && EffWeather != NoWeather) m *= 2;
            if (code.StartsWith("TwoTurnAttackOneTurnInSun") && EffWeather != NoWeather && EffWeather != Sun) m *= 0.5;
            // Type-boosting held items (Charcoal, Mystic Water...) and plates
            string boost = TypeBoostItem(user.Pk.Item);
            if (boost != null && boost == type) m *= 1.2;
            return m * MorePowerMod(user, target, md, type, code);
        }

        static string TypeBoostItem(string item)
        {
            switch (item)
            {
                case "SILKSCARF": return "NORMAL"; case "BLACKBELT": return "FIGHTING"; case "SHARPBEAK": return "FLYING"; case "POISONBARB": return "POISON";
                case "SOFTSAND": return "GROUND"; case "HARDSTONE": return "ROCK"; case "SILVERPOWDER": return "BUG"; case "SPELLTAG": return "GHOST";
                case "METALCOAT": return "STEEL"; case "CHARCOAL": return "FIRE"; case "MYSTICWATER": return "WATER"; case "MIRACLESEED": return "GRASS";
                case "MAGNET": return "ELECTRIC"; case "TWISTEDSPOON": return "PSYCHIC"; case "NEVERMELTICE": return "ICE"; case "DRAGONFANG": return "DRAGON";
                case "BLACKGLASSES": return "DARK"; case "FAIRYFEATHER": return "FAIRY";
                default: return null;
            }
        }

        /// <summary>Attack stat multiplier (Huge Power, Guts, Blaze at low HP, Choice Band...).</summary>
        double AttackMod(Battler user, Battler target, bool physical, string type)
        {
            double m = 1;
            int hp = user.Pk.HP, max = user.Pk.TotalHP(P);
            bool pinch = hp * 3 <= max;
            if (physical && (HasAbility(user, "HUGEPOWER") || HasAbility(user, "PUREPOWER"))) m *= 2;
            if (physical && HasAbility(user, "GUTS") && !string.IsNullOrEmpty(user.Pk.Status)) m *= 1.5;
            if (physical && HasAbility(user, "HUSTLE")) m *= 1.5;
            if (!physical && HasAbility(user, "SOLARPOWER") && EffWeather == Sun) m *= 1.5;
            if (pinch && ((type == "FIRE" && HasAbility(user, "BLAZE")) || (type == "WATER" && HasAbility(user, "TORRENT")) || (type == "GRASS" && HasAbility(user, "OVERGROW")) || (type == "BUG" && HasAbility(user, "SWARM")))) m *= 1.5;
            if (type == "FIRE" && user.FlashFire) m *= 1.5;
            if (physical && user.Pk.Item == "CHOICEBAND") m *= 1.5;
            if (!physical && user.Pk.Item == "CHOICESPECS") m *= 1.5;
            if (TargetHas(user, target, "THICKFAT") && (type == "FIRE" || type == "ICE")) m *= 0.5;
            return m * MoreAttackMod(user, target, physical, type);
        }

        /// <summary>Defense stat multiplier (Marvel Scale, Rock types in a sandstorm...).</summary>
        double DefenseMod(Battler user, Battler target, bool physical)
        {
            double m = 1;
            if (physical && TargetHas(user, target, "MARVELSCALE") && !string.IsNullOrEmpty(target.Pk.Status)) m *= 1.5;
            if (physical && TargetHas(user, target, "FURCOAT")) m *= 2;
            if (!physical && EffWeather == Sandstorm && TypesOf(target).Contains("ROCK")) m *= 1.5;
            if (!physical && target.Pk.Item == "ASSAULTVEST") m *= 1.5;
            if (target.Pk.Item == "EVIOLITE")
            {
                var d = target.Pk.Data(P);
                if (d != null && d.Evolutions != null && d.Evolutions.Count > 0) m *= 1.5;
            }
            return m * MoreDefenseMod(user, target, physical);
        }

        /// <summary>Final damage multiplier: weather, Filter/Solid Rock, Tinted Lens, Sniper, Multiscale, Life Orb...</summary>
        double FinalMod(Battler user, Battler target, string type, double typeMod, bool crit)
        {
            double m = 1;
            string w = EffWeather;
            if (w == Rain) { if (type == "WATER") m *= 1.5; else if (type == "FIRE") m *= 0.5; }
            if (w == Sun) { if (type == "FIRE") m *= 1.5; else if (type == "WATER") m *= 0.5; }
            if (typeMod > 1 && (TargetHas(user, target, "FILTER") || TargetHas(user, target, "SOLIDROCK") || HasAbility(target, "PRISMARMOR"))) m *= 0.75;
            if (typeMod < 1 && HasAbility(user, "TINTEDLENS")) m *= 2;
            if (crit && HasAbility(user, "SNIPER")) m *= 1.5;
            if (target.Pk.HP >= target.Pk.TotalHP(P) && (TargetHas(user, target, "MULTISCALE") || HasAbility(target, "SHADOWSHIELD"))) m *= 0.5;
            if (typeMod > 1 && user.Pk.Item == "EXPERTBELT") m *= 1.2;
            if (user.Pk.Item == "LIFEORB") m *= 1.3;
            return m;
        }

        /// <summary>Accuracy changes: No Guard, Compound Eyes, Hustle, Sand Veil, weather moves...</summary>
        double AccuracyMod(Battler user, Battler target, MoveData md, string code, out bool alwaysHits)
        {
            alwaysHits = HasAbility(user, "NOGUARD") || HasAbility(target, "NOGUARD");
            string w = EffWeather;
            if (code.Contains("AlwaysHitsInRain") && w == Rain) alwaysHits = true;
            if (code.Contains("AlwaysHitsInHail") && w == Hail) alwaysHits = true;
            double m = 1;
            if (code.Contains("AlwaysHitsInRain") && w == Sun) m *= 0.5 / Math.Max(0.01, md.Accuracy / 100.0) * 1.0; // 50% in sun
            if (HasAbility(user, "COMPOUNDEYES")) m *= 1.3;
            if (HasAbility(user, "VICTORYSTAR")) m *= 1.1;
            if (HasAbility(user, "HUSTLE") && md.Category == "Physical") m *= 0.8;
            if (TargetHas(user, target, "SANDVEIL") && w == Sandstorm) m *= 0.8;
            if (TargetHas(user, target, "SNOWCLOAK") && w == Hail) m *= 0.8;
            if (TargetHas(user, target, "TANGLEDFEET") && target.Confusion > 0) m *= 0.5;
            if (target.Pk.Item == "BRIGHTPOWDER" || target.Pk.Item == "LAXINCENSE") m *= 0.9;
            if (user.Pk.Item == "WIDELENS") m *= 1.1;
            return m;
        }

        /// <summary>Statuses an ability (or the weather) prevents.</summary>
        bool AbilityPreventsStatus(Battler b, string status, Battler source)
        {
            Func<string, bool> has = id => source != null && source != b ? TargetHas(source, b, id) : HasAbility(b, id);
            if (has("PURIFYINGSALT") || has("COMATOSE") || (has("LEAFGUARD") && EffWeather == Sun)) return true;
            if (TypesOf(b).Contains("GRASS") && (has("FLOWERVEIL") || (Ally(b) != null && HasAbility(Ally(b), "FLOWERVEIL")))) return true;
            if ((status == "POISON" || status == "TOXIC") && (has("PASTELVEIL") || (Ally(b) != null && HasAbility(Ally(b), "PASTELVEIL")))) return true;
            if (status == "BURN" && has("THERMALEXCHANGE")) return true;
            if (status == "SLEEP" && Ally(b) != null && HasAbility(Ally(b), "SWEETVEIL")) return true;
            switch (status)
            {
                case "PARALYSIS": return has("LIMBER");
                case "SLEEP": return has("INSOMNIA") || has("VITALSPIRIT") || has("SWEETVEIL");
                case "POISON": case "TOXIC": return has("IMMUNITY");
                case "BURN": return has("WATERVEIL") || has("WATERBUBBLE");
                case "FROZEN": return has("MAGMAARMOR") || EffWeather == Sun;
            }
            return false;
        }

        /// <summary>Clear Body and friends stop other Pokémon lowering this one's stats.</summary>
        bool AbilityBlocksStatDrop(Battler b, int stat, Battler source)
        {
            if (source == null || source == b) return false;
            Func<string, bool> has = id => TargetHas(source, b, id);
            if (has("CLEARBODY") || has("WHITESMOKE") || has("FULLMETALBODY")) return true;
            if (TypesOf(b).Contains("GRASS") && (has("FLOWERVEIL") || (Ally(b) != null && HasAbility(Ally(b), "FLOWERVEIL")))) return true;
            if (stat == PokeCreator.Pkmn.Stat.Attack && has("HYPERCUTTER")) return true;
            if (stat == 6 && has("KEENEYE")) return true;
            if (stat == PokeCreator.Pkmn.Stat.Defense && has("BIGPECKS")) return true;
            return false;
        }

        /// <summary>Abilities triggered by being hit with a contact move: Static, Flame Body, Rough Skin...</summary>
        void ContactAbilities(Battler user, Battler target, MoveData md)
        {
            if (!md.Flags.Contains("Contact") || user.Pk.Fainted || HasAbility(user, "LONGREACH")) return;
            if (user.Pk.Item == "PROTECTIVEPADS") return;
            string ta = target.AbilitySuppressed ? "" : AbilityOf(target);
            switch (ta)
            {
                case "STATIC": if (Rng.Next(100) < 30 && CanStatus(user, "PARALYSIS", false)) { Splash(target); Inflict(user, "PARALYSIS", false); } break;
                case "FLAMEBODY": if (Rng.Next(100) < 30 && CanStatus(user, "BURN", false)) { Splash(target); Inflict(user, "BURN", false); } break;
                case "POISONPOINT": if (Rng.Next(100) < 30 && CanStatus(user, "POISON", false)) { Splash(target); Inflict(user, "POISON", false); } break;
                case "EFFECTSPORE":
                    {
                        if (TypesOf(user).Contains("GRASS") || HasAbility(user, "OVERCOAT") || Rng.Next(100) >= 30) break;
                        string st = new[] { "POISON", "PARALYSIS", "SLEEP" }[Rng.Next(3)];
                        if (CanStatus(user, st, false)) { Splash(target); Inflict(user, st, false); }
                        break;
                    }
                case "ROUGHSKIN": case "IRONBARBS":
                    if (!HasAbility(user, "MAGICGUARD")) { Splash(target); Residual(user, Math.Max(1, user.Pk.TotalHP(P) / 8), Name(user) + " was hurt!"); }
                    break;
                case "CUTECHARM": break;
            }
            if (target.Pk.Item == "ROCKYHELMET" && !user.Pk.Fainted && !HasAbility(user, "MAGICGUARD"))
                Residual(user, Math.Max(1, user.Pk.TotalHP(P) / 6), Name(user) + " was hurt by the Rocky Helmet!");
        }

        /// <summary>End-of-turn abilities and weather healing: Speed Boost, Shed Skin, Rain Dish, Ice Body, Dry Skin, Hydration.</summary>
        void EndOfTurnAbilities(Battler b)
        {
            if (b.Pk.Fainted) return;
            int max = b.Pk.TotalHP(P);
            string w = EffWeather;
            Action<int, string> heal = (amt, text) =>
            {
                if (b.Pk.HP >= max) return;
                Splash(b);
                int f = b.Pk.HP; b.Pk.HP = Math.Min(max, b.Pk.HP + Math.Max(1, amt)); Hp(b, f); Say(text);
            };
            switch (AbilityOf(b))
            {
                case "SPEEDBOOST": if (b.TurnsOut > 0 && b.Stages[PokeCreator.Pkmn.Stat.Speed] < 6) { Splash(b); ChangeStat(b, PokeCreator.Pkmn.Stat.Speed, 1); } break;
                case "SHEDSKIN": if (!string.IsNullOrEmpty(b.Pk.Status) && Rng.Next(3) == 0) { Splash(b); b.Pk.Status = ""; StatusEvent(b); Say(Name(b) + " shed its skin and was cured!"); } break;
                case "HYDRATION": if (w == Rain && !string.IsNullOrEmpty(b.Pk.Status)) { Splash(b); b.Pk.Status = ""; StatusEvent(b); Say(Name(b) + "'s status condition was cured!"); } break;
                case "RAINDISH": if (w == Rain) heal(max / 16, Name(b) + " restored its HP."); break;
                case "ICEBODY": if (w == Hail) heal(max / 16, Name(b) + " restored its HP."); break;
                case "DRYSKIN":
                    if (w == Rain) heal(max / 8, Name(b) + " restored its HP.");
                    else if (w == Sun) { Splash(b); Residual(b, Math.Max(1, max / 8), Name(b) + " was hurt by the sunlight!"); }
                    break;
                case "SOLARPOWER": if (w == Sun) { Splash(b); Residual(b, Math.Max(1, max / 8), Name(b) + " was hurt by the sunlight!"); } break;
                case "POISONHEAL": break; // handled with poison damage
            }
        }

        /// <summary>Moxie, and other abilities that act when the user knocks a Pokémon out.</summary>
        void OnKnockOut(Battler user)
        {
            if (user.Pk.Fainted) return;
            if (HasAbility(user, "BATTLEBOND") && user.Pk.Species == "GRENINJA" && user.Pk.Form == 1)
                ChangeForm(user, 2, Name(user) + " became fully charged due to its bond with its Trainer!");
            if (HasAbility(user, "MOXIE") && user.Stages[PokeCreator.Pkmn.Stat.Attack] < 6) { Splash(user); ChangeStat(user, PokeCreator.Pkmn.Stat.Attack, 1); }
            if (HasAbility(user, "BEASTBOOST") && user.Stages[PokeCreator.Pkmn.Stat.Attack] < 6) { Splash(user); ChangeStat(user, PokeCreator.Pkmn.Stat.Attack, 1); }
        }

        /// <summary>HP healed by Moonlight, Synthesis and Morning Sun.</summary>
        int WeatherHeal(int max)
        {
            switch (EffWeather) { case Sun: return max * 2 / 3; case NoWeather: return (max + 1) / 2; default: return max / 4; }
        }
    }
}
