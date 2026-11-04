namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;

    // The games a car can be ported between (Tools > Import Car From Another Forza, Create DLC Package).
    // One table: the menu, the window titles and later the install/port screens read the pair from here,
    // so a new route = one line in Supported (user 2026-11-02: every game to every game).
    internal static class PortPairs
    {
        // Menu order. The tag is what the window titles show.
        public static readonly string[] Games = { "FM2", "FM3", "FM4", "FH1", "FH2" };

        // Routes with a working conversion AND a window. target -> sources.
        // FM4 <- FM2/FM3/FH1/FH2 is the CarPortForm. FM3 <- FM4 is CarPortFm3Form (CarPortToFm3): code kept, route
        // switched off (FM3 work suspended until FM4 is satisfactory, user 2026-11-03): add { "FM3", new[] { "FM4" } } back.
        private static readonly Dictionary<string, string[]> Supported = new Dictionary<string, string[]>
        {
            { "FM4", new[] { "FM2", "FM3", "FH1", "FH2" } },
        };

        // Targets whose Create DLC Package exists (FM3 suspended too: add "FM3" back).
        private static readonly string[] DlcTargets = { "FM4" };

        public static bool CanImport(string target, string source)
        {
            string[] sources;
            return Supported.TryGetValue(target, out sources) && Array.IndexOf(sources, source) >= 0;
        }

        public static bool CanCreateDlc(string target)
        {
            return Array.IndexOf(DlcTargets, target) >= 0;
        }

        public static string FullName(string tag)
        {
            switch (tag)
            {
                case "FM2": return "Forza Motorsport 2";
                case "FM3": return "Forza Motorsport 3";
                case "FM4": return "Forza Motorsport 4";
                case "FH1": return "Forza Horizon";
                case "FH2": return "Forza Horizon 2";
                default: return tag;
            }
        }
    }
}
