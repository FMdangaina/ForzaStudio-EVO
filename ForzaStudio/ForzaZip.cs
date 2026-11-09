namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.IO.Compression;
    using System.Text;

    // Editable zip archive (Forza XMem LZX, deflate or stored entries). Unchanged entries are
    // written back with their original bytes; replaced entries are recompressed with the
    // entry's method (LZX output is identical to the game's own archives).
    //
    // It can also wrap a plain folder (the game disc with uncompressed car folders): the entries are
    // the files below it, named by relative path, and saving writes back only the replaced files.
    public class ForzaZip
    {
        public const int MethodStored = 0;
        public const int MethodDeflate = 8;
        public const int MethodLzx = 21;

        public class Entry
        {
            public string Name;
            public int Method;
            public uint Crc;
            public int CompressedSize;
            public int UncompressedSize;
            public string FullPath;         // folder mode: the file on disk
            internal byte[] LocalHeader;    // fixed part + name + extra
            internal byte[] CentralRecord;  // fixed part + name + extra + comment
            internal int DataOffset;
            internal byte[] Replacement;    // pending uncompressed data
            internal bool IsNew;            // added since the last save (Revert removes it)
            internal bool deleted;          // marked for deletion: Save leaves it out (Revert takes the mark off)

            public bool IsDirectory { get { return this.Name.EndsWith("/"); } }
            public bool IsDeleted { get { return this.deleted; } }
            public bool IsModified { get { return this.Replacement != null || this.deleted; } }
            public bool IsAdded { get { return this.IsNew; } }
        }

        private byte[] endComment = new byte[0];

        public List<Entry> Entries = new List<Entry>();
        public string FilePath { get; private set; }
        public bool IsFolder { get; private set; }

        public ForzaZip(string path)
        {
            this.FilePath = path;
            using (Stream file = OpenRead(path))
            {
                this.Parse(file);
            }
        }

        private ForzaZip()
        {
        }

        // One loose file (e.g. a single .xds) handled like a folder holding only it: view, edit, save
        // in place; files added go next to it.
        public static ForzaZip OpenFile(string path)
        {
            ForzaZip single = new ForzaZip();
            single.IsFolder = true;
            string full = Path.GetFullPath(path);
            single.FilePath = Path.GetDirectoryName(full);
            Entry entry = new Entry();
            entry.FullPath = full;
            entry.Name = Path.GetFileName(full);
            entry.Method = MethodStored;
            entry.UncompressedSize = (int) Math.Min(new FileInfo(full).Length, int.MaxValue);
            entry.CompressedSize = entry.UncompressedSize;
            single.Entries.Add(entry);
            return single;
        }

        public static ForzaZip OpenFolder(string path)
        {
            ForzaZip folder = new ForzaZip();
            folder.IsFolder = true;
            string root = Path.GetFullPath(path);
            if (root.Length > 3)
            {
                root = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            folder.FilePath = root;
            string prefix = root.EndsWith(Path.DirectorySeparatorChar.ToString()) ? root : (root + Path.DirectorySeparatorChar);
            string[] files = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            foreach (string file in files)
            {
                Entry entry = new Entry();
                entry.FullPath = file;
                entry.Name = file.Substring(prefix.Length).Replace(Path.DirectorySeparatorChar, '/');
                entry.Method = MethodStored;
                long length = new FileInfo(file).Length;
                entry.UncompressedSize = (int) Math.Min(length, int.MaxValue);
                entry.CompressedSize = entry.UncompressedSize;
                folder.Entries.Add(entry);
            }
            return folder;
        }

        public List<Entry> ModifiedEntries
        {
            get { return this.Entries.FindAll(delegate(Entry entry) { return entry.IsModified; }); }
        }

        public bool IsModified
        {
            get
            {
                foreach (Entry entry in this.Entries)
                {
                    if (entry.IsModified)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        public Entry Find(string name)
        {
            foreach (Entry entry in this.Entries)
            {
                if (string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }
            return null;
        }

        // Zips that are not files on disk (a zip inside an Xbox 360 package, read in place): a made-up path
        // that opens a stream. Read-only; Save and the folder modes are for real files.
        public delegate Stream StreamOpener();

        private static readonly Dictionary<string, StreamOpener> mounted = new Dictionary<string, StreamOpener>(StringComparer.OrdinalIgnoreCase);

        public static void Mount(string path, StreamOpener open)
        {
            lock (mounted)
            {
                mounted[path] = open;
            }
        }

        // Opened for each read and closed again, so the file is never kept locked.
        private static Stream OpenRead(string path)
        {
            StreamOpener open;
            lock (mounted)
            {
                mounted.TryGetValue(path, out open);
            }
            if (open != null)
            {
                return open();
            }
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        }

        private static byte[] ReadAt(Stream file, long offset, int length)
        {
            byte[] data = new byte[length];
            file.Position = offset;
            int total = 0;
            while (total < length)
            {
                int read = file.Read(data, total, length - total);
                if (read <= 0)
                {
                    throw new InvalidDataException("Unexpected end of the zip archive.");
                }
                total += read;
            }
            return data;
        }

        // Only the central directory and the local headers are read: big archives (UI\Textures.zip is
        // 435 MB) are never loaded whole.
        private void Parse(Stream file)
        {
            int tailLength = (int) Math.Min(file.Length, 65557);
            byte[] tail = ReadAt(file, file.Length - tailLength, tailLength);
            int eocd = -1;
            for (int i = tail.Length - 22; i >= 0; i--)
            {
                if (BitConverter.ToUInt32(tail, i) == 0x06054b50)
                {
                    eocd = i;
                    break;
                }
            }
            if (eocd < 0)
            {
                throw new InvalidDataException("This is not a valid zip archive.");
            }
            int count = BitConverter.ToUInt16(tail, eocd + 10);
            int directorySize = BitConverter.ToInt32(tail, eocd + 12);
            long directoryStart = BitConverter.ToUInt32(tail, eocd + 16);
            int commentLength = BitConverter.ToUInt16(tail, eocd + 20);
            this.endComment = Slice(tail, eocd + 22, commentLength);
            byte[] z = ReadAt(file, directoryStart, directorySize);
            int position = 0;

            for (int i = 0; i < count; i++)
            {
                if (BitConverter.ToUInt32(z, position) != 0x02014b50)
                {
                    throw new InvalidDataException("Corrupted zip central directory.");
                }
                int nameLength = BitConverter.ToUInt16(z, position + 28);
                int extraLength = BitConverter.ToUInt16(z, position + 30);
                int entryCommentLength = BitConverter.ToUInt16(z, position + 32);
                int localOffset = BitConverter.ToInt32(z, position + 42);
                Entry entry = new Entry();
                entry.Name = Encoding.ASCII.GetString(z, position + 46, nameLength);
                entry.Method = BitConverter.ToUInt16(z, position + 10);
                entry.Crc = BitConverter.ToUInt32(z, position + 16);
                entry.CompressedSize = BitConverter.ToInt32(z, position + 20);
                entry.UncompressedSize = BitConverter.ToInt32(z, position + 24);
                entry.CentralRecord = Slice(z, position, 46 + nameLength + extraLength + entryCommentLength);
                position += entry.CentralRecord.Length;

                byte[] local = ReadAt(file, localOffset, 30);
                if (BitConverter.ToUInt32(local, 0) != 0x04034b50)
                {
                    throw new InvalidDataException("Corrupted zip local header for " + entry.Name);
                }
                int localLength = 30 + BitConverter.ToUInt16(local, 26) + BitConverter.ToUInt16(local, 28);
                entry.LocalHeader = ReadAt(file, localOffset, localLength);
                entry.DataOffset = localOffset + localLength;
                this.Entries.Add(entry);
            }
        }

        public byte[] Read(Entry entry)
        {
            if (entry.Replacement != null)
            {
                return entry.Replacement;
            }
            if (this.IsFolder)
            {
                return File.ReadAllBytes(entry.FullPath);
            }
            byte[] compressed;
            using (Stream file = OpenRead(this.FilePath))
            {
                compressed = ReadAt(file, entry.DataOffset, entry.CompressedSize);
            }
            switch (entry.Method)
            {
                case MethodStored:
                    return compressed;

                case MethodDeflate:
                    using (DeflateStream stream = new DeflateStream(new MemoryStream(compressed), CompressionMode.Decompress))
                    {
                        byte[] output = new byte[entry.UncompressedSize];
                        int total = 0;
                        int read;
                        while (total < output.Length && (read = stream.Read(output, total, output.Length - total)) > 0)
                        {
                            total += read;
                        }
                        return output;
                    }

                case MethodLzx:
                    return XMemLzx.Decompress(compressed, entry.UncompressedSize);
            }
            throw new NotSupportedException(string.Format("Compression method {0} is not supported.", entry.Method));
        }

        // Materials renamed by a long-mesh split (CarbinModel.LongMeshes: body -> body_2), per .carbin written to this
        // archive, so the car's shadersettings.xml gets the entries whether it is written before or after the model.
        private readonly List<KeyValuePair<string, List<string[]>>> splitRenames = new List<KeyValuePair<string, List<string[]>>>();

        private byte[] ForGame(string name, byte[] data)
        {
            List<string[]> renamed;
            data = CarbinModel.ForGame(name, data, out renamed);
            if (renamed.Count > 0)
            {
                this.splitRenames.Add(new KeyValuePair<string, List<string[]>>(name, renamed));
                ForzaZip.Entry settings = this.Find(FolderOf(name) + "shadersettings.xml");
                if (settings != null)
                {
                    byte[] followed = CarbinModel.FollowSplit(this.Read(settings), name, renamed);
                    if (followed != null)
                    {
                        settings.deleted = false;
                        settings.Replacement = followed;
                    }
                }
            }
            else if (name.EndsWith("shadersettings.xml", StringComparison.OrdinalIgnoreCase))
            {
                foreach (KeyValuePair<string, List<string[]>> split in this.splitRenames)
                {
                    if (string.Equals(FolderOf(split.Key), FolderOf(name), StringComparison.OrdinalIgnoreCase))
                    {
                        data = CarbinModel.FollowSplit(data, split.Key, split.Value) ?? data;
                    }
                }
            }
            return data;
        }

        private static string FolderOf(string name)
        {
            return name.Substring(0, name.LastIndexOf('/') + 1);
        }

        public void Replace(Entry entry, byte[] data)
        {
            data = this.ForGame(entry.Name, data);   // every carbin written: glass draw pass + part flag right, long meshes split
            entry.deleted = false;
            entry.Replacement = data;
        }

        // Marks a file for deletion: it stays in the archive (and can be read) until Save, which writes the archive
        // without it (a zip) or removes the file (a folder; RemoveDeleted). A file added and not saved just goes.
        public void MarkDeleted(Entry entry)
        {
            if (entry.IsNew)
            {
                this.Entries.Remove(entry);
                return;
            }
            entry.deleted = true;
        }

        // An empty archive whose new entries copy the headers (versions, 0x1123 extra field,
        // compression per extension) of a game archive; Save writes it anywhere.
        public static ForzaZip CreateLike(ForzaZip model)
        {
            ForzaZip archive = new ForzaZip();
            archive.FilePath = model.FilePath;
            archive.templates = new List<Entry>(model.Entries);
            return archive;
        }

        private List<Entry> templates;

        public void Remove(Entry entry)
        {
            this.Entries.Remove(entry);
        }

        // Gives an entry another name without touching its data: the name in its local header and central record is
        // replaced (Save writes both and recomputes the data offsets), so a rename costs no recompression.
        public void Rename(Entry entry, string name)
        {
            if (this.IsFolder || entry.LocalHeader == null || entry.CentralRecord == null)
            {
                throw new InvalidOperationException("Only an entry of a zip file can be renamed.");
            }
            if (!string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase) && this.Find(name) != null)
            {
                throw new ArgumentException("The archive already has " + name + ".");
            }
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(name);
            entry.LocalHeader = Respell(entry.LocalHeader, 30, 26, bytes);
            entry.CentralRecord = Respell(entry.CentralRecord, 46, 28, bytes);
            entry.Name = name;
        }

        // The record with the name at 'start' (its length at 'lengthAt') replaced.
        private static byte[] Respell(byte[] record, int start, int lengthAt, byte[] name)
        {
            int old = BitConverter.ToUInt16(record, lengthAt);
            byte[] result = new byte[record.Length - old + name.Length];
            Buffer.BlockCopy(record, 0, result, 0, start);
            Buffer.BlockCopy(name, 0, result, start, name.Length);
            Buffer.BlockCopy(record, start + old, result, start + name.Length, record.Length - start - old);
            Buffer.BlockCopy(BitConverter.GetBytes((ushort) name.Length), 0, result, lengthAt, 2);
            return result;
        }

        // Takes the deletion mark off (a replaced file keeps its replacement).
        public void Undelete(Entry entry)
        {
            entry.deleted = false;
        }

        public void Revert(Entry entry)
        {
            entry.Replacement = null;
            entry.deleted = false;
            if (entry.IsNew)
            {
                this.Entries.Remove(entry);
            }
        }

        // A new file, pending until saved. Its headers copy those of an entry of the archive (the
        // game's version fields and the 0x1123 data-offset extra field); the compression is the one
        // the archive uses for that extension (else its most common one). It goes in alphabetical
        // place when the archive is sorted, else at the end.
        public Entry Add(string name, byte[] data)
        {
            if (this.Find(name) != null)
            {
                throw new ArgumentException("The archive already has " + name + ".");
            }
            data = this.ForGame(name, data);   // every carbin written: glass draw pass + part flag right, long meshes split
            Entry entry = new Entry();
            entry.Name = name;
            entry.IsNew = true;
            entry.Replacement = data;
            entry.UncompressedSize = data.Length;
            entry.CompressedSize = data.Length;
            if (this.IsFolder)
            {
                entry.FullPath = Path.Combine(this.FilePath, name.Replace('/', Path.DirectorySeparatorChar));
                entry.Method = MethodStored;
            }
            else
            {
                Entry template = this.TemplateFor(name);
                entry.Method = (template != null) ? template.Method : MethodLzx;
                entry.LocalHeader = BuildLocalHeader(template, name, entry.Method);
                entry.CentralRecord = BuildCentralRecord(template, name, entry.Method);
            }
            this.Entries.Insert(this.InsertPosition(name), entry);
            return entry;
        }

        private int InsertPosition(string name)
        {
            // Right after the last entry that sorts before the name, so a few misplaced entries (a game archive
            // that is sorted differently, files added earlier) do not push the new file to the end of the archive.
            int after = -1;
            for (int i = 0; i < this.Entries.Count; i++)
            {
                if (StringComparer.OrdinalIgnoreCase.Compare(this.Entries[i].Name, name) < 0)
                {
                    after = i;
                }
            }
            return after + 1;
        }

        // An existing entry with the same extension, else one with the archive's most common method.
        private Entry TemplateFor(string name)
        {
            string extension = Path.GetExtension(name);
            Dictionary<int, int> methods = new Dictionary<int, int>();
            List<Entry> candidates = new List<Entry>(this.Entries);
            if (this.templates != null)
            {
                candidates.AddRange(this.templates);
            }
            foreach (Entry entry in candidates)
            {
                if (entry.CentralRecord == null)
                {
                    continue;
                }
                if (string.Equals(Path.GetExtension(entry.Name), extension, StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
                int count;
                methods.TryGetValue(entry.Method, out count);
                methods[entry.Method] = count + 1;
            }
            Entry best = null;
            int bestCount = 0;
            foreach (Entry entry in candidates)
            {
                if (entry.CentralRecord != null && methods[entry.Method] > bestCount)
                {
                    best = entry;
                    bestCount = methods[entry.Method];
                }
            }
            return best;
        }

        private static uint DosTime(DateTime time)
        {
            uint date = (uint) ((((time.Year - 1980) & 0x7F) << 9) | (time.Month << 5) | time.Day);
            uint clock = (uint) ((time.Hour << 11) | (time.Minute << 5) | (time.Second / 2));
            return (date << 16) | clock;
        }

        // Local header: signature, version needed, flags, method, time, date, crc/sizes (set on
        // save), name; no extra field (as in the game's archives).
        private static byte[] BuildLocalHeader(Entry template, string name, int method)
        {
            byte[] nameBytes = Encoding.ASCII.GetBytes(name);
            byte[] header = new byte[30 + nameBytes.Length];
            Buffer.BlockCopy(BitConverter.GetBytes(0x04034b50), 0, header, 0, 4);
            ushort needed = (template != null) ? BitConverter.ToUInt16(template.LocalHeader, 4) : (ushort) 10;
            Buffer.BlockCopy(BitConverter.GetBytes(needed), 0, header, 4, 2);
            Buffer.BlockCopy(BitConverter.GetBytes((ushort) method), 0, header, 8, 2);
            Buffer.BlockCopy(BitConverter.GetBytes(DosTime(DateTime.Now)), 0, header, 10, 4);
            Buffer.BlockCopy(BitConverter.GetBytes((ushort) nameBytes.Length), 0, header, 26, 2);
            Buffer.BlockCopy(nameBytes, 0, header, 30, nameBytes.Length);
            return header;
        }

        // Central record: the template's version and attribute fields, the new name, and the
        // template's extra fields (the 0x1123 data offset is written on save).
        private static byte[] BuildCentralRecord(Entry template, string name, int method)
        {
            byte[] nameBytes = Encoding.ASCII.GetBytes(name);
            byte[] extra = new byte[0];
            if (template != null)
            {
                int templateName = BitConverter.ToUInt16(template.CentralRecord, 28);
                int templateExtra = BitConverter.ToUInt16(template.CentralRecord, 30);
                extra = Slice(template.CentralRecord, 46 + templateName, templateExtra);
            }
            byte[] record = new byte[46 + nameBytes.Length + extra.Length];
            if (template != null)
            {
                Buffer.BlockCopy(template.CentralRecord, 0, record, 0, 46);
            }
            else
            {
                Buffer.BlockCopy(BitConverter.GetBytes(0x02014b50), 0, record, 0, 4);
                Buffer.BlockCopy(BitConverter.GetBytes((ushort) 10), 0, record, 4, 2);
                Buffer.BlockCopy(BitConverter.GetBytes((ushort) 10), 0, record, 6, 2);
            }
            Buffer.BlockCopy(BitConverter.GetBytes((ushort) 0), 0, record, 8, 2);
            Buffer.BlockCopy(BitConverter.GetBytes((ushort) method), 0, record, 10, 2);
            Buffer.BlockCopy(BitConverter.GetBytes(DosTime(DateTime.Now)), 0, record, 12, 4);
            Buffer.BlockCopy(BitConverter.GetBytes((ushort) nameBytes.Length), 0, record, 28, 2);
            Buffer.BlockCopy(BitConverter.GetBytes((ushort) extra.Length), 0, record, 30, 2);
            Buffer.BlockCopy(BitConverter.GetBytes((ushort) 0), 0, record, 32, 2);
            Buffer.BlockCopy(nameBytes, 0, record, 46, nameBytes.Length);
            Buffer.BlockCopy(extra, 0, record, 46 + nameBytes.Length, extra.Length);
            return record;
        }

        private static byte[] Compress(int method, byte[] data)
        {
            switch (method)
            {
                case MethodStored:
                    return data;

                case MethodDeflate:
                    using (MemoryStream output = new MemoryStream())
                    {
                        using (DeflateStream stream = new DeflateStream(output, CompressionMode.Compress, true))
                        {
                            stream.Write(data, 0, data.Length);
                        }
                        return output.ToArray();
                    }

                case MethodLzx:
                    return XMemLzx.Compress(data);
            }
            throw new NotSupportedException(string.Format("Compression method {0} is not supported.", method));
        }

        // Folder mode: writes every replaced file (through a temporary file) and returns them.
        public List<Entry> SaveFolder()
        {
            List<Entry> saved = this.ModifiedEntries;
            saved.RemoveAll(delegate(Entry entry) { return entry.deleted; });
            foreach (Entry entry in saved)
            {
                string temp = entry.FullPath + ".tmp";
                Directory.CreateDirectory(Path.GetDirectoryName(entry.FullPath));
                File.WriteAllBytes(temp, entry.Replacement);
                File.Delete(entry.FullPath);
                File.Move(temp, entry.FullPath);
                entry.UncompressedSize = entry.Replacement.Length;
                entry.CompressedSize = entry.UncompressedSize;
                entry.Replacement = null;
                entry.IsNew = false;
            }
            return saved;
        }

        // Folder mode: the files marked for deletion go (deleteFile is told each path: the Recycle Bin, or File.Delete)
        // and leave the archive. Returns them.
        public List<Entry> RemoveDeleted(Action<string> deleteFile)
        {
            List<Entry> gone = this.Entries.FindAll(delegate(Entry entry) { return entry.deleted; });
            foreach (Entry entry in gone)
            {
                if (this.IsFolder && entry.FullPath != null && File.Exists(entry.FullPath))
                {
                    deleteFile(entry.FullPath);
                }
                this.Entries.Remove(entry);
            }
            return gone;
        }

        // Writes the archive to a temporary file first, then moves it over the target.
        public void Save(string path)
        {
            if (this.IsFolder)
            {
                throw new InvalidOperationException("Use SaveFolder for a folder.");
            }
            string temp = path + ".tmp";
            using (Stream source = OpenRead(this.FilePath))
            using (FileStream file = new FileStream(temp, FileMode.Create, FileAccess.Write))
            {
                BinaryWriter writer = new BinaryWriter(file);
                List<byte[]> central = new List<byte[]>();
                // "Version needed" of an existing LZX entry, copied to the entries that become LZX.
                byte[] lzxNeeded = null;
                // The game's StringTables archives are stored (M0) and stay so.
                bool keepStored = Path.GetFileName(path).StartsWith("StringTables", StringComparison.OrdinalIgnoreCase)
                    || Path.GetFileName(this.FilePath).StartsWith("StringTables", StringComparison.OrdinalIgnoreCase);
                foreach (Entry entry in this.Entries)
                {
                    if (entry.Method == MethodLzx && entry.LocalHeader != null)
                    {
                        lzxNeeded = Slice(entry.LocalHeader, 4, 2);
                        break;
                    }
                }
                foreach (Entry entry in this.Entries)
                {
                    if (entry.deleted)
                    {
                        continue;
                    }
                    byte[] data;
                    uint crc = entry.Crc;
                    int uncompressed = entry.UncompressedSize;
                    if (entry.Replacement != null)
                    {
                        data = Compress(entry.Method, entry.Replacement);
                        crc = Crc32(entry.Replacement);
                        uncompressed = entry.Replacement.Length;
                    }
                    else
                    {
                        data = ReadAt(source, entry.DataOffset, entry.CompressedSize);
                    }

                    // Stored (M0) entries are written as Xbox LZX, unless that does not make them smaller.
                    int method = entry.Method;
                    if (method == MethodStored && data.Length > 0 && !keepStored)
                    {
                        byte[] packed = XMemLzx.Compress(data);
                        if (packed.Length < data.Length)
                        {
                            data = packed;
                            method = MethodLzx;
                        }
                    }

                    // Sizes always live in the headers; any data descriptor flag is dropped.
                    byte[] local = (byte[]) entry.LocalHeader.Clone();
                    PatchHeader(local, 6, 14, crc, data.Length, uncompressed);
                    byte[] record = (byte[]) entry.CentralRecord.Clone();
                    PatchHeader(record, 8, 16, crc, data.Length, uncompressed);
                    if (method != entry.Method)
                    {
                        Buffer.BlockCopy(BitConverter.GetBytes((ushort) method), 0, local, 8, 2);
                        Buffer.BlockCopy(BitConverter.GetBytes((ushort) method), 0, record, 10, 2);
                        if (lzxNeeded != null)
                        {
                            Buffer.BlockCopy(lzxNeeded, 0, local, 4, 2);
                            Buffer.BlockCopy(lzxNeeded, 0, record, 6, 2);
                        }
                    }
                    Buffer.BlockCopy(BitConverter.GetBytes((int) file.Position), 0, record, 42, 4);
                    PatchDataOffset(record, (int) file.Position + local.Length);
                    central.Add(record);

                    writer.Write(local);
                    writer.Write(data);
                }

                int centralOffset = (int) file.Position;
                foreach (byte[] record in central)
                {
                    writer.Write(record);
                }
                int centralSize = (int) file.Position - centralOffset;
                writer.Write(0x06054b50);
                writer.Write((short) 0);
                writer.Write((short) 0);
                writer.Write((short) central.Count);
                writer.Write((short) central.Count);
                writer.Write(centralSize);
                writer.Write(centralOffset);
                writer.Write((short) this.endComment.Length);
                writer.Write(this.endComment);
            }
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            File.Move(temp, path);

            this.FilePath = path;
            this.Entries.Clear();
            using (Stream file = OpenRead(path))
            {
                this.Parse(file);
            }
        }

        // Forza's central records carry an extra field 0x1123 (4 bytes) holding the absolute offset of
        // the entry's data, which the game reads directly. It must follow the entry when the data moves.
        private static void PatchDataOffset(byte[] record, int dataOffset)
        {
            int nameLength = BitConverter.ToUInt16(record, 28);
            int extraLength = BitConverter.ToUInt16(record, 30);
            int position = 46 + nameLength;
            int end = position + extraLength;
            while (position + 4 <= end)
            {
                int id = BitConverter.ToUInt16(record, position);
                int size = BitConverter.ToUInt16(record, position + 2);
                if (id == 0x1123 && size == 4)
                {
                    Buffer.BlockCopy(BitConverter.GetBytes(dataOffset), 0, record, position + 4, 4);
                }
                position += 4 + size;
            }
        }

        private static void PatchHeader(byte[] header, int flagsOffset, int crcOffset, uint crc, int compressed, int uncompressed)
        {
            ushort flags = (ushort) (BitConverter.ToUInt16(header, flagsOffset) & ~8);
            Buffer.BlockCopy(BitConverter.GetBytes(flags), 0, header, flagsOffset, 2);
            Buffer.BlockCopy(BitConverter.GetBytes(crc), 0, header, crcOffset, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(compressed), 0, header, crcOffset + 4, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(uncompressed), 0, header, crcOffset + 8, 4);
        }

        private static byte[] Slice(byte[] source, int offset, int length)
        {
            byte[] result = new byte[length];
            Buffer.BlockCopy(source, offset, result, 0, length);
            return result;
        }

        private static uint[] crcTable;

        public static uint Crc32(byte[] data)
        {
            if (crcTable == null)
            {
                uint[] table = new uint[256];
                for (uint i = 0; i < 256; i++)
                {
                    uint c = i;
                    for (int k = 0; k < 8; k++)
                    {
                        c = ((c & 1) != 0) ? (0xEDB88320 ^ (c >> 1)) : (c >> 1);
                    }
                    table[i] = c;
                }
                crcTable = table;
            }
            uint crc = 0xFFFFFFFF;
            for (int i = 0; i < data.Length; i++)
            {
                crc = crcTable[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            }
            return ~crc;
        }
    }
}
