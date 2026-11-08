namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text;

    // An Xbox 360 STFS package (LIVE, marketplace content) of a folder, for a modded console (RGH/JTAG:
    // the RSA signature is left empty, those consoles do not check it; the hash tables and the content id
    // are valid). A C# port of the stfs_write.py of Aiden-Piercee (https://github.com/Aiden-Piercee, fm4dlc toolchain); the header fields
    // were read from the FH1 car package XTD (@xtd_ac) built with the Xbox 360 authoring tool, which a
    // console loads: licence 0 open to everyone (FFFFFFFFFFFFFFFF), header size 0xAD0E, one hash table
    // per level (block separation 1), transfer flags C0.
    //
    // Layout (Free60 / Velocity): header 0xB000 bytes, then 4 KB blocks. Every 170 data blocks are
    // preceded by their level-0 hash table (SHA-1 of each block + the next block of the file chain), every
    // 170 level-0 tables by a level-1 table, and above 170 * 170 blocks one level-2 table. The file table
    // (64-byte entries, folders before their children, children of a folder contiguous) fills the first
    // data blocks; every file's blocks are consecutive.
    // Names hold at most 40 characters (0x28): longer ones are refused.
    public static class StfsPackage
    {
        private const int Block = 0x1000;
        private const int PerTable = 170;
        private const int Super = PerTable * PerTable;      // 0x70E4
        private const int HeaderSize = 0xB000;
        private const int MaxName = 0x28;

        // The package of 'folder' into 'output'; returns the file name the console expects (the content
        // id in hex + "4D", as official packages are named).
        public static string Write(string folder, string output, uint titleId, uint contentType, string displayName, string description, string titleName, byte[] thumbnail)
        {
            return WriteCore(folder, output, Header(titleId, contentType, displayName, description, titleName, thumbnail));
        }

        // A package edited in the editor (Open DLC Package): 'folder' packed again with the header of
        // the package it came from (StfsReader.Header: magic, licences, names, thumbnails, metadata
        // kept). Only the volume descriptor, the content size and the content id change, as in Write;
        // the signature becomes stale (a modded console and Xenia do not check it).
        public static string Rewrite(string folder, string output, byte[] header)
        {
            if (header == null || header.Length < 0x1712 || (header.Length % Block) != 0)
            {
                throw new ArgumentException("Not an STFS package header.", "header");
            }
            byte[] copy = (byte[]) header.Clone();
            for (int i = 0x379; i < 0x39D; i++)
            {
                copy[i] = 0;
            }
            return WriteCore(folder, output, copy);
        }

        private static string WriteCore(string folder, string output, byte[] header)
        {
            long headerSize = header.Length;
            // Entries in the order of the file table: breadth first, each folder's children sorted by name.
            List<Entry> entries = new List<Entry>();
            Queue<KeyValuePair<string, int>> queue = new Queue<KeyValuePair<string, int>>();
            queue.Enqueue(new KeyValuePair<string, int>(Path.GetFullPath(folder), -1));
            while (queue.Count > 0)
            {
                KeyValuePair<string, int> parent = queue.Dequeue();
                List<string> children = new List<string>(Directory.GetDirectories(parent.Key));
                children.AddRange(Directory.GetFiles(parent.Key));
                children.Sort(delegate(string a, string b) { return string.Compare(Path.GetFileName(a).ToLowerInvariant(), Path.GetFileName(b).ToLowerInvariant(), StringComparison.Ordinal); });
                foreach (string child in children)
                {
                    Entry entry = new Entry();
                    entry.Name = Path.GetFileName(child);
                    entry.Path = child;
                    entry.Parent = parent.Value;
                    entry.IsFolder = Directory.Exists(child);
                    if (Encoding.ASCII.GetByteCount(entry.Name) > MaxName)
                    {
                        throw new InvalidOperationException("A name is longer than the 40 characters an Xbox 360 package allows: " + child);
                    }
                    entries.Add(entry);
                    if (entry.IsFolder)
                    {
                        queue.Enqueue(new KeyValuePair<string, int>(child, entries.Count - 1));
                    }
                }
            }

            // Data blocks: the file table first, then every file.
            int tableBlocks = ((entries.Count * 0x40) + Block - 1) / Block;
            int blocks = tableBlocks;
            foreach (Entry entry in entries)
            {
                if (entry.IsFolder)
                {
                    continue;
                }
                entry.Size = new FileInfo(entry.Path).Length;
                if (entry.Size > uint.MaxValue)
                {
                    throw new InvalidOperationException("A file is too large for an Xbox 360 package: " + entry.Path);
                }
                entry.Start = blocks;
                entry.Blocks = (int) ((entry.Size + Block - 1) / Block);
                blocks += entry.Blocks;
            }
            int level0 = (blocks + PerTable - 1) / PerTable;
            int level1 = (level0 > 1) ? (level0 + PerTable - 1) / PerTable : 0;
            bool level2 = level1 > 1;
            if (level1 > PerTable)
            {
                throw new InvalidOperationException("The package is too large (more than 170 * 170 * 170 blocks).");
            }
            long physical = blocks + level0 + level1 + (level2 ? 1 : 0);

            byte[][] blockHashes = new byte[blocks][];
            using (FileStream file = new FileStream(output, FileMode.Create, FileAccess.ReadWrite))
            using (SHA1 sha = SHA1.Create())
            {
                file.SetLength(headerSize + (physical * Block));

                // File table.
                byte[] table = new byte[tableBlocks * Block];
                for (int i = 0; i < entries.Count; i++)
                {
                    Entry entry = entries[i];
                    int at = i * 0x40;
                    byte[] name = Encoding.ASCII.GetBytes(entry.Name);
                    Buffer.BlockCopy(name, 0, table, at, name.Length);
                    table[at + 0x28] = (byte) (name.Length | 0x40 | (entry.IsFolder ? 0x80 : 0));   // 0x40: consecutive blocks
                    if (!entry.IsFolder)
                    {
                        PutLittle24(table, at + 0x29, entry.Blocks);
                        PutLittle24(table, at + 0x2C, entry.Blocks);
                        PutLittle24(table, at + 0x2F, entry.Start);
                        PutBig32(table, at + 0x34, (uint) entry.Size);
                    }
                    ushort parent = (entry.Parent < 0) ? (ushort) 0xFFFF : (ushort) entry.Parent;
                    table[at + 0x32] = (byte) (parent >> 8);
                    table[at + 0x33] = (byte) parent;
                    byte[] stamp = { 0x2D, 0x6F, 0x00, 0x20 };
                    Buffer.BlockCopy(stamp, 0, table, at + 0x38, 4);
                    Buffer.BlockCopy(stamp, 0, table, at + 0x3C, 4);
                }
                for (int b = 0; b < tableBlocks; b++)
                {
                    byte[] data = new byte[Block];
                    Buffer.BlockCopy(table, b * Block, data, 0, Block);
                    blockHashes[b] = WriteBlock(file, headerSize, sha, Backing(b), data);
                }

                // Files, block by block.
                byte[] buffer = new byte[Block];
                foreach (Entry entry in entries)
                {
                    if (entry.IsFolder)
                    {
                        continue;
                    }
                    using (FileStream input = File.OpenRead(entry.Path))
                    {
                        for (int k = 0; k < entry.Blocks; k++)
                        {
                            Array.Clear(buffer, 0, Block);
                            ReadFull(input, buffer);
                            blockHashes[entry.Start + k] = WriteBlock(file, headerSize, sha, Backing(entry.Start + k), buffer);
                        }
                    }
                }

                // Level 0: per data block its SHA-1, 0x80 and the next block of the chain (FFFFFF at the end).
                byte[][] level0Hashes = new byte[level0][];
                for (int g = 0; g < level0; g++)
                {
                    byte[] hashes = new byte[Block];
                    for (int k = 0; k < PerTable; k++)
                    {
                        int b = (g * PerTable) + k;
                        if (b >= blocks)
                        {
                            break;
                        }
                        int next = (b + 1 < blocks) ? b + 1 : 0xFFFFFF;
                        Buffer.BlockCopy(blockHashes[b], 0, hashes, k * 0x18, 20);
                        hashes[(k * 0x18) + 20] = 0x80;
                        hashes[(k * 0x18) + 21] = (byte) (next >> 16);
                        hashes[(k * 0x18) + 22] = (byte) (next >> 8);
                        hashes[(k * 0x18) + 23] = (byte) next;
                    }
                    level0Hashes[g] = WriteBlock(file, headerSize, sha, Level0Position(g), hashes);
                }
                byte[] top;
                if (level1 == 0)
                {
                    top = level0Hashes[0];
                }
                else
                {
                    byte[][] level1Hashes = new byte[level1][];
                    for (int s = 0; s < level1; s++)
                    {
                        byte[] hashes = new byte[Block];
                        for (int k = 0; k < PerTable && (s * PerTable) + k < level0; k++)
                        {
                            Buffer.BlockCopy(level0Hashes[(s * PerTable) + k], 0, hashes, k * 0x18, 20);
                        }
                        level1Hashes[s] = WriteBlock(file, headerSize, sha, Level1Position(s), hashes);
                    }
                    if (level2)
                    {
                        byte[] hashes = new byte[Block];
                        for (int s = 0; s < level1; s++)
                        {
                            Buffer.BlockCopy(level1Hashes[s], 0, hashes, s * 0x18, 20);
                        }
                        top = WriteBlock(file, headerSize, sha, (PerTable * 171) + 1, hashes);
                    }
                    else
                    {
                        top = level1Hashes[0];
                    }
                }

                // Volume descriptor: size 0x24, block separation 1, file table blocks and start, top hash,
                // allocated blocks.
                header[0x379] = 0x24;
                header[0x37B] = 1;
                header[0x37C] = (byte) tableBlocks;
                header[0x37D] = (byte) (tableBlocks >> 8);
                Buffer.BlockCopy(top, 0, header, 0x381, 20);
                PutBig32(header, 0x395, (uint) blocks);
                PutBig64(header, 0x34C, physical * Block);
                // Content id: SHA-1 of the header from 0x344 on.
                byte[] id = sha.ComputeHash(header, 0x344, header.Length - 0x344);
                Buffer.BlockCopy(id, 0, header, 0x32C, 20);
                file.Position = 0;
                file.Write(header, 0, header.Length);
                return Hex(id) + "4D";
            }
        }

        private class Entry
        {
            public string Name;
            public string Path;
            public int Parent;
            public bool IsFolder;
            public long Size;
            public int Start;
            public int Blocks;
        }

        // Header with the fields of a LIVE marketplace package; the volume descriptor, the content size
        // and the content id are set by Write.
        private static byte[] Header(uint titleId, uint contentType, string displayName, string description, string titleName, byte[] thumbnail)
        {
            byte[] header = new byte[HeaderSize];
            Encoding.ASCII.GetBytes("LIVE").CopyTo(header, 0);
            for (int i = 0; i < 8; i++)
            {
                header[0x22C + i] = 0xFF;                          // licence 0: everyone
            }
            PutBig32(header, 0x340, 0xAD0E);                        // header size
            PutBig32(header, 0x344, contentType);
            PutBig32(header, 0x348, 2);                             // metadata version
            PutBig32(header, 0x360, titleId);
            PutText(header, 0x411, displayName);
            PutText(header, 0xD11, description);
            PutText(header, 0x1691, titleName);
            header[0x1711] = 0xC0;                                  // transfer flags
            if (thumbnail != null && thumbnail.Length <= 0x4000)
            {
                PutBig32(header, 0x1712, (uint) thumbnail.Length);
                PutBig32(header, 0x1716, (uint) thumbnail.Length);
                Buffer.BlockCopy(thumbnail, 0, header, 0x171A, thumbnail.Length);
                Buffer.BlockCopy(thumbnail, 0, header, 0x571A, thumbnail.Length);
            }
            return header;
        }

        // Where data block b sits among the blocks after the header (its hash tables come before it).
        private static long Backing(int b)
        {
            long r = ((b + PerTable) / PerTable) + (long) b;
            if (b < PerTable)
            {
                return r;
            }
            if (b < Super)
            {
                return r + ((b + Super) / Super);
            }
            return 1 + r + ((b + Super) / Super);
        }

        private static long Level0Position(int g)
        {
            if (g == 0)
            {
                return 0;
            }
            int sg = g / PerTable;
            return ((long) g * 171) + sg + 1 + ((sg > 0) ? 1 : 0);
        }

        private static long Level1Position(int s)
        {
            return (s == 0) ? 171 : ((long) s * PerTable * 171) + s + 1;
        }

        private static byte[] WriteBlock(FileStream file, long headerSize, SHA1 sha, long position, byte[] data)
        {
            file.Position = headerSize + (position * Block);
            file.Write(data, 0, Block);
            return sha.ComputeHash(data, 0, Block);
        }

        private static void ReadFull(Stream input, byte[] buffer)
        {
            int done = 0;
            while (done < buffer.Length)
            {
                int read = input.Read(buffer, done, buffer.Length - done);
                if (read <= 0)
                {
                    return;
                }
                done += read;
            }
        }

        private static void PutText(byte[] data, int offset, string text)
        {
            byte[] bytes = Encoding.BigEndianUnicode.GetBytes(text ?? string.Empty);
            Buffer.BlockCopy(bytes, 0, data, offset, Math.Min(bytes.Length, 0x7E));
        }

        private static void PutLittle24(byte[] data, int offset, int value)
        {
            data[offset] = (byte) value;
            data[offset + 1] = (byte) (value >> 8);
            data[offset + 2] = (byte) (value >> 16);
        }

        private static void PutBig32(byte[] data, int offset, uint value)
        {
            data[offset] = (byte) (value >> 24);
            data[offset + 1] = (byte) (value >> 16);
            data[offset + 2] = (byte) (value >> 8);
            data[offset + 3] = (byte) value;
        }

        private static void PutBig64(byte[] data, int offset, long value)
        {
            PutBig32(data, offset, (uint) (value >> 32));
            PutBig32(data, offset + 4, (uint) value);
        }

        private static string Hex(byte[] bytes)
        {
            StringBuilder text = new StringBuilder();
            foreach (byte b in bytes)
            {
                text.Append(b.ToString("X2"));
            }
            return text.ToString();
        }
    }
}
