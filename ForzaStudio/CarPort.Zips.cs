namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Xml;

    // The car and wheel zips: model, textures, dash, cockpit, brakes, wheels, tires.
    public partial class CarPort
    {
        #region Car and wheel zips

        // The donor's carattribs.xml, the template of the car's. A donor without one (some cars of the DLC packs) lends
        // the first FM4 car's that has it, and the report says so.
        private string CarAttribsTemplate(ForzaZip donor)
        {
            ForzaZip.Entry own = donor.Find("carattribs.xml");
            if (own != null)
            {
                return Encoding.UTF8.GetString(donor.Read(own));
            }
            string cars = Path.Combine(Path.Combine(this.TargetRoot, "Media"), "cars");
            int tried = 0;
            foreach (string zip in Directory.Exists(cars) ? Directory.GetFiles(cars, "*.zip") : new string[0])
            {
                if (++tried > 80)
                {
                    break;
                }
                try
                {
                    ForzaZip other = new ForzaZip(zip);
                    ForzaZip.Entry entry = other.Find("carattribs.xml");
                    if (entry != null)
                    {
                        this.donorContent.Add("carattribs.xml: the template of " + Path.GetFileNameWithoutExtension(zip) + " (the donor " + this.DonorMediaName + " has none)");
                        return Encoding.UTF8.GetString(other.Read(entry));
                    }
                }
                catch (Exception)
                {
                    // not a readable car zip: the next one
                }
            }
            throw new InvalidOperationException("The donor " + this.DonorMediaName + " has no carattribs.xml and no FM4 car zip has one to use instead. Pick another donor.");
        }

        private void BuildCarZip()
        {
            string sourcePath = GamePaths.FindCar(this.SourceRoot, this.MediaName);
            string donorPath = GamePaths.FindCar(this.DonorRoot ?? this.TargetRoot, this.DonorMediaName);
            if (sourcePath == null || donorPath == null || !sourcePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Car zips not found (" + this.MediaName + " in " + this.game + ", " + this.DonorMediaName + " in FM4).");
            }
            ForzaZip from = new ForzaZip(sourcePath);
            ForzaZip donor = new ForzaZip(donorPath);
            ForzaZip car = ForzaZip.CreateLike(donor);
            this.dropPlates = HasPlaceholderCloseUp(sourcePath, this.MediaName);
            if (this.RenameTo != null)
            {
                from = this.RenamedCopy(from);
            }
            this.donorFormat.Add("car zip: entry headers (zip versions, extra field, compression) of " + Path.GetFileName(donorPath));
            this.donorFormat.Add("zipmanifest.xml: the car zip's line modelled on the donor's");
            string prefix = this.CarName.ToLowerInvariant();
            string donorPrefix = this.DonorMediaName.ToLowerInvariant();
            int models = 0;
            List<string> materials = new List<string>();
            byte[] shaderSettings = null;
            // Granny animations: FM4 reads little-endian .gr2 files, FH2 writes big-endian ones (file
            // version 7 at 0x20 as 00000007, measured on the MP4-12C both games have). Without a
            // converter the car goes without them, as FM4 cars without moving parts do.
            bool animations = true;
            int grannyFiles = 0;
            foreach (ForzaZip.Entry entry in from.Entries)
            {
                if (entry.Name.EndsWith(".gr2", StringComparison.OrdinalIgnoreCase))
                {
                    byte[] granny = from.Read(entry);
                    animations &= granny.Length > 0x24 && granny[0x20] == 7 && granny[0x23] == 0;
                    grannyFiles++;
                }
            }
            if (grannyFiles == 0)
            {
                animations = false;
                this.Line("  animations: none in " + this.game + " (no .gr2 files), the donor's left out");
            }
            else if (!animations)
            {
                this.Line("  animations: " + this.game + " .gr2 files are big-endian, left out (no moving parts)");
            }
            foreach (ForzaZip.Entry entry in from.Entries)
            {
                string name = entry.Name.ToLowerInvariant();
                if (name.StartsWith("stripped_") || name == "physicsdefinition.bin" || name == "manufacturercolors.xml")
                {
                    continue;   // FH2 only
                }
                if (!animations && (name.EndsWith(".gr2") || name == "caranimationevents.xml"))
                {
                    continue;
                }
                byte[] data = from.Read(entry);
                if (name.EndsWith(".fbf"))
                {
                    int layers;
                    data = ConvertAnarkLayout(data, out layers);
                    this.Line("  dash {0}: {1} text layers to the FM4 layout", Path.GetFileName(name), layers);
                }
                if (name.EndsWith(".bgf"))
                {
                    data = this.FixDashFonts(data, Path.GetFileName(name));
                }
                if (name.EndsWith(".carbin"))
                {
                    if ((name.Contains("_caliper") || name.Contains("_rotor")) && !name.EndsWith("_lod0.carbin") && this.motorsport3)
                    {
                        name = name.Substring(0, name.Length - ".carbin".Length) + "_lod0.carbin";   // FM3 caliperlf.carbin
                    }
                    if (name.Contains("_caliper") || name.Contains("_rotor"))
                    {
                        // Brakes (type 1): converted; the donor's, renamed, if that fails.
                        try
                        {
                            // Its warnings only for a brake that stays: a placeholder becomes the donor's (ReplacePlaceholderBrakes).
                            StringBuilder warnings = new StringBuilder();
                            data = ConvertBrake(data, Path.GetFileName(name), warnings);
                            if (!IsPlaceholderBrake(data) || donor.Find(donorPrefix + name.Substring(prefix.Length)) == null)
                            {
                                this.Report.Append(warnings.ToString());
                            }
                        }
                        catch (Exception ex)
                        {
                            string description;
                            byte[] generic = this.GenericBrake(car, prefix, name, out description);
                            this.Line("  brake {0}: {1}; {2}", name, ex.Message, (generic != null) ? description + " is used" : "left out");
                            if (generic == null)
                            {
                                continue;
                            }
                            data = generic;
                            this.donorContent.Add("brake " + name + ": " + description + " (conversion failed: " + ex.Message + ")");
                        }
                    }
                    else
                    {
                        data = ConvertModel(data, name.EndsWith("_lod0.carbin"), this.Report, materials, this.MaterialFixes(), this.unknownMaterials, this.DamageReference(name));
                        if (this.dropPlates)
                        {
                            data = this.DropMaterial(data, name, PlateMaterial);
                        }
                        models++;
                    }
                }
                else if (name == "carattribs.xml")
                {
                    List<string> kept = new List<string>();
                    data = ConvertCarAttributes(Encoding.UTF8.GetString(data), this.CarAttribsTemplate(donor), animations, this.Report, kept, !this.motorsport3 && !this.fm2Stage);
                    foreach (string element in kept)
                    {
                        this.donorContent.Add("carattribs.xml " + element);
                    }
                }
                else if (name == "shadersettings.xml")
                {
                    shaderSettings = data;   // needs the materials of every model
                    continue;
                }
                else if (name.EndsWith(".xds") && this.motorsport3)
                {
                    data = ConvertMotorsport3Texture(data, name);
                }
                car.Add(name, data);
            }
            if (this.dropPlates)
            {
                this.unknownMaterials.Remove(PlateMaterial);    // gone from the models: nothing to review
                materials.Remove(PlateMaterial);
            }
            this.AddSharedTextures(from, car);
            this.MoveSectionsToLod0(car, donor, prefix, donorPrefix);
            if (this.dropPlates)
            {
                this.SplitLights(car, prefix);
            }
            this.RebuildCloseUpModels(car, donor, prefix, donorPrefix, materials);
            this.ReplacePlaceholderBrakes(car, donor, prefix, donorPrefix);
            this.ReplacePlaceholderCollision(car, donor, prefix);
            this.CockpitRig(car, donor, prefix, donorPrefix);
            this.DriverHandsFromSteeringWheel(car, prefix);
            if (this.fm2Stage)
            {
                this.NeutralDriverOffsets(car);
                this.ApplyExhaustFile(car, prefix);
            }
            if (shaderSettings != null)
            {
                car.Add("shadersettings.xml", ConvertShaderSettings(Encoding.UTF8.GetString(shaderSettings), materials, this.MaterialFixes(), this.Report));
            }
            if (this.motorsport3 && !string.IsNullOrEmpty(this.motorsport3Dash))
            {
                this.AddMotorsport3Dash(car);
            }
            ForzaZip.Entry version = donor.Find("versiondata.xml");
            if (version != null && car.Find("versiondata.xml") == null)
            {
                car.Add("versiondata.xml", donor.Read(version));
                this.donorContent.Add("car zip versiondata.xml: the donor's (" + this.game + " has none)");
            }
            string path = Path.Combine(Path.Combine(Path.Combine(this.TargetRoot, "Media"), "cars"), this.CarName + ".zip");
            // Saved with the other files at the end, with the report as port_log.txt.
            this.carZip = car;
            this.carZipPath = path;
            this.Line("car zip: {0} entries + port_log.txt, {1} models converted -> {2}", car.Entries.Count, models, this.Relative(path));
        }

        // FH keeps the LOD 0 of the headlights (headlightL / headlightR, deformable) in the cockpit file; FM4 has
        // them in the _lod0 file, which is the one that deforms when the car is alone on the track (MP4-12C: FH
        // lod0 lacks them, FM4 lod0 has them, FM4 cockpit lacks them). So every part the donor's _lod0 file has,
        // the car's _lod0 lacks and the car's cockpit file has moves from the cockpit to the _lod0 file, with
        // its damage words from the same FM4 reference as the other parts (Words = donor). Port.ini [Lod0]
        // MoveSections = off keeps the FH layout. Not for traffic cars (their close-up files are rebuilt).
        private void MoveSectionsToLod0(ForzaZip car, ForzaZip donor, string prefix, string donorPrefix)
        {
            if (this.dropPlates || PortSettings.Get("Lod0", "MoveSections", "on").ToLowerInvariant() == "off")
            {
                return;
            }
            ForzaZip.Entry lodEntry = car.Find(prefix + "_lod0.carbin");
            ForzaZip.Entry cockpitEntry = car.Find(prefix + "_cockpit.carbin");
            ForzaZip.Entry donorEntry = donor.Find(donorPrefix + "_lod0.carbin");
            if (lodEntry == null || cockpitEntry == null || donorEntry == null)
            {
                return;
            }
            CarbinModel lod0 = new CarbinModel(car.Read(lodEntry));
            CarbinModel cockpit = new CarbinModel(car.Read(cockpitEntry));
            CarbinModel reference = new CarbinModel(donor.Read(donorEntry));
            List<string> moved = new List<string>();
            foreach (CarbinModel.Section section in reference.Sections)
            {
                if (lod0.Find(section.Name) == null && cockpit.Find(section.Name) != null)
                {
                    moved.Add(section.Name);
                }
            }
            if (moved.Count == 0)
            {
                return;
            }
            CarbinModel words = this.DamageReference(prefix + "_lod0.carbin");
            foreach (string name in moved)
            {
                // Each subsection keeps its own UV range (the quantisation of its vertices), so the part is moved as it is.
                lod0.ImportSection(cockpit, name, name, false);
                cockpit.RemoveSection(name);
                if (words != null)
                {
                    this.Line("  {0}: {1}", name, lod0.BorrowDamageWords(words, name));
                }
                this.Line("  {0} moved from the cockpit file to the _lod0 file", name);
            }
            car.Replace(lodEntry, lod0.ToBytes());
            car.Replace(cockpitEntry, cockpit.ToBytes());
            this.Line("lod0 parts: {0} moved from the cockpit file to the _lod0 file (FM4 keeps them there; they deform in the LOD 0)", string.Join(", ", moved.ToArray()));
        }

        private ForzaZip damageZip;
        private bool damageZipRead;
        private bool damageByShape;
        private string damagePrefix;

        // B1 test T4 (Port.ini [Damage] Words = donor): the per-vertex damage words from an FM4 model of this file:
        // the same car's own, when FM4 has the car under the source's MediaName (matched by position); else the
        // donor car's (matched by place in each section's box, an approximation: the donor's words are FM4's own,
        // a smooth field over the part). Null when neither exists: the words stay as the Words setting makes them.
        private CarbinModel DamageReference(string entryName)
        {
            string words = PortSettings.Get("Damage", "Words", "donor").ToLowerInvariant();
            if (words != "donor" && words != "donorcar")
            {
                return null;
            }
            if (!this.damageZipRead)
            {
                this.damageZipRead = true;
                string path = GamePaths.FindCar(this.TargetRoot, this.MediaName);
                this.damagePrefix = this.MediaName;
                if (path == null || !path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || words == "donorcar")
                {
                    path = GamePaths.FindCar(this.DonorRoot ?? this.TargetRoot, this.DonorMediaName);
                    this.damagePrefix = this.DonorMediaName;
                    this.damageByShape = true;
                }
                if (path != null && path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    this.damageZip = new ForzaZip(this.In(path));
                }
                this.Line("damage words: " + ((this.damageZip == null) ? "no FM4 file to take them from, the source's stay"
                    : this.damageByShape ? "from the donor " + this.DonorMediaName + " (not the same car: matched by place in each section's box, Words = donor)" : "from the FM4 car's own files (Words = donor)"));
            }
            if (this.damageZip == null)
            {
                return null;
            }
            string own = entryName;
            if (own.StartsWith(this.CarName, StringComparison.OrdinalIgnoreCase))
            {
                own = this.damagePrefix.ToLowerInvariant() + own.Substring(this.CarName.Length);
            }
            ForzaZip.Entry entry = this.damageZip.Find(own);
            if (entry == null)
            {
                return null;
            }
            CarbinModel reference = new CarbinModel(this.damageZip.Read(entry));
            reference.DamageByShape = this.damageByShape;
            return reference;
        }

        // The source car zip under the new name (RenameTo), in memory: every entry name and the text files carry the
        // new name, case kept (as CarDuplicate does for an FM4 car); buildnumber.txt stays the source's.
        private ForzaZip RenamedCopy(ForzaZip from)
        {
            Regex name = new Regex(Regex.Escape(this.MediaName), RegexOptions.IgnoreCase);
            string lower = this.RenameTo.ToLowerInvariant();
            MatchEvaluator keepCase = delegate(Match m) { return (m.Value == m.Value.ToLowerInvariant()) ? lower : this.RenameTo; };
            Encoding bytes = Encoding.GetEncoding(28591);
            ForzaZip renamed = ForzaZip.CreateLike(from);
            int names = 0;
            int texts = 0;
            foreach (ForzaZip.Entry entry in from.Entries)
            {
                if (entry.IsDirectory)
                {
                    continue;
                }
                byte[] data = from.Read(entry);
                string entryName = name.Replace(entry.Name, keepCase);
                names += (entryName != entry.Name) ? 1 : 0;
                string ext = Path.GetExtension(entry.Name).ToLowerInvariant();
                if ((ext == ".xml" || ext == ".txt" || ext == ".html") && !string.Equals(entry.Name, "buildnumber.txt", StringComparison.OrdinalIgnoreCase))
                {
                    string text = bytes.GetString(data);
                    string replaced = name.Replace(text, keepCase);
                    if (replaced != text)
                    {
                        data = bytes.GetBytes(replaced);
                        texts++;
                    }
                }
                renamed.Add(entryName, data);
            }
            this.Line("renamed {0} -> {1}: {2} entry names, {3} text files", this.MediaName, this.RenameTo, names, texts);
            return renamed;
        }

        // FM4 keeps each light in its own part, the same in the main file and _lod0 (FOR_Fiesta_09 and the
        // other 313 cars): headlightL/R (lamp, chrome, frame, reflector of one front corner), taillightL/R,
        // and their glass in glassLHL/RHL and glassLTL/RTL; L = left (x below the body's centre), front =
        // z below it. FH traffic models merge them (FOR_FocusZX4_05: taillightR holds the headlights,
        // indicators and plate light of both ends, glassRTL the front and rear glass; headlightL/R and
        // taillightL are empty-part cubes), and the close-up files rebuilt from LOD 1 inherited that (user
        // 2026-10-20). So, on cars with placeholder close-ups, before the rebuild: every triangle of those
        // eight parts goes, every LOD, to the part of its corner (CarbinModel.SplitSection), and an
        // empty-part cube in a part that got real geometry goes. Kept generic for FM1/FM2 sources.
        private static readonly string[] LightParts = { "headlightL", "headlightR", "taillightL", "taillightR" };
        private static readonly string[] LightGlassParts = { "glassLHL", "glassRHL", "glassLTL", "glassRTL" };

        private void SplitLights(ForzaZip car, string prefix)
        {
            ForzaZip.Entry entry = car.Find(prefix + ".carbin");
            if (entry == null)
            {
                return;
            }
            CarbinModel model = new CarbinModel(car.Read(entry));
            CarbinModel.Section body = model.Find("body");
            if (body == null)
            {
                return;
            }
            float[] b = body.Bounds;
            double cx = b[0] + ((b[3] + b[6]) / 2);
            double cz = b[2] + ((b[5] + b[8]) / 2);
            bool changed = false;
            List<string> parts = new List<string>(LightParts);
            parts.AddRange(LightGlassParts);
            foreach (string part in parts)
            {
                CarbinModel.Section section = model.Find(part);
                if (section == null || IsEmptyPartSection(section))
                {
                    continue;
                }
                bool glass = Array.IndexOf(LightGlassParts, part) >= 0;
                string report = model.SplitSection(section.Name, delegate(double x, double y, double z)
                {
                    if (Math.Abs(x - cx) < LightPlaceholderReach && Math.Abs(z - cz) < LightPlaceholderReach)
                    {
                        return null;    // a placeholder at the car's centre (no light is there): stays
                    }
                    bool left = x < cx;
                    bool front = z < cz;
                    return glass ? ("glass" + (left ? "L" : "R") + (front ? "HL" : "TL")) : ((front ? "headlight" : "taillight") + (left ? "L" : "R"));
                });
                if (report.Length > 0)
                {
                    changed = true;
                    foreach (string line in report.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        this.Line("  lights: {0}", line);
                    }
                }
            }
            if (!changed)
            {
                return;
            }
            // A placeholder left beside real geometry goes: the empty-part cube (headlightL_LOD1 in
            // headlightL) or the 1 cm quad of VW_Beetle_04 (material body), whatever its name: a mesh of at
            // most EmptyPartSize at the car's centre.
            foreach (string part in parts)
            {
                CarbinModel.Section section = model.Find(part);
                if (section == null)
                {
                    continue;
                }
                List<int> placeholders = new List<int>();
                List<string> names = new List<string>();
                for (int k = 0; k < section.SubSections.Count; k++)
                {
                    double[] box = model.SubSectionBox(section, section.SubSections[k]);
                    bool small = box[3] - box[0] <= EmptyPartSize && box[4] - box[1] <= EmptyPartSize && box[5] - box[2] <= EmptyPartSize;
                    bool central = Math.Abs(((box[0] + box[3]) / 2) - cx) < LightPlaceholderReach && Math.Abs(((box[2] + box[5]) / 2) - cz) < LightPlaceholderReach;
                    if (small && central)
                    {
                        placeholders.Add(k);
                        if (!names.Contains(section.SubSections[k].Name))
                        {
                            names.Add(section.SubSections[k].Name);
                        }
                    }
                }
                if (placeholders.Count > 0 && placeholders.Count < section.SubSections.Count)
                {
                    model.RemoveSubSections(section.Name, placeholders);
                    this.Line("  lights: {0}: placeholder {1} removed (the part has real geometry now)", part, string.Join(", ", names.ToArray()));
                }
            }
            car.Replace(entry, model.ToBytes());
        }

        // Half the side of the square around the body's centre (x, z) where no light sits: placeholders do.
        private const double LightPlaceholderReach = 0.3;

        // FH traffic cars lose their number plate (material `numberplate`, a quad in bumperFrameF/R): FM4
        // has no plate shader, it only stayed red / black and needed outside work (user 2026-10-20: "lo
        // leviamo, non serve"). Decided by the car list's quick test (HasPlaceholderCloseUp), since the
        // models are converted before the close-up rebuild sets this.traffic. A section left empty goes.
        private const string PlateMaterial = "numberplate";
        private bool dropPlates;

        private byte[] DropMaterial(byte[] data, string file, string material)
        {
            CarbinModel model = new CarbinModel(data);
            List<string> sections = new List<string>();
            foreach (CarbinModel.Section section in model.Sections)
            {
                if (section.SubSections.Exists(delegate(CarbinModel.SubSection sub) { return string.Equals(sub.Name, material, StringComparison.OrdinalIgnoreCase); }))
                {
                    sections.Add(section.Name);
                }
            }
            if (sections.Count == 0)
            {
                return data;
            }
            int removed = 0;
            foreach (string section in sections)
            {
                removed += model.RemoveSubSections(section, material);
            }
            this.Line("  {0}: traffic car, number plate removed ({1} meshes in {2})", file, removed, string.Join(" ", sections.ToArray()));
            return model.ToBytes();
        }

        // Colour textures the car's materials take from the source game's Media\cars\Shared.zip (its shaders and
        // Shared.zip\textures.xml, ShaderTextures) that FM4's Shared.zip lacks go into the car zip, so
        // the car carries them (user 2026-10-20: GMC_YukonXL_01's number plates,
        // traffic_numplates_DXT1.xds, FH1 only). FM4's shaders do not read them from there: they are for
        // the editor's preview and for later material work, not a change in game.
        private void AddSharedTextures(ForzaZip from, ForzaZip car)
        {
            ShaderTextures source = ShaderTextures.For(this.SourceRoot);
            ShaderTextures target = ShaderTextures.For(this.TargetRoot);
            if (source.Problem != null || target.SharedZipPath == null || !File.Exists(target.SharedZipPath))
            {
                this.Line("  shared textures: not checked ({0})", source.Problem ?? "no FM4 Media\\cars\\Shared.zip");
                return;
            }
            ForzaZip sourceShared = GamePaths.OpenZip(source.SharedZipPath);
            ForzaZip targetShared = GamePaths.OpenZip(target.SharedZipPath);
            SortedDictionary<string, List<string>> needed = new SortedDictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (ForzaZip.Entry entry in from.Entries)
            {
                string name = entry.Name.ToLowerInvariant();
                if (!name.EndsWith(".carbin") || name.Contains("_caliper") || name.Contains("_rotor") || name.StartsWith("stripped_"))
                {
                    continue;   // stripped_*: FH2-only copies, not ported
                }
                ShaderTextures.Kind fileKind = name.EndsWith("_cockpit.carbin") ? ShaderTextures.Kind.Cockpit
                    : name.EndsWith("_lod0.carbin") ? ShaderTextures.Kind.Lod0 : ShaderTextures.Kind.Main;
                try
                {
                    using (ForzaCarBin bin = new ForzaCarBin(name, from.Read(entry)))
                    {
                        foreach (ForzaCarSection section in bin.Sections)
                        {
                            foreach (ForzaCarSubSection sub in section.SubSections)
                            {
                                if (this.dropPlates && string.Equals(sub.Name, PlateMaterial, StringComparison.OrdinalIgnoreCase))
                                {
                                    continue;   // removed from the models (DropMaterial): no plate texture
                                }
                                ShaderTextures.Kind kind = (fileKind == ShaderTextures.Kind.Main && sub.Lod == 0) ? ShaderTextures.Kind.Lod0 : fileKind;
                                // Only a material's own colour texture: FH1's `body` reads the plates too
                                // (a switch the traffic cars turn on), so every FH1 car would carry them;
                                // `numberplate` draws with them (user 2026-10-20: only cars with a
                                // material that fits).
                                List<ShaderTextures.Texture> colours = source.ColourTextures(sub.Name, kind);
                                foreach (ShaderTextures.Texture texture in colours.GetRange(0, Math.Min(1, colours.Count)))
                                {
                                    if (texture.Where != ShaderTextures.Place.Shared)
                                    {
                                        continue;
                                    }
                                    List<string> users;
                                    if (!needed.TryGetValue(texture.File, out users))
                                    {
                                        needed[texture.File] = users = new List<string>();
                                    }
                                    if (!users.Contains(sub.Name))
                                    {
                                        users.Add(sub.Name);
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    this.Line("  shared textures: {0} not read ({1})", name, ex.Message);
                }
            }
            foreach (KeyValuePair<string, List<string>> pair in needed)
            {
                ForzaZip.Entry file = sourceShared.Find(pair.Key);
                if (targetShared.Find(pair.Key) != null || file == null || car.Find(pair.Key.ToLowerInvariant()) != null)
                {
                    continue;
                }
                byte[] data = sourceShared.Read(file);
                if (this.motorsport3)
                {
                    data = ConvertMotorsport3Texture(data, pair.Key.ToLowerInvariant());
                }
                car.Add(pair.Key.ToLowerInvariant(), data);
                this.Line("  shared texture {0}: not in FM4's Shared.zip, copied from {1}'s into the car zip (used by {2})", pair.Key, this.game, string.Join(", ", pair.Value.ToArray()));
            }
        }

        // The collision (physics/maxdata.xml: a bevelled box and spheres; Flags 2 = body, 1 = contact
        // points of r 0.05). 44 FH1 and 2 FH2 cars have a placeholder there: 4 spheres of r 0.375, Flags
        // 3, low at the wheel corners (GMC_YukonXL_01, BUS_TOUR_12), and a box that may not fit the
        // model (GMC_YukonXL_01: 26 cm short of the front bumper): in FM4 a guardrail went through the
        // Yukon (user 2026-10-18). FM4 cars have 26-39 spheres (190 of 313: 20 + 12). On a traffic car
        // such a collision is rebuilt. The box from the main model's body section by FM4's rule (313 cars, medians): x =
        // body x - 0.018, top = body top - 0.010, front = body front + 0.096, rear = body rear - 0.046,
        // floor = the ground (wheel centre y - tyre radius, +0.006), bevels 0.611 / 0.744 of the width
        // (front / rear) and 0.819 / 0.805 of the length. The spheres are the donor's, moved from its
        // box into this one axis by axis, radii scaled by the smaller of the x and y ratios, so they stick out
        // of the box as much as the donor's do (the source has
        // nothing; decided for traffic cars' brakes by the user 2026-10-17).
        private const int PlaceholderSpheres = 10;

        private void ReplacePlaceholderCollision(ForzaZip car, ForzaZip donor, string prefix)
        {
            ForzaZip.Entry entry = car.Find("physics/maxdata.xml");
            ForzaZip.Entry template = donor.Find("physics/maxdata.xml");
            ForzaZip.Entry main = car.Find(prefix + ".carbin");
            if (entry == null || template == null || main == null)
            {
                return;
            }
            XmlDocument own = new XmlDocument();
            own.LoadXml(Encoding.UTF8.GetString(car.Read(entry)).Trim('﻿', ' ', '\r', '\n', '\t'));
            XmlElement spheres = (XmlElement) own.SelectSingleNode("/MAXData/Collision/CollSpheres");
            XmlElement box = (XmlElement) own.SelectSingleNode("/MAXData/Collision/BoundingBox");
            if (spheres == null || box == null || spheres.ChildNodes.Count >= PlaceholderSpheres)
            {
                return;
            }
            if (!this.traffic)
            {
                // Driveable FH1 cars have it too (AUD_R8GT_11, MER_S65AMG_12, NIS_GTR_12... 26 of the 44),
                // ported and working: left as it is (decided by the user 2026-10-18, traffic cars only).
                this.Line("  collision: a placeholder of {0} spheres in the source; kept (only traffic cars get it rebuilt)", spheres.ChildNodes.Count);
                return;
            }
            CarbinModel.Section body = new CarbinModel(car.Read(main)).Find("body");
            if (body == null)
            {
                this.Line("  collision: a placeholder of {0} spheres, but the main model has no body section; left as is", spheres.ChildNodes.Count);
                return;
            }
            float[] b = body.Bounds;
            double wheelY = Convert.ToDouble(this.target.Scalar("SELECT PartPosY FROM CarPartPositions WHERE Ordinal = ? AND PartId = 5", this.carId) ?? 0.0, CultureInfo.InvariantCulture);
            object[] tyre = this.target.Query("SELECT FrontTireWidthMM, FrontTireAspect, FrontWheelDiameterIN FROM Data_Car WHERE Id = ?", this.carId).Rows[0];
            double radius = (Convert.ToDouble(tyre[2], CultureInfo.InvariantCulture) * 0.0254 / 2) + (Convert.ToDouble(tyre[0], CultureInfo.InvariantCulture) * Convert.ToDouble(tyre[1], CultureInfo.InvariantCulture) / 100000.0);
            double maxX = Math.Max(Math.Abs(b[0] + b[3]), Math.Abs(b[0] + b[6])) - 0.018;
            double minY = wheelY - radius + 0.006;
            double maxY = b[1] + b[7] - 0.010;
            double maxZ = -(b[2] + b[5]) + 0.096;    // model Z backward
            double minZ = -(b[2] + b[8]) - 0.046;
            // Front and rear from the whole LOD 1 mesh (vertices, all parts): an FH traffic body holds
            // the bumpers too (GMC_YukonXL_01), so the body rule above put the box and the spheres
            // ~0.2 m past both ends (seen in the collision view, user 2026-10-19). The collision's Z is
            // measured from the wheelbase centre (Misc/BottomCenterWheelbasePos z = the middle of the DB
            // wheels' Z): with it FM4's ends spread half as much (314 cars, fm4dev\Traffic\tools\colsurvey)
            // and the Yukon (z 0.188) stopped short at the front and went into a wall at the back
            // (user 2026-10-20). FM4 medians, model axes: box front = mesh front - 0.127, rear = mesh
            // rear + 0.045; body spheres front = mesh front - 0.092, rear = mesh rear - 0.003. The rear is
            // then pulled in 0.05 m more and the front pushed out 0.05 m (box and spheres), tuned in game
            // on the Yukon against walls (user 2026-10-20, "quasi perfetta"). Final rule, Z forward from
            // the wheelbase centre: box front = mesh front - 0.077, rear = mesh rear + 0.095; body
            // spheres front = mesh front - 0.042, rear = mesh rear + 0.047.
            const double RearIn = 0.05;
            const double FrontOut = 0.05;
            XmlElement wheelbase = (XmlElement) own.SelectSingleNode("/MAXData/Misc/BottomCenterWheelbasePos");
            double centreZ = (wheelbase != null && wheelbase.HasAttribute("z")) ? Attribute(wheelbase, "z") : 0;
            double meshFront = double.MinValue, meshRear = double.MaxValue;
            using (ForzaCarBin bin = new ForzaCarBin(prefix, car.Read(main)))
            {
                foreach (ForzaCarSection section in bin.Sections)
                {
                    foreach (ForzaCarSubSection sub in section.SubSections)
                    {
                        if (sub.Lod != 1)
                        {
                            continue;
                        }
                        foreach (int i in sub.Indices)
                        {
                            double z = -sub.Vertices[i].position.Z;
                            meshFront = Math.Max(meshFront, z);
                            meshRear = Math.Min(meshRear, z);
                        }
                    }
                }
            }
            if (meshFront > meshRear)
            {
                maxZ = meshFront - 0.127 + FrontOut - centreZ;
                minZ = meshRear + 0.045 + RearIn - centreZ;
            }
            double width = 2 * maxX, length = maxZ - minZ;

            XmlDocument theirs = new XmlDocument();
            theirs.LoadXml(Encoding.UTF8.GetString(donor.Read(template)).Trim('﻿', ' ', '\r', '\n', '\t'));
            XmlElement donorBox = (XmlElement) theirs.SelectSingleNode("/MAXData/Collision/BoundingBox");
            XmlElement donorSpheres = (XmlElement) theirs.SelectSingleNode("/MAXData/Collision/CollSpheres");
            double dMinX = Attribute(donorBox, "MinX"), dMaxX = Attribute(donorBox, "MaxX"), dMinY = Attribute(donorBox, "MinY"), dMaxY = Attribute(donorBox, "MaxY"), dMinZ = Attribute(donorBox, "MinZ"), dMaxZ = Attribute(donorBox, "MaxZ");
            double sx = width / (dMaxX - dMinX), sy = (maxY - minY) / (dMaxY - dMinY), sz = length / (dMaxZ - dMinZ);

            SetAttribute(box, "MinX", -maxX);
            SetAttribute(box, "MaxX", maxX);
            SetAttribute(box, "MinY", minY);
            SetAttribute(box, "MaxY", maxY);
            SetAttribute(box, "MinZ", minZ);
            SetAttribute(box, "MaxZ", maxZ);
            SetAttribute(box, "BevelFrontX", width * 0.611);
            SetAttribute(box, "BevelFrontZ", length * 0.819);
            SetAttribute(box, "BevelRearX", width * 0.744);
            SetAttribute(box, "BevelRearZ", length * 0.805);
            int placeholder = spheres.ChildNodes.Count;
            spheres.RemoveAll();
            foreach (XmlAttribute attribute in donorSpheres.Attributes)
            {
                spheres.SetAttribute(attribute.Name, attribute.Value);
            }
            foreach (XmlNode node in donorSpheres.ChildNodes)
            {
                XmlElement sphere = node as XmlElement;
                if (sphere == null)
                {
                    continue;
                }
                XmlElement copy = (XmlElement) own.ImportNode(sphere, true);
                SetAttribute(copy, "PosX", (Attribute(sphere, "PosX") - ((dMinX + dMaxX) / 2)) * sx);
                SetAttribute(copy, "PosY", minY + ((Attribute(sphere, "PosY") - dMinY) * sy));
                SetAttribute(copy, "PosZ", minZ + ((Attribute(sphere, "PosZ") - dMinZ) * sz));
                SetAttribute(copy, "Radius", Attribute(sphere, "Radius") * Math.Min(sx, sy));
                spheres.AppendChild(copy);
            }
            if (meshFront > meshRear)
            {
                StretchSpheresZ(spheres, meshFront - 0.092 + FrontOut - centreZ, meshRear - 0.003 + RearIn - centreZ);
            }
            int filled = FillSphereGaps(own, spheres);
            StringBuilder text = new StringBuilder();
            using (XmlWriter writer = XmlWriter.Create(text, new XmlWriterSettings { Indent = true, IndentChars = "  ", OmitXmlDeclaration = true }))
            {
                own.DocumentElement.WriteTo(writer);
            }
            car.Replace(entry, Encoding.UTF8.GetBytes("\r\n" + text.ToString().Replace(" />", "/>") + "\r\n"));
            this.donorContent.Add(string.Format(CultureInfo.InvariantCulture, "collision: the donor's {0} spheres fitted to the car's box (scale x {1:0.###}, y {2:0.###}, z {3:0.###}), the source's is a placeholder of {4}", spheres.ChildNodes.Count, sx, sy, sz, placeholder));
            this.Line(string.Format(CultureInfo.InvariantCulture, "  collision: a placeholder of {0} spheres; box from the model x +-{1:0.###}, y {2:0.###}..{3:0.###}, z {4:0.###}..{5:0.###}, the donor's {6} spheres fitted to it", placeholder, maxX, minY, maxY, minZ, maxZ, spheres.ChildNodes.Count - filled));
            if (filled > 0)
            {
                this.Line("  collision: {0} spheres added between the side spheres the stretch left apart ({1} in all; FM4 cars have at most 39, more is untested)", filled, spheres.ChildNodes.Count);
            }
        }

        // FM4's body spheres (Flags 2) end near the mesh ends (314 cars, medians: front surface = mesh
        // front - 0.092, rear = mesh rear - 0.003, wheelbase-centre axes; colsurvey). The donor's spheres, fitted to the box,
        // fall short of that (GMC_YukonXL_01: 0.23 / 0.14 m inside): their centres are moved along Z,
        // linearly, so the outermost surfaces land there. Radii stay; contact points move with them.
        private static void StretchSpheresZ(XmlElement spheres, double front, double rear)
        {
            List<XmlElement> all = new List<XmlElement>();
            foreach (XmlNode node in spheres.ChildNodes)
            {
                if (node is XmlElement)
                {
                    all.Add((XmlElement) node);
                }
            }
            XmlElement first = null, last = null;
            foreach (XmlElement s in all)
            {
                if (s.GetAttribute("Flags") != "2")
                {
                    continue;
                }
                if (first == null || Attribute(s, "PosZ") + Attribute(s, "Radius") > Attribute(first, "PosZ") + Attribute(first, "Radius"))
                {
                    first = s;
                }
                if (last == null || Attribute(s, "PosZ") - Attribute(s, "Radius") < Attribute(last, "PosZ") - Attribute(last, "Radius"))
                {
                    last = s;
                }
            }
            if (first == null || first == last)
            {
                return;
            }
            double z1 = Attribute(first, "PosZ"), z0 = Attribute(last, "PosZ");
            double t1 = front - Attribute(first, "Radius"), t0 = rear + Attribute(last, "Radius");
            if (z1 - z0 < 0.01 || t1 <= t0)
            {
                return;
            }
            double a = (t1 - t0) / (z1 - z0);
            foreach (XmlElement s in all)
            {
                SetAttribute(s, "PosZ", t0 + ((Attribute(s, "PosZ") - z0) * a));
            }
        }

        // A donor collision stretched along a much longer car (BUS_TOUR_12: the Hummer's x 2.9) leaves gaps
        // between the body spheres (Flags 2) along each side. On each side (x < 0, x > 0) the large ones
        // (at least half the side's largest radius), front to back: two that do not touch get spheres in
        // between, position and radius interpolated, until each touches the next. Returns how many.
        private static int FillSphereGaps(XmlDocument document, XmlElement spheres)
        {
            List<XmlElement> all = new List<XmlElement>();
            foreach (XmlNode node in spheres.ChildNodes)
            {
                if (node is XmlElement)
                {
                    all.Add((XmlElement) node);
                }
            }
            int added = 0;
            foreach (int side in new int[] { -1, 1 })
            {
                List<XmlElement> chain = all.FindAll(delegate(XmlElement s) { return s.GetAttribute("Flags") == "2" && Attribute(s, "PosX") * side > 0.05; });
                double largest = 0;
                foreach (XmlElement s in chain)
                {
                    largest = Math.Max(largest, Attribute(s, "Radius"));
                }
                chain = chain.FindAll(delegate(XmlElement s) { return Attribute(s, "Radius") >= largest / 2; });
                chain.Sort(delegate(XmlElement a, XmlElement b) { return Attribute(b, "PosZ").CompareTo(Attribute(a, "PosZ")); });
                for (int i = 0; i + 1 < chain.Count; i++)
                {
                    XmlElement a = chain[i], b = chain[i + 1];
                    double ra = Attribute(a, "Radius"), rb = Attribute(b, "Radius");
                    double dx = Attribute(b, "PosX") - Attribute(a, "PosX"), dy = Attribute(b, "PosY") - Attribute(a, "PosY"), dz = Attribute(b, "PosZ") - Attribute(a, "PosZ");
                    double distance = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
                    int count = (int) Math.Ceiling(distance / (ra + rb)) - 1;
                    for (int k = 1; k <= count; k++)
                    {
                        double t = (double) k / (count + 1);
                        XmlElement sphere = document.CreateElement("Sphere" + (all.Count + added));
                        SetAttribute(sphere, "Radius", ra + ((rb - ra) * t));
                        SetAttribute(sphere, "PosX", Attribute(a, "PosX") + (dx * t));
                        SetAttribute(sphere, "PosY", Attribute(a, "PosY") + (dy * t));
                        SetAttribute(sphere, "PosZ", Attribute(a, "PosZ") + (dz * t));
                        sphere.SetAttribute("Flags", "2");
                        spheres.AppendChild(sphere);
                        added++;
                    }
                }
            }
            return added;
        }

        private static double Attribute(XmlElement element, string name)
        {
            return double.Parse(element.GetAttribute(name), CultureInfo.InvariantCulture);
        }

        private static void SetAttribute(XmlElement element, string name, double value)
        {
            element.SetAttribute(name, value.ToString("0.000000", CultureInfo.InvariantCulture));
        }

        // FH traffic cars' brakes are placeholders (GMC_YukonXL_01: one section of 17 vertices per file,
        // material rotorLF_LOD0...) and their main model has no brake part either: nothing behind the rim
        // (checked by the user 2026-10-17). Source data being absent, such a file (or a caliper the source
        // lacks while it has the rotor) becomes the game's own generic brake of Media\brakes (no donor car's
        // part; idea of the user 2026-10-20): N_caliper_4 / _2 and N_rotor_slotted _LOD0, N = rotor
        // diameter in inches (the file's rotor has a radius of N * 0.0127 m, origin at the wheel centre).
        // The generic caliper sits at +Y: turned about X to +Z on the front, -Z on the rear (what most FM4
        // cars have), mirrored for the right side (rotors are symmetric).
        private const int PlaceholderBrakeVertices = 50;

        private static int BrakeVertices(byte[] data)
        {
            int vertices = 0;
            foreach (CarbinModel.Section section in new CarbinModel(data).Sections)
            {
                vertices += section.Lod.Count + section.Lod0.Count;
            }
            return vertices;
        }

        private static bool IsPlaceholderBrake(byte[] data)
        {
            return BrakeVertices(data) < PlaceholderBrakeVertices;
        }

        // Rotor diameter in inches for the generic brake: the car's own rotor when it has a real one,
        // else the rim minus 4.5 (FM4 cars: 15" rim 10-12", 18" 13-15", 19" 14-15"). 7..16, the generic sizes.
        private int GenericBrakeInches(ForzaZip car, string prefix, bool front)
        {
            ForzaZip.Entry rotor = car.Find(prefix + (front ? "_rotorlf_lod0.carbin" : "_rotorlr_lod0.carbin"));
            double inches = 0;
            if (rotor != null)
            {
                byte[] data = car.Read(rotor);
                if (!IsPlaceholderBrake(data))
                {
                    foreach (CarbinModel.Section section in new CarbinModel(data).Sections)
                    {
                        double[] p = CarbinModel.Positions(section, (section.Lod0.Count > 0) ? section.Lod0 : section.Lod);
                        double radius = 0;
                        for (int i = 0; i < p.Length / 3; i++)
                        {
                            radius = Math.Max(radius, Math.Max(Math.Abs(p[(i * 3) + 1]), Math.Abs(p[(i * 3) + 2])));
                        }
                        inches = Math.Max(inches, radius * 2 / 0.0254);
                    }
                }
            }
            if (inches <= 0)
            {
                string column = front ? "FrontWheelDiameterIN" : "RearWheelDiameterIN";
                inches = Convert.ToDouble(this.target.Scalar("SELECT " + column + " FROM Data_Car WHERE Id = ?", this.carId), CultureInfo.InvariantCulture) - 4.5;
            }
            return Math.Max(7, Math.Min(16, (int) Math.Floor(inches + 0.5)));
        }

        // What goes on a car that has no brakes of its own (FH traffic, FM2, anything the source lacks), asked
        // from the user for each car (2026-10-20, instead of a weight/power rule): the caliper of the front and
        // of the rear (the generic set has 2, 4 and 6 pistons with a LOD0) and the angle the caliper sits at, in
        // degrees around the axle (the row's CaliperAngleFront/Rear, radians, when left out; a value given is
        // written to the row). The rotor is one model per size, the slotted one (user 2026-10-24: the game has
        // no LOD0 plain disc, only the caliper changes); its size comes from the rim.
        public class BrakeChoice
        {
            public int FrontPistons = 2;
            public int RearPistons = 2;
            public double? FrontAngle;
            public double? RearAngle;

            // "2,2,-75,90": front pistons, rear pistons, front/rear angle (empty = the row's)
            public override string ToString()
            {
                return string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3}", this.FrontPistons, this.RearPistons,
                    this.FrontAngle, this.RearAngle);
            }

            // Also reads the older "2,2,0,0,-75,90" (with the rotor types, no longer chosen).
            public static BrakeChoice Parse(string text)
            {
                BrakeChoice choice = new BrakeChoice();
                string[] p = (text ?? "").Split(',');
                int pistons;
                double angle;
                if (p.Length >= 4)
                {
                    int angles = (p.Length >= 6) ? 4 : 2;
                    choice.FrontPistons = (int.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out pistons) && pistons >= 2 && pistons <= 6) ? pistons : 2;
                    choice.RearPistons = (int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out pistons) && pistons >= 2 && pistons <= 6) ? pistons : 2;
                    choice.FrontAngle = double.TryParse(p[angles], NumberStyles.Float, CultureInfo.InvariantCulture, out angle) ? (double?) angle : null;
                    choice.RearAngle = double.TryParse(p[angles + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out angle) ? (double?) angle : null;
                }
                return choice;
            }
        }

        public BrakeChoice Brakes = new BrakeChoice();

        // The generic brake keeps the material and the UV transform of the game's own file (Media\brakes): it
        // is the model the showroom takes from Shared when a brake upgrade is fitted, and its material reads the
        // game's shared brake texture. The car's textures are not touched (user 2026-10-22).

        // The generic brake for a car file name (prefix_caliperlf_lod0.carbin...), or null without the game file.
        private byte[] GenericBrake(ForzaZip car, string prefix, string name, out string description)
        {
            description = null;
            bool front = name.Contains("lf_") || name.Contains("rf_");
            bool right = name.Contains("rf_") || name.Contains("rr_");
            bool caliper = name.Contains("_caliper");
            int inches = this.GenericBrakeInches(car, prefix, front);
            string why = string.Format(CultureInfo.InvariantCulture, "chosen: {0} piston", front ? this.Brakes.FrontPistons : this.Brakes.RearPistons);
            string kind = caliper ? "caliper_" + (front ? this.Brakes.FrontPistons : this.Brakes.RearPistons).ToString(CultureInfo.InvariantCulture) : "rotor_slotted";
            string file = string.Format(CultureInfo.InvariantCulture, "{0}_{1}_LOD0.rmb.carbin", inches, kind);
            string path = Path.Combine(Path.Combine(Path.Combine(this.TargetRoot, "Media"), "brakes"), file);
            if (!File.Exists(path))
            {
                return null;
            }
            // The caliper's angle is the row's (CaliperAngleFront/Rear, radians: the model's own angle as the
            // FM4 cars show it, atan2(z, y) of the caliper = minus the value); the generic one sits at +Y.
            string angleColumn = front ? "CaliperAngleFront" : "CaliperAngleRear";
            double? chosen = front ? this.Brakes.FrontAngle : this.Brakes.RearAngle;
            if (chosen != null)
            {
                this.target.Execute("UPDATE Data_Car SET " + angleColumn + " = ? WHERE Id = ?", chosen.Value * Math.PI / 180, this.carId);   // the row follows the model
            }
            double angle = Convert.ToDouble(this.target.Scalar("SELECT " + angleColumn + " FROM Data_Car WHERE Id = ?", this.carId), CultureInfo.InvariantCulture);
            // The car's own brake files hold LOD 0 only (the lod buffer is empty in every FM4 _lod0 brake file): the game takes
            // the other LODs from its generic file without the suffix by itself (user 2026-10-24: LOD 0 from _LOD0, the file
            // without LOD for all the others), so only LOD 0 is written here.
            CarbinModel brake = new CarbinModel(File.ReadAllBytes(path));
            foreach (CarbinModel.Section section in brake.Sections)
            {
                if (!caliper)
                {
                    brake.RemoveSubSections(section.Name, "caliper");   // the rotor's file may carry a caliper too
                }
                else
                {
                    brake.RemoveSubSections(section.Name, "rotor");     // and the 6 piston caliper a rotor
                }
                if (caliper)
                {
                    brake.TransformSection(section.Name, Microsoft.Xna.Framework.Matrix.CreateRotationX((float) -angle));
                }
                if (right)
                {
                    brake.MirrorSection(section.Name, 0);
                }
                string part = name.Substring(name.LastIndexOf('/') + 1);
                part = part.Substring(prefix.Length + 1, part.IndexOf("_lod0") - prefix.Length - 1);   // caliperlf
                section.Name = part.Substring(0, part.Length - 2) + part.Substring(part.Length - 2).ToUpperInvariant();
            }
            description = string.Format(CultureInfo.InvariantCulture, "the game's generic brake {0} ({1}; angle {2:0.00} rad; material and shared texture of the game's file kept)", file, why, angle);
            return brake.ToBytes();
        }

        // An FM2 stage (fm2Stage) takes the game's generic brakes for every position, whatever its files hold
        // (the donor's, no FM2 brake art: decided by the user 2026-10-20 for FM2 traffic).
        private void ReplacePlaceholderBrakes(ForzaZip car, ForzaZip donor, string prefix, string donorPrefix)
        {
            // Calipers the source lacks while it has the rotor (an FM2 stage: every missing file).
            foreach (string position in new string[] { "lf", "lr", "rf", "rr" })
            {
                foreach (string part in new string[] { "caliper", "rotor" })
                {
                    string brakeName = prefix + "_" + part + position + "_lod0.carbin";
                    bool missing = car.Find(brakeName) == null;
                    if (!missing || (!this.fm2Stage && (part != "caliper" || car.Find(prefix + "_rotor" + position + "_lod0.carbin") == null)))
                    {
                        continue;
                    }
                    string description;
                    byte[] generic = this.GenericBrake(car, prefix, brakeName, out description);
                    if (generic != null)
                    {
                        car.Add(brakeName, generic);
                        this.donorContent.Add("brake " + brakeName + ": " + description + " (the source has none)");
                        this.Line("  brake {0}: the source has none, {1}", brakeName, description);
                    }
                }
            }
            foreach (ForzaZip.Entry entry in new List<ForzaZip.Entry>(car.Entries))
            {
                string name = entry.Name.ToLowerInvariant();
                if (!name.EndsWith(".carbin") || (!name.Contains("_caliper") && !name.Contains("_rotor")) || !name.StartsWith(prefix))
                {
                    continue;
                }
                int vertices = BrakeVertices(car.Read(entry));
                if (vertices >= PlaceholderBrakeVertices && !this.fm2Stage)
                {
                    continue;
                }
                string description;
                byte[] generic = this.GenericBrake(car, prefix, name, out description);
                if (generic == null)
                {
                    this.Line("  brake {0}: a placeholder of {1} vertices, and the game has no generic brake of that size; left as is", name, vertices);
                    continue;
                }
                car.Replace(entry, generic);
                this.donorContent.Add(string.Format(CultureInfo.InvariantCulture, "brake {0}: {1}, the source's is a placeholder", name, description));
                this.Line("  brake {0}: {1} vertices, now {2}", name, vertices, description);
            }
        }

        // A quick test for the car list, from the zip directory only: a _lod0 or _cockpit file missing or
        // smaller than a tenth of the main file. Close-up files of real cars are 40% of it and up (FM4 313,
        // FH1, FH2); placeholders 3% at most (18 FH1 and 14 FH2 cars). The port itself decides by vertices.
        public static bool HasPlaceholderCloseUp(string zipPath, string mediaName)
        {
            string prefix = mediaName.ToLowerInvariant();
            ForzaZip car = new ForzaZip(zipPath);
            ForzaZip.Entry main = car.Find(prefix + ".carbin");
            if (main == null)
            {
                return false;
            }
            foreach (string kind in new string[] { "_lod0", "_cockpit" })
            {
                ForzaZip.Entry close = car.Find(prefix + kind + ".carbin");
                if (close == null || close.UncompressedSize < main.UncompressedSize / 10)
                {
                    return true;
                }
            }
            return false;
        }

        // FM4 draws a car up close from its _lod0 (exterior) and _cockpit (interior) files. FH traffic models
        // (GMC_YukonXL_01, FOR_Transit_11, TOY_Camry_07... 19 FH1/FH2 cars) have only placeholders there,
        // 10-17 parts of 17 vertices, and a few have no file at all (FOR_Explorer_10): the car vanishes
        // up close. A close-up file with less than 2% of the main file's vertices (real ones have 5% and
        // up; FM4's lod0 holds more than the main file) is rebuilt from the main file's LOD 1 by FM4's rule
        // (313 cars): the cockpit parts (CockpitPart) go to _cockpit, the rest to _lod0. The cockpit also
        // gets the body, so the cockpit view always shows the car (decided by the user 2026-10-17; such
        // cars often have no hood part). The header is the source's own close-up file's, else the donor's.
        // The seat with the traffic driver merged in (DriverSeat) is replaced by the other one mirrored:
        // FM4 draws its own driver.
        private void RebuildCloseUpModels(ForzaZip car, ForzaZip donor, string prefix, string donorPrefix, List<string> materials)
        {
            ForzaZip.Entry mainEntry = car.Find(prefix + ".carbin");
            if (mainEntry == null)
            {
                return;
            }
            CarbinModel main = new CarbinModel(car.Read(mainEntry));
            bool rebuilt = false;
            bool lod0Rebuilt = false;
            bool cockpitRebuilt = false;
            CarbinModel cockpitModel = null;
            string driverSeat = DriverSeat(main);
            CarbinModel source = main;
            if (driverSeat != null)
            {
                // A bus has no passenger seat: the other seat is an empty part (BUS_TOUR_12 seatR, the
                // 0.2 m cube). Mirrored, it would take the driver's seat away: the seat stays as it is.
                CarbinModel.Section other = main.Find((driverSeat == "seatL") ? "seatR" : "seatL");
                if (other == null || IsEmptyPartSection(other))
                {
                    this.Line("  {0}: the traffic driver is part of it, but the other seat is {1}: kept as it is, with the driver", driverSeat, (other == null) ? "missing" : "an empty part");
                    driverSeat = null;
                }
            }
            if (driverSeat != null)
            {
                // The driver's seat becomes the other seat mirrored across the body's center plane
                // (decided by the user 2026-10-17).
                string twin = (driverSeat == "seatL") ? "seatR" : "seatL";
                CarbinModel.Section body = main.Find("body");
                source = new CarbinModel(main.ToBytes());
                source.RemoveSection(driverSeat);
                source.DuplicateSection(twin, driverSeat);
                source.MirrorSection(driverSeat, (body != null) ? body.Bounds[0] : 0);
            }
            int mainVertices = 0;
            foreach (CarbinModel.Section section in main.Sections)
            {
                mainVertices += section.Lod.Count;
            }
            // Empty parts stay out of the close-up models (decided by the user 2026-10-18): FM4's cockpits
            // have none, and they would be drawn with 'missing'.
            List<string> emptyParts = new List<string>();
            foreach (CarbinModel.Section section in source.Sections)
            {
                if (IsEmptyPartSection(section))
                {
                    emptyParts.Add(section.Name);
                }
            }
            foreach (string kind in new string[] { "_lod0", "_cockpit" })
            {
                string name = prefix + kind + ".carbin";
                ForzaZip.Entry own = car.Find(name);
                byte[] head = null;
                int vertices = 0;
                if (own != null)
                {
                    CarbinModel model = new CarbinModel(car.Read(own));
                    head = model.Head;
                    foreach (CarbinModel.Section section in model.Sections)
                    {
                        vertices += section.Lod0.Count;
                    }
                }
                if (own != null && vertices >= mainVertices * 0.02)
                {
                    continue;
                }
                if (head == null)
                {
                    ForzaZip.Entry template = donor.Find(donorPrefix + kind + ".carbin");
                    if (template == null)
                    {
                        this.Line("  {0}: {1}, and the donor has no such file for the header; left as is", name, (own == null) ? "missing" : "a placeholder");
                        continue;
                    }
                    head = new CarbinModel(donor.Read(template)).Head;
                    this.donorFormat.Add(name + ": header of the donor's " + template.Name);
                }
                bool cockpit = kind == "_cockpit";
                CarbinModel built;
                try
                {
                    built = source.CloseUpFromLod1(head, delegate(string part) { return !emptyParts.Contains(part) && (CockpitPart(part) == cockpit || (cockpit && part == "body")); });
                }
                catch (InvalidDataException ex)
                {
                    this.Line("  {0}: not rebuilt from LOD 1 ({1})", name, ex.Message);
                    continue;
                }
                List<string> parts = new List<string>();
                int built0 = 0;
                int atlasRenames = 0;
                foreach (CarbinModel.Section section in built.Sections)
                {
                    parts.Add(section.Name);
                    built0 += section.Lod0.Count;
                    RenameLod0Tire(section);
                    foreach (CarbinModel.SubSection sub in section.SubSections)
                    {
                        sub.Name = LodVariant(sub.Name, name);
                        // `interior` (the main file's LOD 1 interior material) has no cockpit_ technique:
                        // drawn with the plain one it reads nodamage_LOD0, not the interior atlas its UVs are
                        // laid out on (InteriorAtlas). FM4 itself uses it in 5 of 313 cockpits; leather
                        // (cockpit_leather: the atlas only) is on 262, and the user picked it by hand on the
                        // Focus ZX4 (2026-10-20).
                        if (cockpit && string.Equals(sub.Name, "interior", StringComparison.OrdinalIgnoreCase))
                        {
                            sub.Name = "leather";
                            atlasRenames++;
                        }
                        if (!materials.Contains(sub.Name))
                        {
                            materials.Add(sub.Name);
                        }
                    }
                }
                byte[] data = built.ToBytes();
                if (own != null)
                {
                    car.Replace(own, data);
                }
                else
                {
                    car.Add(name, data);
                }
                this.Line("  {0}: {1} (main file {2}), rebuilt from LOD 1: {3} parts, {4} vertices ({5})", name, (own == null) ? "missing" : "a placeholder of " + vertices + " vertices", mainVertices, parts.Count, built0, string.Join(" ", parts.ToArray()));
                if (atlasRenames > 0)
                {
                    this.Line("  {0}: material interior -> leather on {1} meshes (reads the interior atlas)", name, atlasRenames);
                }
                rebuilt = true;
                lod0Rebuilt |= !cockpit;
                cockpitRebuilt |= cockpit;
                if (cockpit)
                {
                    cockpitModel = built;
                }
            }
            if (!rebuilt)
            {
                return;
            }
            this.traffic = true;
            if (emptyParts.Count > 0)
            {
                this.Line("  close-up models: empty parts left out ({0})", string.Join(" ", emptyParts.ToArray()));
            }
            if (driverSeat != null)
            {
                this.Line("  {0}: the traffic driver is part of it (FM4 draws its own); in the close-up models it is the other seat mirrored", driverSeat);
            }
            // The close-up models sample the _lod0 textures (every FM4 car, 313, has all four); FH traffic cars'
            // are laid out for the placeholders (GMC_YukonXL_01: white wheel arches, another car's hood
            // decal) or missing (lights). The rebuilt models are LOD 1 geometry, mapped on the LOD 1 textures.
            if (lod0Rebuilt)
            {
                foreach (string texture in new string[] { "nodamage", "damage", "lights", "damagelights" })
                {
                    ForzaZip.Entry plain = car.Find(texture + ".xds");
                    if (plain == null)
                    {
                        continue;
                    }
                    ForzaZip.Entry close = car.Find(texture + "_lod0.xds");
                    if (close != null)
                    {
                        car.Replace(close, car.Read(plain));
                    }
                    else
                    {
                        car.Add(texture + "_lod0.xds", car.Read(plain));
                    }
                    this.Line("  {0}_lod0.xds: {1}, now a copy of {0}.xds", texture, (close != null) ? "the placeholder's" : "missing");
                }
            }
            if (cockpitRebuilt && cockpitModel != null)
            {
                this.InteriorAtlas(car, cockpitModel);
            }
            // The placeholders' materials (body_LOD0, hooda_LOD0...) are gone with them: out of the review
            // and of the shader settings.
            List<string> used = new List<string>();
            foreach (ForzaZip.Entry entry in car.Entries)
            {
                string entryName = entry.Name.ToLowerInvariant();
                if (entryName.EndsWith(".carbin") && !entryName.Contains("_caliper") && !entryName.Contains("_rotor"))
                {
                    foreach (CarbinModel.Section section in new CarbinModel(car.Read(entry)).Sections)
                    {
                        foreach (CarbinModel.SubSection sub in section.SubSections)
                        {
                            if (!used.Contains(sub.Name))
                            {
                                used.Add(sub.Name);
                            }
                        }
                    }
                }
            }
            foreach (string material in new List<string>(this.unknownMaterials.Keys))
            {
                if (!used.Contains(material))
                {
                    this.unknownMaterials.Remove(material);
                }
            }
            materials.RemoveAll(delegate(string material) { return !used.Contains(material); });
        }

        // A rebuilt cockpit's interior_lod0.xds is always a copy of nodamage.xds (user 2026-10-20, checked
        // by hand in game: "quando converti traffic prendi il nodamage.xds e fai una copia che chiami
        // interior_lod0.xds"), replacing the source's own when it has one.
        private void InteriorAtlas(ForzaZip car, CarbinModel cockpit)
        {
            ForzaZip.Entry nodamage = car.Find("nodamage.xds");
            if (nodamage == null)
            {
                this.Line("  interior_lod0.xds: no nodamage.xds to copy, left as it is");
                return;
            }
            ForzaZip.Entry interior = car.Find("interior_lod0.xds");
            if (interior != null)
            {
                car.Replace(interior, car.Read(nodamage));
            }
            else
            {
                car.Add("interior_lod0.xds", car.Read(nodamage));
            }
            this.Line("  interior_lod0.xds: {0}, now a copy of nodamage.xds", (interior != null) ? "the source's, replaced" : "missing");
        }

        // FM4's cockpit parts (_cockpit file, never in _lod0; measured on the 313 cars of the game):
        // interior, steering wheel, mirrors, hood, seats, windscreen and front side windows (glass or
        // lexan), the side mirror glass, the A pillars, the roll cage and the gauge needles.
        private static bool CockpitPart(string name)
        {
            string part = Regex.Replace(name, "[0-9]+$", "");
            foreach (string prefix in new string[] { "interior", "steering_wheel", "mirror", "hood", "seat", "a_pillar", "cage", "tach_", "speed_", "fuel_" })
            {
                if (part.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            foreach (string glass in new string[] { "glassF", "glassLF", "glassRF", "glassLM", "glassRM", "lexanF", "lexanLF", "lexanRF" })
            {
                if (string.Equals(part, glass, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        // Anark digital dash layout (.fbf), FH2 version 0x3F0 -> FM4 0x3EF. A text layer is
        //   u32 length + name, 6 bytes, u16 length + default text, 14 bytes, u16 length + font, 33 bytes,
        //   [FH2 only: a float], ...
        // FM4 keeps the default text empty and has no float. The FM4 game reads an FH2 file wrongly past the
        // first text layer (the P1 crashed while loading the race: a 5014-byte copy over its stack).
        // On the 38 dashes both games have, 29 come out byte for byte as FM4's. The others differ in content FH2
        // changed (fonts horizon_dg*, a few values). .bgf/.bsg are the same in both games.
        // Rewritten on AnarkFbf (2026-10-15): the old byte-pattern version assumed an empty tag and an
        // untagged text layer, and broke 9 FH and 6 FH2 dashes (tag "NLOC", 458 Spider, V12 Zagato, RS5...),
        // which FM4 would then read as garbage. Each text record keeps its tag; the 4 bytes dropped are
        // tail bytes 33..36 (the old converter's choice, byte-identical to FM4 on 29 of 38 shared dashes).
        public static byte[] ConvertAnarkLayout(byte[] fh2, out int layers)
        {
            layers = 0;
            if (fh2.Length < 4 || AnarkFbf.ReadU32(fh2, 0) != AnarkFbf.VersionFH2)
            {
                return fh2;
            }
            AnarkFbf fbf = AnarkFbf.Read(fh2);
            fbf.Version = AnarkFbf.VersionFM4;
            foreach (AnarkFbf.Record record in fbf.Records)
            {
                if (record.Type != AnarkFbf.RecordType.Text)
                {
                    continue;
                }
                byte[] tail = new byte[34];
                Array.Copy(record.TextTail, 0, tail, 0, 33);
                tail[33] = record.TextTail[37];
                record.TextTail = tail;
                record.Text = "";
                layers++;
            }
            DashFonts.Fix(fbf, null);
            return fbf.Write();
        }

        // .bgf font names FM4's fontmap does not know become FM4's names (DashFonts); a name with no rule is reported.
        private byte[] FixDashFonts(byte[] bgf, string label)
        {
            int changed;
            List<string> unknown = new List<string>();
            byte[] fixedData = DashFonts.Fix(bgf, out changed, unknown);
            if (changed > 0)
            {
                this.Line("  dash {0}: {1} font names changed to FM4's", label, changed);
            }
            if (unknown.Count > 0)
            {
                this.Line("  WARNING dash {0}: font {1} has no FM4 equivalent rule, FM4 may show a generic font", label, string.Join(", ", unknown.ToArray()));
            }
            return fixedData;
        }

        // The FM3 car's digital dash into the car zip, as FM4 keeps it: digitalgauge/dash_<car>.fbf/.bgf/.bsg
        // and the textures it shows as digitalgauge/<name>.xds (from FM3's Media\UI\Textures.zip, colour
        // textures to FM4's gamma form). A gauge zip can hold more than one dash (Dash_AUD_8_R10_06 has 3):
        // the one named after the car, else after the zip, else the only one.
        private void AddMotorsport3Dash(ForzaZip car)
        {
            string zipPath = Path.Combine(Path.Combine(Path.Combine(this.SourceRoot, "Media"), "DigitalGauges"), this.motorsport3Dash + ".zip");
            if (!File.Exists(zipPath))
            {
                this.Line("  WARNING digital dash: {0} not found, the car goes without it", this.Relative(zipPath));
                return;
            }
            ForzaZip gauges = new ForzaZip(zipPath);
            List<string> stems = new List<string>();
            foreach (ForzaZip.Entry entry in gauges.Entries)
            {
                if (entry.Name.EndsWith(".fbf", StringComparison.OrdinalIgnoreCase))
                {
                    stems.Add(entry.Name.Substring(0, entry.Name.Length - 4));
                }
            }
            string stem = null;
            foreach (string wanted in new string[] { "dash_" + this.MediaName, this.motorsport3Dash })
            {
                foreach (string candidate in stems)
                {
                    if (stem == null && string.Equals(Path.GetFileName(candidate), wanted, StringComparison.OrdinalIgnoreCase))
                    {
                        stem = candidate;
                    }
                }
            }
            if (stem == null && stems.Count == 1)
            {
                stem = stems[0];
            }
            ForzaZip.Entry bgf = (stem != null) ? gauges.Find(stem + ".bgf") : null;
            ForzaZip.Entry bsg = (stem != null) ? gauges.Find(stem + ".bsg") : null;
            if (bgf == null || bsg == null)
            {
                this.Line("  WARNING digital dash: no complete dash in {0} ({1} .fbf files), the car goes without it", Path.GetFileName(zipPath), stems.Count);
                return;
            }
            string texturesPath = Path.Combine(Path.Combine(Path.Combine(this.SourceRoot, "Media"), "UI"), "Textures.zip");
            ForzaZip textures = File.Exists(texturesPath) ? new ForzaZip(texturesPath) : null;
            Dictionary<string, byte[]> images = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            List<string> missing = new List<string>();
            List<string> fromFm4 = new List<string>();
            byte[] fbf = ConvertMotorsport3Dash(gauges.Read(gauges.Find(stem + ".fbf")), this.MediaName, delegate(string path)
            {
                // GAME:\MEDIA\UI\TEXTURES\<rest>.TGA -> Textures.zip <rest>.xds
                const string ui = @"GAME:\MEDIA\UI\TEXTURES\";
                if (!path.StartsWith(ui, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
                string rest = path.Substring(ui.Length).Replace('\\', '/');
                string name = Path.GetFileNameWithoutExtension(rest);
                if (images.ContainsKey(name))
                {
                    return true;
                }
                ForzaZip.Entry texture = (textures != null) ? textures.Find(Path.ChangeExtension(rest, ".xds")) : null;
                if (texture == null)
                {
                    // Not in the source (an FM3 DLC pack has no Textures.zip): the same image as an FM4
                    // car's dash carries it, already in FM4's format.
                    byte[] fm4 = this.Fm4DashImage(name);
                    if (fm4 == null)
                    {
                        missing.Add(path);
                        return false;
                    }
                    images[name] = fm4;
                    fromFm4.Add(name);
                    return true;
                }
                images[name] = ConvertMotorsport3Texture(textures.Read(texture), texture.Name);
                return true;
            });
            string prefix = "digitalgauge/dash_" + this.MediaName.ToLowerInvariant();
            car.Add(prefix + ".fbf", fbf);
            car.Add(prefix + ".bgf", this.FixDashFonts(gauges.Read(bgf), prefix + ".bgf"));
            car.Add(prefix + ".bsg", gauges.Read(bsg));
            foreach (KeyValuePair<string, byte[]> image in images)
            {
                car.Add("digitalgauge/" + image.Key.ToLowerInvariant() + ".xds", image.Value);
            }
            this.Line("  digital dash: {0}/{1} -> {2}.fbf/.bgf/.bsg, {3} textures from Media\\UI\\Textures.zip", Path.GetFileName(zipPath), Path.GetFileName(stem), prefix, images.Count - fromFm4.Count);
            if (fromFm4.Count > 0)
            {
                this.Line("  digital dash: {0} images not in the source, taken from FM4 cars' dashes: {1}", fromFm4.Count, string.Join(", ", fromFm4.ToArray()));
            }
            if (missing.Count > 0)
            {
                this.Line("  WARNING digital dash: {0} images not found, left pointing at FM3's path: {1}", missing.Count, string.Join(", ", missing.ToArray()));
            }
        }

        // An image of an FM4 car's dash (digitalgauge/<name>.xds in the car zips of the target), or null.
        // The index of the target's dash images is built once per target folder. FM3's shared dash
        // images (ui4\dashboards\mfd_oval, mfd_square01...) are carried by the FM4 cars whose dash came
        // from FM3, the same bytes in every car (checked on the Hyundai pack's 19, 2026-10-18).
        private byte[] Fm4DashImage(string name)
        {
            lock (Fm4DashLock)
            {
                if (fm4DashImages == null || !string.Equals(fm4DashRoot, this.TargetRoot, StringComparison.OrdinalIgnoreCase))
                {
                    fm4DashImages = new Dictionary<string, KeyValuePair<string, string>>(StringComparer.OrdinalIgnoreCase);
                    fm4DashRoot = this.TargetRoot;
                    string cars = Path.Combine(Path.Combine(this.TargetRoot, "Media"), "cars");
                    foreach (string zip in Directory.Exists(cars) ? Directory.GetFiles(cars, "*.zip") : new string[0])
                    {
                        try
                        {
                            foreach (ForzaZip.Entry entry in new ForzaZip(zip).Entries)
                            {
                                string entryName = entry.Name.Replace('\\', '/');
                                if (entryName.StartsWith("digitalgauge/", StringComparison.OrdinalIgnoreCase) && entryName.EndsWith(".xds", StringComparison.OrdinalIgnoreCase)
                                    && !fm4DashImages.ContainsKey(Path.GetFileNameWithoutExtension(entryName)))
                                {
                                    fm4DashImages[Path.GetFileNameWithoutExtension(entryName)] = new KeyValuePair<string, string>(zip, entry.Name);
                                }
                            }
                        }
                        catch (Exception)
                        {
                            // A damaged or foreign zip: not a source of dash images.
                        }
                    }
                }
                KeyValuePair<string, string> found;
                if (!fm4DashImages.TryGetValue(name, out found))
                {
                    return null;
                }
                ForzaZip car = new ForzaZip(found.Key);
                return car.Read(car.Find(found.Value));
            }
        }

        private static readonly object Fm4DashLock = new object();
        private static Dictionary<string, KeyValuePair<string, string>> fm4DashImages;
        private static string fm4DashRoot;

        // FM3 dash scene (.fbf 0x3EE) -> FM4 (0x3EF): text tails get FM4's 34th byte (0 on every FM4
        // file), the default text is emptied as in FM4's files (the .bgf holds it), and every image the
        // takeTexture callback accepts points at the car's own digitalgauge folder,
        // GAME:\MEDIA\CARS\<CAR>\DIGITALGAUGE\<NAME>.TGA. .bgf and .bsg are the same format in both
        // games. Checked on the 59 dashes both games have: everything else in the records as FM4's
        // (they differ only where FM4 re-authored the dash or moved its textures).
        public static byte[] ConvertMotorsport3Dash(byte[] data, string mediaName, Predicate<string> takeTexture)
        {
            AnarkFbf fbf = AnarkFbf.Read(data);
            if (!fbf.IsFM3)
            {
                return data;
            }
            fbf.Version = AnarkFbf.VersionFM4;
            foreach (AnarkFbf.Record record in fbf.Records)
            {
                if (record.Type == AnarkFbf.RecordType.Text)
                {
                    byte[] tail = new byte[34];
                    Array.Copy(record.TextTail, 0, tail, 0, 33);
                    record.TextTail = tail;
                    record.Text = "";
                }
                else if (record.Type == AnarkFbf.RecordType.Image && takeTexture != null && takeTexture(record.Path))
                {
                    record.Path = @"GAME:\MEDIA\CARS\" + mediaName.ToUpperInvariant() + @"\DIGITALGAUGE\" + Path.GetFileNameWithoutExtension(record.Path.Replace('\\', '/')).ToUpperInvariant() + ".TGA";
                }
            }
            DashFonts.Fix(fbf, null);
            return fbf.Write();
        }

        // One FH2 model file in FM4 form: converted, FH2-only materials renamed (lod0 lights to the
        // _lod0 variants FM4 uses there), the 'deprecated' material removed.
        // Calipers and rotors (carbin type 1): the same section layout as the car models, a fixed
        // 0x354-byte header whose only FH2/FM4 difference is the format word. 1838 FH2 files convert
        // and re-read as FM4; of the 870 both games have, 630 come out as FM4's but for the vertex
        // tails (SNORM8 -> SNORM16 rounding), the others differ in content FH2 changed (renamed
        // materials such as rotor -> brake, which FM4 also uses, and remodelled parts).
        // Brake materials are not in ShaderSettings.xml; a name no FM4 brake uses is reported.
        public static byte[] ConvertBrake(byte[] data, string name, StringBuilder report)
        {
            CarbinModel model = new CarbinModel(data, true);
            if (model.Type != 1)
            {
                throw new NotSupportedException("not a type 1 carbin (type " + model.Type + ")");
            }
            ConvertToMotorsport4(model);
            List<string> unknown = new List<string>();
            foreach (CarbinModel.Section section in model.Sections)
            {
                foreach (CarbinModel.SubSection sub in section.SubSections)
                {
                    if (Array.IndexOf(BrakeMaterials, sub.Name) < 0 && !unknown.Contains(sub.Name))
                    {
                        unknown.Add(sub.Name);
                    }
                }
            }
            if (unknown.Count > 0 && report != null)
            {
                report.AppendFormat("  brake {0}: material {1} is not used by any FM4 brake", name, string.Join(", ", unknown.ToArray())).AppendLine();
            }
            return model.ToBytes();
        }

        // An FM3 texture in FM4 form. FM3 stores every texture as linear (fetch dword 0 low byte 0x02)
        // in plain DXT1/DXT5 (0x52/0x54); FM4 marks the colour textures gamma (0xFE: sign x/y/z = 3) in the
        // AS_16 formats (0x73/0x75) and leaves only the normal maps linear (4224 FM3, 11775 FM4 car and
        // wheel textures, 2026-10-16). Left linear, FM4 skips the gamma decode and the car looks washed
        // out (BMW X5 in game). The colours differ too: FM3 holds sRGB values, FM4 the same colours
        // re-encoded for the Xbox 360 gamma curve, FM4 = PWL^-1(sRGB to linear(FM3)), alpha unchanged
        // (Supra in both games, 2026-10-20: interior_lod0 mean error 14.6 -> 2.5, bias 0; with the flag
        // alone the dark tones come out about twice as bright). The curve goes on the DXT colour
        // endpoints of every level. FM3's DXT1 normal maps (FM4: DXN) are kept as they are.
        public static byte[] ConvertMotorsport3Texture(byte[] data, string name)
        {
            string file = Path.GetFileNameWithoutExtension(name).ToLowerInvariant();
            if (data.Length < XdsTexture.HeaderSize || file.Contains("_nrm") || file.Contains("normal"))
            {
                return data;
            }
            XdsTexture texture = new XdsTexture(data);
            if (!texture.IsGamma)
            {
                DxtFormat codec = texture.Codec;
                for (int level = 0; level < texture.LevelCount; level++)
                {
                    byte[] blocks = texture.ReadLevelBlocks(level);
                    if (codec == DxtFormat.A8R8G8B8)
                    {
                        for (int i = 0; i < blocks.Length; i += 4)
                        {
                            blocks[i] = SrgbToGamma360[blocks[i]];
                            blocks[i + 1] = SrgbToGamma360[blocks[i + 1]];
                            blocks[i + 2] = SrgbToGamma360[blocks[i + 2]];
                        }
                    }
                    else if (codec == DxtFormat.Dxt1 || codec == DxtFormat.Dxt3 || codec == DxtFormat.Dxt5)
                    {
                        int size = texture.BytesPerBlock;
                        for (int offset = size - 8; offset < blocks.Length; offset += size)
                        {
                            ConvertColorEndpoints(blocks, offset, codec == DxtFormat.Dxt1);
                        }
                    }
                    texture.WriteLevelBlocks(level, blocks);
                }
            }
            byte[] result = texture.ToBytes();
            result[0x1F] = (byte) ((result[0x1F] & 0x03) | 0xFC);   // fetch 0: sign x, y, z = gamma
            int format = result[0x23] & 0x3F;
            if (format == 0x12 || format == 0x14)
            {
                result[0x23] = (byte) ((result[0x23] & 0xC0) | (format + 0x21));   // DXT1/DXT4_5 -> their AS_16 forms
            }
            return result;
        }

        // The two 565 endpoints of a DXT colour block through SrgbToGamma360. A DXT1 block keeps its
        // mode (c0 > c1: four colours, else three + transparent): when the new endpoints swap order they
        // are swapped back with their indices, and a four-colour block whose endpoints became equal
        // points every texel at endpoint 0 (index 3 would turn transparent).
        private static void ConvertColorEndpoints(byte[] blocks, int offset, bool dxt1)
        {
            int c0 = blocks[offset] | (blocks[offset + 1] << 8);
            int c1 = blocks[offset + 2] | (blocks[offset + 3] << 8);
            uint indices = (uint) (blocks[offset + 4] | (blocks[offset + 5] << 8) | (blocks[offset + 6] << 16) | (blocks[offset + 7] << 24));
            int n0 = ConvertEndpoint(c0);
            int n1 = ConvertEndpoint(c1);
            if (dxt1)
            {
                bool four = c0 > c1;
                if (four && n0 == n1)
                {
                    indices = 0;
                }
                else if ((four && n0 < n1) || (!four && n0 > n1))
                {
                    int swap = n0;
                    n0 = n1;
                    n1 = swap;
                    uint swapped = 0;
                    for (int i = 0; i < 16; i++)
                    {
                        uint index = (indices >> (i * 2)) & 3;
                        index = four ? (index ^ 1) : ((index < 2) ? (index ^ 1) : index);   // 0<->1, 2<->3 / 0<->1
                        swapped |= index << (i * 2);
                    }
                    indices = swapped;
                }
            }
            blocks[offset] = (byte) n0;
            blocks[offset + 1] = (byte) (n0 >> 8);
            blocks[offset + 2] = (byte) n1;
            blocks[offset + 3] = (byte) (n1 >> 8);
            blocks[offset + 4] = (byte) indices;
            blocks[offset + 5] = (byte) (indices >> 8);
            blocks[offset + 6] = (byte) (indices >> 16);
            blocks[offset + 7] = (byte) (indices >> 24);
        }

        private static int ConvertEndpoint(int c)
        {
            return (Endpoint5[(c >> 11) & 0x1F] << 11) | (Endpoint6[(c >> 5) & 0x3F] << 5) | Endpoint5[c & 0x1F];
        }

        // sRGB byte -> Xbox 360 gamma byte: sRGB to linear, then the inverse of the PWL curve the
        // texture unit decodes gamma textures with (64 -> 16, 96 -> 32, 128 -> 64, 192 -> 128, 255 -> 255).
        private static readonly byte[] SrgbToGamma360 = BuildSrgbToGamma360();
        private static readonly int[] Endpoint5 = BuildEndpointMap(5);
        private static readonly int[] Endpoint6 = BuildEndpointMap(6);

        private static byte[] BuildSrgbToGamma360()
        {
            byte[] map = new byte[256];
            for (int v = 0; v < 256; v++)
            {
                double c = v / 255.0;
                double l = 255.0 * ((c <= 0.04045) ? (c / 12.92) : Math.Pow((c + 0.055) / 1.055, 2.4));
                double g = (l < 16) ? (l * 4) : (l < 32) ? (64 + ((l - 16) * 2)) : (l < 128) ? (96 + (l - 32)) : (192 + ((l - 128) * 63.0 / 127.0));
                map[v] = (byte) Math.Max(0, Math.Min(255, (int) Math.Round(g)));
            }
            return map;
        }

        // A 5 or 6 bit endpoint channel through SrgbToGamma360, back to the nearest code of that width.
        private static int[] BuildEndpointMap(int bits)
        {
            int count = 1 << bits;
            int[] map = new int[count];
            for (int code = 0; code < count; code++)
            {
                int target = SrgbToGamma360[(code << (8 - bits)) | (code >> ((2 * bits) - 8))];
                int best = 0;
                for (int other = 1; other < count; other++)
                {
                    int expanded = (other << (8 - bits)) | (other >> ((2 * bits) - 8));
                    int bestExpanded = (best << (8 - bits)) | (best >> ((2 * bits) - 8));
                    if (Math.Abs(expanded - target) < Math.Abs(bestExpanded - target))
                    {
                        best = other;
                    }
                }
                map[code] = best;
            }
            return map;
        }

        // A source model (read with the FM3 opt-in) in FM4 form: FM3 through its own converter, FH (and
        // FM4 files, which it leaves as they are) through ConvertHorizonToMotorsport4.
        private static void ConvertToMotorsport4(CarbinModel model)
        {
            if (model.IsMotorsport3)
            {
                model.ConvertMotorsport3ToMotorsport4();
            }
            else
            {
                model.ConvertHorizonToMotorsport4();
            }
        }

        // Every material name of the 2664 FM4 type-1 carbins (car zips and Media\brakes).
        private static readonly string[] BrakeMaterials = new string[]
        {
            "back", "badge", "black_noOcclude", "brake", "brake_bump_metal", "brake_bump_metal2", "brake_bump_metal3",
            "brake_bump_metal4", "brake_chrome", "brake_smooth_metal", "caliper", "caliper_", "caliper_noOcclude",
            "caliperLF_LOD0", "caliperLR_LOD0", "caliperRF_LOD0", "caliperRR_LOD0", "chrome", "chrome_2", "chrome_noOcclude",
            "detail_glass_clear", "drum", "drum_", "front", "hub", "hub_", "left_side", "Material__2", "Material__4",
            "Material__5", "matte_colors", "matte_colors_NoOcclude_NoShadow", "matte_colors_NoOcclude_NoShadows", "misc",
            "misc_noOcclude", "misc_NoOcclude_NoShadow", "outer_rim", "right_side", "rotor", "rotor_", "top", "undercarriage",
        };

        // True when some FM4 car uses the material in a model of this carbin type (1 brakes, else models).
        public static bool IsFm4Material(string name, int type)
        {
            return (type == 1) ? Array.IndexOf(BrakeMaterials, name) >= 0 : Fm4MaterialUse.UsedAnywhere(name);
        }

        // The FM4 material names offered for a carbin type, sorted, for picking a replacement (models: the
        // common ones, CommonMaterials).
        public static string[] Fm4Materials(int type)
        {
            if (type != 1)
            {
                // only what some FM4 car model really uses (not lights_amber and the like)
                List<string> used = new List<string>();
                foreach (string name in CommonMaterials)
                {
                    if (Fm4MaterialUse.UsedAnywhere(name))
                    {
                        used.Add(name);
                    }
                }
                used.Sort(StringComparer.OrdinalIgnoreCase);
                return used.ToArray();
            }
            string[] names = (string[]) BrakeMaterials.Clone();
            Array.Sort(names, StringComparer.OrdinalIgnoreCase);
            return names;
        }

        // The FM4 name MaterialMap gives an FH2 material, or the FM4 name differing only in case; null if none.
        public static string SuggestMaterial(string name, string fileName)
        {
            string to;
            if (MapMaterial(name, out to) && to != null)
            {
                return LodVariant(to, fileName);
            }
            foreach (string known in Fm4MaterialUse.CarMaterials())
            {
                if (string.Equals(known, name, StringComparison.OrdinalIgnoreCase))
                {
                    return known;
                }
            }
            return null;
        }

        // The name a light material has in a given model file of an FM4 car: tail_light_lod0 in _lod0 and
        // _cockpit (FM4 cockpits never use tail_light), reverse_light_lod0 in _lod0, the plain name in the main model.
        public static string LodVariant(string material, string fileName)
        {
            string plain = (material == "tail_light_lod0" || material == "reverse_light_lod0") ? material.Substring(0, material.Length - 5) : material;
            bool lod0 = fileName.EndsWith("_lod0.carbin", StringComparison.OrdinalIgnoreCase);
            bool cockpit = fileName.EndsWith("_cockpit.carbin", StringComparison.OrdinalIgnoreCase);
            if ((plain == "tail_light" && (lod0 || cockpit)) || (plain == "reverse_light" && lod0))
            {
                return plain + "_lod0";
            }
            return plain;
        }

        // Every material name of the original FM4 car models (.carbin, _lod0, _cockpit of the 317 car zips of
        // the untouched game, 943 models, 150 names; fm4dev\DEV_DLC\tools\matcount, 2026-10-16). An FH material
        // outside this list (after MaterialMap) has no FM4 shader and renders black. The list measured before
        // (on a game holding ported cars) also had 25 FH names (lights_gls_*, fogred, oldhead...) that were
        // then taken for FM4 ones and never reported.
        private static readonly string[] ModelMaterials = new string[]
        {
            "01___Default", "4_MER_SLR_05_damage", "ADV_RGII_Wheel_LOD0", "badge", "badge_chrome", "badge2", "black",
            "black_glass", "black_glass_breakable", "blur_lip", "blur_rim", "body", "body_2", "body_3", "bottom",
            "brake", "brushed_metal", "bump_leather", "bump_leather2", "bump_leather3", "bump_metal", "bump_metal2",
            "bump_metal3", "bump_metal4", "bump_plastic", "bump_plastic2", "bump_plastic3", "bump_plastic4",
            "bump_plastic5", "bump_stitching", "bump_stitchingA", "bumper_frame", "bumperFa_LOD1", "carbon_fiber",
            "carbon_fiber_no_env", "carbon_fiber2", "carbon_fiber3", "chrome", "chrome_2", "chrome_3",
            "chrome_blur_lip", "chrome_blur_rim", "chrome_bump", "chrome_rim", "chrome2", "cloth", "defrost_lines",
            "detail_glass_amber", "detail_glass_clear", "detail_glass_red", "digital_gauge", "drum", "emblem",
            "embossed_metal", "embossed_plastic", "frame", "gauge_emissive", "gauge_needle", "glass", "grille1",
            "grille1_alpha", "grille2", "grille2_alpha", "grille3", "head_light", "headlight_paint", "headliner",
            "headliner2", "inner_rim", "interior", "leather", "lights_amber", "lights_chrome", "lights_glass",
            "lights_metal", "lights_plastic", "liveryBadge", "Material__0", "Material__1", "Material__11",
            "Material__12", "Material__14", "Material__15", "Material__16", "Material__3", "Material__6",
            "matt_colors", "matt_colors2", "matte_colors", "metal", "metal2", "mirrorLeft", "mirrorMiddle",
            "mirrorRight", "misc", "misc2", "mottled", "mottled2", "outer_rim", "plastic", "Plastic2", "plastic2",
            "race_kit_red", "red_glass", "reflector", "reverse_light", "reverse_light_lod0", "rim", "rim_black",
            "rim_bump_metal", "rim_chrome", "rim_chrome_blur_lip", "rim_inner_rim", "rim_outer_rim",
            "rim_wheel_emblem", "rubber", "rubber_trim", "smooth_metal", "smooth_plastic", "solid", "stitching",
            "stripe_badge", "suede", "tail_light", "tail_light_lod0", "Tail_light_lod0", "tail_light_on",
            "tail_light2", "taillightL_LOD1", "taillightL_LOD5", "taillightR_LOD1", "taillightR_LOD5",
            "textured_reflector", "tiling_cloth", "tiling_cloth2", "tire", "undercarriage", "unicolor_carbon_fiber",
            "weatherstrip", "wheel_black", "wheel_emblem", "white_brake", "window", "wingz_LOD0", "wingz_LOD1",
            "wingz_LOD2", "wingz_LOD3", "wingz_LOD4", "wingz_LOD5", "wood",
        };

        // The materials offered as fixes: those of at least 50 of the 317 FM4 cars (67 names). The rare ones
        // (one-off names such as Material__11 or wingz_LOD3) stay valid but are not offered.
        private static readonly string[] CommonMaterials = new string[]
        {
            "badge", "black", "black_glass", "blur_lip", "blur_rim", "body", "body_2", "bottom", "bump_leather",
            "bump_leather2", "bump_plastic", "bump_plastic2", "bump_stitching", "bumper_frame", "carbon_fiber",
            "chrome", "chrome_2", "chrome_rim", "cloth", "defrost_lines", "detail_glass_amber", "detail_glass_clear",
            "detail_glass_red", "digital_gauge", "emblem", "frame", "gauge_emissive", "gauge_needle", "glass",
            "grille1", "grille1_alpha", "grille2", "grille2_alpha", "head_light", "headliner", "inner_rim",
            "interior", "leather", "lights_glass", "matte_colors", "metal", "mirrorLeft", "mirrorMiddle",
            "mirrorRight", "misc", "mottled", "outer_rim", "plastic", "plastic2", "race_kit_red", "reflector",
            "reverse_light", "reverse_light_lod0", "rim", "rubber", "rubber_trim", "solid", "tail_light",
            "tail_light_lod0", "textured_reflector", "tire", "undercarriage", "weatherstrip", "wheel_black",
            "wheel_emblem", "window", "wood",
        };

        public static byte[] ConvertModel(byte[] data, bool lod0, StringBuilder report)
        {
            return ConvertModel(data, lod0, report, null);
        }

        public static byte[] ConvertModel(byte[] data, bool lod0, StringBuilder report, List<string> materials)
        {
            return ConvertModel(data, lod0, report, materials, null, null);
        }

        // An empty part: a section standing for a part the car does not have, drawn as a few cm of geometry
        // with the material <section>_LOD<n>, which no shader has (FM4 draws it with 'missing', too small
        // to see). FH cars use it (BUS_TOUR_12: headlightL, winga, glassT, seatR... as 0.2 m cubes at the
        // origin; also LAM_ReventonRoadster_09, AUD_RS3_11) and so do FM4's own (BMW_M3E30_91 wingz_LOD1,
        // LAN_Stratos_74 bumperFa_LOD1; wingz_LOD0 in two _lod0 files, none in the 313 _cockpit files).
        // Measured 2026-10-18; the largest box seen is 0.33 m. In a section with real parts the empty part is
        // a LOD 2-5 subsection only (wheel_LOD5, seatL_LOD4, bumperFrameR_LOD5: the 0.2 m cube, 23 indices;
        // FM4's PON_FirebirdTransAm_77 taillightL_LOD5: 29), told by its few indices.
        private const float EmptyPartSize = 0.35f;
        private const int EmptyPartIndices = 64;

        public static bool IsEmptyPart(CarbinModel.Section section, string material)
        {
            float[] b = section.Bounds;
            return Regex.IsMatch(material, "^" + Regex.Escape(section.Name) + "_LOD\\d$", RegexOptions.IgnoreCase)
                && b[6] - b[3] <= EmptyPartSize && b[7] - b[4] <= EmptyPartSize && b[8] - b[5] <= EmptyPartSize;
        }

        public static bool IsEmptyPart(CarbinModel.Section section, CarbinModel.SubSection sub)
        {
            return IsEmptyPart(section, sub.Name)
                || (Regex.IsMatch(sub.Name, "^" + Regex.Escape(section.Name) + "_LOD\\d$", RegexOptions.IgnoreCase) && sub.Lod >= 2 && sub.Indices.Count <= EmptyPartIndices);
        }

        // True when every LOD 1 subsection of the section is an empty part.
        private static bool IsEmptyPartSection(CarbinModel.Section section)
        {
            bool any = false;
            foreach (CarbinModel.SubSection sub in section.SubSections)
            {
                if (sub.Lod != 1)
                {
                    continue;
                }
                if (!IsEmptyPart(section, sub.Name))
                {
                    return false;
                }
                any = true;
            }
            return any;
        }

        // 'materials' (when given) collects the material names the converted model uses; 'renames' are
        // the user's fixes from the review (FH material -> FM4 material, before MaterialMap); 'unknown'
        // collects the materials no FM4 car uses that are left, with the sections they sit on.
        public static byte[] ConvertModel(byte[] data, bool lod0, StringBuilder report, List<string> materials, Dictionary<string, string> renames, Dictionary<string, List<string>> unknownOut)
        {
            return ConvertModel(data, lod0, report, materials, renames, unknownOut, null);
        }

        // damageReference: the same car's FM4 model, whose per-vertex damage words replace the source's (B1 test T4).
        public static byte[] ConvertModel(byte[] data, bool lod0, StringBuilder report, List<string> materials, Dictionary<string, string> renames, Dictionary<string, List<string>> unknownOut, CarbinModel damageReference)
        {
            CarbinModel model = new CarbinModel(data, true);
            ConvertToMotorsport4(model);
            if (damageReference != null && !model.IsMotorsport3)
            {
                string line = model.BorrowDamageWords(damageReference);
                if (report != null)
                {
                    report.AppendLine("  " + line);
                }
            }
            Dictionary<string, int> renamed = new Dictionary<string, int>();
            List<string[]> remove = new List<string[]>();
            foreach (CarbinModel.Section section in model.Sections)
            {
                foreach (CarbinModel.SubSection sub in section.SubSections)
                {
                    string to;
                    if (renames != null && renames.TryGetValue(sub.Name, out to))
                    {
                        to = (lod0 && to == "tail_light") ? "tail_light_lod0" : to;
                    }
                    else if (!AutoMap(sub.Name, out to))
                    {
                        continue;
                    }
                    if (to == null)
                    {
                        remove.Add(new string[] { section.Name, sub.Name });
                        continue;
                    }
                    if (lod0)
                    {
                        to = LodVariant(to, "x_lod0.carbin");   // tail_light_lod0, reverse_light_lod0
                    }
                    int count;
                    renamed.TryGetValue(sub.Name + " -> " + to, out count);
                    renamed[sub.Name + " -> " + to] = count + 1;
                    sub.Name = to;
                }
            }
            foreach (string[] pair in remove)
            {
                CarbinModel.Section owner = model.Find(pair[0]);
                if (owner.SubSections.Count > 1)
                {
                    model.RemoveSubSections(pair[0], pair[1]);
                }
                else
                {
                    owner.SubSections[0].Name = "black";
                }
            }
            if (materials != null)
            {
                foreach (CarbinModel.Section section in model.Sections)
                {
                    foreach (CarbinModel.SubSection sub in section.SubSections)
                    {
                        if (!materials.Contains(sub.Name))
                        {
                            materials.Add(sub.Name);
                        }
                    }
                }
            }
            if (report != null && renamed.Count > 0)
            {
                List<string> text = new List<string>();
                foreach (KeyValuePair<string, int> pair in renamed)
                {
                    text.Add(pair.Key + " (" + pair.Value + ")");
                }
                report.AppendLine("  materials: " + string.Join(", ", text.ToArray()) + ((remove.Count > 0) ? string.Format(", {0} 'deprecated' removed", remove.Count) : ""));
            }
            if (report != null)
            {
                // Material -> the sections it sits on.
                Dictionary<string, List<string>> unknown = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                List<string> order = new List<string>();
                foreach (CarbinModel.Section section in model.Sections)
                {
                    foreach (CarbinModel.SubSection sub in section.SubSections)
                    {
                        // what FM4 cars really use in a model of this LOD (Fm4MaterialUse); having a compiled
                        // shader is not enough: lights_amber has one and renders black on a normal car
                        if (Fm4MaterialUse.UsedByCarModel(sub.Lod, sub.Name))
                        {
                            continue;
                        }
                        List<string> sections;
                        if (!unknown.TryGetValue(sub.Name, out sections))
                        {
                            sections = new List<string>();
                            unknown[sub.Name] = sections;
                            order.Add(sub.Name);
                        }
                        if (!sections.Contains(section.Name))
                        {
                            sections.Add(section.Name);
                        }
                    }
                }
                if (order.Count > 0)
                {
                    List<string> text = new List<string>();
                    foreach (string name in order)
                    {
                        text.Add(name + " (" + string.Join(" ", unknown[name].ToArray()) + ")");
                        if (unknownOut != null)
                        {
                            List<string> all;
                            if (!unknownOut.TryGetValue(name, out all))
                            {
                                all = new List<string>();
                                unknownOut[name] = all;
                            }
                            foreach (string section in unknown[name])
                            {
                                if (!all.Contains(section))
                                {
                                    all.Add(section);
                                }
                            }
                        }
                    }
                    report.AppendLine("  WARNING materials no FM4 car uses, may render black: " + string.Join(", ", text.ToArray()));
                }
            }
            return model.ToBytes();
        }

        // FM4 CarAttribs version 17: the donor's file with the car's own gauges, driver placement,
        // part drops and animations (FH2 version 21 writes <animation Version="2" ... inrace="1"/>,
        // FM4 <Animation ... inrace="true" />; FH2 repeats each one per LOD). donorKept (may be null)
        // gets the donor's elements that stay and differ from the car's own (DonorElements).
        public static byte[] ConvertCarAttributes(string fh2, string fm4Donor, bool animations, StringBuilder report, List<string> donorKept)
        {
            return ConvertCarAttributes(fh2, fm4Donor, animations, report, donorKept, false);
        }

        // horizon: the car comes from FH1 / FH2, whose crash damage is raised (Port.ini [Damage] ScalarBonus; FM3 is fine as it is).
        public static byte[] ConvertCarAttributes(string fh2, string fm4Donor, bool animations, StringBuilder report, List<string> donorKept, bool horizon)
        {
            byte[] converted = ConvertCarAttributes(fh2, fm4Donor, animations, report, horizon);
            if (donorKept != null)
            {
                string text = Encoding.UTF8.GetString(converted);
                donorKept.AddRange(DonorElements(text, fh2));
                if (!fh2.Contains("<PartDropValues>") && text.Contains("<PartDropValues>"))
                {
                    donorKept.Add("Damage PartDropValues: the donor's (the car's file has none)");
                }
            }
            return converted;
        }

        // The top-level elements of the converted CarAttribs (but Animations, always the car's own or
        // none, and Damage and CarDebris, the car's or FM4's standard, ConvertDamage) that the car's
        // own file lacks or has with other values: what stays the donor's. Gauges the car has count as
        // its own (their repeated tick marks are cut). Compared as parsed XML, so only whitespace is
        // ignored: "0.0" against "0.000000" counts as different.
        private static List<string> DonorElements(string converted, string source)
        {
            List<string> kept = new List<string>();
            XmlDocument result = new XmlDocument();
            XmlDocument own = new XmlDocument();
            try
            {
                result.LoadXml(converted.Trim());
                own.LoadXml(source.Trim());
            }
            catch (XmlException ex)
            {
                kept.Add("compared not: " + ex.Message + " (the donor's file is the base, only gauges, driver, part drops and animations are the car's)");
                return kept;
            }
            foreach (XmlNode node in result.DocumentElement.ChildNodes)
            {
                XmlElement element = node as XmlElement;
                if (element == null || element.Name == "Animations" || element.Name == "Damage" || element.Name == "CarDebris")
                {
                    continue;
                }
                XmlElement mine = own.DocumentElement[element.Name];
                if (mine == null)
                {
                    kept.Add(element.Name + ": the donor's (the car's file has none)");
                }
                else if (element.Name != "Gauges" && mine.OuterXml != element.OuterXml)
                {
                    kept.Add(element.Name + ": the donor's (the car's differs)");
                }
            }
            return kept;
        }

        public static byte[] ConvertCarAttributes(string fh2, string fm4Donor, bool animations, StringBuilder report)
        {
            return ConvertCarAttributes(fh2, fm4Donor, animations, report, false);
        }

        public static byte[] ConvertCarAttributes(string fh2, string fm4Donor, bool animations, StringBuilder report, bool horizon)
        {
            fh2 = Regex.Replace(fh2, "<(\\w+) NumTicks=\"\\d+\">(.*?)</\\1>", delegate(Match gauge)
            {
                // Repeated marks kept once (in order), at most MaxTicks.
                List<string> ticks = new List<string>();
                MatchCollection marks = Regex.Matches(gauge.Groups[2].Value, "<TickMark\\b[^>]*/>");
                foreach (Match mark in marks)
                {
                    if (!ticks.Contains(mark.Value) && ticks.Count < MaxTicks)
                    {
                        ticks.Add(mark.Value);
                    }
                }
                if (ticks.Count == marks.Count)
                {
                    return gauge.Value;
                }
                if (report != null)
                {
                    report.AppendLine(string.Format("  carattribs: {0} gauge {1} -> {2} tick marks", gauge.Groups[1].Value, marks.Count, ticks.Count));
                }
                Match indent = Regex.Match(gauge.Groups[2].Value, "\\r?\\n([ \\t]*)<TickMark");
                string line = indent.Success ? indent.Value.Substring(0, indent.Value.Length - "<TickMark".Length) : "\r\n      ";
                Match close = Regex.Match(gauge.Groups[2].Value, "\\r?\\n[ \\t]*$");
                return "<" + gauge.Groups[1].Value + " NumTicks=\"" + ticks.Count + "\">" + line + string.Join(line, ticks.ToArray()) + (close.Success ? close.Value : "") + "</" + gauge.Groups[1].Value + ">";
            }, RegexOptions.Singleline);
            string result = fm4Donor;
            foreach (string element in new string[] { "DriverHandsSteeringWheelOverride", "DriverHandsOffset", "SteeringWheelAngleOverride", "Driver" })
            {
                Match from = Regex.Match(fh2, "<" + element + "\\b[^>]*/>");
                Match to = Regex.Match(result, "<" + element + "\\b[^>]*/>");
                if (from.Success && to.Success)
                {
                    result = result.Substring(0, to.Index) + from.Value + result.Substring(to.Index + to.Length);
                }
            }
            foreach (string block in new string[] { "Gauges", "PartDropValues" })
            {
                Match from = Regex.Match(fh2, "<" + block + ">.*?</" + block + ">", RegexOptions.Singleline);
                Match to = Regex.Match(result, "<" + block + ">.*?</" + block + ">", RegexOptions.Singleline);
                if (from.Success && to.Success)
                {
                    result = result.Substring(0, to.Index) + from.Value + result.Substring(to.Index + to.Length);
                }
            }
            result = ConvertDamage(result, fh2, report, horizon);
            Match target = Regex.Match(result, "[ \t]*<Animations>.*?</Animations>\r?\n?", RegexOptions.Singleline);
            if (!animations)
            {
                // No animation files: no <Animations> element (as on FM4 cars without moving parts).
                return Encoding.UTF8.GetBytes(target.Success ? result.Remove(target.Index, target.Length) : result);
            }
            Match source = Regex.Match(fh2, "<Animations>.*?</Animations>", RegexOptions.Singleline);
            if (!source.Success || !Regex.IsMatch(source.Value, "<animation\\b", RegexOptions.IgnoreCase))
            {
                // The car has no animations of its own: the donor's would point at bones and .gr2 files
                // the car lacks (AUD_R8GT_11 got the R8 V10's 12, doors and active wing, and crashed
                // the game while loading its model in the showroom).
                return Encoding.UTF8.GetBytes(target.Success ? result.Remove(target.Index, target.Length) : result);
            }
            target = Regex.Match(result, "<Animations>.*?</Animations>", RegexOptions.Singleline);
            if (target.Success)
            {
                StringBuilder list = new StringBuilder("<Animations>\r\n");
                List<string> seen = new List<string>();
                foreach (Match animation in Regex.Matches(source.Value, "<animation\\b([^>]*)/>", RegexOptions.IgnoreCase))
                {
                    string attributes = Regex.Replace(animation.Groups[1].Value, "\\s*Version=\"[^\"]*\"", "");
                    attributes = Regex.Replace(attributes, "inrace=\"1\"", "inrace=\"true\"");
                    attributes = attributes.Trim();
                    if (seen.Contains(attributes))
                    {
                        continue;
                    }
                    seen.Add(attributes);
                    list.Append("    <Animation " + attributes + " />\r\n");
                }
                list.Append("  </Animations>");
                result = result.Substring(0, target.Index) + list.ToString() + result.Substring(target.Index + target.Length);
            }
            return Encoding.UTF8.GetBytes(result);
        }

        // Damage tuning (2026-10-17, CarAttribs of all FM4/FH2/FH1/FM3 cars read, fm4dev\DonorLog\dmg_*.tsv).
        // FM4's standard, on 247 of 315 cars: DamageScalars at 5/35/98 (speed, m/s by the 98 top: 353
        // km/h) giving damage 0.5/1.5/2.5, PartDropGlobalScalar 1, DamageScrape from 0.01 to 0.3 with
        // ScrapeDamage 0.08, CarDebris with its four piece kinds. FH1 and FH2 have a soft preset no FM4
        // car uses on ~138 cars each (the P1): damage 1.0 only at 200 m/s, PartDropGlobalScalar 0.15,
        // scrape from 500 (off), CarDebris empty: the car stays whole after a crash at 200 km/h. The
        // car's own values are kept, each part that is softer than FM4's goes to FM4's standard:
        //   scalars   damage at 200 km/h (55.6 m/s, linear between the points) under 1.7; the common
        //             FM4 curves give 1.83 and 1.71, FH's soft one 0.30, the 60/120/180 one 0.2, the
        //             zeroed blocks (uninitialised, 18 FM4 and 37 FM3 cars) 0;
        //   value     the Damage multiplier outside 1..10 (zeroed blocks have 1.7e29);
        //   drops     PartDropGlobalScalar under 1 (every FM4 car with a real block has 1);
        //   scrape    starting over 0.3 or ScrapeDamage under 0.08, or missing;
        //   debris    CarDebris empty or missing.
        // The car's PartDropValues stay (the donor's only when the car has none).
        private const double DamageSpeed = 200 / 3.6;
        private const double MinimumDamage = 1.7;
        private static readonly double[][] StandardScalars = { new double[] { 5, 0.3, 0.5 }, new double[] { 35, 1.3, 1.5 }, new double[] { 98, 3.2, 2.5 } };
        private const string StandardScrape = "<DamageScrape ScrapeMinSpeed=\"0.010000\" ScrapeMaxSpeed=\"0.300000\" ScrapeRadius=\"1.300000\" ScrapeDamage=\"0.080000\" DamageBias=\"0.750000\" ScrapeBias=\"0.700000\" GlassDamageCausingBreak=\"0.005000\" />";
        private static readonly string[] StandardDebris = { "Mechanical", "Fiber", "Frame", "Crumb" };

        private static string ConvertDamage(string result, string source, StringBuilder report, bool horizon)
        {
            string newline = result.Contains("\r\n") ? "\r\n" : "\n";
            List<string> notes = new List<string>();

            // CarDebris
            Match sourceDebris = Regex.Match(source, "<CarDebris\\s*/>|<CarDebris>.*?</CarDebris>", RegexOptions.Singleline);
            Match debris = Regex.Match(result, "([ \\t]*)(<CarDebris\\s*/>|<CarDebris>.*?</CarDebris>)", RegexOptions.Singleline);
            if (debris.Success)
            {
                string indent = debris.Groups[1].Value;
                string text;
                if (sourceDebris.Success && Regex.IsMatch(sourceDebris.Value, "<\\w+\\b[^>]*MaxDropCount"))
                {
                    text = sourceDebris.Value;
                }
                else
                {
                    StringBuilder pieces = new StringBuilder("<CarDebris>" + newline);
                    foreach (string piece in StandardDebris)
                    {
                        pieces.Append(indent + "  <" + piece + " MaxDropCount_DamageCosmetic=\"1000\" MaxDropCount_DamageLimited=\"1000\" MaxDropCount_DamageFull=\"1000\" MinDropForce=\"1.000000\" MaxDropForce=\"100.000000\" Area=\"\" />" + newline);
                    }
                    text = pieces.Append(indent + "</CarDebris>").ToString();
                    notes.Add("debris FM4 standard (the car's " + (sourceDebris.Success ? "is empty" : "file has none") + ")");
                }
                result = result.Substring(0, debris.Groups[2].Index) + text + result.Substring(debris.Groups[2].Index + debris.Groups[2].Length);
            }

            // Damage
            Match sourceDamage = Regex.Match(source, "<Damage\\b[^>]*?(/>|>.*?</Damage>)", RegexOptions.Singleline);
            Match damage = Regex.Match(result, "([ \\t]*)(<Damage\\b[^>]*?(/>|>.*?</Damage>))", RegexOptions.Singleline);
            string own = sourceDamage.Success ? sourceDamage.Value : "";
            string ownTag = Regex.Match(own, "^<Damage\\b[^>]*>").Value;

            double value = Number(Attribute(ownTag, "value"));
            string valueText = (value >= 1 && value <= 10) ? Attribute(ownTag, "value") : "1.000000";
            if (valueText != Attribute(ownTag, "value"))
            {
                notes.Add("multiplier 1 (the car's " + (Attribute(ownTag, "value") ?? "none") + ")");
            }

            List<double[]> points = new List<double[]>();
            List<string> scalarLines = new List<string>();
            foreach (Match scalar in Regex.Matches(own, "<DamageScalars\\b[^>]*/>"))
            {
                points.Add(new double[] { Number(Attribute(scalar.Value, "speed")), Number(Attribute(scalar.Value, "radius")), Number(Attribute(scalar.Value, "damage")) });
                scalarLines.Add(scalar.Value);
            }
            double at = DamageAt(points, DamageSpeed);
            if (!(at >= MinimumDamage))
            {
                scalarLines.Clear();
                foreach (double[] point in StandardScalars)
                {
                    scalarLines.Add(string.Format(CultureInfo.InvariantCulture, "<DamageScalars speed=\"{0:0.000000}\" radius=\"{1:0.000000}\" damage=\"{2:0.000000}\" />", point[0], point[1], point[2]));
                }
                notes.Add(string.Format(CultureInfo.InvariantCulture, "scalars FM4 standard (the car's: {0} at 200 km/h, under {1})", (points.Count > 0) ? at.ToString("0.00", CultureInfo.InvariantCulture) : "none", MinimumDamage));
            }

            // User 2026-10-27 (Bowler in a pack, C7 Stingray): +1 on the damage of every DamageScalars point deforms
            // a ported car much better than FM4's standard curve.
            double bonus = horizon ? PortSettings.GetDouble("Damage", "ScalarBonus", 1) : 0;
            if (bonus != 0)
            {
                for (int i = 0; i < scalarLines.Count; i++)
                {
                    string line = scalarLines[i];
                    scalarLines[i] = Regex.Replace(line, "\\bdamage=\"([^\"]*)\"", delegate(Match m)
                    {
                        return "damage=\"" + (Number(m.Groups[1].Value) + bonus).ToString("0.000000", CultureInfo.InvariantCulture) + "\"";
                    });
                }
                notes.Add(string.Format(CultureInfo.InvariantCulture, "scalar damage {0:+0.##;-0.##} on every point (Port.ini [Damage] ScalarBonus)", bonus));
            }

            Match drops = Regex.Match(damage.Success ? damage.Value : "", "<PartDropValues>.*?</PartDropValues>", RegexOptions.Singleline);
            if (!drops.Success)
            {
                drops = Regex.Match(own, "<PartDropValues>.*?</PartDropValues>", RegexOptions.Singleline);
            }

            Match global = Regex.Match(own, "<PartDropGlobalScalar\\b[^>]*/>");
            string globalLine = global.Value;
            if (!(Number(Attribute(global.Value, "value")) >= 1 && Number(Attribute(global.Value, "value")) <= 10))
            {
                globalLine = "<PartDropGlobalScalar value=\"1.000000\" />";
                notes.Add("part drop scale 1 (the car's " + (Attribute(global.Value, "value") ?? "none") + ")");
            }

            Match scrape = Regex.Match(own, "<DamageScrape\\b[^>]*/>");
            string scrapeLine = scrape.Value;
            double scrapeFrom = Number(Attribute(scrape.Value, "ScrapeMinSpeed"));
            double scrapeDamage = Number(Attribute(scrape.Value, "ScrapeDamage"));
            double scrapeRadius = Number(Attribute(scrape.Value, "ScrapeRadius"));
            if (!(scrapeFrom >= 0 && scrapeFrom <= 0.3 && scrapeDamage >= 0.08 && scrapeRadius > 0 && scrapeRadius <= 10))
            {
                scrapeLine = StandardScrape;
                notes.Add(scrape.Success ? string.Format(CultureInfo.InvariantCulture, "scrape FM4 standard (the car's from {0}, damage {1})", Attribute(scrape.Value, "ScrapeMinSpeed"), Attribute(scrape.Value, "ScrapeDamage")) : "scrape FM4 standard (the car's file has none)");
            }

            string at0 = damage.Success ? damage.Groups[1].Value : "  ";
            string inner = at0 + "  ";
            StringBuilder block = new StringBuilder("<Damage Version=\"3\" value=\"" + valueText + "\">" + newline);
            foreach (string line in scalarLines)
            {
                block.Append(inner + line + newline);
            }
            if (drops.Success)
            {
                block.Append(inner + drops.Value + newline);
            }
            block.Append(inner + globalLine + newline);
            block.Append(inner + scrapeLine + newline);
            block.Append(at0 + "</Damage>");
            if (damage.Success)
            {
                result = result.Substring(0, damage.Groups[2].Index) + block.ToString() + result.Substring(damage.Groups[2].Index + damage.Groups[2].Length);
            }
            else
            {
                Match before = Regex.Match(result, "[ \\t]*<Tires\\b|</CarAttributes>");
                if (before.Success)
                {
                    result = result.Substring(0, before.Index) + at0 + block.ToString() + newline + result.Substring(before.Index);
                }
            }

            if (report != null)
            {
                report.AppendLine("  carattribs damage: " + ((notes.Count == 0) ? "the car's own" : "the car's, but " + string.Join("; ", notes.ToArray())));
            }
            return result;
        }

        // Damage at a speed on the DamageScalars curve: linear between the points, the end values past them.
        private static double DamageAt(List<double[]> points, double speed)
        {
            if (points.Count == 0)
            {
                return 0;
            }
            List<double[]> sorted = new List<double[]>(points);
            sorted.Sort(delegate(double[] a, double[] b) { return a[0].CompareTo(b[0]); });
            if (speed <= sorted[0][0])
            {
                return sorted[0][2];
            }
            for (int i = 1; i < sorted.Count; i++)
            {
                if (speed <= sorted[i][0])
                {
                    double[] a = sorted[i - 1];
                    double[] b = sorted[i];
                    return (b[0] > a[0]) ? a[2] + (b[2] - a[2]) * (speed - a[0]) / (b[0] - a[0]) : b[2];
                }
            }
            return sorted[sorted.Count - 1][2];
        }

        private static string Attribute(string element, string name)
        {
            Match match = Regex.Match(element ?? "", "\\b" + Regex.Escape(name) + "=\"([^\"]*)\"");
            return match.Success ? match.Groups[1].Value : null;
        }

        private static double Number(string text)
        {
            double number;
            return (text != null && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) ? number : double.NaN;
        }

        // FM4 ShaderSettings.xml: only the materials the converted models use (FH2-only ones under
        // their FM4 names, tail lights also for the _lod0 variant) and only the parameters FM4 has.
        // FM4 cars list at most 24 materials; the FH2 P1 lists 33, most for materials it renamed.
        // 'renames' are the review's material choices, which the models get before MaterialMap
        // (ConvertModel): without them an entry such as fullbeam stayed under its FH name, no model
        // used it any more and it was dropped, so the chosen FM4 material lost the car's values.
        public static byte[] ConvertShaderSettings(string fh2, List<string> materials, StringBuilder report)
        {
            return ConvertShaderSettings(fh2, materials, null, report);
        }

        public static byte[] ConvertShaderSettings(string fh2, List<string> materials, Dictionary<string, string> renames, StringBuilder report)
        {
            List<string> order = new List<string>();
            Dictionary<string, List<string[]>> settings = new Dictionary<string, List<string[]>>(StringComparer.Ordinal);
            List<string> dropped = new List<string>();
            List<string> parameters = new List<string>(ShaderParameters);
            int read = 0;
            Match root = Regex.Match(fh2, "<ShaderSettings>(.*)</ShaderSettings>", RegexOptions.Singleline);
            foreach (Match section in Regex.Matches(root.Success ? root.Groups[1].Value : fh2, "<(\\w+)>(.*?)</\\1>", RegexOptions.Singleline))
            {
                read++;
                // "cockpit_<material>" sets <material> of the cockpit model (FM4 MP4-12C: cockpit_bump_leather2).
                string prefix = section.Groups[1].Value.StartsWith("cockpit_") ? "cockpit_" : "";
                string name = section.Groups[1].Value.Substring(prefix.Length);
                string to;
                List<string> targets = new List<string>();
                if ((renames != null && renames.TryGetValue(name, out to)) || (AutoMap(name, out to) && to != null))
                {
                    // The lod0 model names its lights tail_light_lod0 / reverse_light_lod0 (LodVariant).
                    targets.Add(to);
                    string lod0 = LodVariant(to, "x_lod0.carbin");
                    if (lod0 != to && prefix.Length == 0)
                    {
                        targets.Add(lod0);
                    }
                }
                else if (to == null && AutoMap(name, out to))
                {
                    // Mapped to nothing (dropped from the models): its settings go too.
                }
                else
                {
                    targets.Add(name);
                }
                foreach (string material in targets)
                {
                    if (materials != null && !materials.Contains(material))
                    {
                        continue;
                    }
                    string target = prefix + material;
                    List<string[]> values;
                    if (!settings.TryGetValue(target, out values))
                    {
                        values = new List<string[]>();
                        settings[target] = values;
                        order.Add(target);
                    }
                    foreach (Match value in Regex.Matches(section.Groups[2].Value, "<(\\w+)\\b[^>]*/>"))
                    {
                        string parameter = value.Groups[1].Value;
                        bool known = parameters.Exists(delegate(string p) { return string.Equals(p, parameter, StringComparison.OrdinalIgnoreCase); });
                        if (!known)
                        {
                            if (!dropped.Contains(parameter))
                            {
                                dropped.Add(parameter);
                            }
                            continue;
                        }
                        if (!values.Exists(delegate(string[] v) { return v[0] == parameter; }))
                        {
                            values.Add(new string[] { parameter, value.Value });
                        }
                    }
                }
            }
            StringBuilder xml = new StringBuilder("<ShaderSettings>\r\n");
            int written = 0;
            foreach (string name in order)
            {
                if (settings[name].Count == 0)
                {
                    continue;
                }
                written++;
                xml.Append("  <" + name + ">\r\n");
                foreach (string[] value in settings[name])
                {
                    xml.Append("    " + value[1] + "\r\n");
                }
                xml.Append("  </" + name + ">\r\n");
            }
            xml.Append("</ShaderSettings>\r\n");
            if (report != null)
            {
                report.AppendLine(string.Format("  shadersettings: {0} -> {1} materials{2}", read, written, (dropped.Count > 0) ? "; parameters FM4 lacks left out: " + string.Join(", ", dropped.ToArray()) : ""));
            }
            return Encoding.UTF8.GetBytes(xml.ToString());
        }

        private void BuildWheelZips()
        {
            string donorWheelMedia = this.DonorScalar("SELECT w.MediaName FROM List_Wheels w JOIN Data_Car c ON c.StockWheelID = w.ID WHERE c.Id = ?", this.donorId) as string;
            foreach (long id in this.wheelIds)
            {
                string media = this.source.Scalar("SELECT MediaName FROM List_Wheels WHERE ID = ?", id) as string;
                if (media == null && (this.motorsport3 || this.fm2Stage))
                {
                    media = this.target.Scalar("SELECT MediaName FROM List_Wheels WHERE ID = ?", id) as string;   // a new id (Motorsport3Wheel)
                }
                string sourcePath = GamePaths.FindMedia(this.SourceRoot, "wheels", media);
                string donorPath = (donorWheelMedia != null) ? (GamePaths.FindMedia(this.DonorRoot ?? this.TargetRoot, "wheels", donorWheelMedia) ?? GamePaths.FindMedia(this.TargetRoot, "wheels", donorWheelMedia)) : null;
                if (sourcePath == null || donorPath == null)
                {
                    this.Line("wheel {0}: zip not found, skipped", media);
                    continue;
                }
                string path = Path.Combine(Path.Combine(Path.Combine(this.TargetRoot, "Media"), "wheels"), media + ".zip");
                object original = this.originalWheels.Contains(id) ? (object) true : null;
                if (File.Exists(path) && original != null)
                {
                    this.Line("wheel {0}: an FM4 wheel, kept", media);
                    continue;
                }
                ForzaZip from = new ForzaZip(sourcePath);
                ForzaZip donor = new ForzaZip(donorPath);
                ForzaZip wheel = ForzaZip.CreateLike(donor);
                foreach (ForzaZip.Entry entry in from.Entries)
                {
                    string name = entry.Name.ToLowerInvariant();
                    byte[] data = from.Read(entry);
                    if (name.EndsWith(".carbin"))
                    {
                        CarbinModel model = new CarbinModel(data, true);
                        ConvertToMotorsport4(model);
                        data = model.ToBytes();
                    }
                    else if (name.EndsWith(".xds") && this.motorsport3)
                    {
                        data = ConvertMotorsport3Texture(data, name);
                    }
                    wheel.Add(name, data);
                }
                this.RebuildWheelLod0(wheel, media);
                ForzaZip.Entry version = donor.Find("versiondata.xml");
                if (version != null && wheel.Find("versiondata.xml") == null)
                {
                    wheel.Add("versiondata.xml", donor.Read(version));
                    this.donorContent.Add("wheel " + media + " versiondata.xml: the donor wheel's (" + this.game + " has none)");
                }
                this.donorFormat.Add("wheel " + media + " zip: entry headers and zipmanifest.xml line of the donor wheel " + donorWheelMedia);
                this.SaveNew(wheel, path);
                this.Line("wheel zip: {0} ({1} entries)", this.Relative(path), wheel.Entries.Count);
            }
        }

        // The "offroad" choice: the program's own offroad tire models (Service\OffroadTires\TireA/B/C Offroad.zip, the FM4 tires made
        // for the Raptor: same size as the normal tires) as they are, under this car's name, so the car gets the deep tread of an
        // off-road tire whatever its source has. Only the .carbin entry takes the car's name.
        private void OffroadTire(string kind, string wheels)
        {
            string sourcePath = Path.Combine(Path.Combine(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Service"), "OffroadTires"), "Tire" + kind + "Offroad.zip");
            if (!File.Exists(sourcePath))
            {
                this.Line("WARNING tire Tire{0}{1}: the offroad tire {2} is missing, the generic tire stays", kind, this.CarName, sourcePath);
                return;
            }
            string path = Path.Combine(wheels, "Tire" + kind + this.CarName + ".zip");
            ForzaZip from = new ForzaZip(sourcePath);
            ForzaZip tire = ForzaZip.CreateLike(from);
            foreach (ForzaZip.Entry entry in from.Entries)
            {
                if (entry.IsDirectory)
                {
                    continue;
                }
                string name = entry.Name.ToLowerInvariant();
                if (name.EndsWith(".carbin"))
                {
                    name = ("tire" + kind + this.CarName).ToLowerInvariant() + ".carbin";
                }
                tire.Add(name, from.Read(entry));
            }
            this.SaveNew(tire, path);
            this.Line("tire zip: {0} ({1} entries, the program's offroad tire as it is)", this.Relative(path), tire.Entries.Count);
        }

        // A car with its own tire model (the games ship Media\wheels\TireA/B/C<wheel>.zip beside the generic TireA/B/C.zip: the
        // same textures and a heavier tire .carbin, e.g. FH1 / FH2 Raptor and Wrangler; FM3 has none) brings it: the .carbin
        // converted like the wheel's, the textures as they are, laid out like FM4's generic tire zip. An FM4 file of that name
        // stays (D-16). The game looks the zip up by the NAME OF THE CAR (checked in game 2026-10-27: TireA/B/CFOR_SVTRaptor_12_FH.zip
        // is used by the car FOR_SVTRaptor_12_FH, whose wheel is FOR_SVTRaptor_12; named after the wheel it showed the generic tire).
        private void BuildTireZips()
        {
            string wheels = Path.Combine(Path.Combine(this.TargetRoot, "Media"), "wheels");
            // the source's tire zips are named after its car (its stock wheel has the same name in every case seen)
            string stockWheel = this.source.Scalar("SELECT w.MediaName FROM List_Wheels w JOIN Data_Car c ON c.StockWheelID = w.ID WHERE c.Id = ?", this.carId) as string;
            foreach (string kind in new string[] { "A", "B", "C" })
            {
                if (string.Equals(this.Tires, "offroad", StringComparison.OrdinalIgnoreCase))
                {
                    this.OffroadTire(kind, wheels);
                    continue;
                }
                string sourceName = this.MediaName;
                string sourcePath = GamePaths.FindMedia(this.SourceRoot, "wheels", "Tire" + kind + sourceName);
                if (sourcePath == null && stockWheel != null)
                {
                    sourceName = stockWheel;
                    sourcePath = GamePaths.FindMedia(this.SourceRoot, "wheels", "Tire" + kind + sourceName);
                }
                if (sourcePath == null || !sourcePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                string media = this.CarName;
                string path = Path.Combine(wheels, "Tire" + kind + media + ".zip");
                string generic = GamePaths.FindMedia(this.TargetRoot, "wheels", "Tire" + kind);
                if (File.Exists(path) || generic == null)
                {
                    this.Line("tire Tire{0}{1}: {2}", kind, media, File.Exists(path) ? "an FM4 file, kept" : "FM4's generic Tire" + kind + ".zip not found, skipped");
                    continue;
                }
                ForzaZip from = new ForzaZip(sourcePath);
                ForzaZip model = new ForzaZip(generic);
                ForzaZip tire = ForzaZip.CreateLike(model);
                int models = 0;
                foreach (ForzaZip.Entry entry in from.Entries)
                {
                    string name = entry.Name.ToLowerInvariant().Replace(("tire" + kind + sourceName).ToLowerInvariant(), ("tire" + kind + media).ToLowerInvariant());
                    if (name == "buildnumber.txt" || name.EndsWith("_build_report.html"))
                    {
                        continue;
                    }
                    byte[] data = from.Read(entry);
                    if (name.EndsWith(".carbin"))
                    {
                        CarbinModel carbin = new CarbinModel(data, true);
                        ConvertToMotorsport4(carbin);
                        data = carbin.ToBytes();
                        models++;
                    }
                    tire.Add(name, data);
                }
                ForzaZip.Entry build = model.Find("buildnumber.txt");
                if (build != null)
                {
                    tire.Add("buildnumber.txt", model.Read(build));
                }
                ForzaZip.Entry version = model.Find("versiondata.xml");
                if (version != null && tire.Find("versiondata.xml") == null)
                {
                    tire.Add("versiondata.xml", model.Read(version));
                    this.donorFormat.Add("tire " + kind + media + " versiondata.xml: the generic tire's (" + this.game + " has none)");
                }
                this.SaveNew(tire, path);
                this.Line("tire zip: {0} ({1} entries, {2} model converted)", this.Relative(path), tire.Entries.Count, models);
            }
        }

        // In LOD 0 the tire technique draws the separate FM4 tire (Media\wheels\tirea: tread and sidewall
        // textures), so a LOD 1 tire copied there renders solid black (GMC_YukonXL_01, checked by the user
        // 2026-10-17). It becomes inner_rim, a LOD 0 rim material (every FM4 wheel) sampling wheel_lod0.xds,
        // which is a copy of the wheel.xds its UVs map.
        private static void RenameLod0Tire(CarbinModel.Section section)
        {
            foreach (CarbinModel.SubSection sub in section.SubSections)
            {
                if (sub.Lod == 0 && sub.Name == "tire")
                {
                    sub.Name = "inner_rim";
                }
            }
        }

        // The _cockpit.carbin header holds the driver rig, big-endian floats in model axes (decoded on the 313
        // FM4 cockpits, 2026-10-17):
        //   0x1C8..0x218  three left/right pairs of hand grips (vec4 each, w 0): on the rim circle 20 degrees
        //                 above the centre, on the rim's outer edge, inner edge and tube middle (GripRadii)
        //   0x228         the steering wheel pivot: the steering_wheel origin (x exact, y/z within 1-4 cm)
        //   0x238         the wheel axis: the normal of the wheel's plane, pointing back (0.8 degrees median)
        //   0x248, 0x258  two points (0x258 on the centre console: the shifter, mirrored with the wheel side)
        //   0x270..0x2FF  four records point / direction / value (eye-level points looking forward)
        // FH traffic cockpits (placeholders) have zeros there: the wheel then turns about the car's origin and
        // swings into the windscreen, and the hands meet in one point (GMC_YukonXL_01; the Syclone's whole
        // cockpit file turned in place, its wheel section alone did not; checked by the user 2026-10-17).
        // When the pivot is 0,0,0: pivot, axis and grips come from the steering_wheel geometry, the other
        // records from the donor's cockpit relative to its wheel (x mirrored when the wheels sit on opposite
        // sides). The same when the pivot is not on the car's own wheel: a source without a _cockpit file gets
        // the donor's header, whose rig belongs to the donor's wheel (FOR_FocusZX4_05 left-hand drive with the
        // right-hand drive Fiesta 09 / Supra RZ 98 pivot: the wheel swung over the roof, user 2026-10-20). The
        // side is the sign of the steering_wheel x: on 312 of 313 FM4 cockpits the pivot x has the same sign
        // (McLaren F1 GTR centred), x < 0 = left (233), > 0 = right (80) (fm4dev\Traffic\tools\drivesurvey).
        private const int RigGrips = 0x1C8;
        private const int RigPivot = 0x228;
        private const int RigAxis = 0x238;
        private const int RigRest = 0x248;
        private const int RigEnd = 0x300;
        // Grip radii against the measured rim (289 FM4 cockpits, fm4dev\Traffic\tools\handsurvey, medians):
        // outer edge x 0.995, inner edge x 1.083, tube middle x 1.031. Rim = vertices beyond 75% of the
        // largest radius about the pivot, in the wheel's plane.
        private static readonly float[] GripRadii = { 0.995f, 1.083f, 1.031f };
        // FM4's grips sit 20 degrees above the centre (median and quartiles, 289 cockpits); the user first
        // asked for 9:15 (0), then chose FM4's standard (2026-10-20).
        private const double GripAngle = 20.0 * Math.PI / 180.0;

        private void CockpitRig(ForzaZip car, ForzaZip donor, string prefix, string donorPrefix)
        {
            ForzaZip.Entry entry = car.Find(prefix + "_cockpit.carbin");
            if (entry == null)
            {
                return;
            }
            CarbinModel cockpit = new CarbinModel(car.Read(entry));
            CarbinModel.Section wheel = cockpit.Find("steering_wheel");
            if (wheel == null || cockpit.Head.Length < RigEnd)
            {
                return;
            }
            Microsoft.Xna.Framework.Vector3 pivot = new Microsoft.Xna.Framework.Vector3(wheel.Bounds[0], wheel.Bounds[1], wheel.Bounds[2]);
            Microsoft.Xna.Framework.Vector3 old = RigVector(cockpit.Head, RigPivot);
            string reason;
            if (old == Microsoft.Xna.Framework.Vector3.Zero)
            {
                reason = "driver rig in the header was empty (wheel pivot 0,0,0)";
            }
            else if (this.fm2Stage)
            {
                // The wheel is the FM2 car's own, the header's rig the donor's: it fits only by chance (AB Flug
                // Supra, whose wheel is the donor's twin; the Foose IS300's hands floated off the rim).
                reason = string.Format(CultureInfo.InvariantCulture, "FM2 stage: the header's rig is the donor's (pivot {0:F4}, {1:F4}, {2:F4}), the steering wheel the car's own", old.X, old.Y, old.Z);
            }
            else if (Math.Abs(old.X - pivot.X) > RigOffWheel || Microsoft.Xna.Framework.Vector3.Distance(old, pivot) > RigOffWheelFar)
            {
                reason = string.Format(CultureInfo.InvariantCulture, "driver rig in the header is not on this car's steering wheel (pivot {0:F4}, {1:F4}, {2:F4}, {3} drive; wheel at x {4:F4}, {5} drive)", old.X, old.Y, old.Z, DriveSide(old.X), pivot.X, DriveSide(pivot.X));
            }
            else
            {
                return;
            }
            Microsoft.Xna.Framework.Vector3 axis = WheelNormal(wheel);
            Microsoft.Xna.Framework.Vector3 up = Microsoft.Xna.Framework.Vector3.Normalize(Microsoft.Xna.Framework.Vector3.Cross(axis, Microsoft.Xna.Framework.Vector3.UnitX));
            if (up.Y < 0)
            {
                up = -up;
            }
            float[] rim = RimRadii(wheel, pivot, axis, up);
            float[] radii = { rim[0], rim[1], (rim[0] + rim[1]) / 2 };
            for (int g = 0; g < GripRadii.Length; g++)
            {
                float across = (float) (GripRadii[g] * radii[g] * Math.Cos(GripAngle));
                float above = (float) (GripRadii[g] * radii[g] * Math.Sin(GripAngle));
                WriteRigVector(cockpit.Head, RigGrips + (g * 32), pivot + (across * Microsoft.Xna.Framework.Vector3.UnitX) + (above * up));
                WriteRigVector(cockpit.Head, RigGrips + (g * 32) + 16, pivot - (across * Microsoft.Xna.Framework.Vector3.UnitX) + (above * up));
            }
            WriteRigVector(cockpit.Head, RigPivot, pivot);
            WriteRigVector(cockpit.Head, RigAxis, axis);
            string rest = "left as they were (the donor has none)";
            ForzaZip.Entry template = donor.Find(donorPrefix + "_cockpit.carbin");
            CarbinModel donorCockpit = (template != null) ? new CarbinModel(donor.Read(template)) : null;
            CarbinModel.Section donorWheel = (donorCockpit != null) ? donorCockpit.Find("steering_wheel") : null;
            if (donorWheel != null && donorCockpit.Head.Length >= RigEnd && RigVector(donorCockpit.Head, RigPivot) != Microsoft.Xna.Framework.Vector3.Zero)
            {
                Microsoft.Xna.Framework.Vector3 donorPivot = RigVector(donorCockpit.Head, RigPivot);
                bool mirror = Math.Sign(donorPivot.X) != Math.Sign(pivot.X);
                Buffer.BlockCopy(donorCockpit.Head, RigRest, cockpit.Head, RigRest, RigEnd - RigRest);
                // Points: 0x248, 0x258, then the four records (point, direction, value) from 0x270, 36 bytes apart.
                List<int> points = new List<int> { 0x248, 0x258 };
                List<int> directions = new List<int>();
                for (int r = 0; r < 4; r++)
                {
                    points.Add(0x270 + (r * 36));
                    directions.Add(0x270 + (r * 36) + 16);
                }
                foreach (int offset in points)
                {
                    Microsoft.Xna.Framework.Vector3 p = RigVector(cockpit.Head, offset);
                    if (p == Microsoft.Xna.Framework.Vector3.Zero)
                    {
                        continue;
                    }
                    Microsoft.Xna.Framework.Vector3 relative = p - donorPivot;
                    relative.X = mirror ? -relative.X : relative.X;
                    WriteRigVector(cockpit.Head, offset, pivot + relative);
                }
                foreach (int offset in directions)
                {
                    Microsoft.Xna.Framework.Vector3 d = RigVector(cockpit.Head, offset);
                    d.X = mirror ? -d.X : d.X;
                    WriteRigVector(cockpit.Head, offset, d);
                }
                rest = "the donor's, moved with the wheel" + (mirror ? " and mirrored" : "");
                this.donorContent.Add(entry.Name + ": cockpit rig points but the wheel and grips (shifter, eye-level records), the donor's moved to the wheel");
            }
            car.Replace(entry, cockpit.ToBytes());
            this.Line("  {0}: {1}: pivot {2:F4}, {3:F4}, {4:F4}, axis {5:F4}, {6:F4}, {7:F4}, hand grips from the wheel; other points {8}", entry.Name, reason, pivot.X, pivot.Y, pivot.Z, axis.X, axis.Y, axis.Z, rest);
        }

        // An FM4 pivot sits on the steering_wheel origin: x exact, y / z within 1-4 cm.
        private const float RigOffWheel = 0.03f;
        private const float RigOffWheelFar = 0.15f;

        private static string DriveSide(float x)
        {
            return (Math.Abs(x) < 0.05f) ? "centre" : ((x < 0) ? "left-hand" : "right-hand");
        }

        // Outer and inner radius of the rim about the pivot, in the wheel's plane (x and up).
        private static float[] RimRadii(CarbinModel.Section wheel, Microsoft.Xna.Framework.Vector3 pivot, Microsoft.Xna.Framework.Vector3 axis, Microsoft.Xna.Framework.Vector3 up)
        {
            CarbinModel.VertexBuffer buffer = (wheel.Lod0.Count > 0) ? wheel.Lod0 : wheel.Lod;
            double[] p = CarbinModel.Positions(wheel, buffer);
            List<double> radii = new List<double>();
            double outer = 0;
            for (int i = 0; i < buffer.Count; i++)
            {
                Microsoft.Xna.Framework.Vector3 d = new Microsoft.Xna.Framework.Vector3((float) p[i * 3], (float) p[(i * 3) + 1], (float) p[(i * 3) + 2]) - pivot;
                double u = d.X;
                double v = Microsoft.Xna.Framework.Vector3.Dot(d, up);
                double r = Math.Sqrt((u * u) + (v * v));
                radii.Add(r);
                outer = Math.Max(outer, r);
            }
            double inner = outer;
            foreach (double r in radii)
            {
                if (r >= 0.75 * outer)
                {
                    inner = Math.Min(inner, r);
                }
            }
            return new float[] { (float) outer, (float) inner };
        }

        // The normal of the wheel's plane (smallest axis of its vertex spread), pointing back (+z).
        private static Microsoft.Xna.Framework.Vector3 WheelNormal(CarbinModel.Section wheel)
        {
            CarbinModel.VertexBuffer buffer = (wheel.Lod0.Count > 0) ? wheel.Lod0 : wheel.Lod;
            double[] p = CarbinModel.Positions(wheel, buffer);
            int count = buffer.Count;
            double[] mean = new double[3];
            for (int i = 0; i < count; i++)
            {
                for (int k = 0; k < 3; k++)
                {
                    mean[k] += p[(i * 3) + k] / count;
                }
            }
            double[,] spread = new double[3, 3];
            for (int i = 0; i < count; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    for (int k = 0; k < 3; k++)
                    {
                        spread[j, k] += (p[(i * 3) + j] - mean[j]) * (p[(i * 3) + k] - mean[k]);
                    }
                }
            }
            // Power iteration on (trace I - spread): its largest axis is the spread's smallest.
            double trace = spread[0, 0] + spread[1, 1] + spread[2, 2];
            double[] v = { 0, 0.3, 1 };
            for (int iteration = 0; iteration < 200; iteration++)
            {
                double[] w = new double[3];
                for (int j = 0; j < 3; j++)
                {
                    w[j] = trace * v[j];
                    for (int k = 0; k < 3; k++)
                    {
                        w[j] -= spread[j, k] * v[k];
                    }
                }
                double length = Math.Sqrt((w[0] * w[0]) + (w[1] * w[1]) + (w[2] * w[2]));
                if (length < 1e-12)
                {
                    break;
                }
                v = new double[] { w[0] / length, w[1] / length, w[2] / length };
            }
            Microsoft.Xna.Framework.Vector3 normal = new Microsoft.Xna.Framework.Vector3((float) v[0], (float) v[1], (float) v[2]);
            return (normal.Z < 0) ? -normal : normal;
        }

        private static Microsoft.Xna.Framework.Vector3 RigVector(byte[] head, int offset)
        {
            return new Microsoft.Xna.Framework.Vector3(ReadHeadFloat(head, offset), ReadHeadFloat(head, offset + 4), ReadHeadFloat(head, offset + 8));
        }

        private static void WriteRigVector(byte[] head, int offset, Microsoft.Xna.Framework.Vector3 value)
        {
            float[] parts = { value.X, value.Y, value.Z };
            for (int i = 0; i < 3; i++)
            {
                byte[] bytes = BitConverter.GetBytes(parts[i]);
                for (int k = 0; k < 4; k++)
                {
                    head[offset + (i * 4) + k] = bytes[3 - k];
                }
            }
        }

        // CarAttribs DriverHandsOffset places the driver's hands: on 289 of 307 FM4 cars it is the _cockpit
        // steering_wheel origin (model axes) plus (0, -0.403, 0.425) (x within 5 mm; y -0.418..-0.391,
        // z 0.404..0.454 for 80%). FH traffic cars have values unrelated to their wheel (GMC_YukonXL_01:
        // 0.083, -0.420, 0.394 for a wheel at x -0.428): no hands in view; with the offset rebuilt they
        // showed at the wheel (user, 2026-10-17; the wheel's own turning comes from CockpitRig). When x is
        // more than 3 cm off the wheel, the offset is rebuilt from the wheel. On a traffic car y and z count
        // too: FOR_FocusZX4_05 and FOR_F350SuperDuty_08 share -0.359, 0.390, -0.019, a placeholder whose x
        // fell within 3 cm of the Focus wheel while y sat 6 cm off FM4's rule; the hands showed as black blobs
        // up by the windscreen (user, 2026-10-20). Measured from the rig pivot instead (289 FM4 cars,
        // handsurvey) the offset is (0, -0.402, 0.410), within 1 cm for half of them: FM4's pivot sits 1.4 cm
        // behind the origin, a rebuilt pivot on it, so the origin rule left rebuilt hands 1.5 cm off (user saw
        // the hand's grip hole beside the rim, 2026-10-20). The origin rule stays for a cockpit with no pivot.
        private static readonly Microsoft.Xna.Framework.Vector3 HandsFromWheel = new Microsoft.Xna.Framework.Vector3(0f, -0.403f, 0.425f);
        private static readonly Microsoft.Xna.Framework.Vector3 HandsFromPivot = new Microsoft.Xna.Framework.Vector3(0f, -0.402f, 0.410f);
        private const float HandsOffWheel = 0.03f;

        // exhaust.txt in the stage folder (one "x y z" tip per line, model axes, # comments) replaces the exhaust
        // locators of the main carbin, i.e. where the backfire flames come out (the donor's single locator is not at
        // this car's pipes). CarbinModel.SetExhaustTips grows / shrinks the header.
        private void ApplyExhaustFile(ForzaZip car, string prefix)
        {
            string path = Path.Combine(this.SourceRoot, "exhaust.txt");
            ForzaZip.Entry entry = car.Find(prefix + ".carbin");
            if (!File.Exists(path) || entry == null)
            {
                return;
            }
            List<float[]> tips = new List<float[]>();
            foreach (string line in File.ReadAllLines(path))
            {
                string text = line.Split('#')[0].Trim();
                string[] parts = text.Split(new char[] { ' ', '\t', ';' }, StringSplitOptions.RemoveEmptyEntries);
                float x, y, z;
                if (parts.Length >= 3 && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x) && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y) && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out z))
                {
                    tips.Add(new float[] { x, y, z });
                }
            }
            CarbinModel model = new CarbinModel(car.Read(entry));
            if (!model.HasExhaustLocators || tips.Count == 0)
            {
                return;
            }
            int old = model.ExhaustCount;
            model.SetExhaustTips(tips);
            car.Replace(entry, model.ToBytes());
            this.Line("  {0}: exhaust locators {1} -> {2} from exhaust.txt (backfire points)", entry.Name, old, tips.Count);
        }

        // An FM2 stage has no driver data: the Driver element of carattribs is the donor's, whose offsets belong to the
        // donor's seat and wheel. The offsets of the hands, shifter and hand brake are relative to the rig points
        // (computed from this car's wheel by CockpitRig) and are 0 on FM4's cars (Supra: all 0 but the butt), so they
        // become 0, and so does the butt (no rule from the seat is known: docs\DRIVER_POSITION.md, open).
        private void NeutralDriverOffsets(ForzaZip car)
        {
            ForzaZip.Entry attributes = car.Find("carattribs.xml");
            if (attributes == null)
            {
                return;
            }
            string text = Encoding.UTF8.GetString(car.Read(attributes));
            Match driver = Regex.Match(text, "<Driver [^>]*/>");
            if (!driver.Success)
            {
                return;
            }
            string result = Regex.Replace(driver.Value, "((?:ButtOffset|ButtOffsetLOD0|LeftHandOffset|RightHandOffset|LeftHandOffsetLOD0|RightHandOffsetLOD0|ShifterOffset|HandBrakeOffset)\\.[xyz]=\")[^\"]*\"", "${1}0.000000\"");
            if (result != driver.Value)
            {
                car.Replace(attributes, Encoding.UTF8.GetBytes(text.Substring(0, driver.Index) + result + text.Substring(driver.Index + driver.Length)));
                this.Line("  carattribs Driver: butt, hand, shifter and hand brake offsets 0 (computed from the wheel, not the donor's)");
            }
        }

        private void MirrorDriverOffsets(ForzaZip car, ForzaZip donor, string prefix, string donorPrefix)
        {
            ForzaZip.Entry attributes = car.Find("carattribs.xml");
            ForzaZip.Entry cockpit = car.Find(prefix + "_cockpit.carbin");
            ForzaZip.Entry donorCockpit = donor.Find(donorPrefix + "_cockpit.carbin");
            if (attributes == null || cockpit == null || donorCockpit == null)
            {
                return;
            }
            CarbinModel.Section wheel = new CarbinModel(car.Read(cockpit)).Find("steering_wheel");
            CarbinModel.Section donorWheel = new CarbinModel(donor.Read(donorCockpit)).Find("steering_wheel");
            if (wheel == null || donorWheel == null || Math.Sign(wheel.Bounds[0]) == Math.Sign(donorWheel.Bounds[0]))
            {
                return;
            }
            string text = Encoding.UTF8.GetString(car.Read(attributes));
            Match driver = Regex.Match(text, "<Driver [^>]*/>");
            if (!driver.Success)
            {
                return;
            }
            int flipped = 0;
            string result = Regex.Replace(driver.Value, "((?:ButtOffset|ButtOffsetLOD0|LeftHandOffset|RightHandOffset|LeftHandOffsetLOD0|RightHandOffsetLOD0|ShifterOffset|HandBrakeOffset|IKGuideTranslation)\\.x=\")([^\"]*)\"", delegate(Match m)
            {
                float value;
                if (!float.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || value == 0)
                {
                    return m.Value;
                }
                flipped++;
                return m.Groups[1].Value + (-value).ToString("0.######", CultureInfo.InvariantCulture) + "\"";
            });
            if (flipped > 0)
            {
                car.Replace(attributes, Encoding.UTF8.GetBytes(text.Substring(0, driver.Index) + result + text.Substring(driver.Index + driver.Length)));
                this.Line("  carattribs Driver: {0} x offsets mirrored (donor's wheel on the {1}, this car's on the {2})", flipped, donorWheel.Bounds[0] < 0 ? "left" : "right", wheel.Bounds[0] < 0 ? "left" : "right");
            }
        }

        private void DriverHandsFromSteeringWheel(ForzaZip car, string prefix)
        {
            ForzaZip.Entry attributes = car.Find("carattribs.xml");
            ForzaZip.Entry cockpit = car.Find(prefix + "_cockpit.carbin");
            if (attributes == null || cockpit == null)
            {
                return;
            }
            CarbinModel model = new CarbinModel(car.Read(cockpit));
            CarbinModel.Section wheel = model.Find("steering_wheel");
            if (wheel == null)
            {
                return;
            }
            Microsoft.Xna.Framework.Vector3 pivot = (model.Head.Length >= RigEnd) ? RigVector(model.Head, RigPivot) : Microsoft.Xna.Framework.Vector3.Zero;
            string text = Encoding.UTF8.GetString(car.Read(attributes));
            // The attributes come in any order (FM4 writes w, z, y, x).
            Match match = Regex.Match(text, "<DriverHandsOffset\\b[^>]*/>");
            float x = 0, y = 0, z = 0;
            if (!match.Success)
            {
                return;
            }
            Match mx = Regex.Match(match.Value, "value\\.x=\"([^\"]*)\""), my = Regex.Match(match.Value, "value\\.y=\"([^\"]*)\""), mz = Regex.Match(match.Value, "value\\.z=\"([^\"]*)\"");
            // Other sources keep the rule as it was (only the x, y, z attribute order of the FH files matched).
            if (!this.fm2Stage && !Regex.IsMatch(match.Value, "^<DriverHandsOffset value\\.x=\"[^\"]*\" value\\.y=\"[^\"]*\" value\\.z=\"[^\"]*\""))
            {
                return;
            }
            if (!mx.Success || !my.Success || !mz.Success
                || !float.TryParse(mx.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out x)
                || !float.TryParse(my.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out y)
                || !float.TryParse(mz.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out z))
            {
                return;
            }
            Microsoft.Xna.Framework.Vector3 hands = (pivot != Microsoft.Xna.Framework.Vector3.Zero) ? pivot + HandsFromPivot : new Microsoft.Xna.Framework.Vector3(wheel.Bounds[0], wheel.Bounds[1], wheel.Bounds[2]) + HandsFromWheel;
            bool off = Math.Abs(x - hands.X) > HandsOffWheel;
            if (this.traffic || this.fm2Stage)   // an FM2 stage's offset is the donor's, whatever its x
            {
                off |= Math.Abs(y - hands.Y) > HandsOffWheel || Math.Abs(z - hands.Z) > HandsOffWheel;
            }
            if (!off)
            {
                return;
            }
            string values = match.Value;
            values = Regex.Replace(values, "value\\.x=\"[^\"]*\"", string.Format(CultureInfo.InvariantCulture, "value.x=\"{0:F6}\"", hands.X));
            values = Regex.Replace(values, "value\\.y=\"[^\"]*\"", string.Format(CultureInfo.InvariantCulture, "value.y=\"{0:F6}\"", hands.Y));
            values = Regex.Replace(values, "value\\.z=\"[^\"]*\"", string.Format(CultureInfo.InvariantCulture, "value.z=\"{0:F6}\"", hands.Z));
            car.Replace(attributes, Encoding.UTF8.GetBytes(text.Substring(0, match.Index) + values + text.Substring(match.Index + match.Length)));
            this.Line("  carattribs DriverHandsOffset {0}, {1}, {2} is not at the steering wheel ({3:F6}, {7:F6}, {8:F6}): now {4:F6}, {5:F6}, {6:F6} ({9} + FM4's median offset)", mx.Groups[1].Value, my.Groups[1].Value, mz.Groups[1].Value, wheel.Bounds[0], hands.X, hands.Y, hands.Z, wheel.Bounds[1], wheel.Bounds[2], (pivot != Microsoft.Xna.Framework.Vector3.Zero) ? "rig pivot" : "wheel origin");
        }

        // FH traffic models carry a low-poly driver merged into the driver's seat: seatL has 594-926 LOD 1
        // indices against seatR's 70-161 (GMC_YukonXL_01, TOY_Camry_07, AUD_A4Avant_04, FOR_Explorer_10).
        // The seat with more than three times its twin's indices is that one; null when there is none.
        private static string DriverSeat(CarbinModel main)
        {
            int left = 0;
            int right = 0;
            foreach (CarbinModel.Section section in main.Sections)
            {
                foreach (CarbinModel.SubSection sub in section.SubSections)
                {
                    if (sub.Lod != 1)
                    {
                        continue;
                    }
                    if (section.Name == "seatL")
                    {
                        left += sub.Indices.Count;
                    }
                    else if (section.Name == "seatR")
                    {
                        right += sub.Indices.Count;
                    }
                }
            }
            if (left > 0 && right > 0 && left > right * 3)
            {
                return "seatL";
            }
            if (left > 0 && right > 0 && right > left * 3)
            {
                return "seatR";
            }
            return null;
        }

        // In LOD 0 every FM4 wheel (424 of 424) has outer_rim, the rim barrel the tire sits on: a ring at the
        // rim radius spanning the tire width. FM4 draws its own tire (Media\wheels\tirea/TireB/TireC, sized
        // from Data_Car) in LOD 0, and an FH traffic rim rebuilt from LOD 1 has no outer_rim and got none
        // (bare rims, GMC_YukonXL_01, checked by the user 2026-10-17); the LOD 1 tire copied instead drew
        // solid black as "tire" and wrong as "inner_rim". The copied tire becomes outer_rim with zero-area
        // triangles (strip a a a b b b ...) over its inner ring vertices: nothing visible, the ring's extent
        // kept. Whether outer_rim is what the game looks for is a guess being tested.
        private static bool Lod0TireToOuterRim(CarbinModel.Section section)
        {
            bool changed = false;
            double[] positions = CarbinModel.Positions(section, section.Lod0);
            foreach (CarbinModel.SubSection sub in section.SubSections)
            {
                if (sub.Lod != 0 || sub.Name != "tire")
                {
                    continue;
                }
                double inner = double.MaxValue;
                List<int> used = new List<int>();
                foreach (int index in sub.Indices)
                {
                    if (index >= 0 && index < section.Lod0.Count && !used.Contains(index))
                    {
                        used.Add(index);
                        inner = Math.Min(inner, Radius(positions, index));
                    }
                }
                List<int> ring = new List<int>();
                foreach (int index in used)
                {
                    if (Radius(positions, index) <= inner + 0.01)
                    {
                        ring.Add(index);
                        ring.Add(index);
                        ring.Add(index);
                    }
                }
                sub.Name = "outer_rim";
                sub.Indices = ring;
                changed = true;
            }
            return changed;
        }

        // A wheel carbin (type 3) holds 12 big-endian floats at 0x18 that fit FM4's separate tire to the
        // rim, all extents of the geometry along the axis (x) or from it (r), on 6 FM4 wheels checked
        // (2026-10-17, exact to the mm):
        //   0, 4  LOD 0 wheel_black x min   1  LOD 1 inner_rim x min   2  LOD 0 inner_rim x max
        //   3     LOD 1 inner_rim x max     5  LOD 1 tire x min        6  LOD 0 wheel_black x max
        //   7     LOD 1 tire x max          8  LOD 0 inner_rim r max   9  LOD 1 inner_rim r max
        //   10    LOD 0 wheel_black r max   11 LOD 1 tire r max
        // FH traffic wheels, whose LOD 0 is a placeholder, have +-3.4e38 (an empty min/max) in 0, 2, 4, 6
        // and 10 and junk in 8: FM4 then draws no tire (GMC_YukonXL_01, bare rims in game 2026-10-17).
        // When any value is that, all 12 are rebuilt from LOD 1: wheel_black (the bead seat ring, 2-3 mm
        // wide) is taken from the tire's outer edge, the LOD 0 rim as the LOD 1 one. Returns the values
        // written, or null.
        private const int TireHeaderOffset = 0x18;
        private const float BeadSeatWidth = 0.003f;

        private static string TireHeader(CarbinModel wheel)
        {
            if (wheel.Type != 3 || wheel.Head.Length < TireHeaderOffset + 48)
            {
                return null;
            }
            bool empty = false;
            for (int i = 0; i < 12; i++)
            {
                empty |= Math.Abs(ReadHeadFloat(wheel.Head, TireHeaderOffset + (i * 4))) > 1e30f;
            }
            float[] tire = LodExtents(wheel, "tire");
            float[] rim = LodExtents(wheel, "inner_rim");
            if (!empty || tire == null || rim == null)
            {
                return null;
            }
            float[] values =
            {
                tire[0], rim[0], rim[1], rim[1],
                tire[0], tire[0], tire[0] + BeadSeatWidth, tire[1],
                rim[2], rim[2], rim[2], tire[2],
            };
            for (int i = 0; i < 12; i++)
            {
                byte[] bytes = BitConverter.GetBytes(values[i]);
                for (int k = 0; k < 4; k++)
                {
                    wheel.Head[TireHeaderOffset + (i * 4) + k] = bytes[3 - k];
                }
            }
            return string.Join(" ", Array.ConvertAll(values, v => v.ToString("F4", CultureInfo.InvariantCulture)));
        }

        // x min, x max and r max of a material's LOD 1 triangles; null when there are none.
        private static float[] LodExtents(CarbinModel wheel, string material)
        {
            float[] extents = null;
            foreach (CarbinModel.Section section in wheel.Sections)
            {
                double[] positions = null;
                foreach (CarbinModel.SubSection sub in section.SubSections)
                {
                    if (sub.Lod != 1 || sub.Name != material)
                    {
                        continue;
                    }
                    positions = positions ?? CarbinModel.Positions(section, section.Lod);
                    foreach (int index in sub.Indices)
                    {
                        if (index < 0 || index >= section.Lod.Count)
                        {
                            continue;
                        }
                        float x = (float) positions[index * 3];
                        float r = (float) Radius(positions, index);
                        if (extents == null)
                        {
                            extents = new float[] { x, x, r };
                        }
                        extents[0] = Math.Min(extents[0], x);
                        extents[1] = Math.Max(extents[1], x);
                        extents[2] = Math.Max(extents[2], r);
                    }
                }
            }
            return extents;
        }

        private static float ReadHeadFloat(byte[] data, int offset)
        {
            byte[] value = { data[offset + 3], data[offset + 2], data[offset + 1], data[offset] };
            return BitConverter.ToSingle(value, 0);
        }

        // Distance from the wheel axis (X).
        private static double Radius(double[] positions, int index)
        {
            double y = positions[(index * 3) + 1];
            double z = positions[(index * 3) + 2];
            return Math.Sqrt((y * y) + (z * z));
        }

        // FH traffic cars' wheels have a LOD 0 of one triangle (18 FH1 wheels; FM4: none of 442), drawn up
        // close as a small white triangle with no wheel. As for the car's close-up models
        // (RebuildCloseUpModels), LOD 0 is rebuilt from LOD 1 and wheel_lod0.xds becomes a copy of
        // wheel.xds, the texture the LOD 1 UVs map; the tire becomes outer_rim (Lod0TireToOuterRim).
        private void RebuildWheelLod0(ForzaZip wheel, string media)
        {
            foreach (ForzaZip.Entry entry in new List<ForzaZip.Entry>(wheel.Entries))
            {
                if (!entry.Name.ToLowerInvariant().EndsWith(".carbin"))
                {
                    continue;
                }
                CarbinModel model = new CarbinModel(wheel.Read(entry));
                int lod = 0;
                int lod0 = 0;
                foreach (CarbinModel.Section section in model.Sections)
                {
                    lod += section.Lod.Count;
                    lod0 += section.Lod0.Count;
                }
                // An FM2 stage wheel (fm2stage) has FM2's LOD 1-5 on an FM4 wheel's LOD 0: always rebuilt.
                if ((!this.fm2Stage && lod0 >= lod * 0.02) || !model.Lod0FromLod1(delegate(string material) { return true; }))
                {
                    continue;
                }
                int built = 0;
                bool outerRim = false;
                foreach (CarbinModel.Section section in model.Sections)
                {
                    built += section.Lod0.Count;
                    outerRim |= Lod0TireToOuterRim(section);
                }
                string tire = TireHeader(model);
                if (this.fm2Stage)
                {
                    // The rim must resize with the car's rim size: 15" reference LOD 0 + morph table.
                    double target = model.MakeWheelMorph(0.194);
                    for (int f = 8; f <= 10; f += 2)
                    {
                        byte[] bytes = BitConverter.GetBytes((float) target);
                        for (int k = 0; k < 4; k++)
                        {
                            model.Head[TireHeaderOffset + (f * 4) + k] = bytes[3 - k];
                        }
                    }
                    this.Line("    wheel {0} {1}: LOD 0 = 15\" reference (radius 0.194) with morph table, header radius {2:F4}", media, entry.Name, target);
                }
                wheel.Replace(entry, model.ToBytes());
                this.Line("    wheel {0} {1}: LOD 0 {2}, rebuilt from LOD 1: {3} vertices{4}", media, entry.Name, (lod0 > 0) ? "a placeholder of " + lod0 + " vertices" : "missing (LOD 1-4 only; draw lists now LOD 0-5 as in FM4)", built, outerRim ? ", tire -> outer_rim (FM4 tire)" : "");
                if (tire != null)
                {
                    this.Line("    wheel {0} {1}: tire fit in the header rebuilt from LOD 1: {2}", media, entry.Name, tire);
                }
                ForzaZip.Entry plain = wheel.Find("wheel.xds");
                ForzaZip.Entry close = wheel.Find("wheel_lod0.xds");
                if (plain != null)
                {
                    if (close != null)
                    {
                        wheel.Replace(close, wheel.Read(plain));
                    }
                    else
                    {
                        wheel.Add("wheel_lod0.xds", wheel.Read(plain));
                    }
                    this.Line("    wheel {0} wheel_lod0.xds: a copy of wheel.xds", media);
                }
            }
        }

        #endregion
    }
}
