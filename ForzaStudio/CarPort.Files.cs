namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Xml;

    // Reading, writing and backing up the files of the port, donor, manifest, report.
    public partial class CarPort
    {
        #region Files

        private ForzaZip OpenChanged(string path)
        {
            ForzaZip archive;
            if (!this.changedZips.TryGetValue(path, out archive))
            {
                archive = new ForzaZip(this.In(path));
                this.changedZips[path] = archive;
            }
            return archive;
        }

        private void Put(ForzaZip archive, string name, byte[] data)
        {
            ForzaZip.Entry entry = archive.Find(name);
            if (entry != null)
            {
                archive.Replace(entry, data);
            }
            else
            {
                archive.Add(name, data);
            }
        }

        private void SaveNew(ForzaZip archive, string path)
        {
            string output = this.Out(path);
            if (File.Exists(output))
            {
                this.Backup(output);
            }
            archive.Save(output);
            this.newZips.Add(path);
        }

        // Where a target file is read: its copy under OutputRoot when an earlier car of the same trial
        // folder (a DLC pack) wrote one, else itself.
        private string In(string path)
        {
            return this.InTrial(path) ?? path;
        }

        // The copy of a target file under OutputRoot, or null.
        private string InTrial(string path)
        {
            if (this.OutputRoot == null)
            {
                return null;
            }
            string copy = Path.Combine(this.OutputRoot, this.Relative(path));
            return File.Exists(copy) ? copy : null;
        }

        // Where a target file is written: itself, or its place under OutputRoot on a trial run.
        private string Out(string path)
        {
            if (this.OutputRoot == null)
            {
                return path;
            }
            string output = Path.Combine(this.OutputRoot, this.Relative(path));
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            return output;
        }

        // Before anything is written: every game file the port may replace must be free (the game
        // running in Xenia keeps UI\Textures.zip open; failing halfway would leave rewritten zips
        // without their manifest lines).
        private void CheckWritable(string databasePath)
        {
            string media = Path.Combine(this.TargetRoot, "Media");
            List<string> files = new List<string>();
            files.Add(databasePath);
            files.Add(ZipManifest.PathFor(this.TargetRoot));
            files.Add(MakeTitles.GameRules(this.TargetRoot));
            files.Add(Path.Combine(Path.Combine(media, "UI"), "Textures.zip"));
            files.Add(Path.Combine(Path.Combine(Path.Combine(media, "audio"), "Cars"), "CarModelTuning.zip"));
            files.Add(Path.Combine(Path.Combine(Path.Combine(Path.Combine(media, "audio"), "Cars"), "Engines"), "enginetuning.zip"));
            files.Add(Path.Combine(Path.Combine(Path.Combine(Path.Combine(media, "audio"), "Cars"), "Engines"), "harmonictuning.zip"));
            files.Add(Path.Combine(Path.Combine(media, "cars"), this.CarName + ".zip"));
            files.Add(Path.Combine(Path.Combine(media, "wheels"), this.MediaName + ".zip"));
            string strings = Path.Combine(media, "StringTables");
            if (Directory.Exists(strings))
            {
                files.AddRange(Directory.GetFiles(strings, "*.zip"));
            }
            List<string> busy = new List<string>();
            foreach (string file in files)
            {
                if (!File.Exists(file))
                {
                    continue;
                }
                try
                {
                    using (new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    {
                    }
                }
                catch (IOException)
                {
                    busy.Add(this.Relative(file));
                }
                catch (UnauthorizedAccessException)
                {
                    busy.Add(this.Relative(file));
                }
            }
            // Xenia keeps Media\UI\Textures.zip open while it runs: the port goes on without the thumbnail
            // (a later port of the same car with the emulator closed adds it).
            string textures = this.Relative(Path.Combine(Path.Combine(media, "UI"), "Textures.zip"));
            if (busy.Remove(textures))
            {
                this.skipThumbnail = true;
            }
            if (busy.Count > 0)
            {
                throw new IOException("Files in use (close the game or the emulator first): " + string.Join(", ", busy.ToArray()));
            }
        }

        private void Write(string path, byte[] data)
        {
            path = this.Out(path);
            if (File.Exists(path))
            {
                this.Backup(path);
            }
            string temp = path + ".tmp";
            File.WriteAllBytes(temp, data);
            File.Delete(path);
            File.Move(temp, path);
            this.Line("saved {0}", this.Relative(path));
        }

        private void Backup(string path)
        {
            // Trial files are not the game's (and a pack's second car would copy UI\Textures.zip again).
            if (!File.Exists(path) || this.OutputRoot != null || !BackupPolicy.Enabled)
            {
                return;
            }
            string folder = this.BackupFolder;
            Directory.CreateDirectory(folder);
            string copy = Path.Combine(folder, this.stamp + "_bak_" + Path.GetFileName(path));
            for (int i = 2; File.Exists(copy); i++)
            {
                copy = Path.Combine(folder, this.stamp + "_bak" + i + "_" + Path.GetFileName(path));
            }
            File.Copy(path, copy);
        }

        // ---- Donor from an installed DLC pack (TODO D-18) ----

        private static readonly Dictionary<string, string> donorRoots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // The donor is a car of a pack installed in Xenia (not in the target game's folder): the pack is laid out as a game
        // folder once (DlcSource.Stage, kept under %TEMP%) and the donor's zips, wheels and rows are read from there.
        // Nothing is written to the pack.
        private void ResolveDonor()
        {
            this.donorDb = null;
            if (GamePaths.FindCar(this.TargetRoot, this.DonorMediaName) != null)
            {
                return;
            }
            string notFound = "The donor " + this.DonorMediaName + " has no zip in the FM4 folder and no DLC pack installed in Xenia has it" + (string.IsNullOrEmpty(this.XeniaContent) || !Directory.Exists(this.XeniaContent) ? " (Xenia's content folder is not set: Tools > Settings)" : "") + ". Pick another donor.";
            if (string.IsNullOrEmpty(this.XeniaContent) || !Directory.Exists(this.XeniaContent))
            {
                throw new InvalidOperationException(notFound);
            }
            string root;
            if (!donorRoots.TryGetValue(this.XeniaContent + "|" + this.DonorMediaName, out root))
            {
                root = null;
                System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
                List<InstalledDlc> packs = InstalledPacks(this.XeniaContent);
                double scan = watch.Elapsed.TotalSeconds;
                watch.Reset();
                watch.Start();
                foreach (InstalledDlc dlc in packs)
                {
                    if (dlc.Source == null || dlc.Source.Problem != null)
                    {
                        continue;
                    }
                    if (dlc.Source.Cars().Exists(delegate(string c) { return string.Equals(c, this.DonorMediaName, StringComparison.OrdinalIgnoreCase); }))
                    {
                        root = dlc.Source.Stage(new StringBuilder());
                        double stage = watch.Elapsed.TotalSeconds;
                        watch.Reset();
                        watch.Start();
                        DlcSource.Materialize(root, this.DonorMediaName);   // the donor's zip stays: later cars of the run use it again
                        this.Line("time: donor {0}: packs scanned {1:0.0} s, pack staged {2:0.0} s, donor zip written {3:0.0} s", this.DonorMediaName, scan, stage, watch.Elapsed.TotalSeconds);
                        break;
                    }
                }
                if (root != null)
                {
                    donorRoots[this.XeniaContent + "|" + this.DonorMediaName] = root;   // a miss is not kept: a pack installed meanwhile is found next time
                }
            }
            if (root == null)
            {
                throw new InvalidOperationException(notFound);
            }
            this.DonorRoot = root;
            this.donorDb = SqliteDatabase.FromBytes(File.ReadAllBytes(Path.Combine(Path.Combine(Path.Combine(root, "Media"), "db"), "gamedb.slt")));
            this.Line("donor {0}: a car of the DLC pack installed in Xenia (read from {1})", this.DonorMediaName, root);
        }

        private static List<InstalledDlc> installedPacks;
        private static string installedPacksFor;

        // The packs installed in Xenia, read once: the scan opens every package, and every new donor of a DLC pack scanned
        // them all again (43 s for one car of a 5-car trial, 2026-10-31). Read again when the content folder changed.
        private static List<InstalledDlc> InstalledPacks(string content)
        {
            System.Text.StringBuilder signature = new System.Text.StringBuilder(content).Append('|');
            try
            {
                foreach (string entry in Directory.GetFileSystemEntries(content))
                {
                    signature.Append(entry).Append('|').Append(File.GetLastWriteTimeUtc(entry).Ticks).Append(';');
                    if (File.Exists(entry))
                    {
                        signature.Append(new FileInfo(entry).Length).Append(';');
                    }
                }
            }
            catch (IOException)
            {
            }
            string key = signature.ToString();
            if (installedPacks == null || installedPacksFor != key)
            {
                installedPacks = new List<InstalledDlc>(InstalledDlc.Scan(ForzaGame.Motorsport4, content, null));
                installedPacksFor = key;
            }
            return installedPacks;
        }

        // A value of the donor: in the target database, else in its pack's.
        private object DonorScalar(string sql, params object[] args)
        {
            object value = this.target.Scalar(sql, args);
            if ((value == null || value == DBNull.Value) && this.donorDb != null)
            {
                value = this.donorDb.Scalar(sql, args);
            }
            return value;
        }

        // The zipmanifest lines a new zip can be modelled on: the donor's, and for a DLC donor (it has no line in the game's
        // manifest) the lines of cars or wheels the game has.
        private List<string> ManifestLikes(string folder, string donorMedia)
        {
            List<string> likes = new List<string>();
            likes.Add("game:\\media\\" + folder + "\\" + donorMedia.ToLowerInvariant() + ".zip");
            if (this.DonorRoot != null)
            {
                string sql = (folder == "cars") ? "SELECT MediaName FROM Data_Car ORDER BY Id LIMIT 40" : "SELECT MediaName FROM List_Wheels ORDER BY ID LIMIT 40";
                foreach (object[] row in this.target.Query(sql).Rows)
                {
                    if (row[0] is string)
                    {
                        likes.Add("game:\\media\\" + folder + "\\" + ((string) row[0]).ToLowerInvariant() + ".zip");
                    }
                }
            }
            return likes;
        }

        private void UpdateManifest()
        {
            string manifest = this.Out(ZipManifest.PathFor(this.TargetRoot));
            if (this.OutputRoot != null && !File.Exists(manifest))
            {
                File.Copy(ZipManifest.PathFor(this.TargetRoot), manifest);
            }
            this.Backup(manifest);
            foreach (string path in this.changedZips.Keys)
            {
                ZipManifest.Update(manifest, ZipManifest.GamePath(this.TargetRoot, path), this.Out(path));
            }
            foreach (string path in this.newZips)
            {
                string folder = Path.GetFileName(Path.GetDirectoryName(path)).ToLowerInvariant();
                string like = null;
                foreach (string candidate in this.ManifestLikes(folder, (folder == "cars") ? this.DonorMediaName : this.DonorWheel()))
                {
                    like = candidate;
                    if (ZipManifest.Add(manifest, ZipManifest.GamePath(this.TargetRoot, path), this.Out(path), candidate))
                    {
                        like = null;
                        break;
                    }
                }
                if (like != null)
                {
                    throw new InvalidOperationException("zipmanifest.xml has no line like " + like + ".");
                }
            }
            this.Line("zipmanifest.xml: {0} lines updated, {1} added", this.changedZips.Count, this.newZips.Count);
        }

        // What the car took from the donor, at the end of the report: content (files or values the
        // car uses as its own) apart from donor files used only as a layout model.
        private void DonorReport()
        {
            this.Line("");
            this.Line("donor {0}, used for:", this.DonorMediaName);
            this.Line("  content{0}", (this.donorContent.Count == 0) ? ": nothing" : "");
            foreach (string use in this.donorContent)
            {
                this.Line("    " + use.Replace("{", "{{").Replace("}", "}}"));
            }
            this.Line("  layout only{0}", (this.donorFormat.Count == 0) ? ": nothing" : "");
            foreach (string use in this.donorFormat)
            {
                this.Line("    " + use.Replace("{", "{{").Replace("}", "}}"));
            }
            this.Line("");
        }

        private string DonorWheel()
        {
            return (this.DonorScalar("SELECT w.MediaName FROM List_Wheels w JOIN Data_Car c ON c.StockWheelID = w.ID WHERE c.Id = ?", this.donorId) as string) ?? this.DonorMediaName;
        }

        private string Relative(string path)
        {
            return path.StartsWith(this.TargetRoot, StringComparison.OrdinalIgnoreCase) ? path.Substring(this.TargetRoot.TrimEnd('\\').Length + 1) : path;
        }

        private void Line(string format, params object[] args)
        {
            this.Report.AppendLine(string.Format(CultureInfo.InvariantCulture, format, args));
        }

        #endregion
    }
}
