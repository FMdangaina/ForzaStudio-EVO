using System;
using System.Collections.Generic;
using System.IO;
using ForzaStudio;

// Offline checks of the dash editor on the game's dashes: duplicate in place (+ remove gives the files back) and remove anywhere.
static class DashTest
{
    static Dictionary<string, int> stats = new Dictionary<string, int>();
    static void Count(string k) { int v; stats.TryGetValue(k, out v); stats[k] = v + 1; }
    static int shown = 0;
    static void Fail(string what, string file)
    {
        Count("FAIL " + what);
        if (shown++ < 40) Console.WriteLine("FAIL " + what + "  " + file);
    }

    static AnarkDash Load(byte[] f, byte[] g, byte[] s)
    {
        return new AnarkDash(AnarkFbf.Read(f), AnarkBgf.Read(g), s == null ? null : AnarkBsg.Read(s));
    }

    static bool Same(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    // Owned elements of a component: itself, its elements down to the next component, the components right under it.
    static List<int> Owned(AnarkBgf b, int comp)
    {
        List<int> list = new List<int>();
        list.Add(comp);
        for (int i = comp + 1; i < b.Elements.Count; i++)
        {
            int c = OwnerComponent(b, b.Elements[i].Parent);
            if (c == comp) list.Add(i);
        }
        return list;
    }

    static int OwnerComponent(AnarkBgf b, int i)
    {
        for (; i >= 0; i = b.Elements[i].Parent)
            if (b.Elements[i].Type == AnarkBgf.ElementType.Component) return i;
        return -1;
    }

    // Everything the game's files obey; returns null or what is wrong.
    static string Check(AnarkDash d, bool[] ownedRuleHeld)
    {
        AnarkBgf b = AnarkBgf.Read(d.Bgf.Write());
        AnarkFbf f = AnarkFbf.Read(d.Fbf.Write());
        if (d.Bsg != null) AnarkBsg.Read(d.Bsg.Write());
        if (!b.TailEditable) return "tail no longer reads";
        byte[] h = (byte[]) b.Header.Clone();
        b.UpdateHeader();
        if (!Same(h, b.Header)) return "header counts";
        int n = b.Elements.Count;
        for (int i = 0; i < n; i++)
        {
            int p = b.Elements[i].Parent;
            if (i == 0 ? p != -1 : (p < 0 || p >= i)) return "parent order at " + i;
            // depth first: everything between p and i is under p
            for (int k = p + 1; k < i; k++)
            {
                int q = k;
                while (q > p) q = b.Elements[q].Parent;
                if (q != p) return "not depth first at " + i;
            }
        }
        Dictionary<uint, bool> ids = new Dictionary<uint, bool>();
        foreach (AnarkBgf.Element e in b.Elements)
        {
            if (e.Type == AnarkBgf.ElementType.Behaviour) continue;
            if (ids.ContainsKey(e.Id)) return "duplicate id " + e.Id;
            ids[e.Id] = true;
            if (f.Find(e.Id) == null) return "element without record " + e.Id;
        }
        for (int i = 1; i < b.AnimationTracks.Count; i++)
            if (b.AnimationTracks[i].Element < b.AnimationTracks[i - 1].Element) return "tracks not sorted";
        for (int i = 1; i < b.Declared.Count; i++)
            if (b.Declared[i].Element < b.Declared[i - 1].Element) return "declared not sorted";
        List<AnarkBgf.EventAction> acts = b.AllActions();
        int[] actRefs = new int[acts.Count];
        int[] trackRefs = new int[b.AnimationTracks.Count];
        foreach (AnarkBgf.Slide s in b.Slides)
        {
            for (int k = 1; k < s.Entries.Count; k++)
                if (s.Entries[k].Index <= s.Entries[k - 1].Index) return "slide entries not in index order";
            int comp = (int) s.Entries[0].Index;
            if (b.Elements[comp].Type != AnarkBgf.ElementType.Component) return "slide of a non component";
            if (ownedRuleHeld == null || ownedRuleHeld[0])
            {
                List<int> owned = Owned(b, comp);
                if (owned.Count != s.Entries.Count) return "slide does not list what its component owns (" + owned.Count + " vs " + s.Entries.Count + ")";
                for (int k = 0; k < owned.Count; k++) if (owned[k] != s.Entries[k].Index) return "slide lists other elements";
            }
            foreach (AnarkBgf.SlideEntry e in s.Entries)
                foreach (uint r in e.Refs)
                {
                    int ix = (int) (r & 0xFFFF);
                    if ((r >> 24) == 0) { trackRefs[ix]++; if (b.AnimationTracks[ix].Element != e.Index) return "track ref on another element"; }
                    else { actRefs[ix]++; if (acts[ix].Context != e.Index) return "action ref not on its context"; }
                }
        }
        return null;
    }

    static bool OwnedRule(AnarkBgf b)
    {
        foreach (AnarkBgf.Slide s in b.Slides)
        {
            List<int> owned = Owned(b, (int) s.Entries[0].Index);
            if (owned.Count != s.Entries.Count) return false;
            for (int k = 0; k < owned.Count; k++) if (owned[k] != s.Entries[k].Index) return false;
        }
        return true;
    }

    static bool Plain(AnarkDash d, AnarkDash.Node n)
    {
        foreach (int i in d.Subtree(n))
            if (d.Nodes[i].Element.Type == AnarkBgf.ElementType.Component || d.Nodes[i].Element.Type == AnarkBgf.ElementType.Behaviour) return false;
        return true;
    }

    static int Main(string[] args)
    {
        foreach (string bgfPath in Directory.GetFiles(args[0], "*.bgf", SearchOption.AllDirectories))
        {
            string fbfPath = Path.ChangeExtension(bgfPath, ".fbf"), bsgPath = Path.ChangeExtension(bgfPath, ".bsg");
            if (!File.Exists(fbfPath)) { Count("no fbf"); continue; }
            byte[] F = File.ReadAllBytes(fbfPath), G = File.ReadAllBytes(bgfPath), S = File.Exists(bsgPath) ? File.ReadAllBytes(bsgPath) : null;
            AnarkDash d0 = Load(F, G, S);
            Count("dashes");
            // element hash = full sdbm of the record name
            foreach (AnarkDash.Node nd in d0.Nodes)
            {
                if (nd.Record == null) continue;
                Count(nd.Element.Hash == AnarkBgf.HashFull(nd.Record.Name) ? "hash = HashFull(name)" : "hash != HashFull(name)");
            }
            try { DashReport.Build(d0); Count("report ok"); } catch (Exception e) { Fail("report " + e.Message, bgfPath); }
            bool[] rule = new bool[] { OwnedRule(d0.Bgf) };
            Count(rule[0] ? "owned rule holds" : "owned rule does not hold");
            string c0 = Check(d0, rule);
            if (c0 != null) { Fail("original: " + c0, bgfPath); continue; }

            int count = d0.Nodes.Count;
            for (int i = 1; i < count; i++)
            {
                // duplicate in place, then remove the copy: the three files must come back byte for byte
                AnarkDash d = Load(F, G, S);
                AnarkDash.Node node = d.Nodes[i];
                AnarkBgf.ElementType t = node.Element.Type;
                if (t == AnarkBgf.ElementType.Node || t == AnarkBgf.ElementType.Text)
                {
                    AnarkDash.Node copy = null;
                    try { copy = d.Duplicate(node, node.Name + "_copy", false); }
                    catch (Exception e) { Fail("duplicate threw " + e.GetType().Name + " " + e.Message, bgfPath + " #" + i); }
                    if (copy == null) Count("duplicate blocked");
                    else
                    {
                        Count("duplicated");
                        if (copy.Index != i + d0.Subtree(d0.Nodes[i]).Count) Fail("copy not right after the original", bgfPath);
                        if (copy.Element.Parent != node.Element.Parent) Fail("copy under another parent", bgfPath);
                        if (copy.Element.Hash != AnarkBgf.HashFull(copy.Record.Name) && node.Element.Hash == AnarkBgf.HashFull(node.Record.Name)) Fail("copy of a conforming element without its own hash", bgfPath);
                        string c = Check(d, rule);
                        if (c != null) Fail("after duplicate: " + c, bgfPath + " #" + i);
                        int tracksBefore = 0, tracksAfter = d.Bgf.AnimationTracks.Count;
                        tracksBefore = d0.Bgf.AnimationTracks.Count;
                        if (tracksAfter > tracksBefore) Count("duplicates with animation tracks copied");
                        AnarkDash.Node again = d.Nodes[copy.Index];
                        if (!d.Remove(again)) Fail("remove of the copy blocked: " + d.Blocked, bgfPath);
                        else if (!Same(d.Fbf.Write(), F) || !Same(d.Bgf.Write(), G) || (S != null && !Same(d.Bsg.Write(), S)))
                        {
                            Fail("duplicate + remove does not give the files back"
                                + (Same(d.Fbf.Write(), F) ? "" : " fbf") + (Same(d.Bgf.Write(), G) ? "" : " bgf") + (S == null || Same(d.Bsg.Write(), S) ? "" : " bsg"), bgfPath + " #" + i);
                        }
                        else Count("duplicate + remove = original");
                    }
                }
                // remove anywhere
                d = Load(F, G, S);
                node = d.Nodes[i];
                if (node.Element.Type == AnarkBgf.ElementType.Behaviour) continue;
                try
                {
                    if (!d.Remove(node)) { Count("remove blocked"); continue; }
                }
                catch (Exception e) { Fail("remove threw " + e.GetType().Name + " " + e.Message, bgfPath + " #" + i); continue; }
                Count("removed");
                string cr = Check(d, rule);
                if (cr != null) Fail("after remove: " + cr, bgfPath + " #" + i);
            }
        }
        // import: the first group holding a text of the first dash into every dash (Add > Texts from another car's dash)
        string[] all = Directory.GetFiles(args[0], "*.bgf", SearchOption.AllDirectories);
        AnarkDash src = null; AnarkDash.Node part = null;
        foreach (string g in all)
        {
            string fp = Path.ChangeExtension(g, ".fbf"), sp = Path.ChangeExtension(g, ".bsg");
            if (!File.Exists(fp) || !File.Exists(sp)) continue;
            src = Load(File.ReadAllBytes(fp), File.ReadAllBytes(g), File.ReadAllBytes(sp));
            foreach (AnarkDash.Node nd in src.Nodes)
                if (nd.Record != null && nd.Record.Type == AnarkFbf.RecordType.Group && src.FirstText(nd) != null && nd.Element.Type == AnarkBgf.ElementType.Node && Plain(src, nd)) { part = nd; break; }
            if (part != null) { Console.WriteLine("import source " + g + " part " + part.Name); break; }
        }
        foreach (string g in all)
        {
            string fp = Path.ChangeExtension(g, ".fbf"), sp = Path.ChangeExtension(g, ".bsg");
            if (!File.Exists(fp) || !File.Exists(sp)) continue;
            AnarkDash d = Load(File.ReadAllBytes(fp), File.ReadAllBytes(g), File.ReadAllBytes(sp));
            bool[] rule = new bool[] { OwnedRule(d.Bgf) };
            AnarkDash.Node r = null;
            try { r = d.Import(src, part, part.Name, true); } catch (Exception e) { Fail("import threw " + e.Message, g); continue; }
            if (r == null) { Count("import blocked: " + d.Blocked); continue; }
            Count("imported");
            d.BindImported(src, d.LastImport);
            string c = Check(d, rule);
            if (c != null) Fail("after import: " + c, g);
        }
        List<string> keys = new List<string>(stats.Keys);
        keys.Sort();
        foreach (string k in keys) Console.WriteLine("{0,-50} {1}", k, stats[k]);
        return 0;
    }
}
