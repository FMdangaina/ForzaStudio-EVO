/*
                PRIVATE SOURCE : DO NOT DISTRIBUTE !!!!!!!!!!!!

                MNd:,''''''''...................'....'....;dNM
                0;'''''.....................................,0
                ;,'''........................................'
                '''.............              ................
                '..........                        ...........
                .........    ...................... ..........
                .........   .XMMMMMMMMMMMMMMMMMMMMl   ........
                .......     .NMMMMMMMMMMMMMMMMMMMMl    .......
                .......     .NMMM0           lMMMMc    .......
                .......     .NMMMk           ,MMMMc     ......
                ......      .NMMMk           ,MMMMc    .......
                .......     .NMMMk           dMMMM,     ......
                ........    .O000o          lWMMMO      ......
                ........                  .kMMMMO.     .......
                ........                ;kWMMMWd     .........
                ..........       ..':o0NMMMMWk,     ..........
                ...........   dXNWMMMMMMMMXo.      ...........
                ..............cMMMMMMWKkl'       .............
                ...............odol;'       ..................
                ............................................''
                '........''.......................''''''''',;c
                K;..''....'..........'''''''''',,,;,;;,,,;;:oX
                MWx;'......''.''.....,,,',,;:;,,,,,,,;;;;:d0WM
 
    ______                     _____ _             _ _         _  _        __  
   |  ____|                   / ____| |           | (_)       | || |      / /  
   | |__ ___  _ __ ______ _  | (___ | |_ _   _  __| |_  ___   | || |_    / /_  
   |  __/ _ \| '__|_  / _` |  \___ \| __| | | |/ _` | |/ _ \  |__   _|  |  _ \ 
   | | | (_) | |   / / (_| |  ____) | |_| |_| | (_| | | (_) |    | |  _ | (_) |
   |_|  \___/|_|  /___\__,_| |_____/ \__|\__,_|\__,_|_|\___/     |_| (_) \___/ 
 
   Hey........................................................................
   
   So it's the source of forza studio 4.6....................voila............
 
   The changes are a bit crappy to be said....................................
   
   simple text................................................................
   
   France what is it XD ?.....................................................

*/

/*
    This is applicate to all source code of all project
    
    Forza Studio: Forza Motorsport resource extraction tool.

    Copyright (C) 2012 Mike Davis (http://codeescape.com/)

    Permission is hereby granted, free of charge, to any person 
    obtaining a copy of this software and associated documentation
    files (the "Software"), to deal in the Software without restriction,
    including without limitation the rights to use, copy, modify, merge,
    publish, distribute, sublicense, and/or sell copies of the Software,
    and to permit persons to whom the Software is furnished to do so,
    subject to the following conditions:

    The above copyright notice and this permission notice shall be included 
    in all copies or substantial portions of the Software.

    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
    OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
    FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
    THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
    LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
    FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
    DEALINGS IN THE SOFTWARE.
*/

namespace ForzaStudio
{
    #region Librery

    using Microsoft.Xna.Framework;
    using Microsoft.Xna.Framework.Graphics;
    using Microsoft.Xna.Framework.Input;
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Drawing;
    using System.Drawing.Imaging;
    using System.Globalization;
    using System.IO;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Threading;
    using System.Windows.Forms;

    /* library added */

    //using System.Runtime;
    using System.Diagnostics;
    //using System.Dynamic;
    //using System.Management;
    //using System.Threading;
    //using Microsoft.CSharp;
    //using Microsoft.Xna.Framework.Content;

    #endregion

    public class ForzaStudioForm : Form
    {

        #region CODE REBUILDED CONFUSED IL INSTRUCTIONS

            #region Fields
        private ToolStripComboBox cmbExportUV;
        private ToolStripComboBox cmbViewportSpeed;
        private IContainer components;
        private ContextMenuStrip ctxModelTreeMenu;
        private ContextMenuStrip ctxTextureMenu;
        private ContextMenuStrip ctxTextureTreeMenu;
        private ContextMenuStrip ctxViewportMenu;
        private ToolStripStatusLabel lblInformation;
        private ToolStripStatusLabel lblStatus;
        private ToolStripMenuItem mnuAbout;
        private ToolStripMenuItem mnuCreateLog;
        private ToolStripMenuItem mnuExit;
        private ToolStripMenuItem mnuExportObj;
        private ToolStripMenuItem mnuExtractAllModels;
        private ToolStripMenuItem mnuExtractAllTextures;
        private ToolStripMenuItem mnuExtractCheckedModels;
        private ToolStripMenuItem mnuExtractSelectedTextures;
        private ToolStripMenuItem mnuFile;
        private ToolStripMenuItem mnuImport;
        private MenuStrip mnuMain;
        private ToolStripMenuItem mnuSaveTextureAs;
        private ToolStripMenuItem mnuReplaceTexture;
        private ToolStripMenuItem mnuShowTextureAlpha;
        private ToolStripMenuItem mnuShowWireframe;
        private ToolStripMenuItem mnuUnpackArchive;
        private ToolStripMenuItem mnuUtilities;
        private Panel pnlTextureDisplay;
        private Panel pnlViewport;
        public RenderScene Scene;
        public int SelectedFaceCount;
        public Image SelectedTexture;
        public int SelectedVertexCount;
        private SplitContainer splitContainer1;
        private StatusStrip statusStrip1;
        private TabControl tabForzaObjects;
        private TabPage tabModels;
        private TabPage tabTextures;
        private ToolStripSeparator toolStripSeparator1;
        private TreeView tvModels;
        private TreeView tvTextures;
        public int UVset;
        public uint clicked = 0;
        public bool customkeyboard = false;
        private ToolStripMenuItem hexViewToolStripMenuItem;
        private ToolStripMenuItem saveModdingToolStripMenuItem;
        private ToolStripMenuItem optionsToolStripMenuItem;
        private ToolStripMenuItem customKeyboardToolStripMenuItem;
        private ToolStripMenuItem custom3DColorToolStripMenuItem;
        private ToolStripMenuItem showGreenScreenToolStripMenuItem;
        private ToolStripMenuItem refreechToolStripMenuItem;

        #endregion

            #region Methods

        public ForzaStudioForm()
        {
            this.InitializeComponent();
            if (ForzaTheme.AppIcon != null)
            {
                this.Icon = ForzaTheme.AppIcon;   // the FS icon, not the designer's
            }
            this.BindCameraSpeeds();
            this.BindUVset();
            SetDoubleBuffered(this.pnlTextureDisplay);
            this.Scene = new RenderScene(ref this.tvModels, ref this.pnlViewport);
        }

        private void BindCameraSpeeds()
        {
            this.cmbViewportSpeed.Items.Clear();
            for (int i = 1; i <= 20; i++)
            {
                this.cmbViewportSpeed.Items.Add(new ComboBoxItem<float>((float) i, string.Format("{0}x Camera Speed", i)));
            }
            this.cmbViewportSpeed.SelectedIndex = 0;
        }

        private void BindUVset()
        {
            this.cmbExportUV.Items.Clear();
            this.cmbExportUV.Items.Add(new ComboBoxItem<float>(1f, "Export UV1"));
            this.cmbExportUV.Items.Add(new ComboBoxItem<float>(2f, "Export UV2"));
            this.cmbExportUV.SelectedIndex = 0;
        }

        private TreeNode CreateNode(TreeNodeCollection nodes, string name)
        {
            TreeNode node = nodes[name];
            if (node == null)
            {
                node = new TreeNode(name) {
                    Name = name
                };
                nodes.Add(node);
            }
            return node;
        }

        private void ctxModelMenu_Opening(object sender, CancelEventArgs e)
        {
            e.Cancel = this.tvModels.Nodes.Count == 0;
        }

        private void ctxTextureMenu_Opening(object sender, CancelEventArgs e)
        {
            e.Cancel = this.tvTextures.Nodes.Count == 0;
        }

        private void ctxTextureTreeMenu_Opening(object sender, CancelEventArgs e)
        {
            e.Cancel = this.tvTextures.Nodes.Count == 0;
        }

        private void ctxViewportMenu_Opening(object sender, CancelEventArgs e)
        {
            e.Cancel = this.tvModels.Nodes.Count == 0;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && (this.components != null))
            {
                this.components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void ExportWavefrontObj(bool selective)
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Filter = "Wavefront OBJ (*.obj)|*.obj";
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    List<ForzaMesh> modelMeshes = this.GetModelMeshes(this.tvModels.Nodes, selective);
                    this.UpdateStatus("Building materials");
                    Hashtable hashtable = new Hashtable();
                    foreach (ForzaMesh mesh in modelMeshes)
                    {
                        hashtable[mesh.MaterialName] = 0;
                    }
                    if (hashtable.Count > 0)
                    {
                        using (FileStream stream = new FileStream(dialog.FileName.Replace(".obj", "") + ".mtl", FileMode.Create, FileAccess.Write, FileShare.Read))
                        {
                            using (StreamWriter writer = new StreamWriter(stream))
                            {
                                foreach (DictionaryEntry entry in hashtable)
                                {
                                    writer.WriteLine("newmtl " + entry.Key);
                                    Vector3 vector = Utilities.GetRandomColor(entry.Key.GetHashCode()).ToVector3();
                                    writer.WriteLine("kd {0} {1} {2}", vector.X, vector.Y, vector.Z);
                                }
                            }
                        }
                    }
                    using (FileStream stream2 = new FileStream(dialog.FileName, FileMode.Create, FileAccess.Write, FileShare.Read))
                    {
                        using (StreamWriter writer2 = new StreamWriter(stream2))
                        {
                            writer2.WriteLine("# Extracted by ForzaEditor" + Environment.NewLine);
                            if (hashtable.Count > 0)
                            {
                                writer2.WriteLine("mtllib " + Path.GetFileName(dialog.FileName).Replace(".obj", "") + ".mtl");
                            }
                            int num = 0;
                            foreach (ForzaMesh mesh2 in modelMeshes)
                            {
                                ForzaVertex vertex2;
                                ForzaVertex vertex3;
                                ForzaVertex vertex4;
                                ForzaVertex[] vertices;
                                int num11;
                                ForzaVertex[] vertexArray3;
                                int num14;
                                ForzaVertex[] vertexArray4;
                                int num17;
                                ForzaVertex[] vertexArray6;
                                this.UpdateStatus(string.Format("Extracting {0}", mesh2.Name));
                                writer2.WriteLine("g {0}", mesh2.Name);
                                writer2.WriteLine("# {0} vertices {1} faces", mesh2.VertexCount, mesh2.FaceCount);
                                foreach (ForzaVertex vertex in mesh2.Vertices)
                                {
                                    string introduced59 = vertex.position.X.ToString(CultureInfo.InvariantCulture);
                                    string introduced60 = vertex.position.Y.ToString(CultureInfo.InvariantCulture);
                                    writer2.WriteLine("v {0} {1} {2}", introduced59, introduced60, vertex.position.Z.ToString(CultureInfo.InvariantCulture));
                                }
                                this.UVset = this.cmbExportUV.SelectedIndex;
                                switch (this.UVset)
                                {
                                    case 0:
                                        vertices = mesh2.Vertices;
                                        num11 = 0;
                                        goto Label_035E;

                                    case 1:
                                        vertexArray3 = mesh2.Vertices;
                                        num14 = 0;
                                        goto Label_03CF;

                                    case 2:
                                        vertexArray4 = mesh2.Vertices;
                                        num17 = 0;
                                        goto Label_0440;

                                    default:
                                        goto Label_04B4;
                                }
                            Label_0308:
                                vertex2 = vertices[num11];
                                string introduced61 = vertex2.texture0.X.ToString(CultureInfo.InvariantCulture);
                                writer2.WriteLine("vt {0} {1}", introduced61, vertex2.texture0.Y.ToString(CultureInfo.InvariantCulture));
                                num11++;
                            Label_035E:
                                if (num11 < vertices.Length)
                                {
                                    goto Label_0308;
                                }
                                goto Label_04B4;
                            Label_0379:
                                vertex3 = vertexArray3[num14];
                                string introduced62 = vertex3.texture1.X.ToString(CultureInfo.InvariantCulture);
                                writer2.WriteLine("vt {0} {1}", introduced62, vertex3.texture1.Y.ToString(CultureInfo.InvariantCulture));
                                num14++;
                            Label_03CF:
                                if (num14 < vertexArray3.Length)
                                {
                                    goto Label_0379;
                                }
                                goto Label_04B4;
                            Label_03EA:
                                vertex4 = vertexArray4[num17];
                                string introduced63 = vertex4.texture0.X.ToString(CultureInfo.InvariantCulture);
                                writer2.WriteLine("vt {0} {1}", introduced63, vertex4.texture0.Y.ToString(CultureInfo.InvariantCulture));
                                num17++;
                            Label_0440:
                                if (num17 < vertexArray4.Length)
                                {
                                    goto Label_03EA;
                                }
                                foreach (ForzaVertex vertex5 in mesh2.Vertices)
                                {
                                    string introduced64 = vertex5.texture1.X.ToString(CultureInfo.InvariantCulture);
                                    writer2.WriteLine("#uv2 {0} {1} ", introduced64, vertex5.texture1.Y.ToString(CultureInfo.InvariantCulture));
                                }
                            Label_04B4:
                                vertexArray6 = mesh2.Vertices;
                                for (int i = 0; i < vertexArray6.Length; i++)
                                {
                                    ForzaVertex vertex6 = vertexArray6[i];
                                    string introduced65 = vertex6.normal.X.ToString(CultureInfo.InvariantCulture);
                                    string introduced66 = vertex6.normal.Y.ToString(CultureInfo.InvariantCulture);
                                    writer2.WriteLine("vn {0} {1} {2}", introduced65, introduced66, vertex6.normal.Z.ToString(CultureInfo.InvariantCulture));
                                }
                                writer2.WriteLine("usemtl {0}", mesh2.MaterialName);
                                for (int j = 0; j < mesh2.Indices.Length; j += 3)
                                {
                                    int num3 = mesh2.Indices[j] + 1;
                                    int num4 = mesh2.Indices[j + 1] + 1;
                                    int num5 = mesh2.Indices[j + 2] + 1;
                                    if (((num3 != num4) && (num4 != num5)) && (num3 != num5))
                                    {
                                        writer2.WriteLine("f {0}/{0}/{0} {1}/{1}/{1} {2}/{2}/{2}", (num + mesh2.Indices[j]) + 1, (num + mesh2.Indices[j + 1]) + 1, (num + mesh2.Indices[j + 2]) + 1);
                                    }
                                }
                                num += mesh2.Vertices.Length;
                            }
                        }
                    }
                    if (this.CreateLog)
                    {
                        using (FileStream stream3 = new FileStream(dialog.FileName.Replace(".obj", "") + ".log", FileMode.Create, FileAccess.Write, FileShare.Read))
                        {
                            using (StreamWriter writer3 = new StreamWriter(stream3))
                            {
                                writer3.WriteLine("{0} export log", dialog.FileName);
                                writer3.WriteLine("Created with ForzaEditor" + Environment.NewLine);
                                writer3.WriteLine("mesh name\tvertex count\tface count\tmaterial name" + Environment.NewLine);
                                foreach (ForzaMesh mesh3 in modelMeshes)
                                {
                                    writer3.WriteLine("{0}\t{1}\t{2}\t{3}", new object[] { mesh3.Name, mesh3.VertexCount, mesh3.FaceCount, mesh3.MaterialName });
                                }
                            }
                        }
                    }
                    MessageBox.Show("Extraction Complete!", "Finished", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
                }
            }
            this.UpdateStatus(null);
        }

        private void Form1_MouseWheel(object sender, MouseEventArgs e)
        {
            if (e.Delta < 0)
            {
                this.Scene.Camera.ApplyZoomForce(0.02f);
            }
            else if (e.Delta > 0)
            {
                this.Scene.Camera.ApplyZoomForce(-0.02f);
            }
        }

        private void ForzaStudioForm_Shown(object sender, EventArgs e)
        {
            while (!base.IsDisposed)
            {
                this.UpdateScene(false);
                this.UpdateStatus(null);
                Thread.Sleep(1);
            }
        }

        private List<ForzaMesh> GetModelMeshes(TreeNodeCollection nodes, bool selective)
        {
            List<ForzaMesh> list = new List<ForzaMesh>();
            foreach (TreeNode node in nodes)
            {
                if ((!selective || node.Checked) && ((node.Tag != null) && (node.Tag.GetType() == typeof(ForzaMesh))))
                {
                    list.Add(node.Tag as ForzaMesh);
                }
                list.AddRange(this.GetModelMeshes(node.Nodes, selective));
            }
            return list;
        }

        private int GetSelectedFaceCount(TreeNodeCollection nodes)
        {
            int num = 0;
            foreach (TreeNode node in nodes)
            {
                if ((node.Checked && (node.Tag != null)) && (node.Tag.GetType() == typeof(ForzaMesh)))
                {
                    num += ((ForzaMesh) node.Tag).FaceCount;
                }
                num += this.GetSelectedFaceCount(node.Nodes);
            }
            return num;
        }

        private int GetSelectedVertexCount(TreeNodeCollection nodes)
        {
            int num = 0;
            foreach (TreeNode node in nodes)
            {
                if ((node.Checked && (node.Tag != null)) && (node.Tag.GetType() == typeof(ForzaMesh)))
                {
                    num += ((ForzaMesh) node.Tag).VertexCount;
                }
                num += this.GetSelectedVertexCount(node.Nodes);
            }
            return num;
        }

        private void ImportFile(string fileName)
        {
            try
            {
                this.SelectedVertexCount = 0;
                this.SelectedFaceCount = 0;
                if (this.SelectedTexture != null)
                {
                    this.SelectedTexture.Dispose();
                    this.SelectedTexture = null;
                }
                if (fileName.EndsWith(".zip", StringComparison.InvariantCultureIgnoreCase))
                {
                    using (ForzaArchive archive = new ForzaArchive(fileName))
                    {
                        foreach (ZipFile file in archive.Files)
                        {
                            try
                            {
                                string str = file.FileName;
                                if (str.EndsWith(".carbin", StringComparison.InvariantCultureIgnoreCase))
                                {
                                    this.ImportForzaCarBin(fileName, str);
                                }
                                else if (str.EndsWith(".rmb.bin", StringComparison.InvariantCultureIgnoreCase))
                                {
                                    this.ImportForzaTrackBin(fileName, str);
                                }
                                else if (str.EndsWith(".xds", StringComparison.InvariantCultureIgnoreCase) || (str.EndsWith(".bix", StringComparison.InvariantCultureIgnoreCase) && !str.EndsWith("_b.bix", StringComparison.InvariantCultureIgnoreCase)))
                                {
                                    this.ImportForzaTexture(fileName, str);
                                }
                            }
                            catch (Exception exception)
                            {
                                MessageBox.Show(string.Format("Failed to import {0}\n\n{1}", fileName, exception.ToString()), "ForzaEditor Error", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                            }
                            Application.DoEvents();
                        }
                        goto Label_019C;
                    }
                }
                if (fileName.EndsWith(".carbin", StringComparison.InvariantCultureIgnoreCase))
                {
                    this.ImportForzaCarBin(null, fileName);
                }
                else if (fileName.EndsWith(".rmb.bin", StringComparison.InvariantCultureIgnoreCase))
                {
                    this.ImportForzaTrackBin(null, fileName);
                }
                else if (fileName.EndsWith(".xds", StringComparison.InvariantCultureIgnoreCase) || (fileName.EndsWith(".bix", StringComparison.InvariantCultureIgnoreCase) && !fileName.EndsWith("_b.bix", StringComparison.InvariantCultureIgnoreCase)))
                {
                    this.ImportForzaTexture(null, fileName);
                }
            }
            catch (Exception exception2)
            {
                MessageBox.Show(string.Format("Failed to import {0}\n\n{1}", fileName, exception2.ToString()), "ForzaEditor Error", MessageBoxButtons.OK, MessageBoxIcon.Hand);
            }
        Label_019C:
            Application.DoEvents();
        }

        private void ImportForzaCarBin(string archivePath, string fileName)
        {
            if (!fileName.EndsWith(".carbin", StringComparison.InvariantCultureIgnoreCase))
            {
                throw new NotSupportedException();
            }
            this.UpdateStatus(string.Format("Importing {0}", fileName));
            using (ForzaCarBin bin = new ForzaCarBin(archivePath, fileName))
            {
                TreeNode node = this.CreateNode(this.tvModels.Nodes, bin.FileName);
                foreach (ForzaCarSection section in bin.Sections)
                {
                    foreach (ForzaCarSubSection section2 in section.SubSections)
                    {
                        TreeNode node2 = this.CreateNode(node.Nodes, "LOD" + section2.Lod);
                        TreeNode node3 = this.CreateNode(node2.Nodes, section.Name);
                        TreeNode node4 = new TreeNode(section2.Name) {
                            Tag = new ForzaMesh(section.Name + "_" + section2.Name, section2.Name, section2.Indices, section2.Vertices)
                        };
                        node3.Nodes.Add(node4);
                    }
                }
            }
        }

        private void ImportForzaTexture(string archivePath, string fileName)
        {
            if ((!fileName.EndsWith(".xds", StringComparison.InvariantCultureIgnoreCase) && !fileName.EndsWith(".bix", StringComparison.InvariantCultureIgnoreCase)) && !fileName.EndsWith("_b.bix", StringComparison.InvariantCultureIgnoreCase))
            {
                throw new NotSupportedException();
            }
            this.UpdateStatus(string.Format("Importing {0}", fileName));
            ForzaTexture texture = new ForzaTexture(archivePath, fileName);
            TreeNode node = new TreeNode(texture.FileName) {
                Tag = texture
            };
            this.tvTextures.Nodes.Add(node);
        }

        private void ImportForzaTrackBin(string archivePath, string fileName)
        {
            if (!fileName.EndsWith(".rmb.bin", StringComparison.InvariantCultureIgnoreCase))
            {
                throw new NotSupportedException();
            }
            this.UpdateStatus(string.Format("Importing {0}", fileName));
            using (ForzaTrackBin bin = new ForzaTrackBin(archivePath, fileName))
            {
                TreeNode node = this.CreateNode(this.tvModels.Nodes, bin.FileName.Remove(bin.FileName.IndexOf('.')));
                foreach (ForzaTrackSection section in bin.Sections)
                {
                    TreeNode node2 = this.CreateNode(node.Nodes, section.Type);
                    TreeNode node3 = this.CreateNode(node2.Nodes, section.Name);
                    foreach (ForzaTrackSubSection section2 in section.SubSections)
                    {
                        TreeNode node4 = new TreeNode(section2.Name) {
                            Tag = new ForzaMesh(bin.FileName + "_" + section.Name + "_" + section2.Name, section2.Name, section2.Indices, section2.Vertices)
                        };
                        node3.Nodes.Add(node4);
                    }
                }
            }
        }

        #endregion

            #region Initialize Form
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ForzaStudioForm));
            this.ctxModelTreeMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.mnuExtractCheckedModels = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuExtractAllModels = new System.Windows.Forms.ToolStripMenuItem();
            this.ctxTextureTreeMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.mnuExtractSelectedTextures = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuExtractAllTextures = new System.Windows.Forms.ToolStripMenuItem();
            this.ctxTextureMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.mnuShowTextureAlpha = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuSaveTextureAs = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuReplaceTexture = new System.Windows.Forms.ToolStripMenuItem();
            this.showGreenScreenToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.ctxViewportMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.mnuExportObj = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuCreateLog = new System.Windows.Forms.ToolStripMenuItem();
            this.cmbExportUV = new System.Windows.Forms.ToolStripComboBox();
            this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.mnuShowWireframe = new System.Windows.Forms.ToolStripMenuItem();
            this.cmbViewportSpeed = new System.Windows.Forms.ToolStripComboBox();
            this.splitContainer1 = new System.Windows.Forms.SplitContainer();
            this.tabForzaObjects = new System.Windows.Forms.TabControl();
            this.tabModels = new System.Windows.Forms.TabPage();
            this.tvModels = new System.Windows.Forms.TreeView();
            this.tabTextures = new System.Windows.Forms.TabPage();
            this.tvTextures = new System.Windows.Forms.TreeView();
            this.pnlViewport = new System.Windows.Forms.Panel();
            this.pnlTextureDisplay = new System.Windows.Forms.Panel();
            this.statusStrip1 = new System.Windows.Forms.StatusStrip();
            this.lblStatus = new System.Windows.Forms.ToolStripStatusLabel();
            this.lblInformation = new System.Windows.Forms.ToolStripStatusLabel();
            this.mnuMain = new System.Windows.Forms.MenuStrip();
            this.mnuFile = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuImport = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuExit = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuUtilities = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuUnpackArchive = new System.Windows.Forms.ToolStripMenuItem();
            this.hexViewToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.saveModdingToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.optionsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.customKeyboardToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.custom3DColorToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuAbout = new System.Windows.Forms.ToolStripMenuItem();
            this.refreechToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.ctxModelTreeMenu.SuspendLayout();
            this.ctxTextureTreeMenu.SuspendLayout();
            this.ctxTextureMenu.SuspendLayout();
            this.ctxViewportMenu.SuspendLayout();
            this.splitContainer1.Panel1.SuspendLayout();
            this.splitContainer1.Panel2.SuspendLayout();
            this.splitContainer1.SuspendLayout();
            this.tabForzaObjects.SuspendLayout();
            this.tabModels.SuspendLayout();
            this.tabTextures.SuspendLayout();
            this.statusStrip1.SuspendLayout();
            this.mnuMain.SuspendLayout();
            this.SuspendLayout();
            // 
            // ctxModelTreeMenu
            // 
            this.ctxModelTreeMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.mnuExtractCheckedModels,
            this.mnuExtractAllModels});
            this.ctxModelTreeMenu.Name = "ctxModelMenu";
            this.ctxModelTreeMenu.Size = new System.Drawing.Size(159, 48);
            this.ctxModelTreeMenu.Opening += new System.ComponentModel.CancelEventHandler(this.ctxModelMenu_Opening);
            // 
            // mnuExtractCheckedModels
            // 
            this.mnuExtractCheckedModels.Name = "mnuExtractCheckedModels";
            this.mnuExtractCheckedModels.Size = new System.Drawing.Size(158, 22);
            this.mnuExtractCheckedModels.Text = "Extract Checked";
            this.mnuExtractCheckedModels.Click += new System.EventHandler(this.mnuExtractCheckedModels_Click);
            // 
            // mnuExtractAllModels
            // 
            this.mnuExtractAllModels.Name = "mnuExtractAllModels";
            this.mnuExtractAllModels.Size = new System.Drawing.Size(158, 22);
            this.mnuExtractAllModels.Text = "Extract All";
            this.mnuExtractAllModels.Click += new System.EventHandler(this.mnuExtractAllModels_Click);
            // 
            // ctxTextureTreeMenu
            // 
            this.ctxTextureTreeMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.mnuExtractSelectedTextures,
            this.mnuExtractAllTextures});
            this.ctxTextureTreeMenu.Name = "ctxTextureMenu";
            this.ctxTextureTreeMenu.Size = new System.Drawing.Size(159, 48);
            this.ctxTextureTreeMenu.Opening += new System.ComponentModel.CancelEventHandler(this.ctxTextureTreeMenu_Opening);
            // 
            // mnuExtractSelectedTextures
            // 
            this.mnuExtractSelectedTextures.Name = "mnuExtractSelectedTextures";
            this.mnuExtractSelectedTextures.Size = new System.Drawing.Size(158, 22);
            this.mnuExtractSelectedTextures.Text = "Extract Checked";
            this.mnuExtractSelectedTextures.Click += new System.EventHandler(this.mnuExtractCheckedTextures_Click);
            // 
            // mnuExtractAllTextures
            // 
            this.mnuExtractAllTextures.Name = "mnuExtractAllTextures";
            this.mnuExtractAllTextures.Size = new System.Drawing.Size(158, 22);
            this.mnuExtractAllTextures.Text = "Extract All";
            this.mnuExtractAllTextures.Click += new System.EventHandler(this.mnuExtractAllTextures_Click);
            // 
            // ctxTextureMenu
            // 
            this.ctxTextureMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.mnuShowTextureAlpha,
            this.mnuSaveTextureAs,
            this.mnuReplaceTexture,
            this.showGreenScreenToolStripMenuItem});
            this.ctxTextureMenu.Name = "ctxTextureMenuDetails";
            this.ctxTextureMenu.Size = new System.Drawing.Size(185, 70);
            this.ctxTextureMenu.Opening += new System.ComponentModel.CancelEventHandler(this.ctxTextureMenu_Opening);
            // 
            // mnuShowTextureAlpha
            // 
            this.mnuShowTextureAlpha.CheckOnClick = true;
            this.mnuShowTextureAlpha.Name = "mnuShowTextureAlpha";
            this.mnuShowTextureAlpha.Size = new System.Drawing.Size(184, 22);
            this.mnuShowTextureAlpha.Text = "Show Alpha Channel";
            this.mnuShowTextureAlpha.CheckedChanged += new System.EventHandler(this.mnuShowTextureAlpha_CheckedChanged);
            // 
            // mnuSaveTextureAs
            // 
            this.mnuSaveTextureAs.Name = "mnuSaveTextureAs";
            this.mnuSaveTextureAs.Size = new System.Drawing.Size(184, 22);
            this.mnuSaveTextureAs.Text = "Save As...";
            this.mnuSaveTextureAs.Click += new System.EventHandler(this.mnuSaveTextureAs_Click);
            // 
            // mnuReplaceTexture
            // 
            this.mnuReplaceTexture.Name = "mnuReplaceTexture";
            this.mnuReplaceTexture.Size = new System.Drawing.Size(184, 22);
            this.mnuReplaceTexture.Text = "Replace From Image...";
            this.mnuReplaceTexture.Click += new System.EventHandler(this.mnuReplaceTexture_Click);
            // 
            // showGreenScreenToolStripMenuItem
            // 
            this.showGreenScreenToolStripMenuItem.Name = "showGreenScreenToolStripMenuItem";
            this.showGreenScreenToolStripMenuItem.Size = new System.Drawing.Size(184, 22);
            this.showGreenScreenToolStripMenuItem.Text = "Show Green Screen";
            this.showGreenScreenToolStripMenuItem.Click += new System.EventHandler(this.showGreenScreenToolStripMenuItem_Click);
            // 
            // ctxViewportMenu
            // 
            this.ctxViewportMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.mnuExportObj,
            this.mnuCreateLog,
            this.cmbExportUV,
            this.toolStripSeparator1,
            this.mnuShowWireframe,
            this.cmbViewportSpeed});
            this.ctxViewportMenu.Name = "ctxViewportMenu";
            this.ctxViewportMenu.Size = new System.Drawing.Size(192, 130);
            this.ctxViewportMenu.Opening += new System.ComponentModel.CancelEventHandler(this.ctxViewportMenu_Opening);
            // 
            // mnuExportObj
            // 
            this.mnuExportObj.Name = "mnuExportObj";
            this.mnuExportObj.Size = new System.Drawing.Size(191, 22);
            this.mnuExportObj.Text = "Export Wavefront .OBJ";
            this.mnuExportObj.Click += new System.EventHandler(this.mnuExportObj_Click);
            // 
            // mnuCreateLog
            // 
            this.mnuCreateLog.CheckOnClick = true;
            this.mnuCreateLog.Name = "mnuCreateLog";
            this.mnuCreateLog.Size = new System.Drawing.Size(191, 22);
            this.mnuCreateLog.Text = "Create Export Log";
            this.mnuCreateLog.Click += new System.EventHandler(this.mnuCreateLog_Click);
            // 
            // cmbExportUV
            // 
            this.cmbExportUV.Items.AddRange(new object[] {
            "Export UV1",
            "Export UV2"});
            this.cmbExportUV.Name = "cmbExportUV";
            this.cmbExportUV.Size = new System.Drawing.Size(121, 23);
            // 
            // toolStripSeparator1
            // 
            this.toolStripSeparator1.Name = "toolStripSeparator1";
            this.toolStripSeparator1.Size = new System.Drawing.Size(188, 6);
            // 
            // mnuShowWireframe
            // 
            this.mnuShowWireframe.CheckOnClick = true;
            this.mnuShowWireframe.Name = "mnuShowWireframe";
            this.mnuShowWireframe.Size = new System.Drawing.Size(191, 22);
            this.mnuShowWireframe.Text = "Show Wireframe";
            this.mnuShowWireframe.Click += new System.EventHandler(this.mnuShowWireframe_Click);
            // 
            // cmbViewportSpeed
            // 
            this.cmbViewportSpeed.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbViewportSpeed.Items.AddRange(new object[] {
            "1x Camera Speed",
            "2x Camera Speed"});
            this.cmbViewportSpeed.Name = "cmbViewportSpeed";
            this.cmbViewportSpeed.Size = new System.Drawing.Size(121, 23);
            // 
            // splitContainer1
            // 
            this.splitContainer1.BackgroundImage = global::Properties.Resources.mainfont;
            this.splitContainer1.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.splitContainer1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitContainer1.FixedPanel = System.Windows.Forms.FixedPanel.Panel1;
            this.splitContainer1.Location = new System.Drawing.Point(0, 24);
            this.splitContainer1.Name = "splitContainer1";
            // 
            // splitContainer1.Panel1
            // 
            this.splitContainer1.Panel1.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(17)))), ((int)(((byte)(17)))), ((int)(((byte)(17)))));
            this.splitContainer1.Panel1.Controls.Add(this.tabForzaObjects);
            this.splitContainer1.Panel1MinSize = 200;
            // 
            // splitContainer1.Panel2
            // 
            this.splitContainer1.Panel2.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(17)))), ((int)(((byte)(17)))), ((int)(((byte)(17)))));
            this.splitContainer1.Panel2.Controls.Add(this.pnlViewport);
            this.splitContainer1.Panel2.Controls.Add(this.pnlTextureDisplay);
            this.splitContainer1.Size = new System.Drawing.Size(840, 495);
            this.splitContainer1.SplitterDistance = 233;
            this.splitContainer1.SplitterWidth = 3;
            this.splitContainer1.TabIndex = 2;
            // 
            // tabForzaObjects
            // 
            this.tabForzaObjects.Controls.Add(this.tabModels);
            this.tabForzaObjects.Controls.Add(this.tabTextures);
            this.tabForzaObjects.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tabForzaObjects.Location = new System.Drawing.Point(0, 0);
            this.tabForzaObjects.Name = "tabForzaObjects";
            this.tabForzaObjects.SelectedIndex = 0;
            this.tabForzaObjects.Size = new System.Drawing.Size(231, 493);
            this.tabForzaObjects.TabIndex = 1;
            this.tabForzaObjects.Selecting += new System.Windows.Forms.TabControlCancelEventHandler(this.tabForzaObjects_Selecting);
            // 
            // tabModels
            // 
            this.tabModels.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(17)))), ((int)(((byte)(17)))), ((int)(((byte)(17)))));
            this.tabModels.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.tabModels.Controls.Add(this.tvModels);
            this.tabModels.Location = new System.Drawing.Point(4, 22);
            this.tabModels.Name = "tabModels";
            this.tabModels.Padding = new System.Windows.Forms.Padding(3);
            this.tabModels.Size = new System.Drawing.Size(223, 467);
            this.tabModels.TabIndex = 0;
            this.tabModels.Text = "Models";
            // 
            // tvModels
            // 
            this.tvModels.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(17)))), ((int)(((byte)(17)))), ((int)(((byte)(17)))));
            this.tvModels.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.tvModels.CheckBoxes = true;
            this.tvModels.ContextMenuStrip = this.ctxModelTreeMenu;
            this.tvModels.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tvModels.ForeColor = System.Drawing.Color.White;
            this.tvModels.Location = new System.Drawing.Point(3, 3);
            this.tvModels.Name = "tvModels";
            this.tvModels.Size = new System.Drawing.Size(215, 459);
            this.tvModels.TabIndex = 0;
            this.tvModels.AfterCheck += new System.Windows.Forms.TreeViewEventHandler(this.tvModels_AfterCheck);
            // 
            // tabTextures
            // 
            this.tabTextures.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(17)))), ((int)(((byte)(17)))), ((int)(((byte)(17)))));
            this.tabTextures.Controls.Add(this.tvTextures);
            this.tabTextures.Location = new System.Drawing.Point(4, 22);
            this.tabTextures.Name = "tabTextures";
            this.tabTextures.Padding = new System.Windows.Forms.Padding(3);
            this.tabTextures.Size = new System.Drawing.Size(223, 467);
            this.tabTextures.TabIndex = 1;
            this.tabTextures.Text = "Textures";
            // 
            // tvTextures
            // 
            this.tvTextures.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(17)))), ((int)(((byte)(17)))), ((int)(((byte)(17)))));
            this.tvTextures.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.tvTextures.CheckBoxes = true;
            this.tvTextures.ContextMenuStrip = this.ctxTextureTreeMenu;
            this.tvTextures.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tvTextures.ForeColor = System.Drawing.Color.White;
            this.tvTextures.Location = new System.Drawing.Point(3, 3);
            this.tvTextures.Name = "tvTextures";
            this.tvTextures.Size = new System.Drawing.Size(217, 461);
            this.tvTextures.TabIndex = 0;
            this.tvTextures.AfterSelect += new System.Windows.Forms.TreeViewEventHandler(this.tvTextures_AfterSelect);
            // 
            // pnlViewport
            // 
            this.pnlViewport.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(17)))), ((int)(((byte)(17)))), ((int)(((byte)(17)))));
            this.pnlViewport.ContextMenuStrip = this.ctxViewportMenu;
            this.pnlViewport.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pnlViewport.Location = new System.Drawing.Point(0, 0);
            this.pnlViewport.Name = "pnlViewport";
            this.pnlViewport.Size = new System.Drawing.Size(602, 493);
            this.pnlViewport.TabIndex = 0;
            this.pnlViewport.Paint += new System.Windows.Forms.PaintEventHandler(this.pnlViewport_Paint);
            this.pnlViewport.Resize += new System.EventHandler(this.pnlViewport_Resize);
            // 
            // pnlTextureDisplay
            // 
            this.pnlTextureDisplay.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(17)))), ((int)(((byte)(17)))), ((int)(((byte)(17)))));
            this.pnlTextureDisplay.ContextMenuStrip = this.ctxTextureMenu;
            this.pnlTextureDisplay.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pnlTextureDisplay.Location = new System.Drawing.Point(0, 0);
            this.pnlTextureDisplay.Name = "pnlTextureDisplay";
            this.pnlTextureDisplay.Size = new System.Drawing.Size(602, 493);
            this.pnlTextureDisplay.TabIndex = 1;
            this.pnlTextureDisplay.Visible = false;
            this.pnlTextureDisplay.Paint += new System.Windows.Forms.PaintEventHandler(this.pnlTextureDisplay_Paint);
            this.pnlTextureDisplay.Resize += new System.EventHandler(this.pnlTextureDisplay_Resize);
            // 
            // statusStrip1
            // 
            this.statusStrip1.BackgroundImage = global::Properties.Resources.mainmenu;
            this.statusStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.lblStatus,
            this.lblInformation});
            this.statusStrip1.LayoutStyle = System.Windows.Forms.ToolStripLayoutStyle.HorizontalStackWithOverflow;
            this.statusStrip1.Location = new System.Drawing.Point(0, 519);
            this.statusStrip1.Name = "statusStrip1";
            this.statusStrip1.Size = new System.Drawing.Size(840, 22);
            this.statusStrip1.TabIndex = 0;
            this.statusStrip1.Text = "statusStrip1";
            // 
            // lblStatus
            // 
            this.lblStatus.BackColor = System.Drawing.Color.Transparent;
            this.lblStatus.ForeColor = System.Drawing.Color.White;
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(0, 17);
            // 
            // lblInformation
            // 
            this.lblInformation.BackColor = System.Drawing.Color.Transparent;
            this.lblInformation.ForeColor = System.Drawing.Color.White;
            this.lblInformation.Name = "lblInformation";
            this.lblInformation.Size = new System.Drawing.Size(0, 17);
            // 
            // mnuMain
            // 
            this.mnuMain.BackColor = System.Drawing.Color.Black;
            this.mnuMain.BackgroundImage = global::Properties.Resources.mainmenu;
            this.mnuMain.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.mnuFile,
            this.mnuUtilities,
            this.optionsToolStripMenuItem,
            this.mnuAbout,
            this.refreechToolStripMenuItem});
            this.mnuMain.Location = new System.Drawing.Point(0, 0);
            this.mnuMain.Name = "mnuMain";
            this.mnuMain.Size = new System.Drawing.Size(840, 24);
            this.mnuMain.TabIndex = 1;
            // 
            // mnuFile
            // 
            this.mnuFile.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.mnuImport,
            this.mnuExit});
            this.mnuFile.ForeColor = System.Drawing.Color.White;
            this.mnuFile.Image = global::Properties.Resources.IconFile1;
            this.mnuFile.Name = "mnuFile";
            this.mnuFile.Size = new System.Drawing.Size(53, 20);
            this.mnuFile.Text = "File";
            // 
            // mnuImport
            // 
            this.mnuImport.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(17)))), ((int)(((byte)(17)))), ((int)(((byte)(17)))));
            this.mnuImport.ForeColor = System.Drawing.Color.White;
            this.mnuImport.Image = global::Properties.Resources.IconFile2;
            this.mnuImport.Name = "mnuImport";
            this.mnuImport.Size = new System.Drawing.Size(110, 22);
            this.mnuImport.Text = "Import";
            this.mnuImport.Click += new System.EventHandler(this.mnuImport_Click);
            // 
            // mnuExit
            // 
            this.mnuExit.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(17)))), ((int)(((byte)(17)))), ((int)(((byte)(17)))));
            this.mnuExit.ForeColor = System.Drawing.Color.White;
            this.mnuExit.Image = global::Properties.Resources.IconExit;
            this.mnuExit.Name = "mnuExit";
            this.mnuExit.Size = new System.Drawing.Size(110, 22);
            this.mnuExit.Text = "Exit";
            this.mnuExit.Click += new System.EventHandler(this.mnuExit_Click);
            // 
            // mnuUtilities
            // 
            this.mnuUtilities.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.mnuUnpackArchive,
            this.hexViewToolStripMenuItem,
            this.saveModdingToolStripMenuItem});
            this.mnuUtilities.ForeColor = System.Drawing.Color.White;
            this.mnuUtilities.Image = global::Properties.Resources.IconUtil1;
            this.mnuUtilities.Name = "mnuUtilities";
            this.mnuUtilities.Size = new System.Drawing.Size(74, 20);
            this.mnuUtilities.Text = "Utilities";
            // 
            // mnuUnpackArchive
            // 
            this.mnuUnpackArchive.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(17)))), ((int)(((byte)(17)))), ((int)(((byte)(17)))));
            this.mnuUnpackArchive.ForeColor = System.Drawing.Color.White;
            this.mnuUnpackArchive.Image = global::Properties.Resources.IconUnpack;
            this.mnuUnpackArchive.Name = "mnuUnpackArchive";
            this.mnuUnpackArchive.Size = new System.Drawing.Size(169, 22);
            this.mnuUnpackArchive.Text = "Unpack Archive";
            this.mnuUnpackArchive.Click += new System.EventHandler(this.mnuExtractZip_Click);
            // 
            // hexViewToolStripMenuItem
            // 
            this.hexViewToolStripMenuItem.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(17)))), ((int)(((byte)(17)))), ((int)(((byte)(17)))));
            this.hexViewToolStripMenuItem.ForeColor = System.Drawing.Color.White;
            this.hexViewToolStripMenuItem.Image = global::Properties.Resources.IconHex;
            this.hexViewToolStripMenuItem.Name = "hexViewToolStripMenuItem";
            this.hexViewToolStripMenuItem.Size = new System.Drawing.Size(169, 22);
            this.hexViewToolStripMenuItem.Text = "Hex View";
            this.hexViewToolStripMenuItem.Click += new System.EventHandler(this.hexViewToolStripMenuItem_Click);
            // 
            // saveModdingToolStripMenuItem
            // 
            this.saveModdingToolStripMenuItem.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(17)))), ((int)(((byte)(17)))), ((int)(((byte)(17)))));
            this.saveModdingToolStripMenuItem.ForeColor = System.Drawing.Color.White;
            this.saveModdingToolStripMenuItem.Image = global::Properties.Resources.IconSave;
            this.saveModdingToolStripMenuItem.Name = "saveModdingToolStripMenuItem";
            this.saveModdingToolStripMenuItem.Size = new System.Drawing.Size(169, 22);
            this.saveModdingToolStripMenuItem.Text = "Save Modding";
            this.saveModdingToolStripMenuItem.Visible = false;
            this.saveModdingToolStripMenuItem.Click += new System.EventHandler(this.saveModToolStripMenuItem_Click);
            // 
            // optionsToolStripMenuItem
            // 
            this.optionsToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.customKeyboardToolStripMenuItem,
            this.custom3DColorToolStripMenuItem});
            this.optionsToolStripMenuItem.ForeColor = System.Drawing.Color.White;
            this.optionsToolStripMenuItem.Name = "optionsToolStripMenuItem";
            this.optionsToolStripMenuItem.Size = new System.Drawing.Size(61, 20);
            this.optionsToolStripMenuItem.Text = "Options";
            this.optionsToolStripMenuItem.Visible = false;
            // 
            // customKeyboardToolStripMenuItem
            // 
            this.customKeyboardToolStripMenuItem.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(17)))), ((int)(((byte)(17)))), ((int)(((byte)(17)))));
            this.customKeyboardToolStripMenuItem.ForeColor = System.Drawing.Color.White;
            this.customKeyboardToolStripMenuItem.Name = "customKeyboardToolStripMenuItem";
            this.customKeyboardToolStripMenuItem.Size = new System.Drawing.Size(169, 22);
            this.customKeyboardToolStripMenuItem.Text = "Custom Keyboard";
            this.customKeyboardToolStripMenuItem.Click += new System.EventHandler(this.customKeyboardToolStripMenuItem_Click);
            // 
            // custom3DColorToolStripMenuItem
            // 
            this.custom3DColorToolStripMenuItem.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(17)))), ((int)(((byte)(17)))), ((int)(((byte)(17)))));
            this.custom3DColorToolStripMenuItem.ForeColor = System.Drawing.Color.White;
            this.custom3DColorToolStripMenuItem.Name = "custom3DColorToolStripMenuItem";
            this.custom3DColorToolStripMenuItem.Size = new System.Drawing.Size(169, 22);
            this.custom3DColorToolStripMenuItem.Text = "Custom 3D Color";
            this.custom3DColorToolStripMenuItem.Click += new System.EventHandler(this.custom3DColorToolStripMenuItem_Click);
            // 
            // mnuAbout
            // 
            this.mnuAbout.ForeColor = System.Drawing.Color.White;
            this.mnuAbout.Image = global::Properties.Resources.IconAbout;
            this.mnuAbout.Name = "mnuAbout";
            this.mnuAbout.Size = new System.Drawing.Size(68, 20);
            this.mnuAbout.Text = "About";
            this.mnuAbout.Click += new System.EventHandler(this.mnuAbout_Click);
            // 
            // refreechToolStripMenuItem
            // 
            this.refreechToolStripMenuItem.Name = "refreechToolStripMenuItem";
            this.refreechToolStripMenuItem.Size = new System.Drawing.Size(65, 20);
            this.refreechToolStripMenuItem.Text = "Refreech";
            this.refreechToolStripMenuItem.Visible = false;
            // 
            // ForzaStudioForm
            // 
            this.BackColor = System.Drawing.Color.Black;
            this.ClientSize = new System.Drawing.Size(840, 541);
            this.Controls.Add(this.splitContainer1);
            this.Controls.Add(this.statusStrip1);
            this.Controls.Add(this.mnuMain);
            this.DoubleBuffered = true;
            this.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(17)))), ((int)(((byte)(17)))), ((int)(((byte)(17)))));
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.MainMenuStrip = this.mnuMain;
            this.MinimumSize = new System.Drawing.Size(640, 480);
            this.Name = "ForzaStudioForm";
            this.Text = "Forza Studio 4.6 - Pay Respects";
            this.Load += new System.EventHandler(this.ForzaStudioForm_Load);
            this.Shown += new System.EventHandler(this.ForzaStudioForm_Shown);
            this.MouseWheel += new System.Windows.Forms.MouseEventHandler(this.Form1_MouseWheel);
            this.ctxModelTreeMenu.ResumeLayout(false);
            this.ctxTextureTreeMenu.ResumeLayout(false);
            this.ctxTextureMenu.ResumeLayout(false);
            this.ctxViewportMenu.ResumeLayout(false);
            this.splitContainer1.Panel1.ResumeLayout(false);
            this.splitContainer1.Panel2.ResumeLayout(false);
            this.splitContainer1.ResumeLayout(false);
            this.tabForzaObjects.ResumeLayout(false);
            this.tabModels.ResumeLayout(false);
            this.tabTextures.ResumeLayout(false);
            this.statusStrip1.ResumeLayout(false);
            this.statusStrip1.PerformLayout();
            this.mnuMain.ResumeLayout(false);
            this.mnuMain.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

            #region Functions

        private void mnuAbout_Click(object sender, EventArgs e)
        {
            AboutForm about = new AboutForm();
            about.Show();
        }

        private void mnuCreateLog_Click(object sender, EventArgs e)
        {
            this.CreateLog = true;
        }

        private void mnuExit_Click(object sender, EventArgs e)
        {
            base.Close();
        }

        private void mnuExportObj_Click(object sender, EventArgs e)
        {
            this.ExportWavefrontObj(true);
        }

        private void mnuExtractAllModels_Click(object sender, EventArgs e)
        {
            this.ExportWavefrontObj(false);
        }

        private void mnuExtractAllTextures_Click(object sender, EventArgs e)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    foreach (TreeNode node in this.tvTextures.Nodes)
                    {
                        ForzaTexture tag = node.Tag as ForzaTexture;
                        this.UpdateStatus(string.Format("Extracting {0}", tag.FileName));
                        using (Image image = tag.GetImage())
                        {
                            image.Save(dialog.SelectedPath + @"\" + tag.FileName + ".bmp", ImageFormat.Bmp);
                        }
                    }
                    MessageBox.Show("Extraction Complete!", "Finished", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
                }
            }
        }

        private void mnuExtractCheckedModels_Click(object sender, EventArgs e)
        {
            this.ExportWavefrontObj(true);
        }

        private void mnuExtractCheckedTextures_Click(object sender, EventArgs e)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    foreach (TreeNode node in this.tvTextures.Nodes)
                    {
                        if (node.Checked)
                        {
                            ForzaTexture tag = node.Tag as ForzaTexture;
                            this.UpdateStatus(string.Format("Extracting {0}", tag.FileName));
                            using (Image image = tag.GetImage())
                            {
                                image.Save(dialog.SelectedPath + @"\" + tag.FileName + ".bmp", ImageFormat.Bmp);
                            }
                        }
                    }
                    MessageBox.Show("Extraction Complete!", "Finished", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
                }
            }
        }

        private void mnuExtractZip_Click(object sender, EventArgs e)
        {
            try
            {
                using (OpenFileDialog dialog = new OpenFileDialog())
                {
                    dialog.Filter = "Forza Archive (.zip)|*.zip";
                    if (dialog.ShowDialog() == DialogResult.OK)
                    {
                        using (FolderBrowserDialog dialog2 = new FolderBrowserDialog())
                        {
                            if (dialog2.ShowDialog() == DialogResult.OK)
                            {
                                using (ForzaArchive archive = new ForzaArchive(dialog.FileName))
                                {
                                    foreach (ZipFile file in archive.Files)
                                    {
                                        try
                                        {
                                            this.UpdateStatus(string.Format("Extracting {0}", file.FileName));
                                            string path = dialog2.SelectedPath + @"\" + file.FileName;
                                            Directory.CreateDirectory(Path.GetDirectoryName(path));
                                            file.SaveAs(path);
                                        }
                                        catch (Exception)
                                        {
                                            MessageBox.Show(string.Format("Failed to extract {0} from {1}.", file.FileName, Path.GetFileName(dialog.FileName)), "ForzaEditor Error", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                                        }
                                    }
                                }
                                MessageBox.Show("Extraction Complete!", "Finished", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
                            }
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.ToString(), "ForzaEditor Error", MessageBoxButtons.OK, MessageBoxIcon.Hand);
            }
            this.UpdateStatus(null);
        }

        private void mnuImport_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Multiselect = false;
                dialog.Filter = "All Forza Files|*.zip;*.carbin;*.rmb.bin;*.xds;*.bix|Forza Archive (.zip)|*.zip|Forza Car Object (.carbin)|*.carbin|Forza Track Object (.rmb.bin)|*.rmb.bin|Forza Texture (.xds, .bix)|*.xds;*.bix";
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    this.tvModels.Nodes.Clear();
                    this.tvTextures.Nodes.Clear();
                    this.ImportFile(dialog.FileName);
                    Types.CarbinLocation = dialog.FileName;
                    if (Types.HexView == true)
                    {
                        HexView view = new HexView();
                        view.Show();
                    }
                    MessageBox.Show("Import Complete!", "Finished", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
                }
            }
            this.tvModels.Sort();
            this.tvTextures.Sort();
            this.pnlTextureDisplay.Invalidate();
            this.pnlViewport.Invalidate();
            this.UpdateStatus(null);
        }

        private void mnuSaveTextureAs_Click(object sender, EventArgs e)
        {
            try
            {
                using (SaveFileDialog dialog = new SaveFileDialog())
                {
                    dialog.Filter = "Bitmap (*.bmp)|*.bmp|PNG (*.png)|*.png|JPEG (*.jpg)|*.jpg|GIF (*.gif)|*.gif";
                    if (dialog.ShowDialog() != DialogResult.OK)
                    {
                        return;
                    }
                    string extension = Path.GetExtension(dialog.FileName.ToLowerInvariant());
                    if (extension == null)
                    {
                        goto Label_0087;
                    }
                    if (!(extension == ".bmp"))
                    {
                        if (extension == ".png")
                        {
                            goto Label_006F;
                        }
                        if (extension == ".jpg")
                        {
                            goto Label_0077;
                        }
                        if (extension == ".jpeg")
                        {
                            goto Label_007F;
                        }
                        if (extension == ".gif")
                        {
                            goto Label_666;
                        }
                        goto Label_0087;
                    }
                    ImageFormat bmp = ImageFormat.Bmp;
                    goto Label_008D;
                Label_006F:
                    bmp = ImageFormat.Png;
                    goto Label_008D;
                Label_0077:
                    bmp = ImageFormat.Jpeg;
                    goto Label_008D;
                Label_007F:
                    bmp = ImageFormat.Jpeg;
                    goto Label_008D;
                Label_666:
                    bmp = ImageFormat.Gif;
                    goto Label_008D;
                Label_0087:
                    throw new NotSupportedException();
                Label_008D:
                    using (Image image = ((ForzaTexture) this.tvTextures.SelectedNode.Tag).GetImage())
                    {
                        image.Save(dialog.FileName, bmp);
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        private void mnuReplaceTexture_Click(object sender, EventArgs e)
        {
            if (this.tvTextures.SelectedNode == null)
            {
                return;
            }
            ForzaTexture texture = (ForzaTexture) this.tvTextures.SelectedNode.Tag;
            if (texture.Extension != ".xds")
            {
                MessageBox.Show("Only .xds textures can be replaced.", "Replace Texture", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!Utilities.IsNullOrWhiteSpace(texture.ArchivePath))
            {
                MessageBox.Show("Replacing textures inside a .zip archive is not supported yet.\nExtract the .xds first.", "Replace Texture", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string texconv = Path.Combine(Application.StartupPath, "texconv.exe");
            try
            {
                XdsTexture xds = new XdsTexture(texture.GetData());
                using (OpenFileDialog dialog = new OpenFileDialog())
                {
                    dialog.Title = string.Format("Replace {0} ({1}x{2} {3})", Path.GetFileName(texture.FilePath), xds.Width, xds.Height, xds.Codec);
                    dialog.Filter = "Images (*.png;*.tga;*.bmp;*.tif;*.dds)|*.png;*.tga;*.bmp;*.tif;*.tiff;*.dds";
                    if (dialog.ShowDialog() != DialogResult.OK)
                    {
                        return;
                    }
                    this.UpdateStatus("Encoding texture...");
                    xds.ImportImage(dialog.FileName, texconv);
                }
                string backup = texture.FilePath + ".bak";
                if (!File.Exists(backup))
                {
                    File.Copy(texture.FilePath, backup);
                }
                File.WriteAllBytes(texture.FilePath, xds.ToBytes());
                this.tvTextures_AfterSelect(this, new TreeViewEventArgs(this.tvTextures.SelectedNode));
                this.UpdateStatus(string.Format("Replaced {0} (backup: {1})", Path.GetFileName(texture.FilePath), Path.GetFileName(backup)));
            }
            catch (Exception exception)
            {
                this.UpdateStatus();
                MessageBox.Show(exception.Message, "Replace Texture", MessageBoxButtons.OK, MessageBoxIcon.Hand);
            }
        }

        private void mnuShowTextureAlpha_CheckedChanged(object sender, EventArgs e)
        {
            this.pnlTextureDisplay.Invalidate();
        }

        private void mnuShowWireframe_Click(object sender, EventArgs e)
        {
            this.Scene.ShowWireframe = !this.Scene.ShowWireframe;
            this.Scene.Update(true);
        }

        private void pnlTextureDisplay_Paint(object sender, PaintEventArgs e)
        {
            if ((this.tvTextures.SelectedNode != null) && (this.SelectedTexture != null))
            {
                try
                {
                    int width = Math.Min(e.ClipRectangle.Width, this.SelectedTexture.Width);
                    int height = Math.Min(e.ClipRectangle.Height, this.SelectedTexture.Height);
                    double num3 = Math.Abs(Math.Max((double) (((double) this.SelectedTexture.Width) / ((double) width)), (double) (((double) this.SelectedTexture.Height) / ((double) height))));
                    width = (int) (((double) this.SelectedTexture.Width) / num3);
                    height = (int) (((double) this.SelectedTexture.Height) / num3);
                    int x = (e.ClipRectangle.Width - width) / 2;
                    int y = (e.ClipRectangle.Height - height) / 2;
                    if (this.mnuShowTextureAlpha.Checked)
                    {
                        e.Graphics.DrawImage(this.SelectedTexture, x, y, width, height);
                    }
                    else
                    {
                        using (MemoryStream stream = new MemoryStream())
                        {
                            this.SelectedTexture.Save(stream, ImageFormat.Bmp);
                            e.Graphics.DrawImage(Image.FromStream(stream), x, y, width, height);
                        }
                    }
                }
                catch (Exception)
                {
                }
            }
        }

        private void pnlTextureDisplay_Resize(object sender, EventArgs e)
        {
            this.pnlTextureDisplay.Invalidate();
        }

        private void pnlViewport_Paint(object sender, PaintEventArgs e)
        {
            this.Scene.Update(true);
        }

        private void pnlViewport_Resize(object sender, EventArgs e)
        {
            this.Scene.ResetGraphicsDevice();
        }

        private void SetChildrenCheckedStatus(TreeNode node, bool isChecked)
        {
            foreach (TreeNode node2 in node.Nodes)
            {
                node2.Checked = isChecked;
                this.SetChildrenCheckedStatus(node2, isChecked);
            }
        }

        public static void SetDoubleBuffered(Control control)
        {
            typeof(Control).InvokeMember("DoubleBuffered", BindingFlags.SetProperty | BindingFlags.NonPublic | BindingFlags.Instance, null, control, new object[] { true });
        }

        private void tabForzaObjects_Selecting(object sender, TabControlCancelEventArgs e)
        {
            bool flag = e.TabPageIndex == 0;
            this.pnlViewport.Visible = flag;
            this.pnlTextureDisplay.Visible = !flag;
            this.pnlTextureDisplay.BackColor = System.Drawing.Color.White;
        }

        private void tvModels_AfterCheck(object sender, TreeViewEventArgs e)
        {
            this.tvModels.AfterCheck -= new TreeViewEventHandler(this.tvModels_AfterCheck);
            this.SetChildrenCheckedStatus(e.Node, e.Node.Checked);
            this.tvModels.AfterCheck += new TreeViewEventHandler(this.tvModels_AfterCheck);
            this.pnlViewport.Invalidate();
            this.SelectedVertexCount = this.GetSelectedVertexCount(this.tvModels.Nodes);
            this.SelectedFaceCount = this.GetSelectedFaceCount(this.tvModels.Nodes);
            Application.DoEvents();
        }

        private void tvTextures_AfterSelect(object sender, TreeViewEventArgs e)
        {
            try
            {
                if (this.SelectedTexture != null)
                {
                    this.SelectedTexture.Dispose();
                }
                this.SelectedTexture = ((ForzaTexture) e.Node.Tag).GetImage();
            }
            catch (Exception)
            {
            }
            this.pnlTextureDisplay.Invalidate();
        }

        private void UpdateScene(bool forceUpdate)
        {
            try
            {
                this.Scene.Camera.SpeedScale = ((ComboBoxItem<float>) this.cmbViewportSpeed.SelectedItem).Value;
                Mouse.GetState();
                KeyboardState state = Keyboard.GetState();
                GamePad.GetState(PlayerIndex.One);
                if (state.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.W))
                {
                    this.Scene.Camera.ApplyForce(this.Scene.Camera.ForwardDirection);
                }
                if (state.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.A))
                {
                    this.Scene.Camera.ApplyForce(-this.Scene.Camera.HorizontalDirection);
                }
                if (state.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.S))
                {
                    this.Scene.Camera.ApplyForce(-this.Scene.Camera.ForwardDirection);
                }
                if (state.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.D))
                {
                    this.Scene.Camera.ApplyForce(this.Scene.Camera.HorizontalDirection);
                }
                if (state.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Q))
                {
                    this.Scene.Camera.ApplyForce(new Vector3(0f, -1f, 0f));
                }
                if (state.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.E))
                {
                    this.Scene.Camera.ApplyForce(new Vector3(0f, 1f, 0f));
                }
                if (state.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Up))
                {
                    this.Scene.Camera.ApplyLookForce(new Vector2(0f, 0.1f));
                }
                if (state.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Left))
                {
                    this.Scene.Camera.ApplyLookForce(new Vector2(-0.1f, 0f));
                }
                if (state.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Down))
                {
                    this.Scene.Camera.ApplyLookForce(new Vector2(0f, -0.1f));
                }
                if (state.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Right))
                {
                    this.Scene.Camera.ApplyLookForce(new Vector2(0.1f, 0f));
                }
                this.Scene.Update(false);
            }
            catch (DeviceLostException)
            {
                this.Scene.ResetGraphicsDevice();
            }
        }

        private void UpdateSelectedMeshStatus()
        {
            if ((this.SelectedFaceCount > 0) && (this.SelectedVertexCount > 0))
            {
                this.lblInformation.Text = string.Format("Displaying {0} vertices and {1} faces", this.SelectedVertexCount, this.SelectedFaceCount);
            }
            else
            {
                this.lblInformation.Text = string.Empty;
            }
        }

        private void UpdateSelectedTextureStatus()
        {
            if (this.SelectedTexture != null)
            {
                this.lblInformation.Text = string.Format("Selected texture dimensions of {0}x{1}", this.SelectedTexture.Width, this.SelectedTexture.Height);
            }
            else
            {
                this.lblInformation.Text = string.Empty;
            }
        }

        private void UpdateStatus(string text = null)
        {
            if (text == null)
            {
                if (this.tabForzaObjects.SelectedIndex == 0)
                {
                    this.UpdateSelectedMeshStatus();
                }
                else
                {
                    this.UpdateSelectedTextureStatus();
                }
            }
            else
            {
                this.lblInformation.Text = text;
            }
            Application.DoEvents();
        }

        public bool CreateLog { get; set; }

        #endregion

        #endregion

        #region By JYSB59

        private void ForzaStudioForm_Load(object sender, EventArgs e)
        {
        }

        private void showGreenScreenToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (clicked == 0)
            {
                pnlTextureDisplay.BackColor = System.Drawing.Color.Lime;
                clicked = 1;
            }

            else if (clicked == 1)
            {
                clicked = 0;
                pnlTextureDisplay.BackColor = System.Drawing.Color.White;
            }
        }

        private void saveModToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SaveModding save = new SaveModding();
            save.Show();
        }

        private void hexViewToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                HexView hex = new HexView();

                if (!Types.HexView)
                {
                    Types.HexView = true;
                    MessageBox.Show("Open your file", "Hex View");
                }

                else if (Types.HexView)
                {
                    if (hex.Visible == false)
                    {
                        MessageBox.Show("Open your file", "Hex View");
                    }
                    else
                    {
                        MessageBox.Show("Already in Hex View", "Hex View");
                    }
                }
            }
            catch
            { 
            
            }

        }

        private void custom3DColorToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CustomColor color = new CustomColor();
            color.Show();
        }

        private void customKeyboardToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CustomKeyboard key = new CustomKeyboard();
            key.Show();
        }

        #endregion
    }
}

