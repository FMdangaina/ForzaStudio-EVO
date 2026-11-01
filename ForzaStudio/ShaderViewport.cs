namespace ForzaStudio
{
    using Microsoft.Xna.Framework;
    using Microsoft.Xna.Framework.Graphics;
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Drawing.Imaging;
    using System.Runtime.InteropServices;
    using System.Windows.Forms;
    using Color = Microsoft.Xna.Framework.Graphics.Color;

    public enum PreviewShape { Sphere, Cube, Plane, Cylinder }

    // A second XNA viewport, with its own GraphicsDevice on its own handle, for the shader preview: a shape (or one mesh of the
    // carbin) drawn with the same look the main viewport gives a material (ShaderLookValues), orbit with the left button, wheel zooms.
    // If the device cannot be created Failed is set and nothing is drawn: the main viewport is never touched.
    public sealed class ShaderViewport : Control
    {
        private GraphicsDevice graphics;
        private BasicEffect effect;
        private EnvReflection env;
        private Texture2D gpuTexture;
        private Bitmap pendingTexture;
        private bool textureDirty;
        private bool resetNeeded;
        private ShaderLookValues look;
        private bool hasLook;
        private PreviewShape shape = PreviewShape.Sphere;
        private ForzaMesh carbinMesh;
        private ForzaMesh shapeMesh;
        private Matrix meshWorld = Matrix.Identity;
        private float yaw = 0.6f, pitch = 0.3f, distance = 3.4f;
        private System.Drawing.Point lastMouse;

        public bool Failed { get; private set; }

        public ShaderViewport()
        {
            this.SetStyle(ControlStyles.Opaque | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            this.BackColor = ForzaTheme.Surface;
            this.Size = new Size(300, 300);
        }

        public ShaderLookValues Look { get { return this.look; } }

        public bool HasLook { get { return this.hasLook; } }

        public Bitmap Texture { get { return this.pendingTexture; } }

        public void Clear()
        {
            this.hasLook = false;
            this.Invalidate();
        }

        public void SetLook(ShaderLookValues values)
        {
            this.look = values;
            this.hasLook = true;
            this.Invalidate();
        }

        // The bitmap is copied to the GPU on the next draw; the caller may dispose it afterwards.
        public void SetTexture(Bitmap image)
        {
            this.pendingTexture = image;
            this.textureDirty = true;
            this.Invalidate();
        }

        public void SetShape(PreviewShape value)
        {
            this.shape = value;
            this.shapeMesh = null;
            this.carbinMesh = null;
            this.Invalidate();
        }

        // null = back to the shape.
        public void SetMesh(ForzaMesh mesh)
        {
            this.carbinMesh = mesh;
            this.meshWorld = (mesh == null) ? Matrix.Identity : FitToUnit(mesh);
            this.Invalidate();
        }

        // Centres the mesh on the origin and scales its bounding sphere to radius 1.
        private static Matrix FitToUnit(ForzaMesh mesh)
        {
            if (mesh.Vertices == null || mesh.Vertices.Length == 0)
            {
                return Matrix.Identity;
            }
            Vector3 min = mesh.Vertices[0].position, max = min;
            foreach (ForzaVertex v in mesh.Vertices)
            {
                min = Vector3.Min(min, v.position);
                max = Vector3.Max(max, v.position);
            }
            Vector3 centre = (min + max) * 0.5f;
            float radius = ((max - min) * 0.5f).Length();
            float scale = (radius > 1e-6f) ? 1f / radius : 1f;
            return Matrix.CreateTranslation(-centre) * Matrix.CreateScale(scale);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            this.resetNeeded = true;
            this.Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            this.lastMouse = e.Location;
            this.Focus();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (e.Button == MouseButtons.Left)
            {
                this.yaw -= (e.X - this.lastMouse.X) * 0.01f;
                this.pitch = Math.Max(-1.5f, Math.Min(1.5f, this.pitch + ((e.Y - this.lastMouse.Y) * 0.01f)));
                this.lastMouse = e.Location;
                this.Invalidate();
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            this.distance = Math.Max(1.6f, Math.Min(12f, this.distance * (e.Delta > 0 ? 0.9f : 1.1f)));
            this.Invalidate();
        }

        private bool EnsureDevice()
        {
            if (this.Failed || !this.IsHandleCreated || this.ClientSize.Width < 8 || this.ClientSize.Height < 8)
            {
                return false;
            }
            if (this.graphics != null && !this.resetNeeded)
            {
                return true;
            }
            this.ReleaseDevice();
            try
            {
                PresentationParameters pp = new PresentationParameters {
                    IsFullScreen = false,
                    BackBufferCount = 1,
                    BackBufferWidth = this.ClientSize.Width,
                    BackBufferHeight = this.ClientSize.Height,
                    BackBufferFormat = SurfaceFormat.Unknown,
                    EnableAutoDepthStencil = true,
                    AutoDepthStencilFormat = DepthFormat.Depth24Stencil8,
                    PresentationInterval = PresentInterval.Default
                };
                int quality;
                if (GraphicsAdapter.DefaultAdapter.CheckDeviceMultiSampleType(DeviceType.Hardware, SurfaceFormat.Bgr32, false, MultiSampleType.FourSamples, out quality))
                {
                    pp.MultiSampleType = MultiSampleType.FourSamples;
                    pp.MultiSampleQuality = 0;
                }
                this.graphics = new GraphicsDevice(GraphicsAdapter.DefaultAdapter, DeviceType.Hardware, this.Handle, pp);
                this.graphics.RenderState.CullMode = CullMode.None;
                this.effect = new BasicEffect(this.graphics, null);
                this.effect.LightingEnabled = true;
                this.effect.EnableDefaultLighting();
                this.effect.AmbientLightColor = new Vector3(0.75f, 0.75f, 0.75f);
                this.effect.VertexColorEnabled = false;
                this.effect.DirectionalLight0.Enabled = true;
                this.effect.DirectionalLight0.DiffuseColor = Vector3.One;
                this.effect.DirectionalLight0.SpecularColor = new Vector3(0.6f, 0.6f, 0.6f);
                this.effect.DirectionalLight1.Enabled = true;
                this.effect.DirectionalLight1.DiffuseColor = Vector3.One;
                this.effect.DirectionalLight1.SpecularColor = new Vector3(0.6f, 0.6f, 0.6f);
                this.effect.DirectionalLight1.Direction = new Vector3(0f, -1f, 0f);
                this.env = new EnvReflection(this.graphics);
                this.textureDirty = true;
                this.resetNeeded = false;
                return true;
            }
            catch (Exception)
            {
                this.Failed = true;
                this.ReleaseDevice();
                return false;
            }
        }

        private void ReleaseDevice()
        {
            if (this.gpuTexture != null) { this.gpuTexture.Dispose(); this.gpuTexture = null; }
            if (this.env != null) { this.env.Dispose(); this.env = null; }
            if (this.effect != null) { this.effect.Dispose(); this.effect = null; }
            if (this.graphics != null) { this.graphics.Dispose(); this.graphics = null; }
        }

        private void UploadTexture()
        {
            this.textureDirty = false;
            if (this.gpuTexture != null)
            {
                this.gpuTexture.Dispose();
                this.gpuTexture = null;
            }
            Bitmap image = this.pendingTexture;
            if (image == null)
            {
                return;
            }
            try
            {
                Texture2D texture = new Texture2D(this.graphics, image.Width, image.Height, 0, TextureUsage.AutoGenerateMipMap, SurfaceFormat.Color);
                BitmapData data = image.LockBits(new System.Drawing.Rectangle(0, 0, image.Width, image.Height), ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                byte[] pixels = new byte[image.Width * image.Height * 4];
                for (int y = 0; y < image.Height; y++)
                {
                    Marshal.Copy(new IntPtr(data.Scan0.ToInt64() + ((long) y * data.Stride)), pixels, y * image.Width * 4, image.Width * 4);
                }
                image.UnlockBits(data);
                for (int i = 3; i < pixels.Length; i += 4)
                {
                    pixels[i] = 255;      // opaque, as in the main viewport
                }
                texture.SetData<byte>(pixels);
                texture.GenerateMipMaps(TextureFilter.Linear);
                this.gpuTexture = texture;
            }
            catch (Exception)
            {
                this.gpuTexture = null;      // the cached bitmap was disposed under us
                this.pendingTexture = null;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (!this.EnsureDevice())
            {
                e.Graphics.Clear(this.BackColor);
                using (Brush brush = new SolidBrush(ForzaTheme.TextDim))
                {
                    e.Graphics.DrawString(this.Failed ? "3D preview not available on this graphics card" : "preview", this.Font, brush, 6, 6);
                }
                return;
            }
            try
            {
                this.Render();
            }
            catch (Exception)
            {
                this.resetNeeded = true;      // device lost: rebuilt on the next paint
            }
        }

        private void Render()
        {
            if (this.textureDirty)
            {
                this.UploadTexture();
            }
            this.graphics.Clear(new Color(75, 75, 70, 255));
            if (this.hasLook)
            {
                ForzaMesh mesh = this.carbinMesh;
                if (mesh == null)
                {
                    if (this.shapeMesh == null)
                    {
                        this.shapeMesh = PreviewShapes.Build(this.shape);
                    }
                    mesh = this.shapeMesh;
                }
                this.DrawMesh(mesh, (this.carbinMesh == null) ? Matrix.Identity : this.meshWorld);
            }
            this.graphics.Present();
        }

        private void DrawMesh(ForzaMesh mesh, Matrix world)
        {
            ShaderLookValues l = this.look;
            Vector3 eye = new Vector3((float) (Math.Cos(this.pitch) * Math.Sin(this.yaw)), (float) Math.Sin(this.pitch), (float) (Math.Cos(this.pitch) * Math.Cos(this.yaw))) * this.distance;
            Matrix view = Matrix.CreateLookAt(eye, Vector3.Zero, Vector3.Up);
            Matrix projection = Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(45f), (float) this.ClientSize.Width / this.ClientSize.Height, 0.1f, 100f);
            Vector3 forward = -eye;
            forward.Normalize();
            this.effect.View = view;
            this.effect.Projection = projection;
            this.effect.World = world;
            this.effect.DirectionalLight0.Direction = forward;

            Texture2D texture = this.gpuTexture;
            bool glassTexture = l.Kind == ShaderKind.Glass && texture != null;
            float alpha = l.Alpha;
            if (glassTexture && !l.OpacitySet) { alpha = Math.Max(alpha, ShaderLookValues.TexturedGlassAlpha); }
            this.effect.TextureEnabled = texture != null;
            this.effect.Texture = texture;
            this.effect.Alpha = alpha;
            this.effect.EmissiveColor = new Vector3(l.Emissive.X, l.Emissive.Y, l.Emissive.Z);
            this.effect.SpecularColor = new Vector3(l.Specular.X, l.Specular.Y, l.Specular.Z);
            this.effect.SpecularPower = l.SpecularPower;
            Vector3 diffuse = (texture != null) ? Vector3.One : l.HasPaintColor ? new Vector3(l.PaintColor.X, l.PaintColor.Y, l.PaintColor.Z) : new Vector3(l.Kind == ShaderKind.Paint ? 0.72f : 0.55f);
            if (!(glassTexture && !l.GlassTinted))
            {
                diffuse *= new Vector3(l.Diffuse.X, l.Diffuse.Y, l.Diffuse.Z);
            }
            this.effect.DiffuseColor = diffuse;
            if (this.carbinMesh == null)
            {
                mesh.UvScale = l.UseTexture ? l.UvScale : 0f;      // our own mesh: the texture repeat of the shader settings
            }
            bool translucent = alpha < 1f;
            this.graphics.RenderState.AlphaBlendEnable = translucent;
            this.graphics.RenderState.SourceBlend = Blend.SourceAlpha;
            this.graphics.RenderState.DestinationBlend = Blend.InverseSourceAlpha;
            this.graphics.RenderState.DepthBufferWriteEnable = !translucent;
            this.effect.Begin();
            foreach (EffectPass pass in this.effect.CurrentTechnique.Passes)
            {
                pass.Begin();
                mesh.Draw(ref this.graphics);
                pass.End();
            }
            this.effect.End();
            float reflect = ShaderLook.Reflectivity(l.Kind);
            if (reflect > 0f && this.env != null && this.env.Ready)
            {
                this.graphics.RenderState.DepthBias = -0.00005f;
                this.env.Draw(this.graphics, mesh, world, view, projection, reflect);
                this.graphics.RenderState.DepthBias = 0f;
            }
            this.graphics.RenderState.AlphaBlendEnable = false;
            this.graphics.RenderState.DepthBufferWriteEnable = true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.ReleaseDevice();
            }
            base.Dispose(disposing);
        }
    }

    // Unit-sized meshes for the preview: positions, normals and UVs (bottom-up like the carbin parser, ForzaMesh.Draw flips them).
    internal static class PreviewShapes
    {
        public static ForzaMesh Build(PreviewShape shape)
        {
            List<ForzaVertex> vertices = new List<ForzaVertex>();
            List<int> indices = new List<int>();
            switch (shape)
            {
                case PreviewShape.Cube:
                    Cube(vertices, indices);
                    break;
                case PreviewShape.Plane:
                    Quad(vertices, indices, new Vector3(-1f, -1f, 0f), new Vector3(2f, 0f, 0f), new Vector3(0f, 2f, 0f), Vector3.Backward);
                    break;
                case PreviewShape.Cylinder:
                    Cylinder(vertices, indices);
                    break;
                default:
                    Sphere(vertices, indices);
                    break;
            }
            return new ForzaMesh("preview", "preview", indices.ToArray(), vertices.ToArray());
        }

        private static ForzaVertex Vertex(Vector3 position, Vector3 normal, float u, float v)
        {
            ForzaVertex vertex = new ForzaVertex();
            vertex.position = position;
            vertex.normal = normal;
            vertex.texture0 = new Vector2(u, v);
            vertex.texture1 = vertex.texture0;
            vertex.color = Color.White;
            return vertex;
        }

        private static void Sphere(List<ForzaVertex> vertices, List<int> indices)
        {
            const int stacks = 32, slices = 64;
            for (int i = 0; i <= stacks; i++)
            {
                double phi = Math.PI * i / stacks;
                for (int j = 0; j <= slices; j++)
                {
                    double theta = 2.0 * Math.PI * j / slices;
                    Vector3 n = new Vector3((float) (Math.Sin(phi) * Math.Sin(theta)), (float) Math.Cos(phi), (float) (Math.Sin(phi) * Math.Cos(theta)));
                    vertices.Add(Vertex(n, n, (float) j / slices, 1f - ((float) i / stacks)));
                }
            }
            Grid(indices, stacks, slices);
        }

        private static void Cylinder(List<ForzaVertex> vertices, List<int> indices)
        {
            const int slices = 64;
            for (int j = 0; j <= slices; j++)
            {
                double theta = 2.0 * Math.PI * j / slices;
                Vector3 n = new Vector3((float) Math.Sin(theta), 0f, (float) Math.Cos(theta));
                vertices.Add(Vertex(n + new Vector3(0f, 1f, 0f), n, (float) j / slices, 1f));
                vertices.Add(Vertex(n - new Vector3(0f, 1f, 0f), n, (float) j / slices, 0f));
            }
            for (int j = 0; j < slices; j++)
            {
                int a = j * 2;
                indices.AddRange(new int[] { a, a + 1, a + 2, a + 1, a + 3, a + 2 });
            }
            for (int cap = 0; cap < 2; cap++)
            {
                float y = (cap == 0) ? 1f : -1f;
                Vector3 n = new Vector3(0f, y, 0f);
                int centre = vertices.Count;
                vertices.Add(Vertex(n, n, 0.5f, 0.5f));
                for (int j = 0; j <= slices; j++)
                {
                    double theta = 2.0 * Math.PI * j / slices;
                    float x = (float) Math.Sin(theta), z = (float) Math.Cos(theta);
                    vertices.Add(Vertex(new Vector3(x, y, z), n, 0.5f + (x * 0.5f), 0.5f + (z * 0.5f)));
                }
                for (int j = 0; j < slices; j++)
                {
                    indices.AddRange(new int[] { centre, centre + 1 + j, centre + 2 + j });
                }
            }
        }

        private static void Cube(List<ForzaVertex> vertices, List<int> indices)
        {
            Quad(vertices, indices, new Vector3(-1f, -1f, 1f), new Vector3(2f, 0f, 0f), new Vector3(0f, 2f, 0f), Vector3.Backward);
            Quad(vertices, indices, new Vector3(1f, -1f, -1f), new Vector3(-2f, 0f, 0f), new Vector3(0f, 2f, 0f), Vector3.Forward);
            Quad(vertices, indices, new Vector3(1f, -1f, 1f), new Vector3(0f, 0f, -2f), new Vector3(0f, 2f, 0f), Vector3.Right);
            Quad(vertices, indices, new Vector3(-1f, -1f, -1f), new Vector3(0f, 0f, 2f), new Vector3(0f, 2f, 0f), Vector3.Left);
            Quad(vertices, indices, new Vector3(-1f, 1f, 1f), new Vector3(2f, 0f, 0f), new Vector3(0f, 0f, -2f), Vector3.Up);
            Quad(vertices, indices, new Vector3(-1f, -1f, -1f), new Vector3(2f, 0f, 0f), new Vector3(0f, 0f, 2f), Vector3.Down);
        }

        // origin + u*across + v*up, u and v in 0..1 (v up).
        private static void Quad(List<ForzaVertex> vertices, List<int> indices, Vector3 origin, Vector3 across, Vector3 up, Vector3 normal)
        {
            int first = vertices.Count;
            vertices.Add(Vertex(origin, normal, 0f, 0f));
            vertices.Add(Vertex(origin + across, normal, 1f, 0f));
            vertices.Add(Vertex(origin + across + up, normal, 1f, 1f));
            vertices.Add(Vertex(origin + up, normal, 0f, 1f));
            indices.AddRange(new int[] { first, first + 1, first + 2, first, first + 2, first + 3 });
        }

        private static void Grid(List<int> indices, int rows, int columns)
        {
            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < columns; j++)
                {
                    int a = (i * (columns + 1)) + j;
                    int b = a + columns + 1;
                    indices.AddRange(new int[] { a, b, a + 1, a + 1, b, b + 1 });
                }
            }
        }
    }
}
