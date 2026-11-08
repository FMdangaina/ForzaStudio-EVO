namespace ForzaStudio
{
    using System;
    using System.IO;

    public enum ForzaGame
    {
        Unknown,
        Motorsport2,
        Motorsport3,
        Motorsport4,
        Horizon1,
        Horizon2
    }

    // Which Forza a game folder holds: the title ID in default.xex (XEX2 execution info), else the
    // files that only one game has.
    public static class GameInfo
    {
        public static ForzaGame Detect(string root)
        {
            if (root == null)
            {
                return ForzaGame.Unknown;
            }
            ForzaGame game = FromTitleId(TitleId(Path.Combine(root, "default.xex")));
            if (game == ForzaGame.Unknown)
            {
                // A car DLC package laid out as a source game (DlcSource.Stage).
                game = FromTitleId(DlcSource.StageTitleId(root));
            }
            if (game != ForzaGame.Unknown)
            {
                return game;
            }
            string media = Path.Combine(root, "Media");
            if (File.Exists(Path.Combine(Path.Combine(Path.Combine(media, "audio"), "radio"), "RadioSystem.xml")))
            {
                return ForzaGame.Horizon2;
            }
            string music = Path.Combine(Path.Combine(media, "audio"), "Music");
            if (File.Exists(Path.Combine(music, "InDefaultUIPlaylist.xml")))
            {
                return ForzaGame.Motorsport4;
            }
            if (File.Exists(Path.Combine(music, "OutOfCareerPlaylist.xml")))
            {
                return ForzaGame.Motorsport2;
            }
            return ForzaGame.Unknown;
        }

        public static ForzaGame FromTitleId(uint titleId)
        {
            switch (titleId)
            {
                case 0x4D5307EA:
                    return ForzaGame.Motorsport2;
                case 0x4D53084D:
                    return ForzaGame.Motorsport3;
                case 0x4D530910:
                    return ForzaGame.Motorsport4;
                case 0x4D5309C9:
                    return ForzaGame.Horizon1;
                case 0x4D530AA4:
                    return ForzaGame.Horizon2;
                default:
                    return ForzaGame.Unknown;
            }
        }

        public static string Name(ForzaGame game)
        {
            switch (game)
            {
                case ForzaGame.Motorsport2:
                    return "Forza Motorsport 2";
                case ForzaGame.Motorsport3:
                    return "Forza Motorsport 3";
                case ForzaGame.Motorsport4:
                    return "Forza Motorsport 4";
                case ForzaGame.Horizon1:
                    return "Forza Horizon";
                case ForzaGame.Horizon2:
                    return "Forza Horizon 2";
                default:
                    return null;
            }
        }

        private static readonly System.Collections.Generic.Dictionary<ForzaGame, string> sourceFolders = new System.Collections.Generic.Dictionary<ForzaGame, string>();

        // The port window's source game folder (checked to be that game), used by ConfiguredFolder before Settings.
        public static void UseSourceFolder(ForzaGame game, string folder)
        {
            lock (sourceFolders)
            {
                sourceFolders[game] = folder;
            }
        }

        // The game folder set in Tools > Settings > Folders for a source game (its gamedb, wheels and strings
        // complete the DLC packages of that game), or null when it is not set or holds another game.
        public static string ConfiguredFolder(ForzaGame game)
        {
            // The game folder chosen as the source in the port window wins: the packages of that game take their
            // database from the same game the user is looking at (user 2026-11-08: one place, not Settings behind it).
            lock (sourceFolders)
            {
                string chosen;
                if (sourceFolders.TryGetValue(game, out chosen) && Directory.Exists(chosen))
                {
                    return chosen;
                }
            }
            string key = null;
            switch (game)
            {
                case ForzaGame.Horizon1: key = "Folder.FH1"; break;
                case ForzaGame.Horizon2: key = "Folder.FH2"; break;
                case ForzaGame.Motorsport3: key = "Folder.FM3"; break;
                case ForzaGame.Motorsport2: key = "Folder.FM2"; break;
                case ForzaGame.Motorsport4: key = "CarPort.Target"; break;     // the port target: its database holds the expansions' rows
            }
            string folder = (key == null) ? "" : AppSettings.Get(key, "");
            return (folder.Length > 0 && Directory.Exists(folder) && Detect(folder) == game) ? folder : null;
        }

        // Short tag for the status bar and music label.
        public static string Tag(ForzaGame game)
        {
            switch (game)
            {
                case ForzaGame.Motorsport2:
                    return "FM2";
                case ForzaGame.Motorsport3:
                    return "FM3";
                case ForzaGame.Motorsport4:
                    return "FM4";
                case ForzaGame.Horizon1:
                    return "FH";
                case ForzaGame.Horizon2:
                    return "FH2";
                default:
                    return "Forza";
            }
        }

        // XEX2 optional header 0x00040006 (execution info): title ID at +12. 0 when unreadable.
        public static uint TitleId(string xex)
        {
            try
            {
                if (!File.Exists(xex))
                {
                    return 0;
                }
                byte[] head = new byte[65536];
                int length;
                using (FileStream file = new FileStream(xex, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    length = file.Read(head, 0, head.Length);
                }
                if (length < 24 || head[0] != 'X' || head[1] != 'E' || head[2] != 'X' || head[3] != '2')
                {
                    return 0;
                }
                uint count = U32(head, 20);
                for (int i = 0; i < count && 32 + (i * 8) <= length; i++)
                {
                    if (U32(head, 24 + (i * 8)) == 0x40006)
                    {
                        int info = (int) U32(head, 28 + (i * 8));
                        return (info + 16 <= length) ? U32(head, info + 12) : 0;
                    }
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            return 0;
        }

        private static uint U32(byte[] data, int offset)
        {
            return (uint) ((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);
        }
    }
}
