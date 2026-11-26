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
            if (car && TrafficPlates(family, sourceZipPath, sourceName))
            {
                CarPort.DropMaterial(model4, CarPort.PlateMaterial, null);   // as Run does: the plan counts the car without it
            }
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
                        ForzaZip.Entry physics = FindEntry(source, FhPhysicsDefinition.EntryName);
                        target.Replace(entry, MaxDataToFm2(source.Read(from), (physics == null) ? null : source.Read(physics), this.Log));
                    }
                }
                else if (name.EndsWith(".xds"))
                {
                    this.Texture(source, target, entry, family, "car");
                }
            }
            this.MaskSet(source, target);
            this.VariantTextures(source, target, family);
            this.PortShaderSettings(source, target);
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

        // ---- ShaderSettings.xml / ColorShaderSettings<n>.xml ----
        // Per-car shader values by material: <ShaderSettings><material><Parameter value=".."/ or r g b a>. FM2 has the file on
        // 116 of its 310 cars, FM4 / FM3 / FH on most; same layout in every game, and where FM2 and FM3 (FM4) give the same
        // material the same parameter, the value is the same (E:\DEV\fm4dev\PortToFM2\shaderset, shaderparams, shadercmp:
        // dirt 53 / 53, specular 50 / 51). User rule: nothing of the template car stays (2026-11-26, plan A1).
        // So: the template's files are removed; the source's are kept with the materials the ported model uses (a material
        // renamed by the user's choice keeps its settings under the new name) and the parameters FM2's own cars use; a file
        // left empty is not written (as FM2's 194 cars without one).
        private readonly HashSet<string> modelMaterials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // The parameters FM2's own cars use in their ShaderSettings.xml (shaderparams.exe on FM2's 116 files, 2026-11-26). A
        // fixed list, not read from the target folder: a DLC pack's stage holds a few cars only.
        private static readonly HashSet<string> Fm2ShaderParameterNames = new HashSet<string>(new string[]
        {
            "DirtCylinderHeight", "DirtOffsetU", "DirtOffsetV", "DirtSpecularKillAmount", "DamageSpecularKillAmount",
            "SpecularAmount", "SpecularPower", "SpecularColor", "SpecularAmount2", "SpecularPower2", "Specular2Scale", "Specular2Clamp",
            "FresnelMin", "FresnelMax", "FresnelScale", "FresnelPower", "GlassMaxOpacity", "GlassColor",
            "CubeMultiplier", "CubePower", "CubeScale", "TrackLightmapWVPS", "BrakeColor", "ReverseLightColor",
            "GhostMinDistance", "GhostMaxDistance", "GrilleScale",
            "CarbonFiberScale", "CarbonFiberEnvDampner", "CarbonFiberDiffuseColor", "CarbonFiberSpecularColor", "CarbonFiberSpecularColor2",
            "CarbonFiberSpecularPower", "CarbonFiberSpecularPower2",
            "PaintColor", "PaintScale", "ColorScale", "ColorClamp", "LiveryScale", "LiveryClamp", "FinalAlpha", "FinalScale",
            "SHLowerScale", "SHUpperScale", "SHTransition", "SHTransitionWidth",
            "TwoToneColor", "TwoTonePower", "TwoToneBias", "TwoToneScale", "MetalColor", "MetalPower", "MetalBias", "MetalScale",
        }, StringComparer.OrdinalIgnoreCase);

        private static bool IsShaderSettings(string name)
        {
            string file = Path.GetFileName(name.Replace('/', '\\'));
            return string.Equals(file, "ShaderSettings.xml", StringComparison.OrdinalIgnoreCase)
                || (file.StartsWith("ColorShaderSettings", StringComparison.OrdinalIgnoreCase) && file.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
        }

        private static System.Xml.Linq.XElement ParseShader(byte[] data)
        {
            string text = Encoding.UTF8.GetString(data);
            int start = text.IndexOf('<');
            try
            {
                return (start < 0) ? null : System.Xml.Linq.XElement.Parse(text.Substring(start));
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void PortShaderSettings(ForzaZip source, ForzaZip target)
        {
            int removed = 0;
            foreach (ForzaZip.Entry entry in target.Entries.ToArray())
            {
                if (!entry.IsDirectory && IsShaderSettings(entry.Name))
                {
                    target.Remove(entry);
                    removed++;
                }
            }
            HashSet<string> known = Fm2ShaderParameterNames;
            List<string> written = new List<string>();
            List<string> droppedNames = new List<string>();
            int droppedParameters = 0, droppedMaterials = 0;
            foreach (ForzaZip.Entry entry in source.Entries)
            {
                if (entry.IsDirectory || !IsShaderSettings(entry.Name))
                {
                    continue;
                }
                System.Xml.Linq.XElement root = ParseShader(source.Read(entry));
                if (root == null)
                {
                    this.Log.Add("shader settings: " + entry.Name + " of the source could not be read, left out");
                    continue;
                }
                System.Xml.Linq.XElement result = new System.Xml.Linq.XElement("ShaderSettings");
                foreach (System.Xml.Linq.XElement material in root.Elements())
                {
                    string name = material.Name.LocalName, renamed;
                    if (this.CarMaterials != null && this.CarMaterials.TryGetValue(name, out renamed) && !string.IsNullOrEmpty(renamed))
                    {
                        name = renamed;
                    }
                    if (!this.modelMaterials.Contains(name))
                    {
                        droppedMaterials++;
                        if (!droppedNames.Contains(name))
                        {
                            droppedNames.Add(name);
                        }
                        continue;
                    }
                    System.Xml.Linq.XElement into = result.Element(name);
                    if (into == null)
                    {
                        into = new System.Xml.Linq.XElement(name);
                        result.Add(into);
                    }
                    foreach (System.Xml.Linq.XElement parameter in material.Elements())
                    {
                        if (!known.Contains(parameter.Name.LocalName) || into.Element(parameter.Name.LocalName) != null)
                        {
                            droppedParameters++;
                            continue;
                        }
                        into.Add(Fm2Parameter(parameter));
                    }
                    if (!into.HasElements)
                    {
                        into.Remove();
                    }
                }
                if (!result.HasElements)
                {
                    continue;
                }
                string file = Path.GetFileName(entry.Name.Replace('/', '\\'));
                file = file.StartsWith("Color", StringComparison.OrdinalIgnoreCase) ? "ColorShaderSettings" + file.Substring("ColorShaderSettings".Length) : "ShaderSettings.xml";
                target.Add(file, Encoding.ASCII.GetBytes(result.ToString().Replace("\n", "\r\n").Replace("\r\r\n", "\r\n") + "\r\n"));
                int materials = 0;
                foreach (System.Xml.Linq.XElement one in result.Elements())
                {
                    materials++;
                }
                written.Add(file + " (" + materials + " materials)");
            }
            this.Log.Add(string.Format(CultureInfo.InvariantCulture, "shader settings: the template's {0} file(s) removed; from the source: {1}; left out {2} material(s) the model does not use{3} and {4} parameter(s) FM2's cars do not use",
                removed, written.Count == 0 ? "none (no file, as 194 of FM2's 310 cars)" : string.Join(", ", written.ToArray()), droppedMaterials,
                droppedNames.Count == 0 ? "" : " (" + string.Join(", ", droppedNames.ToArray()) + ")", droppedParameters));
        }

        // A parameter in FM2's layout: colours as r g b a (some source files give a b g r), values as written.
        private static System.Xml.Linq.XElement Fm2Parameter(System.Xml.Linq.XElement parameter)
        {
            System.Xml.Linq.XElement copy = new System.Xml.Linq.XElement(parameter.Name.LocalName);
            if (parameter.Attribute("r") != null && parameter.Attribute("g") != null && parameter.Attribute("b") != null)
            {
                foreach (string channel in new string[] { "r", "g", "b", "a" })
                {
                    if (parameter.Attribute(channel) != null)
                    {
                        copy.SetAttributeValue(channel, parameter.Attribute(channel).Value);
                    }
                }
                return copy;
            }
            foreach (System.Xml.Linq.XAttribute attribute in parameter.Attributes())
            {
                copy.SetAttributeValue(attribute.Name.LocalName, attribute.Value);
            }
            return copy;
        }

        // An FH1 / FH2 traffic car (placeholder close-up models, the yellow rows of the port windows) loses its number plate
        // (CarPort.PlateMaterial) in every port route (user 2026-11-26). Only Horizon models: the quick test is true for
        // any car without close-up files, which FM3's are not to be treated as.
        private static bool TrafficPlates(CarbinFamily family, string sourceZipPath, string sourceName)
        {
            return family == CarbinFamily.Horizon && sourceZipPath != null && File.Exists(sourceZipPath) && CarPort.HasPlaceholderCloseUp(sourceZipPath, sourceName);
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
            if (car && TrafficPlates(family, source.FilePath, sourceName))
            {
                List<string> plateParts = new List<string>();
                int plates = CarPort.DropMaterial(model4, CarPort.PlateMaterial, plateParts);
                if (plates > 0)
                {
                    this.Log.Add(string.Format(CultureInfo.InvariantCulture, "car model: FH traffic car, number plate removed ({0} meshes in {1}), as the FM4 and FM3 ports do", plates, string.Join(" ", plateParts.ToArray())));
                }
            }
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
                this.modelMaterials.Clear();
                foreach (Fm2CarbinModel.Part part in model.Parts)
                {
                    this.ModelParts.Add(part.Name);
                    foreach (Fm2CarbinModel.Mesh mesh in part.Meshes)
                    {
                        this.modelMaterials.Add(mesh.MaterialName);
                    }
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

        // The car textures every FM2 car zip has (310 / 310 Lights, Damagelights; 309 Damage, Nodamage): kept from the
        // template when the source has none, any other template texture goes.
        private static readonly string[] CarTexturesEveryCarHas = { "damage.xds", "damagelights.xds", "lights.xds", "nodamage.xds" };

        // A texture the FM2 zip has, from the source's texture of the same name.
        private void Texture(ForzaZip source, ForzaZip target, ForzaZip.Entry entry, CarbinFamily family, string what)
        {
            ForzaZip.Entry from = FindEntry(source, entry.Name.Replace('\\', '/'));
            if (from == null)
            {
                if (what == "car" && entry.Name.IndexOf('/') < 0 && IsVariant(entry.Name))
                {
                    return;     // a paint variant: VariantTextures keeps the source's set
                }
                if (what == "car" && entry.Name.IndexOf('/') < 0 && Array.IndexOf(CarTexturesEveryCarHas, entry.Name.ToLowerInvariant()) < 0)
                {
                    // nothing of the template stays (user rule): a texture only some FM2 cars have (Layer_1.xds: 2 of 310,
                    // glass_texture.xds: 3; the Focus 676 kept the Focus SVT's, 2026-11-26) goes when the source has none
                    target.Remove(entry);
                    this.Log.Add(string.Format("{0} texture {1}: the template's, removed (the source has none; FM2 needs only {2})", what, entry.Name, string.Join(", ", CarTexturesEveryCarHas)));
                    return;
                }
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
        // FM2's collision spheres as its cars have them: 308 of its 310 cars have 20, laid out the same way in their bounding box
        // (shtest\sph.cs, 2026-11-26). Medians of x / half width, (y - MinY) / height, (z - MinZ) / length, radius / height of the
        // right side (the steadier one), mirrored for the left, and the two centre spheres; in FM2's order (left low chain front
        // to rear, left roof, front and rear centre, right roof, right low chain rear to front).
        private static readonly double[][] Fm2SphereLayout = new double[][]
        {
            new double[] { -0.62, 0.30, 0.92, 0.24 }, new double[] { -0.64, 0.41, 0.79, 0.34 }, new double[] { -0.63, 0.41, 0.61, 0.40 },
            new double[] { -0.64, 0.43, 0.40, 0.41 }, new double[] { -0.64, 0.43, 0.21, 0.37 }, new double[] { -0.61, 0.38, 0.08, 0.27 },
            new double[] { -0.51, 0.85, 0.32, 0.15 }, new double[] { -0.54, 0.86, 0.53, 0.13 }, new double[] { -0.61, 0.72, 0.07, 0.15 },
            new double[] { 0.00, 0.35, 0.91, 0.31 }, new double[] { 0.00, 0.43, 0.09, 0.31 },
            new double[] { 0.61, 0.72, 0.07, 0.15 }, new double[] { 0.54, 0.86, 0.53, 0.13 }, new double[] { 0.51, 0.85, 0.32, 0.15 },
            new double[] { 0.61, 0.38, 0.08, 0.27 }, new double[] { 0.64, 0.43, 0.21, 0.37 }, new double[] { 0.64, 0.43, 0.40, 0.41 },
            new double[] { 0.63, 0.41, 0.61, 0.40 }, new double[] { 0.64, 0.41, 0.79, 0.34 }, new double[] { 0.62, 0.30, 0.92, 0.24 },
        };

        // The 20 spheres of FM2's layout in the car's bounding box; returns the lowest sphere bottom.
        private static double Fm2TypicalSpheres(System.Xml.XmlDocument doc, System.Xml.XmlElement spheres, System.Xml.XmlElement box, List<string> log)
        {
            double minX = double.Parse(box.GetAttribute("MinX"), CultureInfo.InvariantCulture), maxX = double.Parse(box.GetAttribute("MaxX"), CultureInfo.InvariantCulture);
            double minY = double.Parse(box.GetAttribute("MinY"), CultureInfo.InvariantCulture), maxY = double.Parse(box.GetAttribute("MaxY"), CultureInfo.InvariantCulture);
            double minZ = double.Parse(box.GetAttribute("MinZ"), CultureInfo.InvariantCulture), maxZ = double.Parse(box.GetAttribute("MaxZ"), CultureInfo.InvariantCulture);
            double half = (maxX - minX) / 2, centre = (maxX + minX) / 2, height = maxY - minY, length = maxZ - minZ, lowest = double.MaxValue;
            for (int i = 0; i < Fm2SphereLayout.Length; i++)
            {
                double[] s = Fm2SphereLayout[i];
                double radius = s[3] * height, y = minY + s[1] * height;
                System.Xml.XmlElement sphere = doc.CreateElement("Sphere" + i);
                sphere.SetAttribute("Radius", radius.ToString("0.000000", CultureInfo.InvariantCulture));
                sphere.SetAttribute("PosX", (centre + s[0] * half).ToString("0.000000", CultureInfo.InvariantCulture));
                sphere.SetAttribute("PosY", y.ToString("0.000000", CultureInfo.InvariantCulture));
                sphere.SetAttribute("PosZ", (minZ + s[2] * length).ToString("0.000000", CultureInfo.InvariantCulture));
                spheres.AppendChild(sphere);
                lowest = Math.Min(lowest, y - radius);
            }
            log.Add("MAXData.xml: COLLISIONS FIXED AUTOMATICALLY: no valid sphere was left, FM2's typical 20 spheres (its 308 cars' layout) laid out in the car's bounding box");
            return lowest;
        }

        // physicsDefinition = the source's physicsdefinition.bin (Forza Horizon cars) or null. User rule 2026-11-26: maxdata.xml
        // not valid (FhPhysicsDefinition.IsInvalid) -> the spheres come from the .bin, then go through FM2's form below as the
        // xml's would (Flags 1 left out); FM2's typical layout stays for a source without a readable .bin.
        public static byte[] MaxDataToFm2(byte[] data, byte[] physicsDefinition, List<string> log)
        {
            System.Xml.XmlDocument doc = new System.Xml.XmlDocument();
            doc.LoadXml(System.Text.Encoding.UTF8.GetString(data).Trim('\uFEFF', ' ', '\r', '\n', '\t', '\0'));
            System.Xml.XmlElement spheres = doc.SelectSingleNode("//CollSpheres") as System.Xml.XmlElement;
            System.Xml.XmlElement box = doc.SelectSingleNode("//BoundingBox") as System.Xml.XmlElement;
            bool fromBin = false;
            if (spheres != null && physicsDefinition != null && FhPhysicsDefinition.IsInvalid(spheres))
            {
                int before = spheres.ChildNodes.Count, count;
                fromBin = FhPhysicsDefinition.ApplySpheres(doc, spheres, physicsDefinition, out count);
                log.Add(fromBin
                    ? string.Format(CultureInfo.InvariantCulture, "MAXData.xml: COLLISIONS FIXED AUTOMATICALLY: the source's is not valid ({0} spheres), {1} spheres from its physicsdefinition.bin", before, count)
                    : "MAXData.xml: the source's is not valid and its physicsdefinition.bin is not readable");
            }
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
                // Flags 1 = FM4 / FH's small corner spheres (left out); 2 and 3 are kept. An FH1 traffic car (Focus ZX4,
                // FM2 car 676, crashed the game on selection 2026-11-26) has only 4 spheres, all Flags 3: dropping all but 2
                // left an empty CollSpheres (44 FH1 cars, 2 FH2 cars; focus676\spheres harness).
                foreach (System.Xml.XmlElement sphere in all)
                {
                    string flags = sphere.GetAttribute("Flags");
                    int value;
                    spheres.RemoveChild(sphere);
                    double radiusValue, y;
                    if ((flags.Length > 0 && !(int.TryParse(flags, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && (value & 2) != 0))
                        || !double.TryParse(sphere.GetAttribute("Radius"), NumberStyles.Float, CultureInfo.InvariantCulture, out radiusValue) || !(radiusValue > 0) || double.IsInfinity(radiusValue)
                        || !double.TryParse(sphere.GetAttribute("PosY"), NumberStyles.Float, CultureInfo.InvariantCulture, out y) || double.IsNaN(y))
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
            // Invalid: no sphere left, or FH's placeholder (4 spheres of r 0.375 at the wheel corners: fewer than 10, the
            // FM4 port's test, CarPort.PlaceholderSpheres; FM2 cars have 20). User rule 2026-11-26: collisions found invalid
            // are fixed automatically (FM2, FM3, FM4).
            if (spheres != null && kept < 10 && box != null && !(fromBin && kept > 0))
            {
                while (spheres.FirstChild != null)
                {
                    spheres.RemoveChild(spheres.FirstChild);
                }
                if (kept > 0)
                {
                    log.Add(string.Format(CultureInfo.InvariantCulture, "MAXData.xml: the source has a placeholder collision ({0} spheres)", kept));
                }
                lowest = Fm2TypicalSpheres(doc, spheres, box, log);
                kept = spheres.ChildNodes.Count;
                if (lowest < double.MaxValue)
                {
                    box.SetAttribute("MinY", lowest.ToString("0.000000", CultureInfo.InvariantCulture));
                }
            }
            if (spheres != null && kept == 0)
            {
                throw new InvalidOperationException("MAXData.xml: no collision sphere left for FM2 (" + dropped + " left out) and no bounding box to build them in: an FM2 car without spheres crashes the game.");
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
