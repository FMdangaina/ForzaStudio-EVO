namespace ForzaStudio
{
    using System.Drawing;
    using System.Windows.Forms;

    // The banner: the target game's name in big letters with the source under it, so the direction is plain.
    internal sealed class GameBanner : Panel
    {
        private readonly string game;
        private readonly string sub;
        private readonly Color accent;

        public GameBanner(string game, string sub, Color accent)
        {
            this.game = game;
            this.sub = sub;
            this.accent = accent;
            this.Height = 74;
            this.DoubleBuffered = true;
            this.BackColor = ForzaTheme.Background;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using (Brush bar = new SolidBrush(this.accent))
            {
                e.Graphics.FillRectangle(bar, 0, 0, 8, this.Height);
            }
            using (Font big = new Font("Segoe UI", 24f, FontStyle.Bold | FontStyle.Italic))
            {
                TextRenderer.DrawText(e.Graphics, "FORZA", big, new Point(18, 4), ForzaTheme.Red);
                Size forza = TextRenderer.MeasureText(e.Graphics, "FORZA", big, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
                TextRenderer.DrawText(e.Graphics, this.game.Substring(6), big, new Point(18 + forza.Width, 4), ForzaTheme.Text);
            }
            TextRenderer.DrawText(e.Graphics, this.sub, ForzaTheme.Bold, new Point(22, 50), ForzaTheme.TextDim);
            using (Pen line = new Pen(ForzaTheme.Red, 2f))
            {
                e.Graphics.DrawLine(line, 0, this.Height - 1, this.Width, this.Height - 1);
            }
        }
    }
}
