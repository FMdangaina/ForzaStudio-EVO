namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;

    // A WHOLE car (or wheel) of another game put into an FM2 model: the model step of a port INTO FM2 (opened 2026-11-22,
    // study and trials in E:\DEV\fm4dev\PortToFM2). FM2 has one model file per car, LODs 1-5, so "the whole car" = every
    // part of the source's main model that has a LOD 1-5 mesh.
    //
    // This model is the template (an FM2 car close to the source: its header - wheel lighting, emitters, driver anchors -
    // and, for the parts both cars have, the place in the draw order, the physics and the glass shards stay). Each source
    // part is made by FromMotorsport4, the conversion Copy Part uses (checked in game 2026-11-21), so a ported car and a
    // copied part are the same data.
    //
    // Baked light: Copy Part computes a part against the car it lands on. Here no part of the template is left, so the
    // baker (light sent back, ground) is fitted on the template BEFORE it is replaced and every part is computed
    // against the finished car afterwards, stock parts first.
    public partial class Fm2CarbinModel
    {
        // The source parts the last ReplaceWholeModel could not fit (FM2's 65536 vertices per shared buffer).
        public List<string> LeftOutParts = new List<string>();

        // An upgrade variant: a part whose name ends with a lowercase letter other than a, or with "race" (wingrace,
        // bumperFb, skirtLc); the stock parts are the others (body, bumperFa, glassF...).
        private static bool IsUpgradeVariant(string name)
        {
            if (name.EndsWith("race", StringComparison.Ordinal) && name.Length > 4)
            {
                return true;
            }
            char last = name[name.Length - 1];
            if (name.Length < 2 || !char.IsLower(last) || last == 'a')
            {
                return false;
            }
            return IsKitName(name);
        }

        // the names of the body kit groups (FM2 cars: bumperF / bumperR / skirtL / skirtR / hood / wing / exhaust... + letter)
        private static bool IsKitName(string name)
        {
            string group = name.Substring(0, name.Length - 1);
            foreach (string kit in new string[] { "bumperF", "bumperR", "skirtL", "skirtR", "hood", "wing", "exhaust", "exhaustL", "exhaustR", "trunk", "bumperFrameF", "bumperFrameR" })
            {
                if (string.Equals(group, kit, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        // One source part as ReplaceWholeModel would write it (the port window's plan: nothing is changed here).
        public sealed class PlanPart
        {
            public string Name;
            public int Vertices;
            public bool Variant;        // an upgrade variant: the user may leave it out when a buffer is full
            public bool SeeThrough;     // first shared buffer (a part with a see-through material), else the second
            public Dictionary<string, List<uint>> Materials = new Dictionary<string, List<uint>>(StringComparer.Ordinal);   // the source's material -> the LODs it is on

            // The buffer of the part with these material choices (source material -> FM2 material; null: none).
            public void Choose(Dictionary<string, string> materials)
            {
                this.SeeThrough = false;
                foreach (string material in this.Materials.Keys)
                {
                    string chosen;
                    this.SeeThrough |= IsSeeThrough((materials != null && materials.TryGetValue(material, out chosen)) ? chosen : material);
                }
            }
        }

        // The user's material choices for a whole-car port (the port window's Materials grid): a source material ->
        // the FM2 material its meshes get. Read by ReplaceWholeModel and PortPlan as each part is converted, before the
        // part goes into its buffer (a see-through material decides the buffer). Null or empty: every name stays.
        public Dictionary<string, string> PortMaterials;

        // Returns how many meshes of the converted part were renamed.
        private int ChooseMaterials(Part made)
        {
            int renamed = 0;
            if (this.PortMaterials != null)
            {
                foreach (Mesh mesh in made.Meshes)
                {
                    string chosen;
                    if (this.PortMaterials.TryGetValue(mesh.MaterialName, out chosen) && chosen != mesh.MaterialName)
                    {
                        mesh.MaterialName = chosen;
                        renamed++;
                    }
                }
            }
            return renamed;
        }

        // The parts ReplaceWholeModel(source, horizon, leaveOut, ...) would port, stock parts first, each with the
        // vertices it takes in its shared buffer (65536 each). This model (the template) is not changed.
        public List<PlanPart> PortPlan(CarbinModel source, bool horizon, Predicate<string> leaveOut)
        {
            List<string> names = new List<string>();
            foreach (CarbinModel.Section section in source.Sections)
            {
                bool any = false;
                foreach (CarbinModel.SubSection sub in section.SubSections)
                {
                    any |= sub.Lod != 0 && sub.Indices.Count > 0 && sub.Stripped == null;
                }
                if (!any || (leaveOut != null && leaveOut(section.Name))
                    || names.Exists(delegate(string n) { return string.Equals(n, section.Name, StringComparison.OrdinalIgnoreCase); }))
                {
                    continue;
                }
                names.Add(section.Name);
            }
            List<string> ordered = names.FindAll(delegate(string n) { return !IsUpgradeVariant(n); });
            ordered.AddRange(names.FindAll(IsUpgradeVariant));
            List<PlanPart> plan = new List<PlanPart>();
            List<string> notes = new List<string>();
            foreach (string name in ordered)
            {
                byte[] vertices;
                Part made = this.FromMotorsport4(source, name, name, horizon, notes, out vertices, null, null, false);
                PlanPart part = new PlanPart();
                part.Name = name;
                part.Vertices = vertices.Length / VertexStride;
                part.Variant = IsUpgradeVariant(name);
                foreach (Mesh mesh in made.Meshes)
                {
                    List<uint> lods;
                    if (!part.Materials.TryGetValue(mesh.MaterialName, out lods))
                    {
                        part.Materials[mesh.MaterialName] = lods = new List<uint>();
                    }
                    if (!lods.Contains(mesh.Lod))
                    {
                        lods.Add(mesh.Lod);
                    }
                }
                part.Choose(this.PortMaterials);
                plan.Add(part);
            }
            return plan;
        }

        // source: the source car's main model in FM4 form (PartSource.ForMotorsport2). horizon: FH damage words are not
        // FM4 words, the nearest vertex of this car gives them. leaveOut: source parts that do not go into an FM2 car
        // (null: none). light: compute the baked light (false: the nearest template vertex's values stay, wheels).
        // Returns what was done, line by line.
        public List<string> ReplaceWholeModel(CarbinModel source, bool horizon, Predicate<string> leaveOut, bool light)
        {
            List<string> log = new List<string>();
            List<string> names = new List<string>();
            List<string> left = new List<string>();
            List<string> twice = new List<string>();
            foreach (CarbinModel.Section section in source.Sections)
            {
                bool any = false;
                foreach (CarbinModel.SubSection sub in section.SubSections)
                {
                    any |= sub.Lod != 0 && sub.Indices.Count > 0 && sub.Stripped == null;
                }
                if (!any)
                {
                    continue;
                }
                if (leaveOut != null && leaveOut(section.Name))
                {
                    left.Add(section.Name);
                    continue;
                }
                // FM4's R33 holds glassLF and a second glasslF (the same 39 vertices): FM2 part names are found without
                // regard to case, the first one stands.
                if (names.Exists(delegate(string n) { return string.Equals(n, section.Name, StringComparison.OrdinalIgnoreCase); }))
                {
                    twice.Add(section.Name);
                    continue;
                }
                names.Add(section.Name);
            }
            if (names.Count == 0)
            {
                throw new InvalidOperationException("The source model has no part with LODs 1-5.");
            }
            if (left.Count > 0)
            {
                log.Add("left out (no FM2 car has them): " + string.Join(", ", left.ToArray()));
            }
            if (twice.Count > 0)
            {
                log.Add("left out (the source has an earlier part of the same name): " + string.Join(", ", twice.ToArray()));
            }

            // the template's baker, as numbers that do not depend on its size
            double[] baker = null;
            if (light)
            {
                LightSetup();
                LightScene before = this.BuildLightScene(null, null, null, null, null);
                LightModel fitted = this.CalibrateLighting();
                baker = new double[] { fitted.Rho, fitted.GroundY, before.Min.X - fitted.MinX, fitted.MaxX - before.Max.X, before.Min.Z - fitted.MinZ, fitted.MaxZ - before.Max.Z };
                log.Add(string.Format(CultureInfo.InvariantCulture, "baked light: the template's baker (light sent back {0:0.00}, ground {1})", fitted.Rho,
                    (fitted.GroundY < -10) ? "none" : string.Format(CultureInfo.InvariantCulture, "at {0:0.00} m", fitted.GroundY)));
            }

            List<string> removed = new List<string>();
            foreach (Part part in this.Parts.ToArray())
            {
                string name = part.Name;
                if (!names.Exists(delegate(string n) { return string.Equals(n, name, StringComparison.OrdinalIgnoreCase); }))
                {
                    this.RemovePart(name);
                    removed.Add(name);
                }
            }
            // Stock parts first, upgrade variants after (letters other than a, race): when the vertices do not fit FM2's
            // 65536 per shared buffer, the variants are the ones left out. (Opaque parts in the see-through buffer, tried
            // 2026-11-22 with the R33's bumperFrace: drawn dark in game. FirstVertex / LastVertex are 16-bit: a hard limit.)
            List<string> ordered = names.FindAll(delegate(string n) { return !IsUpgradeVariant(n); });
            ordered.AddRange(names.FindAll(IsUpgradeVariant));
            names = ordered;
            if (removed.Count > 0)
            {
                log.Add("template parts the source lacks, removed: " + string.Join(", ", removed.ToArray()));
            }

            List<string> notes = new List<string>();
            List<string> added = new List<string>();
            List<string> full = new List<string>();
            int replacedCount = 0;
            int renamedMeshes = 0;
            foreach (string name in names)
            {
                Part replaced = this.Find(name);
                byte[] vertices;
                Part made = this.FromMotorsport4(source, name, (replaced != null) ? replaced.Name : name, horizon, notes, out vertices,
                    (replaced != null) ? replaced.Name : null, (replaced != null) ? replaced.Name : null, false);
                renamedMeshes += this.ChooseMaterials(made);
                try
                {
                    if (replaced != null)
                    {
                        // the template's part only gives the place in the draw order: nothing of its data stays
                        this.ReplacePart(replaced.Name, made, vertices);
                        replacedCount++;
                    }
                    else
                    {
                        this.AddPart(made, vertices);
                        added.Add(name);
                    }
                    this.PhysicsAndGlassFromSource(source.Find(name), made);
                }
                catch (InvalidOperationException e)
                {
                    if (e.Message.IndexOf("65536", StringComparison.Ordinal) < 0)
                    {
                        throw;
                    }
                    // the edit is undone (Atomic); the template's part of that name must not stay in its place
                    if (replaced != null)
                    {
                        this.RemovePart(replaced.Name);
                    }
                    full.Add(string.Format(CultureInfo.InvariantCulture, "{0} ({1} vertices)", name, vertices.Length / VertexStride));
                }
            }
            this.LeftOutParts = new List<string>();
            foreach (string f in full)
            {
                this.LeftOutParts.Add(f.Substring(0, f.IndexOf(' ')));
            }
            log.Add(string.Format("{0} parts: {1} in the draw-order place of the template's part of the same name, {2} new{3}; physics (flag, centre of mass, detach) and glass shards from the source, none from the template", names.Count - full.Count, replacedCount, added.Count,
                (added.Count > 0) ? ": " + string.Join(", ", added.ToArray()) : ""));
            if (this.PortMaterials != null && this.PortMaterials.Count > 0)
            {
                List<string> choices = new List<string>();
                foreach (KeyValuePair<string, string> pair in this.PortMaterials)
                {
                    choices.Add(pair.Key + " -> " + pair.Value);
                }
                choices.Sort(StringComparer.OrdinalIgnoreCase);
                log.Add(string.Format("materials renamed by the user's choice ({0} meshes): {1}", renamedMeshes, string.Join(", ", choices.ToArray())));
            }
            if (this.HeaderVersion == 2)
            {
                log.Add(this.HeaderFromSource(source));
            }
            foreach (string note in notes)
            {
                log.Add(note);
            }
            if (full.Count > 0)
            {
                log.Add("WARNING: left out, FM2's 65536 vertices of a shared buffer are full (upgrade variants go first): " + string.Join(", ", full.ToArray()));
            }
            log.Add(string.Format("vertices: {0} in the first shared buffer, {1} in the second (65536 each)", this.TransparentVertices.Count, this.OpaqueVertices.Count));

            if (light)
            {
                this.noStockWithoutA = true;
                LightScene after =this.BuildLightScene(null, null, null, null, null);
                LightModel model = MakeLightModel(after, baker);
                bool[] stock = this.StockParts();
                int lit = 0;
                foreach (bool stockPass in new bool[] { true, false })
                {
                    for (int p = 0; p < this.Parts.Count; p++)
                    {
                        Part part = this.Parts[p];
                        if (stock[p] != stockPass || part.Meshes.Count == 0)
                        {
                            continue;
                        }
                        byte[] local = this.PartVertices(part);
                        lit += this.ComputeLighting(part, local, part.Name, model);
                        Buffer.BlockCopy(local, 0, this.BufferOf(part).Data, part.FirstVertex * VertexStride, local.Length);
                    }
                }
                this.noStockWithoutA = false;
                log.Add(string.Format("baked light computed on the finished car: {0} vertices (stock parts first, each upgrade variant in the place of its group's stock part)", lit));
            }
            return log;
        }

        // User rule 2026-11-22: "dalla base va tolto TUTTO" - nothing of the template car's data stays on a ported part.
        // FM2 Physics (20 bytes) = FM4 CCarPartData's first five floats (PortToFM2\basecmp, physrule, 60 FM2 cars):
        //   float 0  1 on the parts that come off / get hit (bumpers, hood, trunk, mirrors, skirts, wings, exhausts), 0 on
        //            the rest (body, glass, lights, interior, seats, frames): FM2's majority per part group. FM4 holds 1.0
        //            on every part, so it is not taken from the source.
        //   1-3      centre of mass = the mean of ALL the part's vertices minus its origin (0.0000 off on 1867 FM2 parts).
        //   4        detach value: the source's (FM4 0 / 0.2, FM2 0 .. 0.9 on mirrors, wings, exhausts).
        // Glass shards: FM2 and FM4 keep them the same way (car space, vec4 w = 1, u16 triangles, the same box; FM4's are
        // finer), so the source's own.
        private void PhysicsAndGlassFromSource(CarbinModel.Section section, Part made)
        {
            byte[] physics = new byte[20];
            PutFloat(physics, 0, HitPart(made.Name) ? 1f : 0f);
            double[] positions = this.Positions(made);
            int count = positions.Length / 3;
            for (int k = 0; k < 3; k++)
            {
                double sum = 0;
                for (int i = 0; i < count; i++)
                {
                    sum += positions[(i * 3) + k];
                }
                PutFloat(physics, 4 + (k * 4), (count > 0) ? (float) ((sum / count) - made.Bounds[k]) : 0f);
            }
            PutFloat(physics, 16, (section != null) ? section.DetachFloat : 0f);
            made.Physics = physics;

            float[] shards = (section != null) ? section.GlassVertices : new float[0];
            int[] triangles = (section != null) ? section.GlassIndices : new int[0];
            made.GlassVertices = new byte[shards.Length * 4];
            for (int i = 0; i < shards.Length; i++)
            {
                PutFloat(made.GlassVertices, i * 4, shards[i]);
            }
            made.GlassIndices = new byte[triangles.Length * 2];
            for (int i = 0; i < triangles.Length; i++)
            {
                made.GlassIndices[i * 2] = (byte) (triangles[i] >> 8);
                made.GlassIndices[(i * 2) + 1] = (byte) triangles[i];
            }
        }

        private static bool HitPart(string name)
        {
            string n = name.ToLowerInvariant();
            if (n.StartsWith("bumperframe"))
            {
                return false;
            }
            foreach (string group in new string[] { "bumperf", "bumperr", "exhaust", "hood", "mirror", "skirt", "trunk", "wing" })
            {
                if (n.StartsWith(group))
                {
                    return true;
                }
            }
            return false;
        }

        private static void PutFloat(byte[] data, int o, float value)
        {
            byte[] b = BitConverter.GetBytes(value);
            data[o] = b[3];
            data[o + 1] = b[2];
            data[o + 2] = b[1];
            data[o + 3] = b[0];
        }

        // Exhaust emitters and driver anchors from the source's header (FM4 CBaseCarModelData: CCarEmitterData 140 bytes,
        // the FM2 record; CDriverControls vectors 0-5 = FM2's six anchors, basecmp 10 cars: emitters equal, anchors equal
        // or the two games' own seats). An emitter's parent part is found again by name. The wheel lighting: FM2's mean
        // (Fm2CarbinModelWheelLight.cs), never the template's.
        private string HeaderFromSource(CarbinModel source)
        {
            this.SetMeanWheelLighting();
            CarbinModel.HeaderLayout layout = (source.Head != null) ? CarbinModel.HeaderLayout.Walk(source.Head) : null;
            if (layout == null)
            {
                return "WARNING header: the source's emitters and driver anchors are not read; the template's stay (wheel lighting: FM2's mean)";
            }
            List<byte[]> emitters = new List<byte[]>();
            for (int i = 0; i < layout.EmitterCount; i++)
            {
                byte[] e = new byte[140];
                Buffer.BlockCopy(source.Head, CarbinModel.HeaderLayout.Emitters + (i * 140), e, 0, 140);
                int parent = (e[8] << 24) | (e[9] << 16) | (e[10] << 8) | e[11];
                int ours = -1;
                if (parent >= 0 && parent < source.Sections.Count)
                {
                    string name = source.Sections[parent].Name;
                    ours = this.Parts.FindIndex(delegate(Part p) { return string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase); });
                }
                e[8] = (byte) (ours >> 24);
                e[9] = (byte) (ours >> 16);
                e[10] = (byte) (ours >> 8);
                e[11] = (byte) ours;
                emitters.Add(e);
            }
            this.Emitters = emitters;
            Buffer.BlockCopy(source.Head, layout.DriverVector(0), this.DriverAnchors, 0, 96);
            return string.Format(CultureInfo.InvariantCulture, "header: {0} exhaust emitters and the driver anchors from the source; wheel lighting = FM2's mean over its cars", emitters.Count);
        }

        // 2026-11-22 (R33 wheel in game: odd sidewall). FM2's 'tire' material samples the shared tire texture
        // (Media\cars\shared\tireA*.xds, tireSampler) laid out as: tread V 0..0.44, sidewall facing -X V 0.51..0.72,
        // sidewall facing +X V 0.75..0.97, U = repeats around the tire (25 on the R34). FM4 / FM3 / FH tires carry the UVs
        // of their own tire textures (FM4: each sidewall in two halves of the circle, one per V band), so in FM2 half of a
        // sidewall read the other side's band and one tread edge read the sidewall. Every tire vertex of this (converted)
        // model takes the template tire's V for its place - sidewall: its side and radius fraction, tread: its width
        // fraction - and U is scaled to the template's repeats. Positions, normals and baked light stay.
        public string FitTireUvs(Fm2CarbinModel template)
        {
            List<TireVertex> theirs = template.TireVertices(true);
            List<TireVertex> ours = this.TireVertices(false);
            if (theirs.Count == 0 || ours.Count == 0)
            {
                return "tire UVs: no 'tire' mesh in the template or in the ported wheel, UVs as converted";
            }
            double uScale = URange(theirs) / Math.Max(1e-6, URange(ours));
            int done = 0;
            for (int group = 0; group < 3; group++)
            {
                List<KeyValuePair<double, double>> profile = Profile(theirs, group);
                if (profile.Count == 0)
                {
                    continue;
                }
                foreach (TireVertex t in ours)
                {
                    if (t.Group != group)
                    {
                        continue;
                    }
                    byte[] data = this.BufferOf(t.Part).Data;
                    int o = this.VertexOffset(t.Part, t.Index) + VertexUv0;
                    WriteUv(data, o, t.U * uScale);
                    WriteUv(data, o + 2, Interpolate(profile, t.Key));
                    done++;
                }
            }
            return string.Format(CultureInfo.InvariantCulture, "tire UVs: {0} of {1} vertices on FM2's tire texture layout (the template tire's V for each place, U x {2:0.###} = its repeats)", done, ours.Count, uScale);
        }

        private sealed class TireVertex
        {
            public Part Part;
            public int Index;
            public int Group;       // 0 tread, 1 sidewall facing -X, 2 sidewall facing +X
            public double Key;      // tread: width fraction (x); sidewall: radius fraction
            public double U;
            public double V;
        }

        // The vertices of the 'tire' meshes (LOD 1 only, or every LOD) that no other material uses, grouped and placed.
        private List<TireVertex> TireVertices(bool lod1Only)
        {
            List<TireVertex> all = new List<TireVertex>();
            foreach (Part part in this.Parts)
            {
                int count = part.VertexCount;
                HashSet<int> tire = new HashSet<int>();
                HashSet<int> other = new HashSet<int>();
                foreach (Mesh mesh in part.Meshes)
                {
                    bool isTire = string.Equals(mesh.MaterialName, "tire", StringComparison.OrdinalIgnoreCase);
                    if (isTire && lod1Only && mesh.Lod != 1)
                    {
                        continue;
                    }
                    foreach (int i in mesh.Indices)
                    {
                        if (i >= 0 && i < count)
                        {
                            (isTire ? tire : other).Add(i);
                        }
                    }
                }
                tire.ExceptWith(other);
                if (tire.Count == 0)
                {
                    continue;
                }
                byte[] data = this.BufferOf(part).Data;
                double[] positions = this.Positions(part);
                double minY = double.MaxValue, maxY = double.MinValue, minZ = double.MaxValue, maxZ = double.MinValue;
                foreach (int i in tire)
                {
                    minY = Math.Min(minY, positions[(i * 3) + 1]);
                    maxY = Math.Max(maxY, positions[(i * 3) + 1]);
                    minZ = Math.Min(minZ, positions[(i * 3) + 2]);
                    maxZ = Math.Max(maxZ, positions[(i * 3) + 2]);
                }
                double cy = (minY + maxY) / 2, cz = (minZ + maxZ) / 2;
                List<TireVertex> these = new List<TireVertex>();
                foreach (int i in tire)
                {
                    int o = this.VertexOffset(part, i);
                    float nx = CarbinModel.ReadPacked111110(data, o + VertexNormal).X;
                    double dy = positions[(i * 3) + 1] - cy, dz = positions[(i * 3) + 2] - cz;
                    TireVertex t = new TireVertex();
                    t.Part = part;
                    t.Index = i;
                    t.Group = (Math.Abs(nx) <= 0.7f) ? 0 : ((nx < 0) ? 1 : 2);
                    t.Key = (t.Group == 0) ? positions[i * 3] : Math.Sqrt((dy * dy) + (dz * dz));
                    t.U = ReadUv(data, o + VertexUv0);
                    t.V = ReadUv(data, o + VertexUv0 + 2);
                    these.Add(t);
                }
                for (int group = 0; group < 3; group++)
                {
                    double lo = double.MaxValue, hi = double.MinValue;
                    foreach (TireVertex t in these)
                    {
                        if (t.Group == group)
                        {
                            lo = Math.Min(lo, t.Key);
                            hi = Math.Max(hi, t.Key);
                        }
                    }
                    foreach (TireVertex t in these)
                    {
                        if (t.Group == group)
                        {
                            t.Key = (hi - lo > 1e-6) ? (t.Key - lo) / (hi - lo) : 0.5;
                        }
                    }
                }
                all.AddRange(these);
            }
            return all;
        }

        private static double URange(List<TireVertex> vertices)
        {
            double lo = double.MaxValue, hi = double.MinValue;
            foreach (TireVertex t in vertices)
            {
                lo = Math.Min(lo, t.U);
                hi = Math.Max(hi, t.U);
            }
            return hi - lo;
        }

        // (place, mean V) of one group, sorted by place; places within 0.005 count as one ring.
        private static List<KeyValuePair<double, double>> Profile(List<TireVertex> vertices, int group)
        {
            SortedDictionary<int, double[]> rings = new SortedDictionary<int, double[]>();
            foreach (TireVertex t in vertices)
            {
                if (t.Group != group)
                {
                    continue;
                }
                int key = (int) Math.Round(t.Key / 0.005);
                double[] sum;
                if (!rings.TryGetValue(key, out sum))
                {
                    rings[key] = sum = new double[3];
                }
                sum[0] += t.Key;
                sum[1] += t.V;
                sum[2]++;
            }
            List<KeyValuePair<double, double>> profile = new List<KeyValuePair<double, double>>();
            foreach (double[] sum in rings.Values)
            {
                profile.Add(new KeyValuePair<double, double>(sum[0] / sum[2], sum[1] / sum[2]));
            }
            return profile;
        }

        private static double Interpolate(List<KeyValuePair<double, double>> profile, double key)
        {
            if (key <= profile[0].Key)
            {
                return profile[0].Value;
            }
            for (int i = 1; i < profile.Count; i++)
            {
                if (key <= profile[i].Key)
                {
                    double f = (key - profile[i - 1].Key) / Math.Max(1e-9, profile[i].Key - profile[i - 1].Key);
                    return profile[i - 1].Value + ((profile[i].Value - profile[i - 1].Value) * f);
                }
            }
            return profile[profile.Count - 1].Value;
        }

        private static double ReadUv(byte[] data, int o)
        {
            return (((data[o] << 8) | data[o + 1]) - 32768) / 1310.72;
        }

        private static void WriteUv(byte[] data, int o, double value)
        {
            int raw = Math.Max(0, Math.Min(65535, (int) Math.Round((value * 1310.72) + 32768)));
            data[o] = (byte) (raw >> 8);
            data[o + 1] = (byte) raw;
        }
    }
}
