namespace ForzaStudio
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Windows.Forms;

    // UNINSTALL of the FM2 window (B6 of docs/PORT_PARITY.md, user 2026-11-27), as the FM4 window's: a car ported into
    // FM2 (above FM2's own ids) goes out of the FM2 folder again (CarUninstall, its FM2 part: the car's rows, its own
    // engines, swap engines and wheel, its strings, zips, sound files, and what the port added to FM2 for it when no other
    // car uses it: make, part makers, cylinders, with their strings and logos). The plan goes into the report first.
    internal sealed partial class CarPortFm2Form
    {
        private Button uninstall;

        private void BuildUninstallButton(Panel options)
        {
            this.uninstall = new Button();
            this.uninstall.Text = "Uninstall...";
            this.uninstall.Size = new Size(100, 28);
            this.uninstall.Location = new Point(260, 94);
            new ToolTip().SetToolTip(this.uninstall, "Take this ported car out of the FM2 folder again (never one of FM2's own cars): the report lists what goes first");
            this.uninstall.Click += delegate { this.Uninstall(); };
            options.Controls.Add(this.uninstall);
        }

        private void Uninstall()
        {
            SourceCar car = this.SelectedCar;
            string target = this.loadedTarget;
            if (car == null || car.Fm2Id <= 0 || string.IsNullOrEmpty(target))
            {
                return;
            }
            CarUninstall job = new CarUninstall();
            job.TargetRoot = target;
            job.MediaName = car.TargetMedia;
            this.tabs.SelectedIndex = 0;
            this.report.Text = string.Format("Uninstall {0} (car {1}) from {2}: what goes\r\n", job.MediaName, car.Fm2Id, target);
            this.status.Text = "Listing what goes with " + car.Name + "...";
            this.Background(delegate
            {
                List<string> plan = job.Plan();
                return delegate
                {
                    this.report.Text += string.Join("\r\n", plan.ToArray()) + "\r\n";
                    if (MessageBox.Show(this, string.Format("Uninstall {0} (car {1}) from the FM2 folder?\n\n{2}\n\nThe report lists every database row, string and file that goes (only what the port added and no other car uses). Every file is backed up first to\n{3}\n\nIf you own this car in an FM2 garage, sell it first: the saved car can crash the game.",
                        car.Name, car.Fm2Id, target, job.BackupFolder), this.Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                    {
                        this.report.Text += "\r\ncancelled, nothing changed\r\n";
                        this.status.Text = "Uninstall cancelled.";
                        return;
                    }
                    if (!XeniaGuard.Confirm(this, this.Text, "the Forza Motorsport 2 files"))
                    {
                        return;
                    }
                    this.status.Text = "Uninstalling " + car.Name + "...";
                    this.Background(delegate
                    {
                        job.Report = new System.Text.StringBuilder();
                        job.Run();
                        return delegate
                        {
                            this.report.Text += "\r\n" + job.Report.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n") + "\r\nuninstalled\r\n";
                            this.report.SelectionStart = this.report.TextLength;
                            this.report.ScrollToCaret();
                            long id = car.Fm2Id;
                            car.Fm2Id = 0;
                            car.Record = null;
                            car.RecordApplied = false;
                            this.trials.Remove(car.MediaName);
                            this.installReviews.Remove(car.MediaName);
                            foreach (ListViewItem item in this.items)
                            {
                                if (item.Tag == car)
                                {
                                    item.SubItems[7].Text = car.Fm2Text;
                                    item.ForeColor = car.InFm2 ? ForzaTheme.TextDim : car.Traffic ? ForzaTheme.Yellow : ForzaTheme.Text;
                                }
                            }
                            this.Filter();
                            this.ShowTrial();
                            this.status.Text = car.Name + " (car " + id + ") is out of the FM2 folder" + (car.Fm2Own ? "." : ": it can be ported again from scratch.");
                        };
                    });
                };
            });
        }
    }
}
