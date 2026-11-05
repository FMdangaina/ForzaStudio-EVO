namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    // A text listing of everything in the three files of a dash and how they join: every element of the .bgf graph
    // with its .fbf record and its .bsg base node, all properties with their data kind, the tail of the .bgf
    // (behaviours, animation tracks, events, slides) and the .bsg table. For reading, not for editing.
    public static class DashReport
    {
        // Words the game's code sends or the editor's authors typed, whose hash is not a name in this file.
        private static readonly string[] KnownNames = { "ON", "OFF", "IDLE", "UIContract", "Scene", "GAUGES", "PROGRESS", "FLASH", "IN GAME", "START UP", "SHUT OFF", "DOOR OPEN", "DOOR_CLOSE", "DOOR CLOSE" };

        // Names for the hashes of the tail: elements (full hash), events and slides (hash with bit 31 cleared), contract behaviours
        // (name + "_CONTRACT"), the strings of the .bgf and the known words.
        private static Dictionary<uint, string> TailNames(AnarkDash dash)
        {
            Dictionary<uint, string> names = new Dictionary<uint, string>();
            List<string> words = new List<string>(KnownNames);
            words.AddRange(dash.Bgf.Strings);
            if (dash.Bgf.Slides != null)
            {
                foreach (AnarkBgf.Slide slide in dash.Bgf.Slides)
                {
                    words.Add(slide.Label);
                }
            }
            foreach (string w in words)
            {
                if (string.IsNullOrEmpty(w))
                {
                    continue;
                }
                uint h = AnarkBgf.HashFull(w);
                if (!names.ContainsKey(h & 0x7FFFFFFF))
                {
                    names[h & 0x7FFFFFFF] = w;
                }
                if (!names.ContainsKey(h))
                {
                    names[h] = w;
                }
                names[AnarkBgf.HashFull(w + "_CONTRACT")] = w + "_CONTRACT";
            }
            foreach (AnarkDash.Node n in dash.Nodes)
            {
                if (n.Record != null && !string.IsNullOrEmpty(n.Name))
                {
                    names[AnarkBgf.HashFull(n.Name)] = n.Name;
                }
            }
            return names;
        }

        private static string Named(Dictionary<uint, string> names, uint hash)
        {
            string name;
            return names.TryGetValue(hash, out name) ? "'" + name + "'" : hash.ToString("X8");
        }

        private static string ElementLabel(AnarkDash dash, int index)
        {
            if (index < 0 || index >= dash.Nodes.Count)
            {
                return "element " + index + " (?)";
            }
            AnarkDash.Node n = dash.Nodes[index];
            string name = n.Name;
            if (n.Element.Type == AnarkBgf.ElementType.Behaviour && n.Parent != null)
            {
                name = "behaviour of " + n.Parent.Name;
            }
            return index + " " + name;
        }

        // Blocks 1-3 of the tail (decoded 2026-11-05, see docs/ANARK_DASH.md): declared events, animation tracks, events.
        private static void AppendTail(StringBuilder s, AnarkDash dash)
        {
            AnarkBgf bgf = dash.Bgf;
            if (bgf.Declared == null)
            {
                s.AppendLine("  (declared events, animation tracks and events not understood in this file)");
                return;
            }
            Dictionary<uint, string> names = TailNames(dash);
            s.AppendFormat("  events declared by behaviours ({0}):\r\n", bgf.Declared.Count);
            foreach (AnarkBgf.DeclaredEvents de in bgf.Declared)
            {
                List<string> list = new List<string>();
                foreach (int i in de.Strings)
                {
                    list.Add(bgf.Strings[i]);
                }
                s.AppendFormat("    {0} (hash {1}): {2}\r\n", ElementLabel(dash, de.Element), Named(names, bgf.Elements[de.Element].Hash),
                    (list.Count > 0) ? string.Join(", ", list.ToArray()) : "none");
            }
            s.AppendFormat("  {0} animation tracks (a property of an element moves through keys: time in ms, value):\r\n", bgf.AnimationTracks.Count);
            for (int i = 0; i < bgf.AnimationTracks.Count; i++)
            {
                AnarkBgf.Track t = bgf.AnimationTracks[i];
                s.AppendFormat("    [{0}] element {1} property {2} (type {3:X}, {4} keys)\r\n", i, ElementLabel(dash, t.Element),
                    AnarkBgf.PropertyName(t.Hash) ?? ("hash " + (t.Hash & 0x07FFFFFF).ToString("X7")), t.Type, t.KeyCount);
            }
            int actions = 0;
            s.AppendFormat("  events ({0} groups): owner, then 'on <event>' and its actions [action number]: command -> target (in the time of)\r\n", bgf.EventGroups.Count);
            foreach (AnarkBgf.EventGroup g in bgf.EventGroups)
            {
                s.AppendFormat("    owner {0}\r\n", ElementLabel(dash, g.Owner));
                foreach (AnarkBgf.Event e in g.Events)
                {
                    s.AppendFormat("      on {0}\r\n", Named(names, e.Hash));
                    foreach (AnarkBgf.EventAction a in e.Actions)
                    {
                        string arg;
                        switch (a.Command)
                        {
                            case AnarkBgf.CommandGotoSlideName:
                            case AnarkBgf.CommandFireEvent:
                                arg = Named(names, a.Arg1);
                                break;
                            case AnarkBgf.CommandSetProperty:
                                arg = (AnarkBgf.PropertyName(a.Arg1) ?? ("hash " + (a.Arg1 & 0x07FFFFFF).ToString("X7"))) + " = "
                                    + BitConverter.ToSingle(BitConverter.GetBytes(a.Arg2), 0).ToString("0.####");
                                break;
                            case AnarkBgf.CommandGotoTime:
                                arg = a.Arg1 + " ms";
                                break;
                            default:
                                arg = (a.Arg1 != 0 || a.Arg2 != 0) ? a.Arg1.ToString("X8") + " " + a.Arg2.ToString("X8") : "";
                                break;
                        }
                        s.AppendFormat("        [{0}] {1} {2} -> {3} (in the time of {4})\r\n", actions++, AnarkBgf.CommandName(a.Command), arg,
                            ElementLabel(dash, a.Target), ElementLabel(dash, a.Context));
                    }
                }
            }
        }

        public static string Build(AnarkDash dash)
        {
            AnarkFbf fbf = dash.Fbf;
            AnarkBgf bgf = dash.Bgf;
            AnarkBsg bsg = dash.Bsg;
            StringBuilder s = new StringBuilder();
            s.AppendLine("HOW THE THREE FILES JOIN");
            s.AppendLine("  .bgf  the graph: one ELEMENT per line below (index = its place in the list, parent = index of its parent).");
            s.AppendLine("        It holds what changes: properties (position, colour, text...), the data contract, animations, slides.");
            s.AppendLine("  .fbf  the scene: one RECORD per element with the same id (name, kind, text/font, material colour, image path) and the meshes.");
            s.AppendLine("  .bsg  the base values: one NODE per element that has a place (same id): position, rotation, scale, pivot, opacity.");
            s.AppendLine("  The id joins the three. A property in the .bgf wins over the .bsg value; without one the .bsg value is used.");
            s.AppendLine("  Behaviour elements (id 0) exist only in the .bgf. Materials and images are in the .fbf and in the .bsg table, not as nodes.");
            s.AppendLine();
            s.AppendLine("DATA KINDS (top 5 bits of a property hash; the low 27 bits are the sdbm hash of its name; +16 = behaviour parameter)");
            s.AppendLine("  1 int   3 float   4 bool   5 string (index into the .bgf strings)");
            s.AppendLine("ELEMENT TYPES (.bgf): 1 node (group, model)  2 camera  3 light  4 text  5 material  6 image  7 component (scene, gauge)  8 behaviour");
            s.AppendLine("RECORD TYPES (.fbf): 0 group  1 text  2 camera  3 layer  4 model (mesh)  5 material  6 light  7 image");
            s.AppendLine();
            s.AppendFormat("FILES: .fbf version 0x{0:X}, {1} records, {2} meshes; .bgf {3} elements, {4} strings; .bsg {5}\r\n",
                fbf.Version, fbf.Records.Count, fbf.Meshes.Count, bgf.Elements.Count, bgf.Strings.Count,
                (bsg == null) ? "none" : bsg.Nodes.Count + " nodes, " + ((bsg.Table != null) ? bsg.Table.Count + " in the material/image table" : "table not read"));
            s.AppendLine();
            s.AppendLine("ELEMENTS (indent = depth)");
            foreach (AnarkDash.Node n in dash.Nodes)
            {
                AnarkBgf.Element e = n.Element;
                int depth = 0;
                for (AnarkDash.Node p = n.Parent; p != null; p = p.Parent)
                {
                    depth++;
                }
                string pad = new string(' ', depth * 2);
                s.AppendFormat("{0,4} {1}{2} \"{3}\"  bgf {4} (hash {5:X8}), id {6}, parent {7}", n.Index, pad, e.Type, n.Name, (int) e.Type, e.Hash, e.Id, e.Parent);
                if (n.Record != null)
                {
                    AnarkFbf.Record r = n.Record;
                    s.AppendFormat("\r\n{0}      fbf {1}", pad, r.Type);
                    switch (r.Type)
                    {
                        case AnarkFbf.RecordType.Text:
                            s.AppendFormat(": default text '{0}', font '{1}', tag '{2}'", r.Text, r.Font, r.TextTag);
                            break;
                        case AnarkFbf.RecordType.Model:
                            int mesh = (int) AnarkFbf.ReadU32(r.Body, 0);
                            s.AppendFormat(": mesh {0}", mesh);
                            if (mesh >= 0 && mesh < fbf.Meshes.Count)
                            {
                                AnarkFbf.Mesh m = fbf.Meshes[mesh];
                                s.AppendFormat(" ({0} vertices, {1} index buffers, bounds {2:0.#},{3:0.#} to {4:0.#},{5:0.#})", m.VertexCount, m.IndexBufferCount,
                                    AnarkFbf.ReadFloat(m.VertexDesc, 16), AnarkFbf.ReadFloat(m.VertexDesc, 20), AnarkFbf.ReadFloat(m.VertexDesc, 32), AnarkFbf.ReadFloat(m.VertexDesc, 36));
                            }
                            break;
                        case AnarkFbf.RecordType.Material:
                            s.AppendFormat(": diffuse {0:0.##} {1:0.##} {2:0.##} {3:0.##}", AnarkFbf.ReadFloat(r.Body, 0), AnarkFbf.ReadFloat(r.Body, 4), AnarkFbf.ReadFloat(r.Body, 8), AnarkFbf.ReadFloat(r.Body, 12));
                            break;
                        case AnarkFbf.RecordType.Image:
                            s.AppendFormat(": {0} (material id {1})", r.Path, r.Owner);
                            break;
                    }
                }
                if (n.Base != null)
                {
                    float[] v = n.Base.Values;
                    s.AppendFormat("\r\n{0}      bsg node (type {1:X}, parent node {2}): pos {3:0.##},{4:0.##},{5:0.##} rot {6:0.###},{7:0.###},{8:0.###} scale {9:0.##},{10:0.##},{11:0.##} pivot {12:0.##},{13:0.##},{14:0.##} opacity {15:0.##}",
                        pad, n.Base.Type >> 24, n.Base.Parent, v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7], v[8], v[9], v[10], v[11], v[18]);
                }
                s.AppendLine();
                foreach (AnarkBgf.Property p in e.Properties)
                {
                    string value;
                    switch (p.Kind)
                    {
                        case AnarkBgf.ValueKind.Float: value = p.Float.ToString("0.####"); break;
                        case AnarkBgf.ValueKind.String: value = "\"" + bgf.GetString(p) + "\""; break;
                        case AnarkBgf.ValueKind.Bool: value = (p.Value != 0) ? "true" : "false"; break;
                        default: value = ((int) p.Value).ToString(); break;
                    }
                    s.AppendFormat("{0}      . {1} [{2}{3}] = {4}\r\n", pad, p.Name ?? ("hash " + p.NameHash.ToString("X7")), p.Kind, p.IsBehaviourParameter ? ", parameter" : "", value);
                }
            }

            s.AppendLine();
            s.AppendLine("BSG TABLE (every material = 6 and image = 7 of the scene)");
            if (bsg != null && bsg.Table != null)
            {
                foreach (AnarkBsg.TableEntry t in bsg.Table)
                {
                    s.AppendFormat("  id {0} kind {1}\r\n", t.Id, t.Kind);
                }
            }

            s.AppendLine();
            s.AppendLine("BGF TAIL");
            AppendTail(s, dash);
            if (bgf.Slides == null)
            {
                s.AppendLine("  (slides not understood in this file)");
            }
            else
            {
                s.AppendFormat("  {0} slides. A slide lists the elements a component owns: index, + = flag, then the values it sets, the animation tracks and the actions that run in it (* = second reference of the pair).\r\n", bgf.Slides.Count);
                foreach (AnarkBgf.Slide slide in bgf.Slides)
                {
                    string owner = (slide.Entries.Count > 0 && slide.Entries[0].Index < dash.Nodes.Count) ? dash.Nodes[(int) slide.Entries[0].Index].Name : "?";
                    s.AppendFormat("  slide '{0}' of {1} (element {2}), duration {3} ms, {4} entries: ", slide.Label, owner, (slide.Entries.Count > 0) ? (int) slide.Entries[0].Index : -1, slide.Duration, slide.Entries.Count);
                    List<string> list = new List<string>();
                    foreach (AnarkBgf.SlideEntry en in slide.Entries)
                    {
                        string refs = "";
                        if (en.ItemCount > 0 || en.TrackCount > 0)
                        {
                            List<string> parts = new List<string>();
                            if (en.ItemCount > 0)
                            {
                                parts.Add(en.ItemCount + " values");
                            }
                            foreach (uint r in en.Refs)
                            {
                                parts.Add((((r >> 24) == 0) ? "track " : "action ") + (r & 0xFFFF) + (((r >> 16) & 0xFF) != 0 ? "*" : ""));
                            }
                            refs = "(" + string.Join(", ", parts.ToArray()) + ")";
                        }
                        list.Add(en.Index + ((en.Flag != 0) ? "+" : "") + refs);
                    }
                    s.AppendLine(string.Join(" ", list.ToArray()));
                }
            }
            return s.ToString();
        }
    }
}
