namespace ForzaStudio
{
    using System;
    using System.Drawing;
    using System.Drawing.Imaging;
    using System.Windows.Forms;

    // A small sphere drawn on the CPU with the viewport's simplified shader look (ShaderLook.Look + Apply):
    // diffuse + emissive + Blinn-Phong specular + a made-up sky/ground reflection weighted by
    // ShaderLook.Reflectivity. Glass lets a checkerboard show through by its alpha. No textures, no cubemap.
    public sealed class ShaderSphere : Control
    {
        private Bitmap picture;
        private ShaderLookValues look;
        private bool hasLook;
        private Bitmap texture;      // not owned: the caller keeps it alive (the form's preview cache)

        public ShaderSphere()
        {
            this.DoubleBuffered = true;
            this.BackColor = ForzaTheme.Surface;
            this.Size = new Size(150, 150);
        }

        public void SetLook(ShaderLookValues values)
        {
            this.look = values;
            this.hasLook = true;
            this.Rebuild();
        }

        public void SetTexture(Bitmap image)
        {
            this.texture = image;
            this.Rebuild();
        }

        public Bitmap Texture
        {
            get { return this.texture; }
        }

        public ShaderLookValues Look
        {
            get { return this.look; }
        }

        public bool HasLook
        {
            get { return this.hasLook; }
        }

        public void Clear()
        {
            this.hasLook = false;
            this.Rebuild();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            this.Rebuild();
        }

        private static float Clamp(float v)
        {
            return Math.Max(0f, Math.Min(1f, v));
        }

        private void Rebuild()
        {
            int size = Math.Min(this.ClientSize.Width, this.ClientSize.Height);
            if (this.picture != null)
            {
                this.picture.Dispose();
                this.picture = null;
            }
            if (size > 8 && this.hasLook)
            {
                try
                {
                    this.picture = Render(this.look, size, this.texture);
                }
                catch (Exception)
                {
                    this.texture = null;      // the cached bitmap was disposed under us
                    this.picture = Render(this.look, size, null);
                }
            }
            this.Invalidate();
        }

        // The picture of a look, size x size. A texture is wrapped on the sphere (longitude/latitude over the visible hemisphere, so the
        // whole map reads and it curves), under the lighting; its alpha is not used (the viewport does not either).
        public static Bitmap Render(ShaderLookValues look, int size, Bitmap texture)
        {
            int[] pixels = null;
            int tw = 0, th = 0;
            if (texture != null)
            {
                tw = texture.Width;
                th = texture.Height;
                pixels = new int[tw * th];
                BitmapData data = texture.LockBits(new Rectangle(0, 0, tw, th), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                texture.UnlockBits(data);
            }
            Bitmap bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            // Albedo: paint colour when the shader takes one, else grey; the class multiplies it.
            Vector3Lite baseColor = look.HasPaintColor ? look.PaintColor : new Vector3Lite(look.Kind == ShaderKind.Paint ? 0.72f : 0.55f);
            float lx = -0.5f, ly = -0.6f, lz = 0.62f;
            float length = (float) Math.Sqrt((lx * lx) + (ly * ly) + (lz * lz));
            lx /= length; ly /= length; lz /= length;
            // half vector with the viewer on +Z
            float hx = lx, hy = ly, hz = lz + 1f;
            float hl = (float) Math.Sqrt((hx * hx) + (hy * hy) + (hz * hz));
            hx /= hl; hy /= hl; hz /= hl;
            float reflect = ShaderLook.Reflectivity(look.Kind);
            float radius = (size / 2f) - 2f;
            for (int py = 0; py < size; py++)
            {
                for (int px = 0; px < size; px++)
                {
                    float nx = (px + 0.5f - (size / 2f)) / radius;
                    float ny = (py + 0.5f - (size / 2f)) / radius;
                    float d2 = (nx * nx) + (ny * ny);
                    if (d2 > 1f)
                    {
                        continue;
                    }
                    float nz = (float) Math.Sqrt(1f - d2);
                    float diffuse = Math.Max(0f, (nx * lx) + (ny * ly) + (nz * lz));
                    float light = 0.28f + (0.72f * diffuse);
                    float spec = (float) Math.Pow(Math.Max(0f, (nx * hx) + (ny * hy) + (nz * hz)), look.SpecularPower);
                    // sky above, dark ground below, mirrored about the view
                    float sky = Clamp(0.5f - (0.5f * ny));
                    float env = (0.25f + (0.75f * sky)) * reflect;
                    float[] albedo = { baseColor.X * look.Diffuse.X, baseColor.Y * look.Diffuse.Y, baseColor.Z * look.Diffuse.Z };
                    if (pixels != null)
                    {
                        // longitude/latitude of the visible hemisphere: the whole map wraps over the front, so it curves with the sphere
                        float u = 0.5f + ((float) Math.Atan2(nx, nz) / (float) Math.PI);
                        float v = 0.5f + ((float) Math.Asin(ny) / (float) Math.PI);
                        int tx = Math.Max(0, Math.Min(tw - 1, (int) (u * tw)));
                        int ty = Math.Max(0, Math.Min(th - 1, (int) (v * th)));
                        int argb = pixels[(ty * tw) + tx];
                        bool tinted = look.Kind != ShaderKind.Glass || look.GlassTinted;
                        albedo[0] = ((argb >> 16) & 255) / 255f * (tinted ? look.Diffuse.X : 1f);
                        albedo[1] = ((argb >> 8) & 255) / 255f * (tinted ? look.Diffuse.Y : 1f);
                        albedo[2] = (argb & 255) / 255f * (tinted ? look.Diffuse.Z : 1f);
                    }
                    float[] emissive = { look.Emissive.X, look.Emissive.Y, look.Emissive.Z };
                    float[] specular = { look.Specular.X, look.Specular.Y, look.Specular.Z };
                    float[] rgb = new float[3];
                    for (int c = 0; c < 3; c++)
                    {
                        rgb[c] = Clamp((albedo[c] * light * (1f - (0.5f * reflect))) + emissive[c] + (specular[c] * spec) + (env * 0.6f));
                    }
                    // checkerboard behind, for glass (alpha < 1)
                    float back = (((px / 8) + (py / 8)) % 2 == 0) ? 0.30f : 0.42f;
                    float baseAlpha = (pixels != null && look.Kind == ShaderKind.Glass && !look.OpacitySet) ? Math.Max(look.Alpha, ShaderLookValues.TexturedGlassAlpha) : look.Alpha;
                    float alpha = Clamp(baseAlpha + (spec * 0.5f));
                    int r = (int) (255 * ((rgb[0] * alpha) + (back * (1f - alpha))));
                    int g = (int) (255 * ((rgb[1] * alpha) + (back * (1f - alpha))));
                    int b = (int) (255 * ((rgb[2] * alpha) + (back * (1f - alpha))));
                    bitmap.SetPixel(px, py, Color.FromArgb(Math.Min(255, r), Math.Min(255, g), Math.Min(255, b)));
                }
            }
            return bitmap;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.Clear(this.BackColor);
            if (this.picture != null)
            {
                e.Graphics.DrawImage(this.picture, (this.ClientSize.Width - this.picture.Width) / 2, (this.ClientSize.Height - this.picture.Height) / 2);
            }
            else
            {
                using (Brush brush = new SolidBrush(ForzaTheme.TextDim))
                {
                    e.Graphics.DrawString("preview", this.Font, brush, 6, 6);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && this.picture != null)
            {
                this.picture.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
