namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;

    // Port of a car INTO FM2 (opened 2026-11-22; study and trials: E:\DEV\fm4dev\PortToFM2). STEP 1 of the plan agreed
    // with the user: the model, the wheel and the textures; the data (database rows, physics file, sound) come next.
    //
    // An FM2 car is one zip with one model (LODs 1-5), four textures (nodamage, damage, lights, damagelights), the six
    // livery masks and their Masks.xml, and Physics\MAXData.xml; its wheel is one zip with one model and wheel*.xds.
    // The target is an FM2 car that already exists in the target folder (a Duplicate Car copy of the FM2 car closest to
    // the source, the template): its database rows are the copy's, its two zips are rewritten here.
    //
    //   model      Fm2CarbinModel.ReplaceWholeModel: every LOD 1-5 part of the source's main model (FM4 as it is, FM3 in
    //              FM4 form, FH through the FH -> FM4 conversion: PartSource.ForMotorsport2), but 'wheel' and
    //              'undercarriage*' (no FM2 car has them, user 2026-11-22);
    //   wheel      the same, the source's stock wheel into the copy's own wheel;
    //   textures   by name; FM2 holds them as FM3 does (linear DXT, sRGB values): FM3's as they are, FM4's and FH's through
    //              Fm3Textures.FromMotorsport4;
    //   masks      FM3 / FM4 / FH keep the livery masks as tga, FM2 as A8R8G8B8 xds with the same pixels (Supra and
    //              R34 in FM2 and FM3: 100 % equal, Masks.xml the same text; PortToFM2\mask.exe).
    public class CarPortToFm2
    {
        public string SourceRoot;           // FM4 / FM3 / FH1 / FH2 game folder
        public string SourceMediaName;
        public string TargetRoot;           // FM2 game folder
        public string TargetMediaName;      // the FM2 car to rewrite (a Duplicate Car copy)
        public string BackupFolder;         // the zips as they were go here first; null: no copy
        public bool Wheel = true;
        public List<string> Log = new List<string>();

        // The FM2 car whose model is the template; null: the target car's own model as it is now (the first port over a
        // fresh Duplicate Car copy). A port run again over a ported car names the template car here.
        public string TemplateMediaName;

        // Source parts left out on top of 'wheel' and 'undercarriage*' (trials).
        public List<string> SkipParts = new List<string>();

        private string wheelTemplateMedia;     // RunWheel: the template car's stock wheel (port run again), else null

        // After Run: the part names of the FM2 car model written (for CarDataToFm2.ModelParts: no upgrade row for a part the
        // model lacks).
        public List<string> ModelParts = new List<string>();

        public static bool LeftOut(string part)
        {
            return string.Equals(part, "wheel", StringComparison.OrdinalIgnoreCase) || part.StartsWith("undercarriage", StringComparison.OrdinalIgnoreCase);
        }

        private bool LeftOutHere(string part)
        {
            return LeftOut(part) || this.SkipParts.Exists(delegate(string skip) { return string.Equals(skip, part, StringComparison.OrdinalIgnoreCase); });
        }

        // The user's material choices (the port window's Materials grid): a source material -> the FM2 material its meshes
        // get, for the car model and for the wheel model. Empty: every material keeps the source's name. Never filled by
        // the tool (user rule: material fixes are the user's choice).
        public Dictionary<string, string> CarMaterials = new Dictionary<string, string>(StringComparer.Ordinal);
        public Dictionary<string, string> WheelMaterials = new Dictionary<string, string>(StringComparer.Ordinal);

        // True when FM2's own cars (wheel: its wheels) use the material in a mesh of this LOD: the editor's rule for the
        // FM2 models (ShaderProfiles, docs\FM2\MaterialUse.txt).
        public static bool Fm2Uses(bool wheel, uint lod, string material)
        {
            return ShaderProfiles.IsUsed(ShaderProfile.Fm2, wheel ? "wheels_LOD" + Math.Max(1u, Math.Min(lod, 5u)) : "car_LOD1-5", material);
        }

        // The materials to choose from: the techniques of FM2's car effect that FM2's cars (wheel: wheels) use, by name.
        public static List<string> Fm2Materials(bool wheel)
        {
            List<string> names = new List<string>();
            foreach (string technique in ShaderTextures.Motorsport2TechniqueNames())
            {
                bool used = false;
                for (uint lod = 1; lod <= (wheel ? 5u : 1u) && !used; lod++)
                {
                    used = Fm2Uses(wheel, lod, technique);
                }
                if (used)
                {
                    names.Add(technique);
                }
            }
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        // The port window's trial: the source parts Run() would write into the FM2 car, with the vertices each takes in its
        // shared buffer and the materials of each. Nothing is written. The template is TemplateMediaName, else the target
        // car itself. SkipParts are not applied: the window shows every variant and the user ticks the ones to port.
        public List<Fm2CarbinModel.PlanPart> Plan()
        {
            string templateName = this.TemplateMediaName ?? this.TargetMediaName;
            return this.PlanOf(GamePaths.FindCar(this.SourceRoot, this.SourceMediaName), this.SourceMediaName, GamePaths.FindCar(this.TargetRoot, templateName), templateName, true);
        }

        // The same for the wheel Run() would port: the source car's stock wheel against the template car's. Null when the
        // databases or the folders do not give both wheels (Run() then leaves the FM2 wheel as it is).
        public List<Fm2CarbinModel.PlanPart> PlanWheel()
        {
            string templateName = this.TemplateMediaName ?? this.TargetMediaName;
            const string Query = "SELECT w.MediaName FROM Data_Car c JOIN List_Wheels w ON w.ID = c.StockWheelID WHERE c.MediaName = ? COLLATE NOCASE";
            string sourceWheel, templateWheel;
            using (SqliteDatabase sourceDb = SqliteDatabase.FromBytes(File.ReadAllBytes(Path.Combine(Path.Combine(Path.Combine(this.SourceRoot, "Media"), "db"), "gamedb.slt"))))
            using (SqliteDatabase targetDb = SqliteDatabase.FromBytes(File.ReadAllBytes(Path.Combine(Path.Combine(Path.Combine(this.TargetRoot, "Media"), "db"), "gamedb.slt"))))
            {
                sourceWheel = sourceDb.Scalar(Query, this.SourceMediaName) as string;
                templateWheel = targetDb.Scalar(Query, templateName) as string;
            }
            if (sourceWheel == null || templateWheel == null)
            {
                return null;
            }
            string sourcePath = GamePaths.FindMedia(this.SourceRoot, "wheels", sourceWheel);
            string templatePath = GamePaths.FindMedia(this.TargetRoot, "wheels", templateWheel);
            if (sourcePath == null || templatePath == null || !File.Exists(sourcePath) || !File.Exists(templatePath))
            {
                return null;
            }
            return this.PlanOf(sourcePath, sourceWheel, templatePath, templateWheel, false);
        }

        private List<Fm2CarbinModel.PlanPart> PlanOf(string sourceZipPath, string sourceName, string templateZipPath, string templateName, bool car)
        {
            string what = car ? "car" : "wheel";
            if (sourceZipPath == null || !File.Exists(sourceZipPath))
            {
                throw new InvalidOperationException(string.Format("The source folder has no {0} {1}.", what, sourceName));
            }
            if (templateZipPath == null || !File.Exists(templateZipPath))
            {
                throw new InvalidOperationException(string.Format("The FM2 folder has no {0} {1}.", what, templateName));
            }
            ForzaZip source = new ForzaZip(sourceZipPath);
            ForzaZip template = new ForzaZip(templateZipPath);
            ForzaZip.Entry from = FindEntry(source, sourceName + ".carbin");
            ForzaZip.Entry to = FindEntry(template, templateName + ".carbin");
            if (from == null)
            {
                throw new InvalidOperationException(string.Format("The source {0} zip has no {1}.carbin.", what, sourceName));
            }
            if (to == null)
            {
                throw new InvalidOperationException(string.Format("The FM2 {0} zip has no {1}.carbin.", what, templateName));
            }
            byte[] raw = source.Read(from);
            CarbinFamily family = CarbinFormat.Identify(raw).Family;
            if (family != CarbinFamily.Motorsport4 && family != CarbinFamily.Motorsport3 && family != CarbinFamily.Horizon)
            {
                throw new InvalidOperationException(string.Format("The source {0} model is not an FM4, FM3 or FH model.", what));
            }
            Dictionary<string, byte[]> files = new Dictionary<string, byte[]>();
            files[""] = raw;
            CarbinModel model4 = new CarbinModel(PartSource.ForMotorsport2(files, new List<string>(), new Dictionary<string, List<string>>())[""]);
            Fm2CarbinModel model = new Fm2CarbinModel(template.Read(to));
            model.PortMaterials = car ? this.CarMaterials : this.WheelMaterials;
            return model.PortPlan(model4, family == CarbinFamily.Horizon, car ? new Predicate<string>(LeftOut) : null);
        }

        public void Run()
        {
            string sourceZipPath = GamePaths.FindCar(this.SourceRoot, this.SourceMediaName);
            string targetZipPath = GamePaths.FindCar(this.TargetRoot, this.TargetMediaName);
            if (sourceZipPath == null || !File.Exists(sourceZipPath))
            {
                throw new InvalidOperationException("The source folder has no car " + this.SourceMediaName + ".");
            }
            if (targetZipPath == null || !File.Exists(targetZipPath))
            {
                throw new InvalidOperationException("The FM2 folder has no car " + this.TargetMediaName + ".");
            }
            ForzaZip source = new ForzaZip(sourceZipPath);
            ForzaZip target = new ForzaZip(targetZipPath);
            CarbinFamily family = this.Model(source, this.SourceMediaName, target, this.TargetMediaName, "car", true);

            foreach (ForzaZip.Entry entry in target.Entries.ToArray())
            {
                string name = entry.Name.Replace('\\', '/').ToLowerInvariant();
                if (name.StartsWith("liverymasks/"))
                {
                    this.LiveryMask(source, target, entry, name);
                }
                else if (name == "physics/maxdata.xml")
                {
                    ForzaZip.Entry from = FindEntry(source, "physics/maxdata.xml");
                    if (from != null)
                    {
                        target.Replace(entry, MaxDataToFm2(source.Read(from), this.Log));
                    }
                }
                else if (name.EndsWith(".xds"))
                {
                    this.Texture(source, target, entry, family, "car");
                }
            }
            this.MaskSet(source, target);
            this.VariantTextures(source, target, family);
            this.Save(target, targetZipPath);

            if (this.Wheel)
            {
                this.RunWheel(family);
            }
        }

        private void RunWheel(CarbinFamily family)
        {
            SqliteDatabase sourceDb = SqliteDatabase.FromBytes(File.ReadAllBytes(Path.Combine(Path.Combine(Path.Combine(this.SourceRoot, "Media"), "db"), "gamedb.slt")));
            SqliteDatabase targetDb = SqliteDatabase.FromBytes(File.ReadAllBytes(Path.Combine(Path.Combine(Path.Combine(this.TargetRoot, "Media"), "db"), "gamedb.slt")));
            object sourceWheel = sourceDb.Scalar("SELECT w.MediaName FROM Data_Car c JOIN List_Wheels w ON w.ID = c.StockWheelID WHERE c.MediaName = ? COLLATE NOCASE", this.SourceMediaName);
            object targetWheel = targetDb.Scalar("SELECT w.MediaName FROM Data_Car c JOIN List_Wheels w ON w.ID = c.StockWheelID WHERE c.MediaName = ? COLLATE NOCASE", this.TargetMediaName);
            if (!(sourceWheel is string) || !(targetWheel is string))
            {
                this.Log.Add("wheel: the stock wheel of the source or of the FM2 car is not in the databases, the FM2 car's wheel stays");
                return;
            }
            long users = Convert.ToInt64(targetDb.Scalar("SELECT COUNT(*) FROM Data_Car c JOIN List_Wheels w ON w.ID = c.StockWheelID WHERE w.MediaName = ? COLLATE NOCASE", targetWheel), CultureInfo.InvariantCulture);
            if (users != 1)
            {
                this.Log.Add(string.Format("wheel: {0} is the stock wheel of {1} FM2 cars, it stays (the ported car needs a wheel of its own)", targetWheel, users));
                return;
            }
            string sourcePath = GamePaths.FindMedia(this.SourceRoot, "wheels", (string) sourceWheel);
            string targetPath = GamePaths.FindMedia(this.TargetRoot, "wheels", (string) targetWheel);
            if (sourcePath == null || targetPath == null || !File.Exists(sourcePath) || !File.Exists(targetPath))
            {
                this.Log.Add(string.Format("wheel: no zip for {0} (source) or {1} (FM2), the FM2 car's wheel stays", sourceWheel, targetWheel));
                return;
            }
            // A port run again: the template car's own stock wheel is the template (its baked light calibrates the baker),
            // never the wheel a first run already rewrote.
            this.wheelTemplateMedia = null;
            if (this.TemplateMediaName != null)
            {
                this.wheelTemplateMedia = targetDb.Scalar("SELECT w.MediaName FROM Data_Car c JOIN List_Wheels w ON w.ID = c.StockWheelID WHERE c.MediaName = ? COLLATE NOCASE", this.TemplateMediaName) as string;
            }
            ForzaZip source = new ForzaZip(sourcePath);
            ForzaZip target = new ForzaZip(targetPath);
            this.Model(source, (string) sourceWheel, target, (string) targetWheel, "wheel", false);
            foreach (ForzaZip.Entry entry in target.Entries.ToArray())
            {
                if (entry.Name.ToLowerInvariant().EndsWith(".xds"))
                {
                    this.Texture(source, target, entry, family, "wheel");
                }
            }
            this.Save(target, targetPath);
        }

        private static ForzaZip.Entry FindEntry(ForzaZip zip, string name)
        {
            foreach (ForzaZip.Entry entry in zip.Entries)
            {
                if (string.Equals(entry.Name.Replace('\\', '/'), name, StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }
            return null;
        }

        private CarbinFamily Model(ForzaZip source, string sourceName, ForzaZip target, string targetName, string what, bool car)
        {
            ForzaZip.Entry from = FindEntry(source, sourceName + ".carbin");
            ForzaZip.Entry to = FindEntry(target, targetName + ".carbin");
            if (from == null)
            {
                throw new InvalidOperationException(string.Format("The source {0} zip has no {1}.carbin.", what, sourceName));
            }
            if (to == null)
            {
                throw new InvalidOperationException(string.Format("The FM2 {0} zip has no {1}.carbin.", what, targetName));
            }
            byte[] raw = source.Read(from);
            CarbinFamily family = CarbinFormat.Identify(raw).Family;
            if (family != CarbinFamily.Motorsport4 && family != CarbinFamily.Motorsport3 && family != CarbinFamily.Horizon)
            {
                throw new InvalidOperationException(string.Format("The source {0} model is not an FM4, FM3 or FH model.", what));
            }
            List<string> notes = new List<string>();
            Dictionary<string, List<string>> unknown = new Dictionary<string, List<string>>();
            Dictionary<string, byte[]> files = new Dictionary<string, byte[]>();
            files[""] = raw;
            CarbinModel model4 = new CarbinModel(PartSource.ForMotorsport2(files, notes, unknown)[""]);
            byte[] template = target.Read(to);
            if (car && this.TemplateMediaName != null)
            {
                string templatePath = GamePaths.FindCar(this.TargetRoot, this.TemplateMediaName);
                ForzaZip templateZip = (templatePath != null && File.Exists(templatePath)) ? new ForzaZip(templatePath) : null;
                ForzaZip.Entry templateEntry = (templateZip != null) ? FindEntry(templateZip, this.TemplateMediaName + ".carbin") : null;
                if (templateEntry == null)
                {
                    throw new InvalidOperationException("The FM2 folder has no model of the template car " + this.TemplateMediaName + ".");
                }
                template = templateZip.Read(templateEntry);
                this.Log.Add("car model: template " + this.TemplateMediaName);
            }
            if (!car && this.wheelTemplateMedia != null)
            {
                string templatePath = GamePaths.FindMedia(this.TargetRoot, "wheels", this.wheelTemplateMedia);
                ForzaZip templateZip = (templatePath != null && File.Exists(templatePath)) ? new ForzaZip(templatePath) : null;
                ForzaZip.Entry templateEntry = (templateZip != null) ? FindEntry(templateZip, this.wheelTemplateMedia + ".carbin") : null;
                if (templateEntry == null)
                {
                    throw new InvalidOperationException("The FM2 folder has no model of the template wheel " + this.wheelTemplateMedia + ".");
                }
                template = templateZip.Read(templateEntry);
                this.Log.Add("wheel model: template " + this.wheelTemplateMedia);
            }
            Fm2CarbinModel model = new Fm2CarbinModel(template);
            List<string> templateMaterials = new List<string>();
            foreach (Fm2CarbinModel.Part part in model.Parts)
            {
                foreach (Fm2CarbinModel.Mesh mesh in part.Meshes)
                {
                    if (!templateMaterials.Contains(mesh.MaterialName))
                    {
                        templateMaterials.Add(mesh.MaterialName);
                    }
                }
            }
            model.PortMaterials = car ? this.CarMaterials : this.WheelMaterials;
            // baked light computed on the wheel too (2026-11-23, user: the R33 rim's light copied from the nearest template
            // vertex looked wrong in game; the baker is calibrated on the template wheel's own values)
            foreach (string line in model.ReplaceWholeModel(model4, family == CarbinFamily.Horizon, car ? new Predicate<string>(this.LeftOutHere) : null, true))
            {
                this.Log.Add(what + " model: " + line);
            }
            if (!car)
            {
                // the template wheel's tire gives FM2's tire texture layout (Fm2CarbinModel.FitTireUvs)
                this.Log.Add(what + " model: " + model.FitTireUvs(new Fm2CarbinModel(template)));
            }
            byte[] bytes = model.ToBytes();
            if (car)
            {
                this.ModelParts.Clear();
                foreach (Fm2CarbinModel.Part part in model.Parts)
                {
                    this.ModelParts.Add(part.Name);
                }
            }
            List<string> problems = new Fm2CarbinModel(bytes).Check();
            if (problems.Count > 0)
            {
                throw new InvalidOperationException(what + " model after the port: " + problems[0]);
            }
            List<string> other = new List<string>();
            foreach (Fm2CarbinModel.Part part in model.Parts)
            {
                foreach (Fm2CarbinModel.Mesh mesh in part.Meshes)
                {
                    if (!templateMaterials.Contains(mesh.MaterialName) && !other.Contains(mesh.MaterialName))
                    {
                        other.Add(mesh.MaterialName);
                    }
                }
            }
            if (other.Count > 0)
            {
                other.Sort(StringComparer.OrdinalIgnoreCase);
                this.Log.Add(what + " model: materials the FM2 template does not use (check them, Rename Material): " + string.Join(", ", other.ToArray()));
            }
            target.Replace(to, bytes);
            this.Log.Add(string.Format(CultureInfo.InvariantCulture, "{0} model: {1} -> {2}.carbin, {3} parts, {4} bytes", what, sourceName, targetName, model.Parts.Count, bytes.Length));
            return family;
        }

        // A texture the FM2 zip has, from the source's texture of the same name.
        private void Texture(ForzaZip source, ForzaZip target, ForzaZip.Entry entry, CarbinFamily family, string what)
        {
            ForzaZip.Entry from = FindEntry(source, entry.Name.Replace('\\', '/'));
            if (from == null)
            {
                this.Log.Add(string.Format("{0} texture {1}: the source has none, the FM2 car's stays", what, entry.Name));
                return;
            }
            byte[] data = source.Read(from);
            if (family != CarbinFamily.Motorsport3)
            {
                data = Fm3Textures.FromMotorsport4(data, entry.Name);
            }
            if (what == "wheel" && string.Equals(Path.GetFileName(entry.Name), "wheel.xds", StringComparison.OrdinalIgnoreCase))
            {
                data = OpaqueAlpha(data);
            }
            XdsTexture was = new XdsTexture(target.Read(entry));
            XdsTexture now = new XdsTexture(data);
            target.Replace(entry, data);
            this.Log.Add(string.Format("{0} texture {1}: {2} x {3} {4}{5}", what, entry.Name, now.Width, now.Height, XdsTexture.FormatLabel(now.Codec),
                (was.Width != now.Width || was.Height != now.Height || was.Codec != now.Codec || now.IsGamma)
                    ? string.Format(" (the FM2 car's was {0} x {1} {2}{3})", was.Width, was.Height, XdsTexture.FormatLabel(was.Codec), now.IsGamma ? "; STILL GAMMA" : "") : ""));
        }

        // FM2 reads the alpha of wheel.xds as the rim's PAINT mask: low alpha = painted with List_Wheels.RGB (91 of 91
        // aftermarket rims with alpha, all with an RGB), full alpha = the texture's own colour (275 of 282 stock rims). FM4 /
        // FH wheels carry another alpha (GT-R SpecV: rim texels RGB 79, alpha 24): ported as is, FM2 painted the black rim
        // white while its blur textures (wheel0-3, alpha = see-through, kept) stayed black (user 2026-11-23). The ported
        // stock rim gets alpha 255 everywhere, like FM2's stock rims. DXT5 only (DXT1 rims have no alpha to fix).
        private static byte[] OpaqueAlpha(byte[] data)
        {
            XdsTexture texture = new XdsTexture(data);
            if (texture.Codec != DxtFormat.Dxt5)
            {
                return data;
            }
            for (int level = 0; level <= texture.MipMaxLevel; level++)
            {
                byte[] blocks = texture.ReadLevelBlocks(level);
                for (int b = 0; b + 16 <= blocks.Length; b += 16)
                {
                    for (int i = 0; i < 8; i++)
                    {
                        blocks[b + i] = 0xFF;       // alpha0 = alpha1 = 255, every index 7 (= 255)
                    }
                }
                texture.WriteLevelBlocks(level, blocks);
            }
            return texture.ToBytes();
        }

        // Paint variant textures (nodamage_<x>.xds / damage_<x>.xds, named by Combo_Colors.BaseTexture / DamageTexture): the
        // source's set (user 2026-11-24: the FH1 GTX1 crashed FM2 and was invisible; its colours, the source's, name
        // nodamage_quicksilver / _black / _gold / _blue, and the zip held the template Ford GT's nodamageblack / grey / blue /
        // _nostripe instead). Every source variant the zip lacks is added, converted like the other textures; every template
        // variant the source lacks goes. nodamage.xds / damage.xds / damagelights.xds and the _LOD0 files are not variants.
        private static bool IsVariant(string name)
        {
            string file = Path.GetFileName(name.Replace('\\', '/')).ToLowerInvariant();
            if (file.Contains("/") || !file.EndsWith(".xds") || file.EndsWith("_lod0.xds") || file.StartsWith("damagelights"))
            {
                return false;
            }
            return (file.StartsWith("nodamage") && file != "nodamage.xds") || (file.StartsWith("damage") && file != "damage.xds");
        }

        private void VariantTextures(ForzaZip source, ForzaZip target, CarbinFamily family)
        {
            HashSet<string> sourceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ForzaZip.Entry entry in source.Entries)
            {
                if (entry.Name.IndexOf('/') < 0 && entry.Name.IndexOf('\\') < 0 && IsVariant(entry.Name))
                {
                    sourceNames.Add(entry.Name);
                    if (FindEntry(target, entry.Name) == null)
                    {
                        byte[] data = source.Read(entry);
                        if (family != CarbinFamily.Motorsport3)
                        {
                            data = Fm3Textures.FromMotorsport4(data, entry.Name);
                        }
                        target.Add(entry.Name, data);
                        this.Log.Add("car texture " + entry.Name + ": added from the source (a paint variant)");
                    }
                }
            }
            foreach (ForzaZip.Entry entry in target.Entries.ToArray())
            {
                if (entry.Name.IndexOf('/') < 0 && IsVariant(entry.Name) && !sourceNames.Contains(entry.Name))
                {
                    target.Remove(entry);
                    this.Log.Add("car texture " + entry.Name + ": the template's paint variant, removed (the source has none)");
                }
            }
        }

        // The livery masks the car zip holds must be the ones its Masks.xml calls valid (user 2026-11-24: the FH2 P1 crashed
        // FM2 on a colour change; its Masks.xml, the source's, had Wing valid="true" and the zip no wing.xds, because the
        // template McLaren F1 GT has no wing mask). A mask the source has and the template lacks is added (the source's
        // pixels in a copy of one of the zip's masks); a valid mask without a file is marked invalid; a template's mask
        // the Masks.xml calls invalid goes (nothing of the template stays).
        private static readonly string[] MaskNames = { "Front", "Back", "Top", "Left", "Right", "Wing" };

        private void MaskSet(ForzaZip source, ForzaZip target)
        {
            ForzaZip.Entry xmlEntry = FindEntry(target, "LiveryMasks/Masks.xml");
            ForzaZip.Entry model = null;
            foreach (string mask in MaskNames)
            {
                model = model ?? FindEntry(target, "LiveryMasks/" + mask.ToLowerInvariant() + ".xds");
            }
            if (xmlEntry == null || model == null)
            {
                return;
            }
            System.Xml.XmlDocument doc = new System.Xml.XmlDocument();
            doc.LoadXml(System.Text.Encoding.UTF8.GetString(target.Read(xmlEntry)).Trim('﻿', ' ', '\r', '\n', '\t', '\0'));
            bool changed = false;
            foreach (string mask in MaskNames)
            {
                string file = "LiveryMasks/" + mask.ToLowerInvariant() + ".xds";
                ForzaZip.Entry own = FindEntry(target, file);
                System.Xml.XmlElement element = doc.DocumentElement.SelectSingleNode(mask) as System.Xml.XmlElement;
                bool valid = element != null && string.Equals(element.GetAttribute("valid"), "true", StringComparison.OrdinalIgnoreCase);
                if (valid && own == null)
                {
                    ForzaZip.Entry from = FindEntry(source, file.Substring(0, file.Length - 4) + ".tga") ?? FindEntry(source, file);
                    byte[] data = null;
                    if (from != null && from.Name.ToLowerInvariant().EndsWith(".xds"))
                    {
                        data = source.Read(from);
                    }
                    else if (from != null)
                    {
                        int width, height;
                        byte[] pixels = ReadTga(source.Read(from), out width, out height);
                        XdsTexture texture = new XdsTexture(target.Read(model));
                        if (texture.Width == width && texture.Height == height && texture.Codec == DxtFormat.A8R8G8B8)
                        {
                            texture.WriteLevelBlocks(0, pixels);
                            data = texture.ToBytes();
                        }
                    }
                    if (data != null)
                    {
                        target.Add(file, data);
                        this.Log.Add("livery masks: " + file + " added from the source (" + from.Name + ")");
                    }
                    else
                    {
                        element.SetAttribute("valid", "false");
                        changed = true;
                        this.Log.Add("livery masks: " + mask + " is valid in Masks.xml but there is no mask for it: marked invalid");
                    }
                }
                else if (!valid && own != null)
                {
                    target.Remove(own);
                    this.Log.Add("livery masks: " + file + " (the template's) removed, Masks.xml calls " + mask + " invalid");
                }
            }
            if (changed)
            {
                using (MemoryStream stream = new MemoryStream())
                {
                    System.Xml.XmlWriterSettings settings = new System.Xml.XmlWriterSettings { Indent = true, IndentChars = "\t", Encoding = new System.Text.UTF8Encoding(false) };
                    using (System.Xml.XmlWriter writer = System.Xml.XmlWriter.Create(stream, settings))
                    {
                        doc.Save(writer);
                    }
                    target.Replace(xmlEntry, stream.ToArray());
                }
            }
        }

        private void LiveryMask(ForzaZip source, ForzaZip target, ForzaZip.Entry entry, string name)
        {
            if (name.EndsWith(".xml"))
            {
                ForzaZip.Entry xml = FindEntry(source, name);
                if (xml != null)
                {
                    target.Replace(entry, source.Read(xml));
                    this.Log.Add("livery masks: " + entry.Name + " from the source");
                }
                return;
            }
            if (!name.EndsWith(".xds"))
            {
                return;
            }
            ForzaZip.Entry from = FindEntry(source, name.Substring(0, name.Length - 4) + ".tga") ?? FindEntry(source, name);
            if (from == null)
            {
                this.Log.Add("livery masks: the source has no " + Path.GetFileNameWithoutExtension(name) + ", the FM2 car's stays");
                return;
            }
            byte[] data = source.Read(from);
            if (from.Name.ToLowerInvariant().EndsWith(".xds"))
            {
                target.Replace(entry, data);
                return;
            }
            int width, height;
            byte[] pixels = ReadTga(data, out width, out height);
            XdsTexture texture = new XdsTexture(target.Read(entry));
            if (texture.Width != width || texture.Height != height || texture.Codec != DxtFormat.A8R8G8B8)
            {
                this.Log.Add(string.Format("livery masks: {0} is {1} x {2}, the FM2 car's {3} x {4}: left as it is", from.Name, width, height, texture.Width, texture.Height));
                return;
            }
            texture.WriteLevelBlocks(0, pixels);
            target.Replace(entry, texture.ToBytes());
            this.Log.Add(string.Format("livery masks: {0} -> {1}", from.Name, entry.Name));
        }

        // Physics\MAXData.xml in FM2's form (E:\DEV\fm4dev\PortToFM2\maxcmp.cs, miny.cs, 79 cars both FM2 and FM4 have):
        // the same Misc and box values, PosScale / RadiusScale and spheres; FM4 / FH add small corner spheres (Flags="1": 12
        // to 16 of them) that FM2 has not, and FM2 writes no Flags. FM2's BoundingBox MinY is the body's bottom, not the
        // tires' (FM4: about - ride height): the lowest sphere (PosY - Radius) on 26 of 40 cars, used here.
        public static byte[] MaxDataToFm2(byte[] data, List<string> log)
        {
            System.Xml.XmlDocument doc = new System.Xml.XmlDocument();
            doc.LoadXml(System.Text.Encoding.UTF8.GetString(data).Trim('\uFEFF', ' ', '\r', '\n', '\t', '\0'));
            System.Xml.XmlElement spheres = doc.SelectSingleNode("//CollSpheres") as System.Xml.XmlElement;
            System.Xml.XmlElement box = doc.SelectSingleNode("//BoundingBox") as System.Xml.XmlElement;
            int kept = 0, dropped = 0;
            double lowest = double.MaxValue;
            if (spheres != null)
            {
                List<System.Xml.XmlElement> all = new List<System.Xml.XmlElement>();
                foreach (System.Xml.XmlNode node in spheres.ChildNodes)
                {
                    if (node is System.Xml.XmlElement)
                    {
                        all.Add((System.Xml.XmlElement) node);
                    }
                }
                foreach (System.Xml.XmlElement sphere in all)
                {
                    string flags = sphere.GetAttribute("Flags");
                    spheres.RemoveChild(sphere);
                    if (flags.Length > 0 && flags != "2")
                    {
                        dropped++;
                        continue;
                    }
                    System.Xml.XmlElement copy = doc.CreateElement("Sphere" + kept);
                    foreach (string attribute in new string[] { "Radius", "PosX", "PosY", "PosZ" })
                    {
                        copy.SetAttribute(attribute, sphere.GetAttribute(attribute));
                    }
                    spheres.AppendChild(copy);
                    lowest = Math.Min(lowest, double.Parse(sphere.GetAttribute("PosY"), CultureInfo.InvariantCulture) - double.Parse(sphere.GetAttribute("Radius"), CultureInfo.InvariantCulture));
                    kept++;
                }
            }
            if (box != null && lowest < double.MaxValue)
            {
                log.Add(string.Format(CultureInfo.InvariantCulture, "MAXData.xml: BoundingBox MinY {0} -> {1:0.000000} (the lowest sphere: FM2's box has the body's bottom)", box.GetAttribute("MinY"), lowest));
                box.SetAttribute("MinY", lowest.ToString("0.000000", CultureInfo.InvariantCulture));
            }
            log.Add(string.Format("MAXData.xml: from the source, {0} spheres kept, {1} small spheres (Flags 1) left out", kept, dropped));
            StringBuilder text = new StringBuilder();
            System.Xml.XmlWriterSettings settings = new System.Xml.XmlWriterSettings();
            settings.Indent = true;
            settings.IndentChars = "\t";
            settings.OmitXmlDeclaration = true;
            using (System.Xml.XmlWriter writer = System.Xml.XmlWriter.Create(text, settings))
            {
                doc.DocumentElement.WriteTo(writer);
            }
            return Encoding.ASCII.GetBytes("<?xml version=\"1.0\" ?>\r\n" + text.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n") + "\r\n");
        }

        // A tga (plain or RLE, 24 / 32 bits) as rows from the top, 4 bytes a pixel in file order (B, G, R, A): the bytes an
        // FM2 mask xds holds.
        public static byte[] ReadTga(byte[] data, out int width, out int height)
        {
            int type = data[2];
            width = data[12] | (data[13] << 8);
            height = data[14] | (data[15] << 8);
            int size = data[16] / 8;
            if ((type != 2 && type != 10) || (size != 3 && size != 4))
            {
                throw new InvalidDataException("Livery mask tga: type " + type + ", " + data[16] + " bits (plain or RLE true colour expected).");
            }
            bool top = (data[17] & 0x20) != 0;
            int at = 18 + data[0];
            int total = width * height;
            byte[] pixels = new byte[total * 4];
            int n = 0;
            while (n < total)
            {
                int run = 1;
                bool repeat = false;
                if (type == 10)
                {
                    int head = data[at++];
                    run = (head & 0x7F) + 1;
                    repeat = (head & 0x80) != 0;
                }
                for (int i = 0; i < run && n < total; i++, n++)
                {
                    pixels[n * 4] = data[at];
                    pixels[(n * 4) + 1] = data[at + 1];
                    pixels[(n * 4) + 2] = data[at + 2];
                    pixels[(n * 4) + 3] = (size == 4) ? data[at + 3] : (byte) 255;
                    if (!repeat)
                    {
                        at += size;
                    }
                }
                if (repeat)
                {
                    at += size;
                }
            }
            if (!top)
            {
                byte[] flipped = new byte[pixels.Length];
                for (int y = 0; y < height; y++)
                {
                    Buffer.BlockCopy(pixels, y * width * 4, flipped, (height - 1 - y) * width * 4, width * 4);
                }
                pixels = flipped;
            }
            return pixels;
        }

        private void Save(ForzaZip zip, string path)
        {
            if (this.BackupFolder != null)
            {
                Directory.CreateDirectory(this.BackupFolder);
                string copy = Path.Combine(this.BackupFolder, Path.GetFileName(path));
                if (!File.Exists(copy))
                {
                    File.Copy(path, copy);
                }
            }
            zip.Save(path);
            string manifest = ZipManifest.PathFor(this.TargetRoot);
            string gamePath = ZipManifest.GamePath(this.TargetRoot, path);
            if (gamePath == null || !ZipManifest.Update(manifest, gamePath, path))
            {
                throw new InvalidOperationException("zipmanifest.xml has no line for " + path + ".");
            }
            this.Log.Add("written: " + path + " (zipmanifest.xml updated)");
        }
    }
}
