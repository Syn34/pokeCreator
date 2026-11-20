using System;
using System.Collections.Generic;
using System.Linq;
using PokeCreator.Pkmn;

namespace PokeCreator.Runtime.World
{
    /// <summary>A recorded battle: everything needed to fight it again exactly the same way.</summary>
    public class BattleRecord
    {
        public int Seed { get; set; }
        public bool Double { get; set; }
        public string Rule { get; set; }
        public string TrainerName { get; set; }
        public string TrainerType { get; set; }
        public string Backdrop { get; set; }
        public string Weather { get; set; }
        public bool SwitchStyle { get; set; }
        public bool MegaRing { get; set; }
        public string PlayerName { get; set; }
        public List<Pokemon> Player { get; set; }
        public List<Pokemon> Foes { get; set; }
        public List<List<int>> Steps { get; set; }
        public BattleRecord() { Player = new List<Pokemon>(); Foes = new List<Pokemon>(); Steps = new List<List<int>>(); }
    }

    /// <summary>
    /// Battle recording (Essentials' pbRecordLastBattle / pbPlayLastBattle): every Battle Frontier battle is recorded
    /// as its starting teams, random seed and the player's decisions; the last one can be kept and watched again.
    /// </summary>
    public static class BattleRecorder
    {
        /// <summary>The battle just fought ($game_temp.last_battle_record); not saved.</summary>
        public static BattleRecord Last;

        public static List<Pokemon> Copy(List<Pokemon> team) { return team.Select(p => Json.Json.Deserialize<Pokemon>(Json.Json.Write(p, false))).ToList(); }

        public static void Play(Game g, BattleRecord r, Action done)
        {
            if (r == null) { if (done != null) done(); return; }
            var tt = g.Project.TrainerTypes.FirstOrDefault(t => t.Id == r.TrainerType);
            var b = new PokeCreator.Battles.Battle(g.Project, Copy(r.Player), Copy(r.Foes), false, new Random(r.Seed), r.Double)
            {
                PlayerName = r.PlayerName, TrainerName = r.TrainerName, FacilityRule = r.Rule,
                TrainerTypeName = tt != null && !string.IsNullOrEmpty(tt.Name) ? tt.Name : "Trainer", TrainerSkill = 100, NoExp = true,
            };
            if (!string.IsNullOrEmpty(r.Weather)) b.Weather = r.Weather;
            b.SwitchStyle = r.SwitchStyle; b.PlayerHasMegaRing = r.MegaRing;
            var ttNoMoney = tt != null ? Json.Json.Deserialize<PokeCreator.Data.TrainerTypeData>(Json.Json.Write(tt)) : null;
            if (ttNoMoney != null) ttNoMoney.BaseMoney = 0;
            g.Audio.PlaySe("Encounter");
            g.PushScene(new UI.BattleScene(g, b, new UI.BattleSetup
            {
                Wild = false, Trainer = new PokeCreator.Data.TrainerData { TrainerType = r.TrainerType, Name = r.TrainerName }, TrainerType = ttNoMoney,
                Backdrop = r.Backdrop ?? "field", CanLose = true, Facility = true, Replay = r.Steps,
                OnEnd = o => { if (done != null) done(); },
            }));
        }
    }
}
