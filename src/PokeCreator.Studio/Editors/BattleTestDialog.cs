using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using PokeCreator.Projects;

namespace PokeCreator.Studio.Editors
{
    /// <summary>
    /// Test Battle: pick a team and an opponent (a trainer from the database or wild Pokémon), single / double / triple,
    /// and the game starts straight into that battle, closing when it ends.
    /// </summary>
    public class BattleTestDialog : Form
    {
        // Remembered while the Studio is open
        static readonly string[] _lastSpecies = { "CHARIZARD", "PIKACHU", "", "", "", "" };
        static readonly int[] _lastLevels = { 50, 50, 50, 50, 50, 50 };
        static int _lastTrainer = -1, _lastSize, _lastTeamTrainer = -1;
        static bool _lastUseTrainer = true, _lastTeamFromTrainer, _lastItems = true;
        static readonly string[] _lastWild = { "PIDGEY", "RATTATA", "SPEAROW" };
        static readonly int[] _lastWildLv = { 10, 10, 10 };

        readonly Project _p;
        readonly ComboBox[] _species = new ComboBox[6];
        readonly NumericUpDown[] _levels = new NumericUpDown[6];
        readonly RadioButton _teamList = new RadioButton { Text = "These Pokémon:", AutoSize = true };
        readonly RadioButton _teamTrainer = new RadioButton { Text = "A trainer's team from the database:", AutoSize = true };
        readonly ComboBox _teamTrainerBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330 };
        readonly RadioButton _vsTrainer = new RadioButton { Text = "Trainer:", AutoSize = true };
        readonly RadioButton _vsWild = new RadioButton { Text = "Wild Pokémon:", AutoSize = true };
        readonly ComboBox _trainer = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330 };
        readonly ComboBox[] _wild = new ComboBox[3];
        readonly NumericUpDown[] _wildLv = new NumericUpDown[3];
        readonly ComboBox _size = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
        readonly CheckBox _items = new CheckBox { Text = "Give Potions, Full Restores and Poké Balls", AutoSize = true };
        readonly List<PokeCreator.Data.TrainerData> _trainers;
        public string[] GameArgs { get; private set; }

        public BattleTestDialog(Project p)
        {
            _p = p;
            Text = "Test Battle";
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(640, 470);
            Font = new Font("Segoe UI", 9f);
            _trainers = p.Trainers.OrderBy(t => t.TrainerType).ThenBy(t => t.Name).ThenBy(t => t.Version).ToList();
            var trainerLabels = _trainers.Select(t => t.TrainerType + " " + t.Name + (t.Version > 0 ? " (v" + t.Version + ")" : "") + " - " + t.Pokemon.Count + " Pokémon").ToArray();
            var speciesIds = p.Species.Select(s => s.Id).ToArray();

            var team = new GroupBox { Text = "Your team", Left = 10, Top = 10, Width = 300, Height = 330 };
            team.Controls.Add(_teamList); _teamList.Location = new Point(10, 22);
            for (int i = 0; i < 6; i++)
            {
                _species[i] = Species(speciesIds, _lastSpecies[i]); _species[i].Location = new Point(28, 48 + i * 30); _species[i].Width = 170;
                _levels[i] = new NumericUpDown { Minimum = 1, Maximum = 100, Value = _lastLevels[i], Location = new Point(206, 48 + i * 30), Width = 60 };
                team.Controls.Add(_species[i]); team.Controls.Add(_levels[i]);
            }
            team.Controls.Add(_teamTrainer); _teamTrainer.Location = new Point(10, 234);
            _teamTrainerBox.Items.AddRange(trainerLabels); _teamTrainerBox.Location = new Point(28, 258); _teamTrainerBox.Width = 260;
            if (_lastTeamTrainer >= 0 && _lastTeamTrainer < _trainers.Count) _teamTrainerBox.SelectedIndex = _lastTeamTrainer;
            team.Controls.Add(_teamTrainerBox);
            team.Controls.Add(new Label { Text = "Tip: make TESTER trainers in the database\nfor teams you test with often.", Location = new Point(28, 288), AutoSize = true, ForeColor = Color.DimGray });

            var foe = new GroupBox { Text = "Opponent", Left = 320, Top = 10, Width = 310, Height = 250 };
            foe.Controls.Add(_vsTrainer); _vsTrainer.Location = new Point(10, 22);
            _trainer.Items.AddRange(trainerLabels); _trainer.Location = new Point(28, 46); _trainer.Width = 270;
            if (_lastTrainer >= 0 && _lastTrainer < _trainers.Count) _trainer.SelectedIndex = _lastTrainer; else if (_trainers.Count > 0) _trainer.SelectedIndex = 0;
            foe.Controls.Add(_trainer);
            foe.Controls.Add(_vsWild); _vsWild.Location = new Point(10, 84);
            for (int i = 0; i < 3; i++)
            {
                _wild[i] = Species(speciesIds, _lastWild[i]); _wild[i].Location = new Point(28, 110 + i * 30); _wild[i].Width = 180;
                _wildLv[i] = new NumericUpDown { Minimum = 1, Maximum = 100, Value = _lastWildLv[i], Location = new Point(216, 110 + i * 30), Width = 60 };
                foe.Controls.Add(_wild[i]); foe.Controls.Add(_wildLv[i]);
            }
            foe.Controls.Add(new Label { Text = "(the 2nd and 3rd are used in double / triple battles)", Location = new Point(28, 202), AutoSize = true, ForeColor = Color.DimGray });

            var opts = new GroupBox { Text = "Battle", Left = 320, Top = 266, Width = 310, Height = 74 };
            _size.Items.AddRange(new object[] { "Single battle", "Double battle", "Triple battle" }); _size.SelectedIndex = _lastSize;
            _size.Location = new Point(12, 20); opts.Controls.Add(_size);
            _items.Location = new Point(12, 48); _items.Checked = _lastItems; opts.Controls.Add(_items);

            if (_lastTeamFromTrainer) _teamTrainer.Checked = true; else _teamList.Checked = true;
            if (_lastUseTrainer && _trainers.Count > 0) _vsTrainer.Checked = true; else _vsWild.Checked = true;

            var note = new Label { Text = "The game starts a new save-less session straight in this battle and closes when it ends. Losing doesn't send you to a Pokémon Center.", Left = 10, Top = 350, Width = 620, Height = 36 };
            var start = new Button { Text = "Start Battle", Left = 430, Top = 420, Width = 100, Height = 32, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", Left = 536, Top = 420, Width = 94, Height = 32, DialogResult = DialogResult.Cancel };
            Controls.AddRange(new Control[] { team, foe, opts, note, start, cancel });
            AcceptButton = start; CancelButton = cancel;
            start.Click += (s, e) => { if (!Build()) DialogResult = DialogResult.None; };
        }

        static ComboBox Species(string[] ids, string value)
        {
            var c = new ComboBox { AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems };
            c.Items.Add(""); c.Items.AddRange(ids);
            c.Text = value ?? "";
            return c;
        }

        static string TrainerSpec(PokeCreator.Data.TrainerData t) { return t.TrainerType + ":" + t.Name + ":" + t.Version; }

        bool Build()
        {
            var args = new List<string>();
            string party;
            if (_teamTrainer.Checked)
            {
                if (_teamTrainerBox.SelectedIndex < 0) { MessageBox.Show(this, "Choose the trainer whose team you'll use.", Text); return false; }
                party = "trainer:" + TrainerSpec(_trainers[_teamTrainerBox.SelectedIndex]);
            }
            else
            {
                var picks = new List<string>();
                for (int i = 0; i < 6; i++)
                {
                    string sp = _species[i].Text.Trim().ToUpperInvariant();
                    _lastSpecies[i] = sp; _lastLevels[i] = (int)_levels[i].Value;
                    if (sp.Length == 0) continue;
                    if (!_p.Species.Any(s => s.Id == sp)) { MessageBox.Show(this, sp + " isn't a Pokémon in this project.", Text); return false; }
                    picks.Add(sp + ":" + (int)_levels[i].Value);
                }
                if (picks.Count == 0) { MessageBox.Show(this, "Add at least one Pokémon to your team.", Text); return false; }
                party = string.Join(",", picks);
            }
            args.Add("--test-party"); args.Add(Q(party));
            if (_vsTrainer.Checked)
            {
                if (_trainer.SelectedIndex < 0) { MessageBox.Show(this, "Choose a trainer to battle.", Text); return false; }
                args.Add("--test-trainer"); args.Add(Q(TrainerSpec(_trainers[_trainer.SelectedIndex])));
            }
            else
            {
                var w = new List<string>();
                for (int i = 0; i < 3; i++)
                {
                    string sp = _wild[i].Text.Trim().ToUpperInvariant();
                    _lastWild[i] = sp; _lastWildLv[i] = (int)_wildLv[i].Value;
                    if (sp.Length == 0) continue;
                    if (!_p.Species.Any(s => s.Id == sp)) { MessageBox.Show(this, sp + " isn't a Pokémon in this project.", Text); return false; }
                    w.Add(sp + ":" + (int)_wildLv[i].Value);
                }
                if (w.Count == 0) { MessageBox.Show(this, "Choose at least one wild Pokémon.", Text); return false; }
                args.Add("--test-wild"); args.Add(Q(string.Join(",", w)));
            }
            args.Add("--test-size"); args.Add((_size.SelectedIndex + 1).ToString());
            if (_items.Checked)
            {
                var give = new[] { "POTION:5", "SUPERPOTION:5", "FULLRESTORE:3", "FULLHEAL:3", "REVIVE:2", "POKEBALL:10", "ULTRABALL:5" }.Where(x => _p.Items.Any(it => it.Id == x.Split(':')[0]));
                args.Add("--test-items"); args.Add(Q(string.Join(",", give)));
            }
            _lastTrainer = _trainer.SelectedIndex; _lastTeamTrainer = _teamTrainerBox.SelectedIndex; _lastSize = _size.SelectedIndex;
            _lastUseTrainer = _vsTrainer.Checked; _lastTeamFromTrainer = _teamTrainer.Checked; _lastItems = _items.Checked;
            GameArgs = args.ToArray();
            return true;
        }

        static string Q(string s) { return "\"" + s.Replace("\"", "") + "\""; }
    }
}
