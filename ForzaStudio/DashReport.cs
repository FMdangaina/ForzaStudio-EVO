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

        // The event table (block 3). Every word is shown with what it is: an element index of the .bgf (name), or a hash that is the name of
        // a slide, an element or a known word (bit 31 may be set or cleared: the same name). 1195C23B is in every record and unnamed.
        // Records are 7 words in the car-presentation dashes: event name hash, behaviour, component, 0, 1195C23B, slide hash, 0.
        private static void AppendEvents(StringBuilder s, AnarkDash dash)
        {
            List<uint> words = dash.Bgf.EventWords();
            if (words == null || words.Count == 0)
            {
                s.AppendLine("  event table: empty");
                return;
            }
            Dictionary<uint, string> names = new Dictionary<uint, string>();
            foreach (string known in KnownNames)
            {
                names[AnarkBgf.HashFull(known) & 0x7FFFFFFF] = known;
            }
            foreach (AnarkDash.Node n in dash.Nodes)
            {
                if (n.Record != null && !string.IsNullOrEmpty(n.Name))
                {
                    names[AnarkBgf.HashFull(n.Name) & 0x7FFFFFFF] = "element " + n.Name;
                }
            }
            if (dash.Bgf.Slides != null)
            {
                foreach (AnarkBgf.Slide slide in dash.Bgf.Slides)
                {
                    names[AnarkBgf.HashFull(slide.Label) & 0x7FFFFFFF] = "slide '" + slide.Label + "'";
                }
            }
            s.AppendFormat("  event table, {0} words (hash = name hash; the same name with bit 31 set or cleared):\r\n", words.Count);
            for (int i = 0; i < words.Count; i++)
            {
                uint w = words[i];
                string label = "";
                string name;
                if (w < dash.Nodes.Count)
                {
                    label = "element " + w + " " + dash.Nodes[(int) w].Name;
                }
                else if (names.TryGetValue(w & 0x7FFFFFFF, out name))
                {
                    label = name;
                }
                else if (w == 0x1195C23B)
                {
                    label = "(in every record)";
                }
                s.AppendFormat("    [{0,3}] {1:X8}  {2}\r\n", i, w, label);
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
            List<uint[]> table = bgf.BehaviourTable();
            if (table != null)
            {
                s.AppendLine("  behaviour table (element index of the behaviour; the numbers after it are slot numbers of its parameters):");
                foreach (uint[] row in table)
                {
                    string name = (row[0] < dash.Nodes.Count && dash.Nodes[(int) row[0]].Parent != null) ? dash.Nodes[(int) row[0]].Parent.Name : "?";
                    List<string> rest = new List<string>();
                    for (int i = 1; i < row.Length; i++)
                    {
                        rest.Add(row[i].ToString());
                    }
                    s.AppendFormat("    behaviour {0} of {1}: {2}\r\n", row[0], name, string.Join(" ", rest.ToArray()));
                }
            }
            List<uint[]> tracks = bgf.Tracks();
            if (tracks != null)
            {
                s.AppendFormat("  {0} animation tracks (a property of an element moves through keys: time in ms, value):\r\n", tracks.Count);
                foreach (uint[] t in tracks)
                {
                    string element = (t[0] < dash.Nodes.Count) ? dash.Nodes[(int) t[0]].Name : "?";
                    s.AppendFormat("    element {0} \"{1}\" property {2} ({3} keys)\r\n", t[0], element, AnarkBgf.PropertyName(t[1]) ?? ("hash " + (t[1] & 0x07FFFFFF).ToString("X7")), t[2]);
                }
            }
            AppendEvents(s, dash);
            if (bgf.Slides == null)
            {
                s.AppendLine("  (slides not understood in this file)");
            }
            else
            {
                s.AppendFormat("  {0} slides. A slide lists the elements a component owns, with a flag and the animation tracks it uses.\r\n", bgf.Slides.Count);
                foreach (AnarkBgf.Slide slide in bgf.Slides)
                {
                    string owner = (slide.Entries.Count > 0 && slide.Entries[0].Index < dash.Nodes.Count) ? dash.Nodes[(int) slide.Entries[0].Index].Name : "?";
                    s.AppendFormat("  slide '{0}' of {1} (element {2}), duration {3} ms, {4} entries: ", slide.Label, owner, (slide.Entries.Count > 0) ? (int) slide.Entries[0].Index : -1, slide.Duration, slide.Entries.Count);
                    List<string> list = new List<string>();
                    foreach (AnarkBgf.SlideEntry en in slide.Entries)
                    {
                        list.Add(en.Index + ((en.Flag != 0) ? "+" : "") + ((en.TrackCount > 0) ? "(" + en.TrackCount + " tracks)" : ""));
                    }
                    s.AppendLine(string.Join(" ", list.ToArray()));
                }
            }
            return s.ToString();
        }
    }
}
