namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using Microsoft.Xna.Framework;

    // Copy Part into an FM2 car (ST-03, plan approved by the user 2026-11-18). The FM2 car has one model file (LODs 1-5),
    // read and written by Fm2CarbinModel (byte for byte when nothing changes), so its own parts never change; only the
    // copied part is made:
    //  - from an FM2 car: the part as it is (meshes, physics, glass, 60-byte vertices), moved in FM2 form when placed;
    //  - from an FM4 / FM3 / FH car (Source in FM4 form, PartSource.ForMotorsport2): moved in FM4 form, then
    //    Fm2CarbinModel.FromMotorsport4 (UV0 = - UV1, normal, damage word; lighting from this car's nearest vertex,
    //    normal-change word 0; FH: damage word from that vertex too, SourceIsHorizon);
    //  - a replaced part's place (draw order, emitters) and its physics and glass shards go to the copy.
    // Source files with LOD 0 only (lod0, cockpit, brakes) cannot become an FM2 part yet (lod0 -> main is the next step).
    public partial class PartImport
    {
        // The source car is an FH car (its damage words are not FM4 words): an FM2 target takes the nearest vertex's.
        public bool SourceIsHorizon;

        private void RunMotorsport2()
        {
            string sourceKey = this.SourceFile ?? "";
            if (!string.IsNullOrEmpty(this.OnlyFile))
            {
                throw new InvalidOperationException("An FM2 car has one model file (main): copy into it.");
            }
            if (!this.Source.ContainsKey(sourceKey))
            {
                throw new InvalidOperationException("The source car has no " + FileLabel(sourceKey) + " model.");
            }
            if (this.Steps.Count == 0)
            {
                throw new InvalidOperationException("No part chosen.");
            }
            Dictionary<string, string> sourceHash = Hashes(this.Source);
            Fm2CarbinModel target = new Fm2CarbinModel(this.Target[""]);
            bool nativeSource = CarbinFormat.Identify(this.Source[sourceKey]).Family == CarbinFamily.Motorsport2;
            Fm2CarbinModel source2 = nativeSource ? new Fm2CarbinModel(this.Source[sourceKey]) : null;
            CarbinModel source4 = nativeSource ? null : Open(this.Source[sourceKey], "source " + FileLabel(sourceKey) + " model");
            if (source4 != null && source4.DrawLists.Count / 2 == 1)
            {
                throw new InvalidOperationException(string.Format("The source's {0} model has LOD 0 only; an FM2 car's model has LODs 1-5. Choose the source's main model.", FileLabel(sourceKey)));
            }

            // Plan checks, as for the other games.
            List<string> newNames = new List<string>();
            foreach (Step step in this.Steps)
            {
                if ((nativeSource ? (object) source2.Find(step.Part) : (object) source4.Find(step.Part)) == null)
                {
                    throw new InvalidOperationException("The source car has no part " + step.Part + " in its " + FileLabel(sourceKey) + " model.");
                }
                if (string.IsNullOrEmpty(step.NewName))
                {
                    step.NewName = step.Part;
                }
                if (step.Replaced != null && target.Find(step.Replaced) == null)
                {
                    throw new InvalidOperationException("This car has no part " + step.Replaced + " to replace.");
                }
                if (!string.Equals(step.NewName, step.Replaced, StringComparison.OrdinalIgnoreCase) && target.Find(step.NewName) != null)
                {
                    throw new InvalidOperationException("This car already has a part named " + step.NewName + ": replace it or use another name.");
                }
                if (newNames.Contains(step.NewName))
                {
                    throw new InvalidOperationException("Two parts would be named " + step.NewName + " in this car.");
                }
                newNames.Add(step.NewName);
            }

            List<string> carMaterials = new List<string>();
            foreach (Fm2CarbinModel.Part part in target.Parts)
            {
                foreach (Fm2CarbinModel.Mesh mesh in part.Meshes)
                {
                    if (!carMaterials.Contains(mesh.MaterialName))
                    {
                        carMaterials.Add(mesh.MaterialName);
                    }
                }
            }
            // every other part of this car, to check it is unchanged afterwards
            Dictionary<string, string> others = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Fm2CarbinModel.Part part in target.Parts)
            {
                others[part.Name] = PartSignature(target, part);
            }

            List<string> notes = new List<string>();
            List<string> missingMaterials = new List<string>();
            for (int i = 0; i < this.Steps.Count; i++)
            {
                Step step = this.Steps[i];
                Fm2CarbinModel.Part replaced = (step.Replaced != null) ? target.Find(step.Replaced) : null;
                Vector3 fromCenter;
                float fromSize;
                if (nativeSource)
                {
                    Fm2CarbinModel.Part p = source2.Find(step.Part);
                    fromCenter = new Vector3(p.Bounds[0], p.Bounds[1], p.Bounds[2]);
                    fromSize = Longest2(p);
                }
                else
                {
                    CarbinModel.Section s = source4.Find(step.Part);
                    fromCenter = Center(s);
                    fromSize = Longest(s);
                }
                Matrix move = Matrix.Identity;
                if (replaced != null && (step.Align || step.Fit))
                {
                    Vector3 toCenter = new Vector3(replaced.Bounds[0], replaced.Bounds[1], replaced.Bounds[2]);
                    float scale = 1f;
                    float toSize = Longest2(replaced);
                    if (step.Fit && fromSize > 1e-6f && toSize > 1e-6f)
                    {
                        scale = toSize / fromSize;
                    }
                    move = Matrix.CreateTranslation(-fromCenter) * Matrix.CreateScale(scale) * Matrix.CreateTranslation(toCenter);
                    this.Log.Add(string.Format("{0}: moved {1:0.000} {2:0.000} {3:0.000} m (center of {4}){5}", step.NewName, toCenter.X - fromCenter.X, toCenter.Y - fromCenter.Y, toCenter.Z - fromCenter.Z, step.Replaced, (scale != 1f) ? string.Format(", scale {0:0.000}", scale) : ""));
                }
                this.Moves.Add(move);
                bool moveHere = move != Matrix.Identity && (step.MoveIn == null || step.MoveIn.Contains(""));

                // the replaced part's physics and glass shards (they sit where the copy is placed)
                byte[] physics = (replaced != null) ? (byte[]) replaced.Physics.Clone() : null;
                byte[] glassVertices = (replaced != null) ? (byte[]) replaced.GlassVertices.Clone() : null;
                byte[] glassIndices = (replaced != null) ? (byte[]) replaced.GlassIndices.Clone() : null;

                Fm2CarbinModel.Part made;
                byte[] vertices;
                if (nativeSource)
                {
                    Fm2CarbinModel.Part from = source2.Find(step.Part);
                    made = Fm2CarbinModel.CopyPart(from);
                    made.Name = step.NewName;
                    vertices = source2.PartVertices(from);
                }
                else
                {
                    CarbinModel carrier = new CarbinModel(source4.ToBytes());
                    if (moveHere)
                    {
                        carrier.TransformSection(step.Part, move);
                    }
                    // baked lighting first from the part being replaced, else from this car's part of the source's name
                    string preferPart = (replaced != null) ? replaced.Name : ((target.Find(step.Part) != null) ? step.Part : null);
                    made = target.FromMotorsport4(carrier, step.Part, step.NewName, this.SourceIsHorizon, notes, out vertices, preferPart,
                        (replaced != null) ? replaced.Name : null, true);
                }
                if (replaced != null)
                {
                    target.ReplacePart(step.Replaced, made, vertices);
                    this.Log.Add(string.Format("main: part {0} replaced by {1} (its place in the draw order, physics and glass shards kept)", step.Replaced, step.NewName));
                }
                else
                {
                    target.AddPart(made, vertices);
                }
                if (nativeSource && moveHere)
                {
                    target.TransformPart(step.NewName, move);
                }
                if (replaced != null)
                {
                    made = target.Find(step.NewName);
                    made.Physics = physics;
                    made.GlassVertices = glassVertices;
                    made.GlassIndices = glassIndices;
                }
                if (move != Matrix.Identity)
                {
                    this.Log.Add(string.Format("main: {0} {1}", step.NewName, moveHere ? "moved" : "kept at the source position, as chosen"));
                }
                Fm2CarbinModel.Part got = target.Find(step.NewName);
                this.Log.Add(string.Format("main: {0} {1} as {2}: {3} meshes, {4} vertices{5}", step.Part, nativeSource ? "copied (FM2 vertices as they are" + (moveHere ? ", positions rewritten)" : ")") : "converted to FM2", step.NewName,
                    got.Meshes.Count, got.VertexCount, nativeSource ? "" : (this.SourceIsHorizon ? " (damage words, lighting: the replaced part, else this car's nearest LOD 1 vertex)" : " (lighting: the replaced part, else this car's nearest LOD 1 vertex)")));
                foreach (Fm2CarbinModel.Mesh mesh in got.Meshes)
                {
                    if (!carMaterials.Contains(mesh.MaterialName) && !missingMaterials.Contains(mesh.MaterialName))
                    {
                        missingMaterials.Add(mesh.MaterialName);
                    }
                }
            }
            foreach (string note in notes)
            {
                this.Log.Add("main: " + note);
            }

            // Checks on the written file, read back.
            byte[] bytes = target.ToBytes();
            Fm2CarbinModel check;
            try
            {
                check = new Fm2CarbinModel(bytes);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException("main after the import: " + exception.Message, exception);
            }
            List<string> problems = check.Check();
            if (problems.Count > 0)
            {
                throw new InvalidOperationException("main after the import: " + problems[0]);
            }
            foreach (Step step in this.Steps)
            {
                Fm2CarbinModel.Part got = check.Find(step.NewName);
                if (got == null)
                {
                    throw new InvalidOperationException("main: " + step.NewName + " missing after the import.");
                }
                int meshes = 0;
                if (nativeSource)
                {
                    meshes = source2.Find(step.Part).Meshes.Count;
                }
                else
                {
                    foreach (CarbinModel.SubSection sub in source4.Find(step.Part).SubSections)
                    {
                        meshes += (sub.Lod != 0) ? 1 : 0;
                    }
                }
                if (got.Meshes.Count != meshes)
                {
                    throw new InvalidOperationException(string.Format("main: {0} has {1} meshes, the source part {2}.", step.NewName, got.Meshes.Count, meshes));
                }
                if (step.Replaced != null && !string.Equals(step.Replaced, step.NewName, StringComparison.OrdinalIgnoreCase) && check.Find(step.Replaced) != null)
                {
                    throw new InvalidOperationException("main: " + step.Replaced + " still there after the import.");
                }
                List<uint> lods = new List<uint>();
                foreach (Fm2CarbinModel.Mesh mesh in got.Meshes)
                {
                    if (!lods.Contains(mesh.Lod))
                    {
                        lods.Add(mesh.Lod);
                    }
                }
                lods.Sort();
                this.Log.Add(string.Format("{0}: LODs {1}", step.NewName, LodText(lods)));
            }
            foreach (Fm2CarbinModel.Part part in check.Parts)
            {
                string before;
                if (others.TryGetValue(part.Name, out before) && !newNames.Exists(delegate(string n) { return string.Equals(n, part.Name, StringComparison.OrdinalIgnoreCase); })
                    && before != PartSignature(check, part))
                {
                    throw new InvalidOperationException("Internal error: part " + part.Name + " of this car changed.");
                }
            }
            foreach (string material in missingMaterials)
            {
                this.Warnings.Add("material " + material + " is not used by this FM2 car: it may render black (Rename Material in the tree)");
            }
            Dictionary<string, string> after = Hashes(this.Source);
            foreach (KeyValuePair<string, string> pair in sourceHash)
            {
                if (after[pair.Key] != pair.Value)
                {
                    throw new InvalidOperationException("Internal error: the source " + FileLabel(pair.Key) + " changed.");
                }
            }
            this.Result = new Dictionary<string, byte[]>();
            this.Result[""] = bytes;
        }

        private static float Longest2(Fm2CarbinModel.Part p)
        {
            return Math.Max(p.Bounds[6] - p.Bounds[3], Math.Max(p.Bounds[7] - p.Bounds[4], p.Bounds[8] - p.Bounds[5]));
        }

        // A part's vertex bytes, meshes and physics, for "unchanged" checks.
        private static string PartSignature(Fm2CarbinModel model, Fm2CarbinModel.Part part)
        {
            System.Text.StringBuilder text = new System.Text.StringBuilder();
            foreach (float value in part.Bounds)
            {
                text.Append(value.ToString("R")).Append(',');
            }
            text.Append(Convert.ToBase64String(part.Physics)).Append(Convert.ToBase64String(part.GlassVertices)).Append(Convert.ToBase64String(part.GlassIndices));
            foreach (Fm2CarbinModel.Mesh mesh in part.Meshes)
            {
                text.Append('|').Append(mesh.MaterialName).Append(mesh.Lod).Append(':').Append(string.Join(",", mesh.Indices.ConvertAll(delegate(int x) { return x.ToString(); }).ToArray()));
            }
            using (System.Security.Cryptography.SHA1 sha = System.Security.Cryptography.SHA1.Create())
            {
                return Convert.ToBase64String(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text.ToString()))) + Convert.ToBase64String(sha.ComputeHash(model.PartVertices(part)));
            }
        }
    }
}
