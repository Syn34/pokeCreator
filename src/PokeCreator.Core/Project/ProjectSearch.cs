using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using PokeCreator.Json;
using PokeCreator.Maps;

namespace PokeCreator.Projects
{
    /// <summary>One place a switch or variable is used.</summary>
    public class Usage
    {
        public bool IsVariable;
        public int Id;
        public string Where;    // "Map 003: Route 1, event 004 (Youngster), page 2"
        public string What;     // "Control Switches: set ON"
        public string Target;   // same targets as Problem.Target ("map:3:event:4", "commonevent:2")
    }

    /// <summary>Finds where switches and variables are used: page conditions, commands, move routes, scripts and common events.</summary>
    public static class ProjectSearch
    {
        static readonly Regex ScriptSwitch = new Regex(@"\$game_switches\[\s*(\d+)\s*\]", RegexOptions.Compiled);
        static readonly Regex ScriptVariable = new Regex(@"\$game_variables\[\s*(\d+)\s*\]", RegexOptions.Compiled);
        const int MaxRange = 5000; // "set switches 1 to 9999" only counts the first ones

        /// <summary>Every switch and variable use in the project, in one pass.</summary>
        public static List<Usage> AllUses(Project p)
        {
            var list = new List<Usage>();
            foreach (var info in p.MapInfos.OrderBy(m => m.Id))
            {
                var map = p.LoadMap(info.Id);
                if (map == null) continue;
                string mapName = "Map " + info.Id.ToString("000") + ": " + info.Name;
                foreach (var ev in map.Events.OrderBy(e => e.Id))
                {
                    string target = "map:" + info.Id + ":event:" + ev.Id;
                    for (int pi = 0; pi < ev.Pages.Count; pi++)
                    {
                        var pg = ev.Pages[pi];
                        string ptarget = target + ":page:" + (pi + 1);
                        string where = mapName + ", event " + ev.Id.ToString("000") + (string.IsNullOrEmpty(ev.Name) ? "" : " (" + ev.Name + ")") + (ev.Pages.Count > 1 ? ", page " + (pi + 1) : "");
                        var c = pg.Condition;
                        if (c != null)
                        {
                            if (c.Switch1Valid) Add(list, false, c.Switch1Id, where, ptarget, "Page condition: switch is ON");
                            if (c.Switch2Valid) Add(list, false, c.Switch2Id, where, ptarget, "Page condition: switch is ON");
                            if (c.VariableValid) Add(list, true, c.VariableId, where, ptarget, "Page condition: variable is at least " + c.VariableValue);
                        }
                        if (pg.MoveType == 3 && pg.MoveRoute != null) RouteUses(list, pg.MoveRoute.List, where, ptarget, "Custom movement");
                        CommandUses(list, pg.List, where, ptarget);
                    }
                }
            }
            foreach (var ce in p.CommonEvents.OrderBy(c => c.Id))
            {
                string where = "Common event " + ce, target = "commonevent:" + ce.Id;
                if (ce.Trigger != 0) Add(list, false, ce.SwitchId, where, target, (ce.Trigger == 1 ? "Autorun" : "Parallel") + " while this switch is ON");
                CommandUses(list, ce.List, where, target);
            }
            return list;
        }

        static void Add(List<Usage> list, bool isVar, int id, string where, string target, string what)
        {
            if (id > 0) list.Add(new Usage { IsVariable = isVar, Id = id, Where = where, Target = target, What = what });
        }

        static void AddRange(List<Usage> list, bool isVar, int a, int b, string where, string target, string what)
        {
            if (b < a) { int t = a; a = b; b = t; }
            for (int id = a; id <= b && id < a + MaxRange; id++) Add(list, isVar, id, where, target, what);
        }

        static readonly string[] VarOps = { "set", "add to", "subtract from", "multiply", "divide", "remainder of" };

        static void CommandUses(List<Usage> list, List<EventCommand> cmds, string where, string target)
        {
            if (cmds == null) return;
            for (int i = 0; i < cmds.Count; i++)
            {
                var c = cmds[i];
                switch (c.Code)
                {
                    case 111:
                        {
                            int type = c.IntParam(0);
                            if (type == 0) Add(list, false, c.IntParam(1), where, target, "Conditional Branch: switch is " + (c.IntParam(2) == 0 ? "ON" : "OFF"));
                            else if (type == 1)
                            {
                                Add(list, true, c.IntParam(1), where, target, "Conditional Branch: compares the variable");
                                if (c.IntParam(2) == 1) Add(list, true, c.IntParam(3), where, target, "Conditional Branch: compared against");
                            }
                            else if (type == 12) ScriptUses(list, c.StrParam(1), where, target);
                            break;
                        }
                    case 121: AddRange(list, false, c.IntParam(0), c.IntParam(1), where, target, "Control Switches: set " + (c.IntParam(2) == 0 ? "ON" : "OFF")); break;
                    case 122:
                        {
                            int op = c.IntParam(2);
                            AddRange(list, true, c.IntParam(0), c.IntParam(1), where, target, "Control Variables: " + (op >= 0 && op < VarOps.Length ? VarOps[op] : "change"));
                            if (c.IntParam(3) == 1) Add(list, true, c.IntParam(4), where, target, "Control Variables: value read from here");
                            break;
                        }
                    case 103: Add(list, true, c.IntParam(0), where, target, "Input Number: stores the number"); break;
                    case 125: if (c.IntParam(1) == 1) Add(list, true, c.IntParam(2), where, target, "Change Money: amount read from here"); break;
                    case 201:
                        if (c.IntParam(0) == 1)
                        {
                            Add(list, true, c.IntParam(1), where, target, "Transfer Player: map ID");
                            Add(list, true, c.IntParam(2), where, target, "Transfer Player: X");
                            Add(list, true, c.IntParam(3), where, target, "Transfer Player: Y");
                        }
                        break;
                    case 202:
                        if (c.IntParam(1) == 1)
                        {
                            Add(list, true, c.IntParam(2), where, target, "Set Event Location: X");
                            Add(list, true, c.IntParam(3), where, target, "Set Event Location: Y");
                        }
                        break;
                    case 209:
                        {
                            var route = c.Params.Count > 1 ? c.Params[1] as JsonObject : null;
                            var steps = route == null ? null : route.GetArray("List");
                            if (steps != null)
                                foreach (var s in steps.OfType<JsonObject>())
                                {
                                    int code = s.GetInt("Code");
                                    if (code != 27 && code != 28) continue;
                                    var ps = s.GetArray("Params");
                                    int id = ps != null && ps.Count > 0 && (ps[0] is double || ps[0] is int || ps[0] is long) ? Convert.ToInt32(ps[0], System.Globalization.CultureInfo.InvariantCulture) : 0;
                                    Add(list, false, id, where, target, "Set Move Route: switch " + (code == 27 ? "ON" : "OFF"));
                                }
                            break;
                        }
                    case 355:
                        {
                            string script = c.StrParam(0);
                            for (int k = i + 1; k < cmds.Count && cmds[k].Code == 655; k++) script += "\n" + cmds[k].StrParam(0);
                            ScriptUses(list, script, where, target);
                            break;
                        }
                }
            }
        }

        static void RouteUses(List<Usage> list, List<EventCommand> steps, string where, string target, string label)
        {
            if (steps == null) return;
            foreach (var s in steps)
                if (s.Code == 27 || s.Code == 28) Add(list, false, s.IntParam(0), where, target, label + ": switch " + (s.Code == 27 ? "ON" : "OFF"));
        }

        static void ScriptUses(List<Usage> list, string script, string where, string target)
        {
            if (string.IsNullOrEmpty(script)) return;
            foreach (Match m in ScriptSwitch.Matches(script)) Add(list, false, int.Parse(m.Groups[1].Value), where, target, "Script: " + Short(script));
            foreach (Match m in ScriptVariable.Matches(script)) Add(list, true, int.Parse(m.Groups[1].Value), where, target, "Script: " + Short(script));
        }

        static string Short(string s) { s = s.Replace("\n", " ").Trim(); return s.Length > 60 ? s.Substring(0, 57) + "..." : s; }
    }
}
