using System;
using System.Collections.Generic;
using System.Globalization;

namespace ForzaStudio
{
    // Format checks of an FM4 .carbin against what the game reads (default.xex, docs/CARBIN_FROM_XEX.md) and what
    // every official FM4 file holds (calibrated on the 240 carbins of 20 DLC cars, 2026-11-07: no finding on any of
    // them). Only FM4-format files are checked (FH and FM3 models are converted first). Read-only.
    //   Error:   the game reads the file differently from what we meant (header walk, counts, indices)
    //   Warning: a value no FM4 file has (object versions, flags, morph words longer than 1)
    public static class CarbinValidator
    {
        public sealed class Finding
        {
            public bool Error;
            public string Where;
            public string Text;

            public override string ToString()
            {
                return (this.Error ? "ERROR " : "warning ") + this.Where + ": " + this.Text;
            }
        }

        // fileName: the entry name, used for the kind (_lod0, _cockpit, main; wheels and brakes by type).
        public static List<Finding> Check(CarbinModel model, string fileName)
        {
            List<Finding> found = new List<Finding>();
            if (model.IsHorizon || model.IsMotorsport3)
            {
                return found;
            }
            string name = (fileName ?? "").ToLowerInvariant();
            bool lod0 = name.EndsWith("_lod0.carbin");
            bool cockpit = name.EndsWith("_cockpit.carbin");
            bool main = model.Type == 2 && !lod0 && !cockpit;
            if (model.Type == 2)
            {
                CheckHeader(model, lod0 || cockpit, found);
                CheckGauges(model, found);
            }
            if (model.Trailer.Length != 24 || ReadU32(model.Trailer, 0) != 1 || ReadU32(model.Trailer, 8) != 1 || ReadU32(model.Trailer, 16) != 1)
            {
                Add(found, false, "trailer", "not the three empty containers (00000001 00000000 x 3) of every FM4 file");
            }
            foreach (CarbinModel.Section section in model.Sections)
            {
                CheckSection(model, section, main, found);
            }
            int perPass = model.DrawLists.Count / 2;
            if ((model.Type == 2 || model.Type == 3) && perPass > 0 && model.DrawLists.Count % 2 == 0)
            {
                for (int l = 0; l < model.DrawLists.Count; l++)
                {
                    foreach (uint entry in model.DrawLists[l])
                    {
                        int s = (int) (entry >> 16);
                        int k = (int) (entry & 0xFFFF);
                        if (s >= model.Sections.Count || k >= model.Sections[s].SubSections.Count)
                        {
                            continue;
                        }
                        string material = model.Sections[s].SubSections[k].Name;
                        if (CarbinModel.IsTransparentPassMaterial(material) != (l < perPass))
                        {
                            Add(found, true, model.Sections[s].Name, string.Format("{0} is in the {1} draw pass: the game does not draw it (Save fixes it)", material, (l < perPass) ? "see-through" : "opaque"));
                        }
                    }
                }
            }
            return found;
        }

        private static void CheckHeader(CarbinModel model, bool lodZero, List<Finding> found)
        {
            CarbinModel.HeaderLayout layout = CarbinModel.HeaderLayout.Walk(model.Head);
            if (layout == null)
            {
                Add(found, true, "header", string.Format(CultureInfo.InvariantCulture, "the field walk (emitters, driver controls, LODs, gauges...) does not end at the draw lists ({0} bytes)", model.Head.Length));
                return;
            }
            byte[] head = model.Head;
            Expect(found, "header", "ShaderVersion", ReadU32(head, CarbinModel.HeaderLayout.ShaderVersion), 16);
            Expect(found, "header", "Emitters container version", ReadU32(head, CarbinModel.HeaderLayout.EmittersContainer), 1);
            Expect(found, "header", "CDriverControls version", ReadU32(head, layout.DriverControls), 3);
            Expect(found, "header", "StartLOD", ReadU32(head, layout.StartLod), lodZero ? 0u : 1u);
            Expect(found, "header", "EndLOD", ReadU32(head, layout.StartLod + 4), lodZero ? 0u : 5u);
            Expect(found, "header", "word after the gauges' floats", ReadU32(head, layout.Word), 1);
            Expect(found, "header", "BoneMappingTable version", ReadU32(head, layout.BoneMappingTable), 1);
            Expect(found, "header", "BoneMappingTable count", ReadU32(head, layout.BoneMappingTable + 4), 0);
            if (lodZero && layout.EmitterCount != 0)
            {
                Add(found, false, "header", string.Format(CultureInfo.InvariantCulture, "{0} exhaust emitters in a lod0 / cockpit file (FM4 keeps them in the main file only)", layout.EmitterCount));
            }
            for (int i = 0; i < layout.EmitterCount; i++)
            {
                Expect(found, "header", "emitter " + i + " version", ReadU32(head, CarbinModel.HeaderLayout.Emitters + (i * CarbinModel.HeaderLayout.EmitterSize)), 2);
            }
        }

        // Analogue gauges: the header axis against the plane of each needle's three poses (a warning only: the
        // game draws the source data as it is, e.g. ACU_NSX_97's tilted speed axis).
        private static void CheckGauges(CarbinModel model, List<Finding> found)
        {
            CarGauges.Gauge[] gauges = CarGauges.Read(model.Head);
            if (gauges == null)
            {
                return;
            }
            foreach (CarGauges.Gauge gauge in gauges)
            {
                if (gauge.IsDefault)
                {
                    continue;
                }
                double[] min, mid, max;
                CarGauges.ReadPoses(model, gauge, out min, out mid, out max);
                string problem = CarGauges.AxisTiltProblem(gauge, min, mid, max);
                if (problem != null)
                {
                    Add(found, false, "gauge " + gauge.XmlName, problem);
                }
            }
        }

        private static void CheckSection(CarbinModel model, CarbinModel.Section section, bool main, List<Finding> found)
        {
            string where = section.Name;
            Expect(found, where, "CCarPartData version", section.PartDataVersion, 5);
            if (section.ClampedFloat != 1f)
            {
                Add(found, false, where, string.Format(CultureInfo.InvariantCulture, "first part float is {0} (1.0 on every FM4 section; FH2 0.1 was not drawn)", section.ClampedFloat));
            }
            Expect(found, where, "GlassParticles version", section.GlassParticlesVersion, 1);
            Expect(found, where, "glass Vertices container version", section.GlassVerticesVersion, 1);
            Expect(found, where, "glass Indices container version", section.GlassIndicesVersion, 1);
            if (section.GlassIndexCount % 3 != 0)
            {
                Add(found, true, where, "glass particle index count " + section.GlassIndexCount + " is not a multiple of 3");
            }
            if (section.GlassIndexCount > 0)
            {
                int[] indices = section.GlassIndices;
                int max = 0;
                foreach (int index in indices)
                {
                    max = Math.Max(max, index);
                }
                if (max >= section.GlassVertexCount)
                {
                    Add(found, true, where, string.Format(CultureInfo.InvariantCulture, "glass particle index {0} past the {1} glass vertices", max, section.GlassVertexCount));
                }
            }
            if (section.GlassVertexCount > 0 && !main)
            {
                Add(found, false, where, "glass particles outside the main file (FM4 has them only in the main carbin)");
            }
            if (section.Pre != null && section.Pre.Length == 4 && !section.PartFlag && section.HasTransparentMaterial)
            {
                Add(found, true, where, "see-through material in a part with flag 0: the game does not draw it (Save fixes it)");
            }
            Expect(found, where, "TSubModel version", section.SubModelVersion, 1);
            Expect(found, where, "VertexBuffer version", section.VertexBufferVersion, 2);
            Expect(found, where, "Mesh container version", section.MeshContainerVersion, 1);
            Expect(found, where, "VertexBuffer_LOD0 version", section.VertexBufferLod0Version, 2);
            Expect(found, where, "MorphTargets version", section.MorphTargetsVersion, 1);
            Expect(found, where, "Morph version", section.MorphVersion, 2);
            Expect(found, where, "Morph_LOD0 version", section.MorphLod0Version, 2);
            CheckMorph(model, section, found);
            foreach (CarbinModel.SubSection sub in section.SubSections)
            {
                CheckMesh(section, sub, found);
            }
        }

        private static void CheckMorph(CarbinModel model, CarbinModel.Section section, List<Finding> found)
        {
            string where = section.Name;
            int stride = (model.Type == 3) ? 16 : 4;
            CarbinModel.Table[] tables = { section.Morph, section.MorphLod0 };
            CarbinModel.VertexBuffer[] buffers = { section.Lod, section.Lod0 };
            string[] names = { "Morph", "Morph_LOD0" };
            for (int t = 0; t < 2; t++)
            {
                CarbinModel.Table table = tables[t];
                if (!section.HasMorphs)
                {
                    if (table.Count != 0)
                    {
                        Add(found, false, where, names[t] + " holds " + table.Count + " entries but HasMorphs is off (the game skips them)");
                    }
                    continue;
                }
                if (table.Count != buffers[t].Count)
                {
                    Add(found, true, where, string.Format(CultureInfo.InvariantCulture, "{0} has {1} entries for {2} vertices", names[t], table.Count, buffers[t].Count));
                    continue;
                }
                if (table.Count > 0 && table.Stride != stride)
                {
                    Add(found, true, where, string.Format(CultureInfo.InvariantCulture, "{0} stride {1} (FM4: {2})", names[t], table.Stride, stride));
                    continue;
                }
                if (stride != 4)
                {
                    continue;
                }
                int odd = 0;
                for (int i = 0; i + 4 <= table.Data.Length; i += 4)
                {
                    double length = MorphLength(ReadU32(table.Data, i));
                    if (length > 1.05)
                    {
                        odd++;
                    }
                }
                if (odd > 0)
                {
                    Add(found, false, where, string.Format(CultureInfo.InvariantCulture, "{0}: {1} of {2} morph words longer than 1 (FM4 words are 10_11_11 vectors of length 0..1; FH words copied as they are?)", names[t], odd, table.Count));
                }
            }
        }

        // FM4 morph word: Xenos 10_11_11 SNORM, x bits 0-10 / 1023, y 11-21 / 1023, z 22-31 / 511. On the 2.8 million
        // words of the DLC cars: 53% below 0.1 (mostly zero), 38% length 0.9-1.0, the rest spread over 0.1-0.9, never above 1.05.
        public static double MorphLength(uint word)
        {
            if (word == 0)
            {
                return 0;
            }
            double x = SignExtend(word, 11) / 1023.0;
            double y = SignExtend(word >> 11, 11) / 1023.0;
            double z = SignExtend(word >> 22, 10) / 511.0;
            return Math.Sqrt((x * x) + (y * y) + (z * z));
        }

        private static void CheckMesh(CarbinModel.Section section, CarbinModel.SubSection sub, List<Finding> found)
        {
            string where = section.Name + "/" + sub.Name + " (LOD " + sub.Lod + ")";
            Expect(found, where, "CCarMaterialData version", sub.MaterialDataVersion, 3);
            Expect(found, where, "CCarMeshBase version", sub.MeshBaseVersion, 2);
            Expect(found, where, "MaterialIndex", sub.MaterialIndex, 0);
            Expect(found, where, "UnpackingData version", sub.UnpackingDataVersion, 1);
            Expect(found, where, "IndexBuffer version", sub.IndexBufferVersion, 3);
            Expect(found, where, "meshFlags", sub.MeshFlags, 0);
            if (sub.PrimitiveType != CarbinModel.PrimitiveTriangleStrip && sub.PrimitiveType != CarbinModel.PrimitiveTriangleList)
            {
                Add(found, true, where, "PrimitiveType " + sub.PrimitiveType + " (FM4: 6 strip, once 4 list)");
            }
            float[] offset = sub.PositionOffset;
            float[] scale = sub.PositionScale;
            float[] uv = sub.UnpackingUvOffsetScale;
            bool identity = offset[0] == 0 && offset[1] == 0 && offset[2] == 0 && offset[3] == 0
                && scale[0] == 1 && scale[1] == 1 && scale[2] == 1 && scale[3] == 1
                && uv[0] == 0 && uv[1] == 0 && uv[2] == 1 && uv[3] == 1;
            if (!identity)
            {
                Add(found, false, where, "UnpackingData is not the identity (offset 0, scale 1, UV 0 0 1 1) of every FM4 mesh");
            }
            if (sub.IndexSize != 2 && sub.IndexSize != 4)
            {
                Add(found, true, where, "index size " + sub.IndexSize);
                return;
            }
            int vertices = (sub.Lod == 0) ? section.Lod0.Count : section.Lod.Count;
            int max = -1;
            foreach (int index in sub.Indices)
            {
                if (!CarbinModel.IsRestart(sub, index))
                {
                    max = Math.Max(max, index);
                }
            }
            if (max >= vertices)
            {
                Add(found, true, where, string.Format(CultureInfo.InvariantCulture, "index {0} past the {1} vertices of its buffer", max, vertices));
            }
            if (sub.PrimitiveType == CarbinModel.PrimitiveTriangleList && sub.Indices.Count % 3 != 0)
            {
                Add(found, true, where, "triangle list with " + sub.Indices.Count + " indices (not a multiple of 3)");
            }
        }

        private static void Expect(List<Finding> found, string where, string field, uint value, uint expected)
        {
            if (value != expected)
            {
                Add(found, false, where, string.Format(CultureInfo.InvariantCulture, "{0} is {1} (FM4: {2})", field, value, expected));
            }
        }

        private static void Add(List<Finding> found, bool error, string where, string text)
        {
            found.Add(new Finding { Error = error, Where = where, Text = text });
        }

        private static uint ReadU32(byte[] data, int offset)
        {
            return (uint) ((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);
        }

        private static int SignExtend(uint value, int bits)
        {
            int v = (int) (value & ((1u << bits) - 1));
            return (v >= (1 << (bits - 1))) ? v - (1 << bits) : v;
        }
    }
}
