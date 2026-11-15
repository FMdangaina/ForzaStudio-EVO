namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;

    // The material (mesh) names of a .carbin of any game whose names the editor can rewrite: FM4 and FH through
    // CarbinModel, FM3 through CarbinModel(data, true), FM2 through Fm2CarbinModel. Part p, mesh m follow the
    // order of the model tree (ForzaCarBin: ForzaMesh.SectionIndex / SubIndex). Checked on every stock car file
    // (fm4dev\ST01\rt.exe, 2026-11-13): FM3 3810 of 3810 and FM2 716 of 718 have the tree's order and names, are
    // written back byte for byte, and a renamed material changes the name bytes only. The other two FM2 files
    // (for_mustanggt_05, por_550_56) do not open in the tree.
    // FM2 and FM3 models can have their material names changed only (geometry edits need the FM4 / FH layout); the
    // draw pass, the part's see-through flag and, in FM2, the shared buffer of the part follow the new names (ToBytes).
    public sealed class MaterialSlots
    {
        private readonly CarbinModel model;      // FM3, FM4, FH
        private readonly Fm2CarbinModel fm2;     // FM2
        private readonly ShaderProfile game;

        // Throws NotSupportedException for a file none of the readers takes.
        public MaterialSlots(byte[] data)
        {
            if (Fm2CarbinModel.CanRead(data))
            {
                this.fm2 = new Fm2CarbinModel(data);
                this.game = ShaderProfile.Fm2;
            }
            else if (CarbinModel.IsMotorsport3File(data))
            {
                this.model = new CarbinModel(data, true);
                this.game = ShaderProfile.Fm3;
            }
            else
            {
                this.model = new CarbinModel(data);
                this.game = this.model.IsHorizon ? ShaderProfile.Horizon : ShaderProfile.Fm4;
            }
        }

        public ShaderProfile Game
        {
            get { return this.game; }
        }

        public int PartCount
        {
            get { return (this.fm2 != null) ? this.fm2.Parts.Count : this.model.Sections.Count; }
        }

        public string PartName(int part)
        {
            return (this.fm2 != null) ? this.fm2.Parts[part].Name : this.model.Sections[part].Name;
        }

        public int MeshCount(int part)
        {
            return (this.fm2 != null) ? this.fm2.Parts[part].Meshes.Count : this.model.Sections[part].SubSections.Count;
        }

        public string Material(int part, int mesh)
        {
            return (this.fm2 != null) ? this.fm2.Parts[part].Meshes[mesh].MaterialName : this.model.Sections[part].SubSections[mesh].Name;
        }

        public void SetMaterial(int part, int mesh, string name)
        {
            if (this.fm2 != null)
            {
                this.fm2.Parts[part].Meshes[mesh].MaterialName = name;
            }
            else
            {
                this.model.Sections[part].SubSections[mesh].Name = name;
            }
        }

        // The materials the stock FM3 models draw in the see-through pass, and only there (fm4dev\ST01\passscan.exe, 3810
        // car + wheel files, 2026-11-13, 0 exceptions). FM3 has no part flag byte before the vertex buffer as FM4 (Pre);
        // its flag is the first byte of the word CarbinModel calls VbTest1: 01 00 00 00 on the 9583 parts with see-through
        // meshes, 0 on the 18070 without, 0 exceptions.
        public static readonly string[] Fm3SeeThroughMaterials = new string[]
        {
            "window", "glass", "red_glass", "detail_glass_clear", "detail_glass_red", "detail_glass_amber", "blur_rim", "chrome_blur_rim",
        };

        private const uint Fm3SeeThroughFlag = 0x01000000;   // VbTest1 as CarbinModel reads it

        private static bool IsFm3SeeThrough(string material)
        {
            return material != null && Array.IndexOf(Fm3SeeThroughMaterials, material.ToLowerInvariant()) >= 0;
        }

        // What the last ToBytes changed besides the names (draw pass, part flag, FM2 buffer), for the report.
        public readonly List<string> Fixes = new List<string>();

        // FM2 / FM3: first every mesh into the draw pass of its material and every part flag as its meshes say, as the
        // stock files have them (a see-through mesh in the opaque pass, or in a part with flag 0, is not drawn in FM4;
        // the same rule holds on every FM2 / FM3 stock file). FM4 / FH: CarbinModel.ToBytes does it (FixDrawListPasses,
        // FixTransparentPartFlags), and skips FM3 files.
        public byte[] ToBytes()
        {
            this.Fixes.Clear();
            if (this.fm2 != null)
            {
                this.Fixes.AddRange(this.fm2.FixSeeThrough());
                return this.fm2.ToBytes();
            }
            if (this.game == ShaderProfile.Fm3)
            {
                this.FixFm3SeeThrough();
            }
            return this.model.ToBytes();
        }

        private void FixFm3SeeThrough()
        {
            int half = this.model.DrawLists.Count / 2;
            if (half >= 1 && this.model.DrawLists.Count % 2 == 0)
            {
                List<List<uint>> lists = new List<List<uint>>();
                foreach (uint[] list in this.model.DrawLists)
                {
                    lists.Add(new List<uint>());
                }
                for (int pass = 0; pass < 2; pass++)   // 0: entries that stay, 1: entries that move (appended)
                {
                    for (int l = 0; l < this.model.DrawLists.Count; l++)
                    {
                        bool seeThroughList = l < half;
                        foreach (uint entry in this.model.DrawLists[l])
                        {
                            int p = (int) (entry >> 16);
                            int k = (int) (entry & 0xFFFF);
                            bool known = p < this.model.Sections.Count && k < this.model.Sections[p].SubSections.Count;
                            bool wanted = known ? IsFm3SeeThrough(this.model.Sections[p].SubSections[k].Name) : seeThroughList;
                            if (pass == 0 && wanted == seeThroughList)
                            {
                                lists[l].Add(entry);
                            }
                            else if (pass == 1 && wanted != seeThroughList)
                            {
                                lists[seeThroughList ? l + half : l - half].Add(entry);
                                this.Fixes.Add(string.Format("{0}.{1} to the {2} pass", this.model.Sections[p].Name, this.model.Sections[p].SubSections[k].Name, seeThroughList ? "opaque" : "see-through"));
                            }
                        }
                    }
                }
                for (int l = 0; l < lists.Count; l++)
                {
                    this.model.DrawLists[l] = lists[l].ToArray();
                }
            }
            foreach (CarbinModel.Section section in this.model.Sections)
            {
                if (section.SubSections.Count == 0)
                {
                    continue;
                }
                bool any = false;
                foreach (CarbinModel.SubSection sub in section.SubSections)
                {
                    any |= IsFm3SeeThrough(sub.Name);
                }
                uint flag = any ? Fm3SeeThroughFlag : 0;
                if (section.VbTest1 != flag && (section.VbTest1 == 0 || section.VbTest1 == Fm3SeeThroughFlag))
                {
                    section.VbTest1 = flag;
                    this.Fixes.Add(string.Format("{0}: see-through flag {1}", section.Name, any ? 1 : 0));
                }
            }
        }
    }
}
