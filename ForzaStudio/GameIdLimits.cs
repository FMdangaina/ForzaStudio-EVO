namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Reflection;
    using System.Text;

    // The largest Id of every table column in each game, base game plus every DLC / title update
    // package: one file per game, docs/<FM2|FM3|FM4>/IdMax.json, docs/HZN/IdMax.<FH1|FH2>.json, every table of the game's database
    // (built by tools/IdMax; embedded in the exe as IdMax.<game>.json). A new row never takes an Id at or
    // below it (user 2026-10-21: inserts in any table start from these limits), so a ported or created row
    // cannot share an Id with a row of an official package that is installed later.
    public static class GameIdLimits
    {
        private static readonly Dictionary<string, Dictionary<string, object>> roots = new Dictionary<string, Dictionary<string, object>>();

        // The key of a game in the file.
        public static string Key(ForzaGame game)
        {
            switch (game)
            {
                case ForzaGame.Motorsport2: return "FM2";
                case ForzaGame.Motorsport3: return "FM3";
                case ForzaGame.Horizon1: return "FH1";
                case ForzaGame.Horizon2: return "FH2";
                default: return "FM4";
            }
        }

        internal static object Parse(string text)
        {
            int at = 0;
            return Value(text, ref at);
        }

        // The largest Id of table.column in the game, or -1 when the file does not know the column.
        public static long Max(ForzaGame game, string table, string column)
        {
            Dictionary<string, object> columns = Columns(game, table);
            if (columns == null)
            {
                return -1;
            }
            foreach (KeyValuePair<string, object> pair in columns)
            {
                if (string.Equals(pair.Key, column, StringComparison.OrdinalIgnoreCase))
                {
                    Dictionary<string, object> info = pair.Value as Dictionary<string, object>;
                    object max;
                    if (info != null && info.TryGetValue("max", out max) && max is long)
                    {
                        return (long) max;
                    }
                }
            }
            return -1;
        }

        private static Dictionary<string, object> Columns(ForzaGame game, string table)
        {
            Dictionary<string, object> one = Load(game);
            Dictionary<string, object> tables = (one != null) ? Child(one, "tables") : null;
            if (tables == null)
            {
                return null;
            }
            foreach (KeyValuePair<string, object> pair in tables)
            {
                if (string.Equals(pair.Key, table, StringComparison.OrdinalIgnoreCase))
                {
                    Dictionary<string, object> t = pair.Value as Dictionary<string, object>;
                    return (t != null) ? Child(t, "columns") : null;
                }
            }
            return null;
        }

        private static Dictionary<string, object> Child(Dictionary<string, object> node, string name)
        {
            object value;
            return node.TryGetValue(name, out value) ? value as Dictionary<string, object> : null;
        }

        // The file of one game, or null when the exe holds none.
        private static Dictionary<string, object> Load(ForzaGame game)
        {
            string key = Key(game);
            Dictionary<string, object> root;
            if (!roots.TryGetValue(key, out root))
            {
                root = null;
                try
                {
                    using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("IdMax." + key + ".json"))
                    {
                        if (stream != null)
                        {
                            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                            {
                                int at = 0;
                                root = Value(reader.ReadToEnd(), ref at) as Dictionary<string, object>;
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    root = null;
                }
                roots[key] = root;
            }
            return root;
        }

        #region Minimal JSON reader (objects, arrays, strings, integers, numbers, true / false / null)

        private static object Value(string s, ref int at)
        {
            Skip(s, ref at);
            switch (s[at])
            {
                case '{':
                    {
                        Dictionary<string, object> map = new Dictionary<string, object>();
                        at++;
                        Skip(s, ref at);
                        if (s[at] == '}')
                        {
                            at++;
                            return map;
                        }
                        while (true)
                        {
                            Skip(s, ref at);
                            string key = Text(s, ref at);
                            Skip(s, ref at);
                            at++;   // ':'
                            map[key] = Value(s, ref at);
                            Skip(s, ref at);
                            if (s[at++] == '}')
                            {
                                return map;
                            }
                        }
                    }
                case '[':
                    {
                        List<object> list = new List<object>();
                        at++;
                        Skip(s, ref at);
                        if (s[at] == ']')
                        {
                            at++;
                            return list;
                        }
                        while (true)
                        {
                            list.Add(Value(s, ref at));
                            Skip(s, ref at);
                            if (s[at++] == ']')
                            {
                                return list;
                            }
                        }
                    }
                case '"':
                    return Text(s, ref at);
                default:
                    {
                        int start = at;
                        while (at < s.Length && ",]} \t\r\n".IndexOf(s[at]) < 0)
                        {
                            at++;
                        }
                        string word = s.Substring(start, at - start);
                        long whole;
                        double real;
                        if (long.TryParse(word, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out whole))
                        {
                            return whole;
                        }
                        if (double.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out real))
                        {
                            return real;
                        }
                        return (word == "true") ? (object) true : (word == "false" ? (object) false : null);
                    }
            }
        }

        private static string Text(string s, ref int at)
        {
            StringBuilder text = new StringBuilder();
            at++;   // opening quote
            while (s[at] != '"')
            {
                char c = s[at++];
                if (c == '\\')
                {
                    c = s[at++];
                    switch (c)
                    {
                        case 'n': c = '\n'; break;
                        case 'r': c = '\r'; break;
                        case 't': c = '\t'; break;
                        case 'u':
                            c = (char) int.Parse(s.Substring(at, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                            at += 4;
                            break;
                    }
                }
                text.Append(c);
            }
            at++;
            return text.ToString();
        }

        private static void Skip(string s, ref int at)
        {
            while (at < s.Length && char.IsWhiteSpace(s[at]))
            {
                at++;
            }
        }

        #endregion
    }
}
