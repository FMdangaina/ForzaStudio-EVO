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
    using Microsoft.Xna.Framework;
    using Microsoft.Xna.Framework.Graphics;
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Drawing.Imaging;
    using System.Runtime.CompilerServices;
    using System.Runtime.InteropServices;
    using System.Windows.Forms;
    using Color = Microsoft.Xna.Framework.Graphics.Color;

    public class RenderScene : IDisposable
    {
        private BasicEffect Ambiance;
        private BasicEffect ColoredVertices;
        private GraphicsDevice Graphics;
        private bool GraphicsDisabled;

        public RenderScene(ref TreeView models, ref Panel viewport)
        {
            if ((models == null) || (viewport == null))
            {
                throw new ArgumentNullException();
            }
            this.Viewport = viewport;
            this.Models = models;
            this.Camera = new ForzaStudio.Camera(viewport);
            try
            {
                this.InitializeGraphicsDevice();
                this.Update(true);
            }
            catch (Exception)
            {
                this.GraphicsDisabled = true;
                MessageBox.Show("Graphics card incompatibility detected. The model viewport will remain disabled.");
            }
        }

        // Display images per mesh; GPU copies are rebuilt whenever the device is recreated.
        // Materials drawn from the game's shader classes (ShaderLook) instead of one flat grey.
        public bool UseShaderLook = true;
        public Vector3? BodyColorOverride;   // colour picked from the viewport menu; replaces Viewer.BodyColor until reset
        private bool drawGlass;
        private EnvReflection env;
        private Vector3 bodyColor = new Vector3(140f / 255f, 140f / 255f, 140f / 255f);   // paint materials without a texture

        private Dictionary<ForzaMesh, Bitmap> meshImages = new Dictionary<ForzaMesh, Bitmap>();
        private Dictionary<Bitmap, Texture2D> gpuTextures = new Dictionary<Bitmap, Texture2D>();

        public void SetMeshTexture(ForzaMesh mesh, Bitmap image)
        {
            if (image == null)
            {
                this.meshImages.Remove(mesh);
            }
            else
            {
                this.meshImages[mesh] = image;
            }
        }

        public void ClearTextures()
        {
            this.meshImages.Clear();
        }

        // Call when the bitmaps passed to SetMeshTexture are disposed or replaced.
        public void ReleaseGpuTextures()
        {
            foreach (Texture2D texture in this.gpuTextures.Values)
            {
                texture.Dispose();
            }
            this.gpuTextures.Clear();
        }

        private Texture2D GetMeshTexture(ForzaMesh mesh)
        {
            Bitmap image;
            if (mesh == null || !this.meshImages.TryGetValue(mesh, out image))
            {
                return null;
            }
            Texture2D texture;
            if (!this.gpuTextures.TryGetValue(image, out texture))
            {
                texture = new Texture2D(this.Graphics, image.Width, image.Height, 0, TextureUsage.AutoGenerateMipMap, SurfaceFormat.Color);
                BitmapData data = image.LockBits(new System.Drawing.Rectangle(0, 0, image.Width, image.Height), ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                byte[] pixels = new byte[image.Width * image.Height * 4];
                for (int y = 0; y < image.Height; y++)
                {
                    Marshal.Copy(new IntPtr(data.Scan0.ToInt64() + ((long) y * data.Stride)), pixels, y * image.Width * 4, image.Width * 4);
                }
                image.UnlockBits(data);
                // opaque: the texture alpha channel is not applied in the viewport
                for (int i = 3; i < pixels.Length; i += 4)
                {
                    pixels[i] = 255;
                }
                texture.SetData<byte>(pixels);
                texture.GenerateMipMaps(TextureFilter.Linear);
                this.gpuTextures[image] = texture;
            }
            return texture;
        }

        public void Dispose()
        {
            this.ReleaseGpuTextures();
            if (this.env != null)
            {
                this.env.Dispose();
                this.env = null;
            }
            if (this.Graphics != null)
            {
                this.Graphics.Dispose();
            }
            if (this.Ambiance != null)
            {
                this.Ambiance.Dispose();
            }
            if (this.ColoredVertices != null)
            {
                this.ColoredVertices.Dispose();
            }
        }

        private void DrawCheckedModels(TreeNodeCollection nodes, FillMode fill)
        {
            if (fill == FillMode.Solid)
            {
                this.Ambiance.VertexColorEnabled = true;
                this.Ambiance.DiffuseColor = new Vector3(0.7f, 0.7f, 0.7f);
                this.Graphics.RenderState.FillMode = FillMode.Solid;
            }
            else if (fill == FillMode.WireFrame)
            {
                this.Ambiance.VertexColorEnabled = false;
                this.Graphics.RenderState.FillMode = FillMode.WireFrame;
                this.Ambiance.DiffuseColor = new Vector3(0f, 0f, 0f);
            }
            foreach (TreeNode node in nodes)
            {
                this.DrawCheckedModels(node.Nodes, fill);
                if ((node.Checked && (node.Tag != null)) && (node.Tag.GetType() == typeof(ForzaMesh)) && !(node.Tag as ForzaMesh).Hidden)
                {
                    ForzaMesh mesh = node.Tag as ForzaMesh;
                    ShaderLookValues look = default(ShaderLookValues);
                    bool styled = fill == FillMode.Solid && this.UseShaderLook;
                    if (styled)
                    {
                        // engine passes (shadow, depth...) and the black fallback stay visible as plain parts
                        look = ShaderLook.Look(ShaderLook.For(mesh.Technique ?? mesh.MaterialName));
                        if (look.Kind == ShaderKind.Pass) { look = ShaderLook.Look(ShaderKind.Generic); }
                        look = ShaderLook.Apply(look, mesh.Settings);
                        if (mesh.Unused) { look = ShaderLook.Look(ShaderKind.Missing); }
                        if ((look.Alpha < 1f) != this.drawGlass) { continue; }
                    }
                    else if (this.drawGlass)
                    {
                        continue;
                    }
                    Texture2D texture = (fill == FillMode.Solid && (!styled || look.UseTexture || mesh.ForceTexture || look.Kind == ShaderKind.Glass)) ? this.GetMeshTexture(mesh) : null;
                    if (mesh.Frozen && fill == FillMode.Solid)
                    {
                        texture = null;
                    }
                    bool glassTexture = styled && look.Kind == ShaderKind.Glass && texture != null;
                    if (glassTexture && !look.OpacitySet) { look.Alpha = Math.Max(look.Alpha, ShaderLookValues.TexturedGlassAlpha); }
                    this.Ambiance.TextureEnabled = texture != null;
                    this.Ambiance.Texture = texture;
                    this.Ambiance.Alpha = styled ? look.Alpha : 1f;
                    this.Ambiance.EmissiveColor = styled ? new Vector3(look.Emissive.X, look.Emissive.Y, look.Emissive.Z) : Vector3.Zero;
                    this.Ambiance.SpecularColor = styled ? new Vector3(look.Specular.X, look.Specular.Y, look.Specular.Z) : new Vector3(0.55f, 0.55f, 0.55f);
                    this.Ambiance.SpecularPower = styled ? look.SpecularPower : 25f;
                    if (fill == FillMode.Solid)
                    {
                        this.Ambiance.VertexColorEnabled = false;
                        this.Ambiance.DiffuseColor = (texture != null) ? Vector3.One : (styled && look.HasPaintColor) ? new Vector3(look.PaintColor.X, look.PaintColor.Y, look.PaintColor.Z) : ((styled && look.Kind == ShaderKind.Paint) ? this.bodyColor : new Vector3(140f / 255f, 140f / 255f, 140f / 255f));
                        if (styled && !(glassTexture && !look.GlassTinted))
                        {
                            this.Ambiance.DiffuseColor *= new Vector3(look.Diffuse.X, look.Diffuse.Y, look.Diffuse.Z);
                        }
                        if (mesh.Frozen)
                        {
                            this.Ambiance.DiffuseColor = new Vector3(0.32f, 0.32f, 0.35f);
                        }
                        if (mesh.Selected)
                        {
                            // Selected in the tree: orange and glowing, whatever the material looks like.
                            this.Ambiance.DiffuseColor = this.Ambiance.DiffuseColor * new Vector3(1f, 0.6f, 0.2f) + new Vector3(0.25f, 0.1f, 0f);
                            this.Ambiance.EmissiveColor = new Vector3(0.55f, 0.22f, 0f);
                        }
                        if (mesh.Highlight)
                        {
                            // Part being edited: Forza red tint.
                            this.Ambiance.DiffuseColor *= new Vector3(1f, 0.45f, 0.4f);
                        }
                    }
                    mesh.UvScale = (styled && !mesh.ForceTexture) ? look.UvScale : 0f;
                    this.Ambiance.World = mesh.PreviewTransform * this.Camera.World;
                    this.Ambiance.Begin();
                    foreach (EffectPass pass in this.Ambiance.CurrentTechnique.Passes)
                    {
                        pass.Begin();
                        mesh.Draw(ref this.Graphics);
                        pass.End();
                    }
                    this.Ambiance.End();
                    float reflect = styled ? ShaderLook.Reflectivity(look.Kind) : 0f;
                    if (reflect > 0f && this.env != null && this.env.Ready)
                    {
                        this.Graphics.RenderState.DepthBias = -0.00005f;
                        this.env.Draw(this.Graphics, mesh, this.Ambiance.World, this.Camera.View, this.Camera.Projection, reflect);
                        this.Graphics.RenderState.DepthBias = 0f;
                        // back to the state the pass was running with (opaque, or the translucent pass)
                        this.Graphics.RenderState.AlphaBlendEnable = this.drawGlass;
                        this.Graphics.RenderState.SourceBlend = Blend.SourceAlpha;
                        this.Graphics.RenderState.DestinationBlend = Blend.InverseSourceAlpha;
                        this.Graphics.RenderState.DepthBufferWriteEnable = !this.drawGlass;
                    }
                    this.Ambiance.World = this.Camera.World;
                }
            }
        }

        // Floor grid on the XZ plane (Y is up) with the X axis in red and the Z axis in blue.
        private void DrawGrid(int size, float spacing)
        {
            Color gridColor = new Color(105, 105, 100, 255);
            Color xColor = new Color(170, 60, 60, 255);
            Color zColor = new Color(60, 90, 180, 255);
            float x = (size * spacing) / 2f;
            List<VertexPositionColor> lines = new List<VertexPositionColor>();
            for (int i = 0; i <= size; i++)
            {
                float z = -x + (i * spacing);
                bool axis = Math.Abs(z) < (spacing / 2f);
                Color along = axis ? xColor : gridColor;
                Color across = axis ? zColor : gridColor;
                lines.Add(new VertexPositionColor(new Vector3(-x, 0f, z), along));
                lines.Add(new VertexPositionColor(new Vector3(x, 0f, z), along));
                lines.Add(new VertexPositionColor(new Vector3(z, 0f, -x), across));
                lines.Add(new VertexPositionColor(new Vector3(z, 0f, x), across));
            }
            this.DrawLines(lines.ToArray());
        }

        // X (red), Y (green) and Z (blue) axes from the origin, drawn over the model so they stay visible.
        private void DrawAxes(float length)
        {
            Color red = new Color(235, 70, 70, 255);
            Color green = new Color(90, 210, 90, 255);
            Color blue = new Color(80, 130, 255, 255);
            VertexPositionColor[] lines = new VertexPositionColor[]
            {
                new VertexPositionColor(Vector3.Zero, red), new VertexPositionColor(new Vector3(length, 0f, 0f), red),
                new VertexPositionColor(Vector3.Zero, green), new VertexPositionColor(new Vector3(0f, length, 0f), green),
                new VertexPositionColor(Vector3.Zero, blue), new VertexPositionColor(new Vector3(0f, 0f, length), blue)
            };
            this.Graphics.RenderState.DepthBufferEnable = false;
            this.DrawLines(lines);
            this.Graphics.RenderState.DepthBufferEnable = true;
        }

        // Translucent triangles over the model (the selected faces of the MESH tab): depth-tested, pulled a little towards
        // the camera so they do not fight with the faces they cover, both sides.
        private void DrawFaces(VertexPositionColor[] vertexData)
        {
            RenderState state = this.Graphics.RenderState;
            CullMode cull = state.CullMode;
            state.FillMode = FillMode.Solid;
            state.CullMode = CullMode.None;
            state.AlphaBlendEnable = true;
            state.SourceBlend = Blend.SourceAlpha;
            state.DestinationBlend = Blend.InverseSourceAlpha;
            state.DepthBias = -0.00002f;
            state.DepthBufferWriteEnable = false;
            this.ColoredVertices.Begin();
            foreach (EffectPass pass in this.ColoredVertices.CurrentTechnique.Passes)
            {
                pass.Begin();
                this.Graphics.VertexDeclaration = new VertexDeclaration(this.Graphics, VertexPositionColor.VertexElements);
                this.Graphics.DrawUserPrimitives<VertexPositionColor>(PrimitiveType.TriangleList, vertexData, 0, vertexData.Length / 3);
                pass.End();
            }
            this.ColoredVertices.End();
            state.AlphaBlendEnable = false;
            state.DepthBias = 0f;
            state.DepthBufferWriteEnable = true;
            state.CullMode = cull;
        }

        private void DrawLines(VertexPositionColor[] vertexData)
        {
            this.Graphics.RenderState.FillMode = FillMode.Solid;
            this.ColoredVertices.Begin();
            foreach (EffectPass pass in this.ColoredVertices.CurrentTechnique.Passes)
            {
                pass.Begin();
                this.Graphics.VertexDeclaration = new VertexDeclaration(this.Graphics, VertexPositionColor.VertexElements);
                this.Graphics.DrawUserPrimitives<VertexPositionColor>(PrimitiveType.LineList, vertexData, 0, vertexData.Length / 2);
                pass.End();
            }
            this.ColoredVertices.End();
        }

        ~RenderScene()
        {
            this.Dispose();
        }

        private void InitializeGraphicsDevice()
        {
            if (!this.GraphicsDisabled)
            {
                PresentationParameters presentationParameters = new PresentationParameters {
                    IsFullScreen = false,
                    BackBufferCount = 2,
                    BackBufferWidth = this.Viewport.Width,
                    BackBufferHeight = this.Viewport.Height,
                    BackBufferFormat = SurfaceFormat.Unknown,
                    EnableAutoDepthStencil = true,
                    AutoDepthStencilFormat = DepthFormat.Depth24Stencil8,
                    PresentationInterval = PresentInterval.Default
                };
                // 4x antialiasing when the card supports it
                int quality;
                if (GraphicsAdapter.DefaultAdapter.CheckDeviceMultiSampleType(DeviceType.Hardware, SurfaceFormat.Bgr32, false, MultiSampleType.FourSamples, out quality))
                {
                    presentationParameters.MultiSampleType = MultiSampleType.FourSamples;
                    presentationParameters.MultiSampleQuality = 0;
                }
                this.Graphics = new GraphicsDevice(GraphicsAdapter.DefaultAdapter, DeviceType.Hardware, this.Viewport.Handle, presentationParameters);
                this.Graphics.RenderState.CullMode = CullMode.CullClockwiseFace;
                this.Ambiance = new BasicEffect(this.Graphics, null);
                this.Ambiance.Alpha = 1f;
                this.Ambiance.DiffuseColor = new Vector3(0.75f, 0.75f, 0.75f);
                this.Ambiance.SpecularColor = new Vector3(0.55f, 0.55f, 0.55f);
                this.Ambiance.SpecularPower = 25f;
                this.Ambiance.AmbientLightColor = new Vector3(0.75f, 0.75f, 0.75f);
                this.Ambiance.LightingEnabled = true;
                this.Ambiance.EnableDefaultLighting();
                this.Ambiance.VertexColorEnabled = true;
                this.Ambiance.DirectionalLight0.Enabled = true;
                this.Ambiance.DirectionalLight0.DiffuseColor = Vector3.One;
                this.Ambiance.DirectionalLight0.SpecularColor = new Vector3(0.6f, 0.6f, 0.6f);
                this.Ambiance.DirectionalLight1.Enabled = true;
                this.Ambiance.DirectionalLight1.DiffuseColor = Vector3.One;
                this.Ambiance.DirectionalLight1.SpecularColor = new Vector3(0.6f, 0.6f, 0.6f);
                this.Ambiance.DirectionalLight1.Direction = new Vector3(0f, -1f, 0f);
                this.env = new EnvReflection(this.Graphics);
                this.ColoredVertices = new BasicEffect(this.Graphics, null);
                this.ColoredVertices.Alpha = 1f;
                this.ColoredVertices.DiffuseColor = Vector3.One;
                this.ColoredVertices.AmbientLightColor = Vector3.One;
                this.ColoredVertices.VertexColorEnabled = true;
            }
        }

        public void ResetGraphicsDevice()
        {
            this.Dispose();
            this.InitializeGraphicsDevice();
            this.Update(true);
        }

        public void Update(bool forceRedraw = false)
        {
            try
            {
                if (!this.GraphicsDisabled)
                {
                    this.Camera.Update((float) this.Viewport.Width, (float) this.Viewport.Height);
                    if (forceRedraw || this.Camera.HasChanged)
                    {
                        int[] rgb = AppSettings.GetRgb("Viewer.BodyColor", "8C8C8C");   // ForzaStudio.ini [App], Tools > Settings > Viewer
                        this.bodyColor = this.BodyColorOverride.HasValue ? this.BodyColorOverride.Value : new Vector3(rgb[0] / 255f, rgb[1] / 255f, rgb[2] / 255f);
                        this.Ambiance.View = this.Camera.View;
                        this.Ambiance.Projection = this.Camera.Projection;
                        this.Ambiance.World = this.Camera.World;
                        this.Ambiance.DirectionalLight0.Direction = this.Camera.ForwardDirection;
                        this.ColoredVertices.View = this.Camera.View;
                        this.ColoredVertices.Projection = this.Camera.Projection;
                        this.ColoredVertices.World = this.Camera.World;
                        this.Graphics.Clear(new Color(75, 75, 70, 255));
                        this.DrawGrid(10, 1f);
                        this.drawGlass = false;
                        this.DrawCheckedModels(this.Models.Nodes, FillMode.Solid);
                        if (this.UseShaderLook)
                        {
                            // translucent parts last, without writing depth
                            this.drawGlass = true;
                            this.Graphics.RenderState.AlphaBlendEnable = true;
                            this.Graphics.RenderState.SourceBlend = Blend.SourceAlpha;
                            this.Graphics.RenderState.DestinationBlend = Blend.InverseSourceAlpha;
                            this.Graphics.RenderState.DepthBufferWriteEnable = false;
                            this.DrawCheckedModels(this.Models.Nodes, FillMode.Solid);
                            this.Graphics.RenderState.DepthBufferWriteEnable = true;
                            this.Graphics.RenderState.AlphaBlendEnable = false;
                            this.drawGlass = false;
                        }
                        if (this.ShowWireframe)
                        {
                            this.DrawCheckedModels(this.Models.Nodes, FillMode.WireFrame);
                        }
                        this.DrawAxes(1f);
                        if (this.OverlayFaces != null && this.OverlayFaces.Length >= 3)
                        {
                            this.DrawFaces(this.OverlayFaces);
                        }
                        if (this.Overlay != null && this.Overlay.Length >= 2)
                        {
                            this.Graphics.RenderState.DepthBufferEnable = false;
                            this.DrawLines(this.Overlay);
                            this.Graphics.RenderState.DepthBufferEnable = true;
                        }
                        this.Graphics.Present();
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        public ForzaStudio.Camera Camera { get; private set; }

        public TreeView Models { get; private set; }

        public bool ShowWireframe { get; set; }

        // Line pairs drawn over the model (no depth test), e.g. the cockpit camera markers.
        public VertexPositionColor[] Overlay { get; set; }

        // Triangles drawn over the model with blending (their colour carries the alpha), e.g. the selected faces.
        public VertexPositionColor[] OverlayFaces { get; set; }

        public Panel Viewport { get; private set; }
    }
}

