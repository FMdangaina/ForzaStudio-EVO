namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Drawing.Drawing2D;
    using System.IO;

    // The thumbnails the game saved in the emulator's profile: ForzaProfile\Thumbnails\Thumbnail_N.xdc is the
    // same texture as a car's Thumbnail_<id>.xds (768 x 288 A8R8G8B8), deflate-packed (XdcFile), so once
    // unpacked it goes into a DLC as is. The folder is looked up under the Xenia folder of the port tool
    // (content\<profile>\4D530910\<slot>\ForzaProfile\Thumbnails) and remembered once chosen by hand.
    public static class XeniaThumbnails
    {
        private const string FolderKey = "Thumbnails.Xenia";

        public sealed class Item
        {
            public string Name;
            public DateTime Time;
            public byte[] Xds;
            public XdsTexture Texture;
        }

        public static string FindFolder()
        {
            return FindFolder("4D530910");
        }

        // title: the game's title id (FM4 4D530910, FM3 4D53084D: its thumbnails are 512 x 256, the same .xdc container); each game remembers its own folder.
        public static string FindFolder(string title)
        {
            string remembered = AppSettings.Get(KeyOf(title), "");
            if (Directory.Exists(remembered))
            {
                return remembered;
            }
            string xenia = AppSettings.Get("CarPort.Xenia", "");
            string best = null;
            DateTime bestTime = DateTime.MinValue;
            try
            {
                string content = Path.Combine(xenia, "content");
                if (xenia.Length > 0 && Directory.Exists(content))
                {
                    foreach (string folder in Directory.GetDirectories(content, "Thumbnails", SearchOption.AllDirectories))
                    {
                        if (folder.IndexOf(title, StringComparison.OrdinalIgnoreCase) >= 0 && Directory.GetFiles(folder, "*.xdc").Length > 0)
                        {
                            DateTime time = Directory.GetLastWriteTime(folder);
                            if (best == null || time > bestTime)
                            {
                                best = folder;
                                bestTime = time;
                            }
                        }
                    }
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            return best;
        }

        public static void Remember(string folder)
        {
            Remember(folder, "4D530910");
        }

        public static void Remember(string folder, string title)
        {
            AppSettings.Set(KeyOf(title), folder);
        }

        private static string KeyOf(string title)
        {
            return string.Equals(title, "4D530910", StringComparison.OrdinalIgnoreCase) ? FolderKey : FolderKey + "." + title;
        }

        // Every thumbnail of the folder with the given size and format, newest first.
        public static List<Item> Load(string folder, int width, int height, string codec)
        {
            List<Item> items = new List<Item>();
            if (folder == null || !Directory.Exists(folder))
            {
                return items;
            }
            FileInfo[] files = new DirectoryInfo(folder).GetFiles("*.xdc");
            Array.Sort(files, delegate(FileInfo a, FileInfo b) { return b.LastWriteTime.CompareTo(a.LastWriteTime); });
            foreach (FileInfo file in files)
            {
                try
                {
                    byte[] xds = XdcFile.Unpack(File.ReadAllBytes(file.FullName));
                    XdsTexture texture = new XdsTexture(xds);
                    if (texture.Width == width && texture.Height == height && texture.Codec.ToString() == codec)
                    {
                        Item item = new Item();
                        item.Name = Path.GetFileNameWithoutExtension(file.Name);
                        item.Time = file.LastWriteTime;
                        item.Xds = xds;
                        item.Texture = texture;
                        items.Add(item);
                    }
                }
                catch (Exception)
                {
                }
            }
            return items;
        }

        // The texture scaled onto a checker in the theme's greys so a transparent background shows.
        public static Bitmap Preview(XdsTexture texture, int width, int height)
        {
            Bitmap preview = new Bitmap(width, height);
            using (Bitmap full = texture.GetLevelBitmap(0))
            using (Graphics g = Graphics.FromImage(preview))
            {
                g.Clear(ForzaTheme.Surface);
                using (Brush square = new SolidBrush(ForzaTheme.Raised))
                {
                    for (int y = 0; y < height; y += 8)
                    {
                        for (int x = ((y / 8) % 2) * 8; x < width; x += 16)
                        {
                            g.FillRectangle(square, x, y, 8, 8);
                        }
                    }
                }
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(full, 0, 0, width, height);
            }
            return preview;
        }
    }
}
