using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ForzaStudio
{
    // Anark digital dash graph (.bgf, "AnarkBGF"), the companion of the .fbf. Big-endian.
    //   0x00 header (0x88 bytes, kept as is), u32 string bytes, strings (u32 length + text),
    //   u32 graph bytes, elements, then slides and animations (kept as is).
    // Element: u32 hash, u32 parent (element index, -1 for the root), u32 id (the .fbf record id), u8 type, u8 flag,
    //   [type 7 (component): u8 + u32 duration], u32 count, count x (u32 property hash, u32 value).
    // A property hash is the sdbm hash ((h + c) * 65599) of the name in its low 27 bits; the top 5 bits give the
    // value type (see ValueKind), +16 for behaviour parameters. A property that is missing has its default value.
    // Verified: the 431 .bgf of FM4, FH2 and FH1 are read and written back byte for byte.
    public class AnarkBgf
    {
        public enum ElementType
        {
            Node = 1,          // scene, layer, group, model
            Camera = 2,
            Light = 3,
            Text = 4,
            Material = 5,
            Image = 6,
            Component = 7,
            Behaviour = 8,
        }

        public enum ValueKind
        {
            Int = 1,
            Float = 3,
            Bool = 4,
            String = 5,        // index into Strings
        }

        public class Property
        {
            public uint Hash;
            public uint Value;

            public uint NameHash { get { return this.Hash & 0x07FFFFFF; } }
            public ValueKind Kind { get { return (ValueKind) ((this.Hash >> 27) & 15); } }
            public bool IsBehaviourParameter { get { return (this.Hash & 0x80000000) != 0; } }
            public string Name { get { return PropertyName(this.Hash); } }

            public float Float
            {
                get { return BitConverter.ToSingle(BitConverter.GetBytes(this.Value), 0); }
                set { this.Value = BitConverter.ToUInt32(BitConverter.GetBytes(value), 0); }
            }
        }

        public class Element
        {
            public uint Hash;
            public int Parent;
            public uint Id;
            public ElementType Type;
            public byte Flag;
            public byte[] ComponentPrefix;   // type 7 only: u8 + u32 duration
            public List<Property> Properties = new List<Property>();

            public Property Find(string name)
            {
                uint hash = HashName(name);
                foreach (Property p in this.Properties)
                {
                    if (p.NameHash == hash)
                    {
                        return p;
                    }
                }
                return null;
            }

            public float GetFloat(string name, float fallback)
            {
                Property p = this.Find(name);
                return (p != null && p.Kind == ValueKind.Float) ? p.Float : fallback;
            }

            public int GetInt(string name, int fallback)
            {
                Property p = this.Find(name);
                return (p != null) ? (int) p.Value : fallback;
            }

            public Element Copy()
            {
                Element e = (Element) this.MemberwiseClone();
                if (this.ComponentPrefix != null)
                {
                    e.ComponentPrefix = (byte[]) this.ComponentPrefix.Clone();
                }
                e.Properties = new List<Property>();
                foreach (Property p in this.Properties)
                {
                    Property c = new Property();
                    c.Hash = p.Hash;
                    c.Value = p.Value;
                    e.Properties.Add(c);
                }
                return e;
            }

            public override string ToString()
            {
                return this.Type + " " + this.Id;
            }
        }

        // The counts the header keeps (checked on the 233 dashes of FM4): nodes (elements that are not components) at
        // 0x24 and 0x70, components 0x28 / 0x74, properties 0x2C / 0x78, behaviours 0x30 / 0x7C, strings 0x3C.
        // Call it after elements, properties or strings were added.
        public void UpdateHeader()
        {
            int components = 0, behaviours = 0, properties = 0;
            foreach (Element e in this.Elements)
            {
                properties += e.Properties.Count;
                if (e.Type == ElementType.Component)
                {
                    components++;
                }
                else if (e.Type == ElementType.Behaviour)
                {
                    behaviours++;
                }
            }
            int nodes = this.Elements.Count - components;
            int[][] fields = new int[][]
            {
                new int[] { 0x24, nodes }, new int[] { 0x70, nodes }, new int[] { 0x28, components }, new int[] { 0x74, components },
                new int[] { 0x2C, properties }, new int[] { 0x78, properties }, new int[] { 0x30, behaviours }, new int[] { 0x7C, behaviours },
                new int[] { 0x3C, this.Strings.Count },
            };
            // 0x1C = highest element id << 8 (431 of 431 files): the game sizes its id tables by it, an id above it
            // is out of range (this was the crash on load of the first copies).
            uint maxId = 0;
            foreach (Element e in this.Elements)
            {
                maxId = Math.Max(maxId, e.Id);
            }
            fields = new List<int[]>(fields) { new int[] { 0x1C, (int) (maxId << 8) } }.ToArray();
            if (this.Slides != null)
            {
                int entries = 0, items = 0, tracks = 0, odd = 0;
                foreach (Slide slide in this.Slides)
                {
                    entries += slide.Entries.Count;
                    foreach (SlideEntry e in slide.Entries)
                    {
                        items += e.ItemCount;
                        tracks += e.TrackCount;
                        odd += e.TrackCount & 1;
                    }
                }
                // 0x6C = entries with an odd number of references (431/431). The game's slide builder (default.xex 82CF0408)
                // sizes its memory with it: the u16 references of each entry are padded to 4 bytes, one pad per odd entry.
                fields = new List<int[]>(fields)
                {
                    new int[] { 0x5C, this.Slides.Count }, new int[] { 0x60, entries }, new int[] { 0x64, items }, new int[] { 0x68, tracks },
                    new int[] { 0x6C, odd },
                }.ToArray();
            }
            if (this.Declared != null)
            {
                int declaredStrings = 0, keys = 0, events = 0, actions = 0;
                foreach (DeclaredEvents de in this.Declared)
                {
                    declaredStrings += de.Strings.Count;
                }
                foreach (Track t in this.AnimationTracks)
                {
                    keys += t.KeyCount;
                }
                foreach (EventGroup g in this.EventGroups)
                {
                    events += g.Events.Count;
                    foreach (Event e in g.Events)
                    {
                        actions += e.Actions.Count;
                    }
                }
                // 0x30 / 0x7C count the declared-event entries, not the behaviours (they differ in 53 of the 141 UI scenes)
                fields = new List<int[]>(fields)
                {
                    new int[] { 0x30, this.Declared.Count }, new int[] { 0x7C, this.Declared.Count }, new int[] { 0x34, declaredStrings }, new int[] { 0x80, declaredStrings },
                    new int[] { 0x44, this.AnimationTracks.Count }, new int[] { 0x48, keys }, new int[] { 0x4C, this.EventGroups.Count },
                    new int[] { 0x50, events }, new int[] { 0x54, actions },
                }.ToArray();
            }
            foreach (int[] f in fields)
            {
                this.Header[f[0]] = (byte) (f[1] >> 24);
                this.Header[f[0] + 1] = (byte) (f[1] >> 16);
                this.Header[f[0] + 2] = (byte) (f[1] >> 8);
                this.Header[f[0] + 3] = (byte) f[1];
            }
        }

        private const int HeaderSize = 0x88;
        private static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

        public byte[] Header;
        public List<string> Strings = new List<string>();
        public List<Element> Elements = new List<Element>();
        public byte[] Rest;                  // the tail as read: declared events, animations, events, block 4, slides

        // ---- The tail after the graph ----
        // Five blocks, each a u32 size + bytes. Decoded 2026-11-05 on the 141 Anark scenes of FM4's Media/UI.zip (the
        // menus are Anark too) and the 233 FM4 dashes: 374/374 read to the last byte and written back byte for byte.
        //   1 declared events: per behaviour u32 element, u32 n, n x u32 string index = the names of the events it declares
        //     (sorted by element)
        //   2 animation tracks: u32 element, u32 property hash, u32 type << 16 | key count, 6 words per key (sorted by element)
        //   3 events: groups (u32 owner element, u32 n events), event (u32 name hash & 0x7FFFFFFF, u32 n actions),
        //     action (u32 target element, u32 context element, u32 command, u32 arg1, u32 arg2); see the Command constants
        //   4 empty in every file
        //   5 slides: they list, for every component (the scene and each gauge), the elements it owns (itself, its elements
        //     down to the next component, and the components under it), in index order; one entry per element.
        //       slide = 25 bytes name, u32 duration, u32 entry count, entries
        //       entry = u32 element index, u8 flag, u32 A, A x (u32 property hash, u32 value), u32 N, N x u32 reference
        //     A reference: top byte 0 = animation track, 1 = action; next byte a flag; low 16 bits the index (in block 2,
        //     or among the actions of block 3 counted in order). Every action is referenced twice (flag 0 and 1) by the
        //     entry of its context element, every track by the entry of its element (374/374).
        // Header: 0x30 / 0x7C declared entries, 0x34 / 0x80 declared strings, 0x44 tracks, 0x48 keys, 0x4C groups,
        // 0x50 events, 0x54 actions, 0x5C slides, 0x60 entries, 0x64 A items, 0x68 references (374/374).
        // When the slides do not read, Slides is null and the tail is written as it was; when only blocks 1-3 do not
        // read as above (other games), Declared is null and those blocks are written as they were.

        public const uint CommandGotoSlideName = 0x1195C23B;   // arg1 = slide name hash
        public const uint CommandSetProperty = 0x7E4906FD;     // arg1 = property hash with kind bits, arg2 = value
        public const uint CommandFireEvent = 0x06875030;       // arg1 = event name hash
        public const uint CommandPlay = 0x573BA2B8;
        public const uint CommandGotoTime = 0x5B2FA69C;        // arg1 = time

        public const uint RefAction = 0x01000000;

        public static string CommandName(uint command)
        {
            switch (command)
            {
                case CommandGotoSlideName: return "go to slide";
                case CommandSetProperty: return "set property";
                case CommandFireEvent: return "fire event";
                case CommandPlay: return "play";
                case CommandGotoTime: return "go to time";
            }
            return "command " + command.ToString("X8");
        }

        // Event and slide names in block 3 are the full hash with bit 31 cleared.
        public static uint HashEvent(string name)
        {
            return HashFull(name) & 0x7FFFFFFF;
        }

        public class DeclaredEvents
        {
            public int Element;
            public List<int> Strings = new List<int>();
        }

        public class Track
        {
            public int Element;
            public uint Hash;
            public uint Type;
            public uint[] Keys = new uint[0];     // 6 words per key: time in ms, value, ...

            public int KeyCount { get { return this.Keys.Length / 6; } }

            public Track Copy()
            {
                Track t = (Track) this.MemberwiseClone();
                t.Keys = (uint[]) this.Keys.Clone();
                return t;
            }
        }

        public class EventAction
        {
            public int Target;
            public int Context;
            public uint Command;
            public uint Arg1;
            public uint Arg2;
        }

        public class Event
        {
            public uint Hash;
            public List<EventAction> Actions = new List<EventAction>();
        }

        public class EventGroup
        {
            public int Owner;
            public List<Event> Events = new List<Event>();
        }

        public class SlideEntry
        {
            public uint Index;
            public byte Flag;
            public List<uint[]> Items = new List<uint[]>();   // { property hash, value }: what this slide sets on the element
            public List<uint> Refs = new List<uint>();        // animation tracks and actions (see RefAction)

            public int ItemCount { get { return this.Items.Count; } }
            public int TrackCount { get { return this.Refs.Count; } }

            // A, A items, N, N references, as in the file.
            public byte[] Data
            {
                get
                {
                    MemoryStream m = new MemoryStream();
                    WriteU32(m, (uint) this.Items.Count);
                    foreach (uint[] item in this.Items)
                    {
                        WriteU32(m, item[0]);
                        WriteU32(m, item[1]);
                    }
                    WriteU32(m, (uint) this.Refs.Count);
                    foreach (uint r in this.Refs)
                    {
                        WriteU32(m, r);
                    }
                    return m.ToArray();
                }
                set
                {
                    this.Items = new List<uint[]>();
                    this.Refs = new List<uint>();
                    int a = ReadU32(value, 0);
                    for (int i = 0; i < a; i++)
                    {
                        this.Items.Add(new uint[] { (uint) ReadU32(value, 4 + 8 * i), (uint) ReadU32(value, 8 + 8 * i) });
                    }
                    int o = 4 + 8 * a;
                    int n = ReadU32(value, o);
                    for (int i = 0; i < n; i++)
                    {
                        this.Refs.Add((uint) ReadU32(value, o + 4 + 4 * i));
                    }
                }
            }
        }

        public class Slide
        {
            public byte[] Name = new byte[25];
            public uint Duration;
            public List<SlideEntry> Entries = new List<SlideEntry>();

            public string Label
            {
                get
                {
                    int n = Array.IndexOf(this.Name, (byte) 0);
                    return Latin1.GetString(this.Name, 0, (n < 0) ? this.Name.Length : n);
                }
            }

            // Name bytes 16-19 = the element index of the component that owns the slide, always its first entry (3684/3684
            // game slides). Copied gauges kept the original's and removals did not renumber it: FM4 crashed in the
            // slide builder (82CF04E8, Brera with Gauge_Gear_2 and a new clock, 2026-11-07).
            public bool OwnerMatchesEntries
            {
                get { return this.Entries.Count == 0 || ReadU32(this.Name, 16) == (int) this.Entries[0].Index; }
            }

            public void SetOwnerFromEntries()
            {
                if (this.Entries.Count > 0)
                {
                    uint owner = this.Entries[0].Index;
                    this.Name[16] = (byte) (owner >> 24);
                    this.Name[17] = (byte) (owner >> 16);
                    this.Name[18] = (byte) (owner >> 8);
                    this.Name[19] = (byte) owner;
                }
            }
        }

        // Slides whose owner (header) is not their first entry: written right by Write, wrong in files saved before 2026-11-07.
        public int SlideOwnerProblems()
        {
            int wrong = 0;
            if (this.Slides != null)
            {
                foreach (Slide slide in this.Slides)
                {
                    wrong += slide.OwnerMatchesEntries ? 0 : 1;
                }
            }
            return wrong;
        }

        public List<Slide> Slides;           // null when the tail did not read as expected: then Rest is written as it is
        public List<DeclaredEvents> Declared;   // blocks 1-3, null when they did not read (see above)
        public List<Track> AnimationTracks;
        public List<EventGroup> EventGroups;
        private byte[][] rawBlocks;          // blocks 1-4 as read

        // True when every element reference of the file is understood: elements can be inserted or removed anywhere.
        public bool TailEditable
        {
            get { return this.Slides != null && this.Declared != null; }
        }

        private void ParseTail()
        {
            this.Slides = null;
            this.Declared = null;
            this.AnimationTracks = null;
            this.EventGroups = null;
            try
            {
                byte[] d = this.Rest;
                int o = 0;
                byte[][] blocks = new byte[4][];
                for (int block = 0; block < 4; block++)
                {
                    long size = (uint) ReadU32(d, o);
                    o += 4;
                    if (o + size > d.Length)
                    {
                        return;
                    }
                    blocks[block] = new byte[size];
                    Buffer.BlockCopy(d, o, blocks[block], 0, (int) size);
                    o += (int) size;
                }
                long slideBytes = (uint) ReadU32(d, o);
                o += 4;
                if (o + slideBytes != d.Length)
                {
                    return;
                }
                List<Slide> slides = new List<Slide>();
                while (o < d.Length)
                {
                    Slide slide = new Slide();
                    Buffer.BlockCopy(d, o, slide.Name, 0, 25);
                    slide.Duration = (uint) ReadU32(d, o + 25);
                    uint count = (uint) ReadU32(d, o + 29);
                    o += 33;
                    for (uint i = 0; i < count; i++)
                    {
                        SlideEntry entry = new SlideEntry();
                        entry.Index = (uint) ReadU32(d, o);
                        entry.Flag = d[o + 4];
                        o += 5;
                        int items = ReadU32(d, o);
                        if (items < 0 || items > 100000)
                        {
                            return;
                        }
                        int tracksAt = o + 4 + items * 8;
                        int tracks = ReadU32(d, tracksAt);
                        if (tracks < 0 || tracks > 100000)
                        {
                            return;
                        }
                        int end = tracksAt + 4 + tracks * 4;
                        if (end > d.Length)
                        {
                            return;
                        }
                        byte[] data = new byte[end - o];
                        Buffer.BlockCopy(d, o, data, 0, data.Length);
                        entry.Data = data;
                        o = end;
                        slide.Entries.Add(entry);
                    }
                    slides.Add(slide);
                }
                if (o != d.Length)
                {
                    return;
                }
                this.rawBlocks = blocks;
                this.Slides = slides;
                this.ParseModel(blocks);
            }
            catch (IndexOutOfRangeException)
            {
                this.Slides = null;
            }
            catch (ArgumentException)
            {
                this.Slides = null;
            }
        }

        private static uint[] Words(byte[] b)
        {
            if (b.Length % 4 != 0)
            {
                return null;
            }
            uint[] w = new uint[b.Length / 4];
            for (int i = 0; i < w.Length; i++)
            {
                w[i] = (uint) ReadU32(b, i * 4);
            }
            return w;
        }

        private void ParseModel(byte[][] blocks)
        {
            uint n = (uint) this.Elements.Count;
            uint[] w = Words(blocks[0]);
            if (w == null)
            {
                return;
            }
            List<DeclaredEvents> declared = new List<DeclaredEvents>();
            long i = 0;
            while (i < w.Length)
            {
                if (i + 2 > w.Length || w[i] >= n || w[i + 1] > w.Length - i - 2)
                {
                    return;
                }
                DeclaredEvents de = new DeclaredEvents();
                de.Element = (int) w[i];
                uint count = w[i + 1];
                i += 2;
                for (long k = 0; k < count; k++)
                {
                    if (w[i + k] >= this.Strings.Count)
                    {
                        return;
                    }
                    de.Strings.Add((int) w[i + k]);
                }
                i += count;
                declared.Add(de);
            }

            w = Words(blocks[1]);
            if (w == null)
            {
                return;
            }
            List<Track> tracks = new List<Track>();
            i = 0;
            while (i < w.Length)
            {
                if (i + 3 > w.Length || w[i] >= n)
                {
                    return;
                }
                Track t = new Track();
                t.Element = (int) w[i];
                t.Hash = w[i + 1];
                t.Type = w[i + 2] >> 16;
                long keys = w[i + 2] & 0xFFFF;
                i += 3;
                if (i + keys * 6 > w.Length)
                {
                    return;
                }
                t.Keys = new uint[keys * 6];
                Array.Copy(w, i, t.Keys, 0, t.Keys.Length);
                i += keys * 6;
                tracks.Add(t);
            }

            w = Words(blocks[2]);
            if (w == null)
            {
                return;
            }
            List<EventGroup> groups = new List<EventGroup>();
            int actions = 0;
            i = 0;
            while (i < w.Length)
            {
                if (i + 2 > w.Length || w[i] >= n)
                {
                    return;
                }
                EventGroup g = new EventGroup();
                g.Owner = (int) w[i];
                uint events = w[i + 1];
                i += 2;
                for (uint k = 0; k < events; k++)
                {
                    if (i + 2 > w.Length)
                    {
                        return;
                    }
                    Event e = new Event();
                    e.Hash = w[i];
                    long count = w[i + 1];
                    i += 2;
                    if (i + count * 5 > w.Length)
                    {
                        return;
                    }
                    for (long a = 0; a < count; a++, i += 5)
                    {
                        if (w[i] >= n || w[i + 1] >= n)
                        {
                            return;
                        }
                        EventAction act = new EventAction();
                        act.Target = (int) w[i];
                        act.Context = (int) w[i + 1];
                        act.Command = w[i + 2];
                        act.Arg1 = w[i + 3];
                        act.Arg2 = w[i + 4];
                        e.Actions.Add(act);
                        actions++;
                    }
                    g.Events.Add(e);
                }
                groups.Add(g);
            }

            foreach (Slide slide in this.Slides)
            {
                foreach (SlideEntry entry in slide.Entries)
                {
                    if (entry.Index >= n)
                    {
                        return;
                    }
                    foreach (uint r in entry.Refs)
                    {
                        uint kind = r >> 24, index = r & 0xFFFF;
                        if ((kind == 0 && index >= tracks.Count) || (kind == 1 && index >= actions) || kind > 1)
                        {
                            return;
                        }
                    }
                }
            }
            this.Declared = declared;
            this.AnimationTracks = tracks;
            this.EventGroups = groups;
        }

        private byte[] BuildRest()
        {
            if (this.Slides == null)
            {
                return this.Rest;
            }
            byte[][] blocks = this.rawBlocks;
            if (this.Declared != null)
            {
                blocks = new byte[4][];
                MemoryStream m = new MemoryStream();
                foreach (DeclaredEvents de in this.Declared)
                {
                    WriteU32(m, (uint) de.Element);
                    WriteU32(m, (uint) de.Strings.Count);
                    foreach (int s in de.Strings)
                    {
                        WriteU32(m, (uint) s);
                    }
                }
                blocks[0] = m.ToArray();
                m = new MemoryStream();
                foreach (Track t in this.AnimationTracks)
                {
                    WriteU32(m, (uint) t.Element);
                    WriteU32(m, t.Hash);
                    WriteU32(m, (t.Type << 16) | (uint) t.KeyCount);
                    foreach (uint k in t.Keys)
                    {
                        WriteU32(m, k);
                    }
                }
                blocks[1] = m.ToArray();
                m = new MemoryStream();
                foreach (EventGroup g in this.EventGroups)
                {
                    WriteU32(m, (uint) g.Owner);
                    WriteU32(m, (uint) g.Events.Count);
                    foreach (Event e in g.Events)
                    {
                        WriteU32(m, e.Hash);
                        WriteU32(m, (uint) e.Actions.Count);
                        foreach (EventAction a in e.Actions)
                        {
                            WriteU32(m, (uint) a.Target);
                            WriteU32(m, (uint) a.Context);
                            WriteU32(m, a.Command);
                            WriteU32(m, a.Arg1);
                            WriteU32(m, a.Arg2);
                        }
                    }
                }
                blocks[2] = m.ToArray();
                blocks[3] = this.rawBlocks[3];
            }
            MemoryStream rest = new MemoryStream();
            foreach (byte[] b in blocks)
            {
                WriteU32(rest, (uint) b.Length);
                rest.Write(b, 0, b.Length);
            }
            MemoryStream slides = new MemoryStream();
            foreach (Slide slide in this.Slides)
            {
                slide.SetOwnerFromEntries();
                slides.Write(slide.Name, 0, 25);
                WriteU32(slides, slide.Duration);
                WriteU32(slides, (uint) slide.Entries.Count);
                foreach (SlideEntry entry in slide.Entries)
                {
                    WriteU32(slides, entry.Index);
                    slides.WriteByte(entry.Flag);
                    byte[] data = entry.Data;
                    slides.Write(data, 0, data.Length);
                }
            }
            WriteU32(rest, (uint) slides.Length);
            slides.WriteTo(rest);
            return rest.ToArray();
        }

        // Every action of block 3 in file order (the order slide references count them in).
        public List<EventAction> AllActions()
        {
            List<EventAction> list = new List<EventAction>();
            if (this.EventGroups != null)
            {
                foreach (EventGroup g in this.EventGroups)
                {
                    foreach (Event e in g.Events)
                    {
                        list.AddRange(e.Actions);
                    }
                }
            }
            return list;
        }

        // The slides of the component at element index owner.
        public List<Slide> SlidesOf(int owner)
        {
            List<Slide> list = new List<Slide>();
            if (this.Slides != null)
            {
                foreach (Slide slide in this.Slides)
                {
                    if (slide.Entries.Count > 0 && slide.Entries[0].Index == owner)
                    {
                        list.Add(slide);
                    }
                }
            }
            return list;
        }

        // Lists a new element in every slide of its component, in index order. The flag and the values the slide gives
        // (items) are copied from the element it was copied from (like), else the flag of the component. Its animation
        // tracks are copied with CopyTracks.
        public int AddToSlides(int owner, int index, int like)
        {
            int n = 0;
            foreach (Slide slide in this.SlidesOf(owner))
            {
                SlideEntry model = null;
                foreach (SlideEntry e in slide.Entries)
                {
                    if (e.Index == like)
                    {
                        model = e;
                    }
                }
                SlideEntry entry = new SlideEntry();
                entry.Index = (uint) index;
                entry.Flag = (model ?? slide.Entries[0]).Flag;
                if (model != null)
                {
                    foreach (uint[] item in model.Items)
                    {
                        entry.Items.Add((uint[]) item.Clone());
                    }
                }
                int at = slide.Entries.Count;
                while (at > 1 && slide.Entries[at - 1].Index > index)
                {
                    at--;
                }
                slide.Entries.Insert(at, entry);
                n++;
            }
            return n;
        }

        // Copies the animation tracks of element 'from' to element 'to' (listed already in the slides, see AddToSlides):
        // the new tracks go in element order, the slides that use a track of 'from' use its copy on 'to'. Returns the
        // number of tracks copied.
        public int CopyTracks(int from, int to)
        {
            if (!this.TailEditable || from == to)
            {
                return 0;
            }
            List<int> sources = new List<int>();
            for (int i = 0; i < this.AnimationTracks.Count; i++)
            {
                if (this.AnimationTracks[i].Element == from)
                {
                    sources.Add(i);
                }
            }
            if (sources.Count == 0)
            {
                return 0;
            }
            int at = 0;
            while (at < this.AnimationTracks.Count && this.AnimationTracks[at].Element <= to)
            {
                at++;
            }
            if (this.AnimationTracks.Count + sources.Count > 0x10000)
            {
                return 0;
            }
            // references to tracks at or after 'at' move by the number inserted; sources keep their own (shifted) numbers
            int count = sources.Count;
            foreach (Slide slide in this.Slides)
            {
                foreach (SlideEntry e in slide.Entries)
                {
                    for (int k = 0; k < e.Refs.Count; k++)
                    {
                        uint r = e.Refs[k];
                        if ((r >> 24) == 0 && (r & 0xFFFF) >= at)
                        {
                            e.Refs[k] = (r & 0xFFFF0000) | ((r & 0xFFFF) + (uint) count);
                        }
                    }
                }
            }
            Dictionary<int, int> copyOf = new Dictionary<int, int>();    // old number of a source track -> number of its copy
            List<Track> copies = new List<Track>();
            foreach (int s in sources)
            {
                Track c = this.AnimationTracks[s].Copy();
                c.Element = to;
                copyOf[(s >= at) ? s + count : s] = at + copies.Count;
                copies.Add(c);
            }
            this.AnimationTracks.InsertRange(at, copies);
            foreach (Slide slide in this.Slides)
            {
                SlideEntry src = null, dst = null;
                foreach (SlideEntry e in slide.Entries)
                {
                    if (e.Index == from)
                    {
                        src = e;
                    }
                    else if (e.Index == to)
                    {
                        dst = e;
                    }
                }
                if (src == null || dst == null)
                {
                    continue;
                }
                foreach (uint r in src.Refs)
                {
                    int copy;
                    if ((r >> 24) == 0 && copyOf.TryGetValue((int) (r & 0xFFFF), out copy))
                    {
                        dst.Refs.Add((r & 0xFFFF0000) | (uint) copy);
                    }
                }
            }
            return copies.Count;
        }

        // Every element index of the file goes through map: parents, and in the tail declared events, tracks, event
        // owners, targets and contexts, slide entries.
        private void RemapElements(Converter<int, int> map)
        {
            foreach (Element e in this.Elements)
            {
                if (e.Parent >= 0)
                {
                    e.Parent = map(e.Parent);
                }
            }
            foreach (DeclaredEvents de in this.Declared)
            {
                de.Element = map(de.Element);
            }
            foreach (Track t in this.AnimationTracks)
            {
                t.Element = map(t.Element);
            }
            foreach (EventGroup g in this.EventGroups)
            {
                g.Owner = map(g.Owner);
                foreach (Event e in g.Events)
                {
                    foreach (EventAction a in e.Actions)
                    {
                        a.Target = map(a.Target);
                        a.Context = map(a.Context);
                    }
                }
            }
            foreach (Slide slide in this.Slides)
            {
                foreach (SlideEntry e in slide.Entries)
                {
                    e.Index = (uint) map((int) e.Index);
                }
            }
        }

        // Inserts elements at index 'at' (depth first order is the caller's: 'at' must follow a whole subtree). Every
        // index at or after 'at' moves; the parents of the new elements are given in the new numbering.
        public void InsertElements(int at, List<Element> added)
        {
            if (!this.TailEditable)
            {
                throw new InvalidOperationException("the tail of this .bgf is not understood");
            }
            int count = added.Count;
            this.RemapElements(delegate(int i) { return (i >= at) ? i + count : i; });
            this.Elements.InsertRange(at, added);
        }

        // Removes the elements first .. first + count - 1 (a whole subtree) and everything of the tail that belongs to
        // them: their declared events, animation tracks, event groups they own, actions that target them or run in their
        // time, slide entries and the slides of a removed component; references are renumbered.
        public void RemoveElements(int first, int count)
        {
            if (!this.TailEditable)
            {
                throw new InvalidOperationException("the tail of this .bgf is not understood");
            }
            int end = first + count;
            for (int i = end; i < this.Elements.Count; i++)
            {
                if (this.Elements[i].Parent >= first && this.Elements[i].Parent < end)
                {
                    throw new InvalidOperationException("the elements to remove are not a whole subtree");
                }
            }
            Predicate<int> gone = delegate(int i) { return i >= first && i < end; };
            this.Declared.RemoveAll(delegate(DeclaredEvents de) { return gone(de.Element); });

            int[] trackMap = new int[this.AnimationTracks.Count];
            List<Track> keptTracks = new List<Track>();
            for (int i = 0; i < trackMap.Length; i++)
            {
                Track t = this.AnimationTracks[i];
                trackMap[i] = gone(t.Element) ? -1 : keptTracks.Count;
                if (trackMap[i] >= 0)
                {
                    keptTracks.Add(t);
                }
            }
            this.AnimationTracks = keptTracks;

            List<int> actionMap = new List<int>();
            int kept = 0;
            List<EventGroup> keptGroups = new List<EventGroup>();
            foreach (EventGroup g in this.EventGroups)
            {
                bool ownerGone = gone(g.Owner);
                foreach (Event e in g.Events)
                {
                    List<EventAction> keptActions = new List<EventAction>();
                    foreach (EventAction a in e.Actions)
                    {
                        bool drop = ownerGone || gone(a.Target) || gone(a.Context);
                        actionMap.Add(drop ? -1 : kept);
                        if (!drop)
                        {
                            keptActions.Add(a);
                            kept++;
                        }
                    }
                    e.Actions = keptActions;
                }
                if (!ownerGone)
                {
                    keptGroups.Add(g);
                }
            }
            this.EventGroups = keptGroups;

            this.Slides.RemoveAll(delegate(Slide s) { return s.Entries.Count == 0 || gone((int) s.Entries[0].Index); });
            foreach (Slide slide in this.Slides)
            {
                slide.Entries.RemoveAll(delegate(SlideEntry e) { return gone((int) e.Index); });
                foreach (SlideEntry e in slide.Entries)
                {
                    List<uint> refs = new List<uint>();
                    foreach (uint r in e.Refs)
                    {
                        int index = (int) (r & 0xFFFF);
                        int mapped = ((r >> 24) == 0) ? trackMap[index] : actionMap[index];
                        if (mapped >= 0)
                        {
                            refs.Add((r & 0xFFFF0000) | (uint) mapped);
                        }
                    }
                    e.Refs = refs;
                }
            }
            this.Elements.RemoveRange(first, count);
            this.RemapElements(delegate(int i) { return (i >= end) ? i - count : i; });
        }

        // Takes the elements at index >= first out of the file's tail (they are the last ones of the graph).
        public void RemoveFromSlides(int first)
        {
            if (this.Slides != null)
            {
                foreach (Slide slide in this.Slides)
                {
                    slide.Entries.RemoveAll(delegate(SlideEntry e) { return e.Index >= first; });
                }
                this.Slides.RemoveAll(delegate(Slide s) { return s.Entries.Count == 0; });      // the slides of a removed component
            }
        }

        // True when a behaviour declaration, an animation track or an event (owner, target, context) names an element
        // first .. last - 1. Always true when the tail is not understood.
        public bool IndexMentioned(int first)
        {
            return this.IndexMentioned(first, this.Elements.Count);
        }

        public bool IndexMentioned(int first, int last)
        {
            if (!this.TailEditable)
            {
                return true;
            }
            Predicate<int> inside = delegate(int i) { return i >= first && i < last; };
            foreach (DeclaredEvents de in this.Declared)
            {
                if (inside(de.Element))
                {
                    return true;
                }
            }
            foreach (Track t in this.AnimationTracks)
            {
                if (inside(t.Element))
                {
                    return true;
                }
            }
            foreach (EventGroup g in this.EventGroups)
            {
                if (inside(g.Owner))
                {
                    return true;
                }
                foreach (Event e in g.Events)
                {
                    foreach (EventAction a in e.Actions)
                    {
                        if (inside(a.Target) || inside(a.Context))
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        // The full 32-bit form of the same hash: the .bgf element hash is this of the element's name in the .fbf ("Scene" = 068C6274, "Layer" = 81A3C24F; checked on one FM3 dash,
        // 2026-11-03), a behaviour's is its contract's ("UIContract" = BAFC7A1A), and the
        // event table names slides by it (the slide's name, "ON" = 2734DC01; some with bit 31 set or cleared).
        public static uint HashFull(string name)
        {
            uint h = 0;
            foreach (char c in name)
            {
                h = (h + c) * 65599;
            }
            return h;
        }

        public static uint HashName(string name)
        {
            uint h = 0;
            foreach (char c in name)
            {
                h = (h + c) * 65599;
            }
            return h & 0x07FFFFFF;
        }

        // Names recognised so far (UI Composer / Qt 3D Studio property names, which come from Anark Gameface).
        private static readonly string[] KnownNames = new string[]
        {
            "position.x", "position.y", "position.z", "rotation.x", "rotation.y", "rotation.z", "scale.x", "scale.y", "scale.z",
            "pivot.x", "pivot.y", "pivot.z", "opacity", "rotationorder", "orientation", "endtime", "starttime", "eyeball",
            "clipnear", "clipfar", "fov", "orthographic",
            "diffuse.r", "diffuse.g", "diffuse.b", "diffuse.a",
            "textstring", "font", "size", "textcolor.r", "textcolor.g", "textcolor.b", "textcolor.a",
            "horzalign", "vertalign", "leading", "tracking", "wordwrap",
            "positionu", "positionv", "scaleu", "scalev", "rotationuv", "tilingmodehorz", "tilingmodevert",
        };

        // The data contract: the first behaviour of every dash (the scene's "UIContract", 94 parameters in order, the same
        // 97 hashes in all 431 dashes of FM4, FH2 and FH1). The game fills the dash through it: a parameter pair "on"
        // (bool) + "target" (string) says which element of the dash receives a value; the target is a path of names
        // from the scene ("parent.Layer.Gauge_Gear.Group.TEXT_GEAR_DATA"), "Scene.UIContract" or "this" when unused.
        // Names in capitals are the game's own (2026-11-07, strings of default.xex at 820317EC, read by 824F43F8; the
        // OIL/WATER text, min/max and units names are not in the FM4 xex: found by their hash, FM4 does not read them).
        // Units: LIST_SPEED_UNIT 0 = mph, 1 = km/h (from LONG_MAX_SPEED ~200 vs 300-450); other units 0/1, meaning unchecked.
        private static readonly string[] ContractLabels = new string[]
        {
            "064CB58A:AKCustomObjType (\"contract\")", "073F25FB:Contract is on", "04A9EC63:_AKContractClass (contract name)", "02C190DD:GROUP_OVERLAYS",
            "02FB3C14:BOOLEAN_ANALOG_SPEED_EXISTS", "012D5B95:CONTROL_PROGRESS_BAR_SPEED", "01CC92B1:BOOLEAN_TEXT_SPEED_EXISTS", "01F1A7EB:TEXT_SPEED", "071FE357:LONG_MAX_SPEED",
            "001001EB:BOOLEAN_ODOMETER_EXISTS", "030765FF:TEXT_ODOMETER", "06E717D2:BOOLEAN_TRIPMETER_EXISTS", "015CE8CE:TEXT_TRIPMETER", "04447E03:LIST_SPEED_UNIT (0 mph, 1 km/h)",
            "078C1FAF:BOOLEAN_ANALOG_TACH_EXISTS", "03F59520:BOOLEAN_ANALOG_GEARED_RPM_TACH_EXISTS", "03FFB820:CONTROL_GEARED_RPM_PROGRESS_BAR", "05667472:CONTROL_PROGRESS_BAR_TACH",
            "026A51D2:BOOLEAN_TEXT_TACH_EXISTS", "001BB29C:TEXT_TACH", "008BDC30:LONG_MAX_TACH", "06B664B0:LIST_TACH_UNIT",
            "07F77F3B:BOOLEAN_PROGRESS_BAR_GEAR_EXISTS", "038EC7D5:CONTROL_PROGRESS_BAR_GEAR", "06BAFBAF:BOOLEAN_TEXT_GEAR_EXISTS", "064405FF:TEXT_GEAR", "01946C81:LONG_MIN_GEAR", "06B42F93:LONG_MAX_GEAR",
            "030EA3D3:BOOLEAN_ANALOG_FUEL_EXISTS", "0201B14E:CONTROL_PROGRESS_BAR_FUEL", "05ECD5F6:BOOLEAN_TEXT_FUEL_EXISTS", "04B6EF78:TEXT_FUEL", "071C402C:FLOAT_MAX_FUEL",
            "072842D0:BOOLEAN_ANALOG_BOOST_EXISTS", "04878BD9:CONTROL_PROGRESS_BAR_BOOST", "05F9996D:BOOLEAN_TEXT_BOOST_EXISTS", "054BD82F:TEXT_BOOST",
            "070BB30D:FLOAT_MIN_BOOST", "04EEB47B:FLOAT_MAX_BOOST", "077022BF:LIST_BOOST_UNIT",
            "045E1DF7:BOOLEAN_TEXT_CURRENT_LAP_TIME_EXISTS", "00B74AB7:TEXT_CURRENT_LAP_TIME", "058A8F84:BOOLEAN_TEXT_LAST_LAP_TIME_EXISTS", "03DC8BD8:TEXT_LAST_LAP_TIME",
            "06AD5476:BOOLEAN_TEXT_BEST_LAP_TIME_EXISTS", "04FB9F66:TEXT_BEST_LAP_TIME", "03019601:BOOLEAN_TEXT_RACE_POSITION_EXISTS", "01446A9B:TEXT_RACE_POSITION",
            "068C0956:TEXT_MAX_RACE_POSITION", "013C521F:BOOLEAN_TEXT_CURRENT_LAP_EXISTS", "042EF57D:TEXT_CURRENT_LAP", "0720D35B:TEXT_MAX_LAPS",
            "06079D40:BOOLEAN_ABS_EXISTS", "05CD6BF1:CONTROL_COCKPIT_LIGHT_ABS", "024544BA:BOOLEAN_STM_EXISTS", "041F2EF7:CONTROL_COCKPIT_LIGHT_STM",
            "04B5A012:BOOLEAN_TCS_EXISTS", "024CF99F:CONTROL_COCKPIT_LIGHT_TCS", "070BB8D0:BOOLEAN_SHIFT_EXISTS", "04EF8E64:CONTROL_SHIFT_LIGHTS",
            "02A10FFC:BOOLEAN_CLOCK_EXISTS", "07C92224:TEXT_CLOCK",
            "0421DCCF:BOOLEAN_ANALOG_OIL_PRESSURE_EXISTS", "000C0752:CONTROL_PROGRESS_BAR_OIL_PRESSURE", "06A108F2:BOOLEAN_TEXT_OIL_PRESSURE_EXISTS (not read by FM4)", "06FE717C:TEXT_OIL_PRESSURE (not read by FM4)",
            "06343B1E:FLOAT_MIN_OIL_PRESSURE (not read by FM4)", "00DD5A30:FLOAT_MAX_OIL_PRESSURE (not read by FM4)",
            "062AC6FE:BOOLEAN_ANALOG_OIL_TEMP_EXISTS", "0260E6E3:CONTROL_PROGRESS_BAR_OIL_TEMP", "06E7F621:BOOLEAN_TEXT_OIL_TEMP_EXISTS (not read by FM4)", "03DFBB0D:TEXT_OIL_TEMP (not read by FM4)",
            "000C66AF:FLOAT_MIN_OIL_TEMP (not read by FM4)", "0127D7C1:FLOAT_MAX_OIL_TEMP (not read by FM4)", "04C7B9AE:LIST_OIL_TEMP_UNITS (not read by FM4)",
            "02D9FCD9:BOOLEAN_ANALOG_WATER_TEMP_EXISTS", "053A43C8:CONTROL_PROGRESS_BAR_WATER_TEMP", "032BCA7C:BOOLEAN_TEXT_WATER_TEMP_EXISTS (not read by FM4)", "013DA2F2:TEXT_WATER_TEMP (not read by FM4)",
            "038A9D94:FLOAT_MIN_WATER_TEMP (not read by FM4)", "04DAA5A6:FLOAT_MAX_WATER_TEMP (not read by FM4)", "01D42193:LIST_WATER_TEMP_UNITS (not read by FM4)",
            "0775A5DF:Extra slot on", "01E4B312:Extra slot target", "07F5FDF2:BOOLEAN_TEXT_EXTERNAL_TEMP_EXISTS", "06CD5CEA:TEXT_EXTERNAL_TEMP", "010BBFA9:LIST_EXTERNAL_TEMP_UNITS",
            "0455E536:BOOLEAN_ANALOG_G_ACCEL_X_EXISTS", "076FA337:BOOLEAN_ANALOG_G_ACCEL_Y_EXISTS", "000912F3:CONTROL_PROGRESS_BAR_G_ACCEL_X", "000A1332:CONTROL_PROGRESS_BAR_G_ACCEL_Y",
            "061C739A:BOOLEAN_TEXT_G_ACCEL_EXISTS", "042B33C2:TEXT_G_ACCEL",
            "0523363B:BOOLEAN_ANALOG_TACH_MEMORY_EXISTS", "03C40DE5:CAR_PRES_GAUGE_ANIM", "0263A72E:CONTROL_PROGRESS_BAR_TACH_MEMORY",
        };

        private static Dictionary<uint, string> names, contract;

        public static string PropertyName(uint hash)
        {
            if (names == null)
            {
                Dictionary<uint, string> map = new Dictionary<uint, string>();
                foreach (string name in KnownNames)
                {
                    map[HashName(name)] = name;
                }
                names = map;
                Dictionary<uint, string> labels = new Dictionary<uint, string>();
                foreach (string line in ContractLabels)
                {
                    labels[Convert.ToUInt32(line.Substring(0, 8), 16)] = line.Substring(9);
                }
                contract = labels;
            }
            string found;
            uint key = hash & 0x07FFFFFF;
            if (names.TryGetValue(key, out found))
            {
                return found;
            }
            return ((hash & 0x80000000) != 0 && contract.TryGetValue(key, out found)) ? found : null;
        }

        public static AnarkBgf Read(byte[] data)
        {
            if (data.Length < HeaderSize + 4 || Latin1.GetString(data, 6, 8) != "AnarkBGF")
            {
                throw new InvalidDataException("not an Anark .bgf");
            }
            AnarkBgf bgf = new AnarkBgf();
            int o = 0;
            bgf.Header = Bytes(data, ref o, HeaderSize);
            int end = U32(data, ref o);
            end += o;
            while (o < end)
            {
                int n = U32(data, ref o);
                bgf.Strings.Add(Latin1.GetString(Bytes(data, ref o, n)));
            }
            if (o != end)
            {
                throw new InvalidDataException("string section overrun");
            }
            int graphEnd = U32(data, ref o);
            graphEnd += o;
            if (graphEnd > data.Length)
            {
                throw new InvalidDataException("graph section past the end of the file");
            }
            while (o < graphEnd)
            {
                Element e = new Element();
                e.Hash = (uint) U32(data, ref o);
                e.Parent = U32(data, ref o);
                e.Id = (uint) U32(data, ref o);
                byte[] tf = Bytes(data, ref o, 2);
                e.Type = (ElementType) tf[0];
                e.Flag = tf[1];
                if (e.Type == ElementType.Component)
                {
                    e.ComponentPrefix = Bytes(data, ref o, 5);
                }
                int count = U32(data, ref o);
                if (count < 0 || o + count * 8 > graphEnd)
                {
                    throw new InvalidDataException(string.Format("element {0}: {1} properties past the graph section", bgf.Elements.Count, count));
                }
                for (int k = 0; k < count; k++)
                {
                    Property p = new Property();
                    p.Hash = (uint) U32(data, ref o);
                    p.Value = (uint) U32(data, ref o);
                    e.Properties.Add(p);
                }
                bgf.Elements.Add(e);
            }
            if (o != graphEnd)
            {
                throw new InvalidDataException("graph section overrun");
            }
            bgf.Rest = Bytes(data, ref o, data.Length - o);
            bgf.ParseTail();
            return bgf;
        }

        public byte[] Write()
        {
            MemoryStream strings = new MemoryStream();
            foreach (string s in this.Strings)
            {
                byte[] b = Latin1.GetBytes(s);
                WriteU32(strings, (uint) b.Length);
                strings.Write(b, 0, b.Length);
            }
            MemoryStream graph = new MemoryStream();
            foreach (Element e in this.Elements)
            {
                WriteU32(graph, e.Hash);
                WriteU32(graph, (uint) e.Parent);
                WriteU32(graph, e.Id);
                graph.WriteByte((byte) e.Type);
                graph.WriteByte(e.Flag);
                if (e.Type == ElementType.Component)
                {
                    graph.Write(e.ComponentPrefix, 0, e.ComponentPrefix.Length);
                }
                WriteU32(graph, (uint) e.Properties.Count);
                foreach (Property p in e.Properties)
                {
                    WriteU32(graph, p.Hash);
                    WriteU32(graph, p.Value);
                }
            }
            MemoryStream s2 = new MemoryStream();
            s2.Write(this.Header, 0, this.Header.Length);
            WriteU32(s2, (uint) strings.Length);
            strings.WriteTo(s2);
            WriteU32(s2, (uint) graph.Length);
            graph.WriteTo(s2);
            byte[] rest = this.BuildRest();
            s2.Write(rest, 0, rest.Length);
            return s2.ToArray();
        }

        public Element FindById(uint id)
        {
            foreach (Element e in this.Elements)
            {
                if (e.Id == id && e.Type != ElementType.Behaviour)
                {
                    return e;
                }
            }
            return null;
        }

        public string GetString(Property p)
        {
            return (p != null && p.Value < this.Strings.Count) ? this.Strings[(int) p.Value] : null;
        }

        internal static int ReadU32(byte[] data, int o)
        {
            return (data[o] << 24) | (data[o + 1] << 16) | (data[o + 2] << 8) | data[o + 3];
        }

        private static int U32(byte[] data, ref int o)
        {
            if (o + 4 > data.Length)
            {
                throw new InvalidDataException(string.Format("unexpected end of file at 0x{0:X}", o));
            }
            int v = (data[o] << 24) | (data[o + 1] << 16) | (data[o + 2] << 8) | data[o + 3];
            o += 4;
            return v;
        }

        private static byte[] Bytes(byte[] data, ref int o, int n)
        {
            if (n < 0 || o + n > data.Length)
            {
                throw new InvalidDataException(string.Format("unexpected end of file at 0x{0:X} (+{1})", o, n));
            }
            byte[] b = new byte[n];
            Buffer.BlockCopy(data, o, b, 0, n);
            o += n;
            return b;
        }

        private static void WriteU32(Stream s, uint v)
        {
            s.WriteByte((byte) (v >> 24));
            s.WriteByte((byte) (v >> 16));
            s.WriteByte((byte) (v >> 8));
            s.WriteByte((byte) v);
        }
    }
}
