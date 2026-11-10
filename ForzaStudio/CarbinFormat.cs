using System;

namespace ForzaStudio
{
    // Which game wrote a .carbin and what it holds, read from the file's own version numbers.
    //
    // A carbin is one serialized object tree in every Xbox 360 Forza: a small header (body, wheel or brake),
    // ModelSettings, then the model (draw order, parts, meshes). Each object starts with its version and the
    // reader branches on it, so the games differ only in these numbers and in the fields the versions add
    // (docs/CARBIN_ALL_GAMES.md). Checked on every carbin of the five extracted games:
    //
    //                        FM2        FM3      FM4    FH1 / FH2
    //   body header           2          1        2        5       (2+: mirror box, 3+: light flares; FM2: wheel lighting only)
    //   wheel header          1          2        3        3       (2: brake pivot, 3: tyre extents)
    //   brake header          1          1        1        1
    //   ModelSettings      713 / 714     13       16       17      (13: 4 gauges, 16: 15 gauges and bones)
    //   shader version        -          11       16       16
    //   part physics         713         2        5        5       (5: bone weights, damage morphs)
    //   mesh material         1          2        3        3       (3: four UV transforms)
    //   mesh                  3          2        2        2
    //   vertex buffer         2          2        2        3       (3: flags and sharing key)
    //   index buffer          3          3        3        4       (4: flags and sharing key)
    //   bytes per vertex      60      40/44/16    32       28
    //
    // The classes that read and write the files: CarbinModel (FM3, FM4, FH), Fm2CarbinModel (FM2).
    public enum CarbinFamily
    {
        Unknown,
        Motorsport2,
        Motorsport3,
        Motorsport4,
        Horizon         // FH1 and FH2 write the same versions everywhere
    }

    public enum CarbinKind
    {
        Unknown,
        Body,           // main, lod0 and cockpit files of a car
        Wheel,
        Brake           // calipers, rotors and Media\brakes
    }

    public class CarbinFormat
    {
        public CarbinFamily Family;
        public CarbinKind Kind;
        public uint HeaderVersion;          // the first word of the file
        public uint SettingsVersion;        // ModelSettings version
        public int SettingsOffset;          // where ModelSettings starts = the length of the header

        // Object versions a game writes (the table above).
        public class Versions
        {
            public uint BodyHeader;
            public uint WheelHeader;
            public uint BrakeHeader;
            public uint ModelSettings;
            public uint Shader;             // 0: the game has no such field
            public uint PartPhysics;
            public uint MeshMaterial;
            public uint Mesh;
            public uint VertexBuffer;
            public uint IndexBuffer;
            public int VertexStride;        // car parts; FM3 wheels 44, brakes 16
            public int Gauges;              // gauge records in ModelSettings
        }

        public static Versions VersionsOf(CarbinFamily family)
        {
            Versions v = new Versions();
            switch (family)
            {
                case CarbinFamily.Motorsport2:
                    v.BodyHeader = 2; v.WheelHeader = 1; v.BrakeHeader = 1; v.ModelSettings = 714; v.Shader = 0;
                    v.PartPhysics = 713; v.MeshMaterial = 1; v.Mesh = 3; v.VertexBuffer = 2; v.IndexBuffer = 3;
                    v.VertexStride = 60; v.Gauges = 0;
                    return v;
                case CarbinFamily.Motorsport3:
                    v.BodyHeader = 1; v.WheelHeader = 2; v.BrakeHeader = 1; v.ModelSettings = 13; v.Shader = 11;
                    v.PartPhysics = 2; v.MeshMaterial = 2; v.Mesh = 2; v.VertexBuffer = 2; v.IndexBuffer = 3;
                    v.VertexStride = 40; v.Gauges = 4;
                    return v;
                case CarbinFamily.Motorsport4:
                    v.BodyHeader = 2; v.WheelHeader = 3; v.BrakeHeader = 1; v.ModelSettings = 16; v.Shader = 16;
                    v.PartPhysics = 5; v.MeshMaterial = 3; v.Mesh = 2; v.VertexBuffer = 2; v.IndexBuffer = 3;
                    v.VertexStride = 32; v.Gauges = 15;
                    return v;
                case CarbinFamily.Horizon:
                    v.BodyHeader = 5; v.WheelHeader = 3; v.BrakeHeader = 1; v.ModelSettings = 17; v.Shader = 16;
                    v.PartPhysics = 5; v.MeshMaterial = 3; v.Mesh = 2; v.VertexBuffer = 3; v.IndexBuffer = 4;
                    v.VertexStride = 28; v.Gauges = 15;
                    return v;
            }
            throw new ArgumentException("No carbin versions for " + family + ".");
        }

        private static uint U32(byte[] d, int o)
        {
            return (o < 0 || o + 4 > d.Length) ? 0xFFFFFFFF : (uint) ((d[o] << 24) | (d[o + 1] << 16) | (d[o + 2] << 8) | d[o + 3]);
        }

        private static bool IsSettingsVersion(uint v)
        {
            return v >= 9 && v <= 17;
        }

        private static bool IsMotorsport2Version(uint v)
        {
            return v == 713 || v == 714;
        }

        // Never throws: a file that is not a known carbin comes back with Family and Kind Unknown.
        public static CarbinFormat Identify(byte[] file)
        {
            CarbinFormat f = new CarbinFormat();
            if (file == null || file.Length < 8)
            {
                return f;
            }
            uint first = U32(file, 0);
            f.HeaderVersion = first;

            // FM2: ModelSettings 713 / 714 right after the header (a body header is the wheel lighting only).
            // Three FM2 wheels are in a later form (ModelSettings 3 or 4) that no class here reads.
            if (first == 1 && (IsMotorsport2Version(U32(file, 4)) || ((U32(file, 4) == 3 || U32(file, 4) == 4) && U32(file, 8) == 1)))
            {
                f.Family = CarbinFamily.Motorsport2;
                f.SettingsOffset = 4;
                f.SettingsVersion = U32(file, 4);
                // Brakes and wheels share the header: a wheel has 5 LODs, a brake 1 (or none).
                int at = 12;
                uint emitters = U32(file, at);
                at += 4 + ((int) Math.Min(emitters, 64u) * 140) + 4 + 96 + 8;
                if (IsMotorsport2Version(f.SettingsVersion))
                {
                    f.Kind = (U32(file, at) > 1 && U32(file, at) <= 16) ? CarbinKind.Wheel : CarbinKind.Brake;
                }
                return f;
            }
            if (first == 2 && IsMotorsport2Version(U32(file, 0x104)))
            {
                f.Family = CarbinFamily.Motorsport2;
                f.Kind = CarbinKind.Body;
                f.SettingsOffset = 0x104;
                f.SettingsVersion = U32(file, 0x104);
                return f;
            }

            // FM3, FM4, FH: wheel header 3 (72 bytes) or 2 (24 bytes), brake header 1 (4 bytes), else a body.
            if (first == 3 && IsSettingsVersion(U32(file, 72)))
            {
                f.Kind = CarbinKind.Wheel;
                f.SettingsOffset = 72;
            }
            else if (first == 2 && IsSettingsVersion(U32(file, 24)))
            {
                f.Kind = CarbinKind.Wheel;
                f.SettingsOffset = 24;
            }
            else if (first == 1 && IsSettingsVersion(U32(file, 4)))
            {
                f.Kind = CarbinKind.Brake;
                f.SettingsOffset = 4;
            }
            else
            {
                // Body: wheel lighting (4 x 16 floats), from version 2 the mirror box (5 vec4), from 3 the light flares.
                int at = 4 + 256;
                if (first > 1)
                {
                    at += 80;
                }
                if (first >= 3)
                {
                    uint flares = U32(file, at);
                    if (flares > 4096)
                    {
                        return f;
                    }
                    at += 4 + ((int) flares * (27 + ((first >= 4) ? 3 : 0) + ((first >= 5) ? 2 : 0)));
                }
                if (first == 0 || first > 16 || !IsSettingsVersion(U32(file, at)))
                {
                    return f;
                }
                f.Kind = CarbinKind.Body;
                f.SettingsOffset = at;
            }
            f.SettingsVersion = U32(file, f.SettingsOffset);
            f.Family = (f.SettingsVersion == 17) ? CarbinFamily.Horizon
                : (f.SettingsVersion == 16) ? CarbinFamily.Motorsport4
                : (f.SettingsVersion == 13) ? CarbinFamily.Motorsport3
                : CarbinFamily.Unknown;
            return f;
        }

        public override string ToString()
        {
            return this.Family + " " + this.Kind + " (header " + this.HeaderVersion + ", settings " + this.SettingsVersion + ")";
        }
    }
}
