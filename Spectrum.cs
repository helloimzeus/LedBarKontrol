using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LedKumanda
{
    // Aynı denetim iki biçimde çizilir: yatay şerit renk tonunu, kare ise o tonun doygunluğunu ve açıklığını seçtirir.
    internal sealed class Spectrum : Control
    {
        public readonly bool HueStrip;
        public double Hue, Saturation, Value;
        public event EventHandler Edited;
        Bitmap gradientImage;
        double cachedHue;
        Point lastPointer;
        Rectangle lastMarker;
        double requestedHue = double.NaN;
        PointF Marker
        {
            get { return new PointF((float)((Width-1) * (HueStrip ? Hue/360 : Saturation)),
                HueStrip ? (Height-1)/2f : (float)((Height-1) * (1-Value))); }
        }
        // Ton değişince karenin renk geçişi de değişir. Yalnızca işaretçi hareket ederse eski ve yeni konumunu çizmek yeterlidir.
        public void UpdateSelection()
        {
            var p = Point.Round(Marker);
            var next = new Rectangle(p.X-10,p.Y-10,21,21);
            if (!HueStrip && requestedHue != Hue) Invalidate();
            else if (next != lastMarker)
            {
                if (!lastMarker.IsEmpty) Invalidate(lastMarker);
                Invalidate(next);
            }
            lastMarker = next;
            requestedHue = Hue;
        }

        public Spectrum(bool hueStrip)
        {
            HueStrip = hueStrip;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Cross;
        }
        public static Color FromHsv(double h, double s, double v)
        {
            // Ton çemberini 60 derecelik altı dilime böleriz. Dilim hangi RGB kanallarının kullanılacağını, x ise aralarındaki geçişi belirler.
            double sector = (h % 360) / 60, chroma = v * s;
            double x = chroma * (1 - Math.Abs(sector % 2 - 1)), m = v - chroma;
            double r = 0, g = 0, b = 0;
            switch ((int)sector)
            {
                case 0: r = chroma; g = x; break;
                case 1: r = x; g = chroma; break;
                case 2: g = chroma; b = x; break;
                case 3: g = x; b = chroma; break;
                case 4: r = x; b = chroma; break;
                case 5: r = chroma; b = x; break;
            }
            return Color.FromArgb((int)Math.Round((r+m)*255), (int)Math.Round((g+m)*255), (int)Math.Round((b+m)*255));
        }
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Width < 2 || Height < 2) return;
            var old = Region;
            using (var path = Glass.Outline(ClientRectangle,HueStrip ? Height/2f : 13))
                Region = new Region(path);
            if (old != null) old.Dispose();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width < 2 || Height < 2) return;
            var g = e.Graphics;
            var bounds = ClientRectangle;
            // Renk geçişini bitmap olarak saklarız. Boyut veya karenin tonu değişene kadar işaretçinin altında aynı resmi kullanırız.
            if (gradientImage == null || gradientImage.Size != bounds.Size || (!HueStrip && cachedHue != Hue))
            {
                if (gradientImage != null) gradientImage.Dispose();
                gradientImage = new Bitmap(Width,Height,System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                using (var canvas = Graphics.FromImage(gradientImage)) DrawGradient(canvas,bounds);
                cachedHue = Hue;
            }
            g.DrawImageUnscaled(gradientImage,0,0);
            var marker = Marker;
            float px = marker.X, py = marker.Y;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var shadow = new Pen(Color.FromArgb(180,20,22,30), 5))
            using (var ring = new Pen(Color.White, 2))
            {
                g.DrawEllipse(shadow, px-6, py-6, 12, 12);
                g.DrawEllipse(ring, px-6, py-6, 12, 12);
            }
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(bounds, -2, -2));
        }
        void DrawGradient(Graphics g,Rectangle bounds)
        {
            if (HueStrip)
            {
                using (var gradient = new LinearGradientBrush(bounds, Color.Red, Color.Red, 0f))
                {
                    gradient.InterpolationColors = new ColorBlend {
                        Colors = new[] { Color.Red, Color.Yellow, Color.Lime, Color.Cyan, Color.Blue, Color.Magenta, Color.Red },
                        Positions = new[] { 0f, 1f/6, 2f/6, .5f, 4f/6, 5f/6, 1f } };
                    g.FillRectangle(gradient, bounds);
                }
            }
            else
            {
                using (var tint = new LinearGradientBrush(bounds, Color.White, FromHsv(Hue, 1, 1), 0f))
                using (var shade = new LinearGradientBrush(bounds, Color.FromArgb(0,0,0,0), Color.Black, 90f))
                {
                    g.FillRectangle(tint, bounds);
                    g.FillRectangle(shade, bounds);
                }
            }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && gradientImage != null) { gradientImage.Dispose(); gradientImage = null; }
            base.Dispose(disposing);
        }
        static double Clamp(double v) { return Math.Max(0, Math.Min(v, 1)); }
        void Pick(int x, int y)
        {
            lastPointer = new Point(x,y);
            // X soldan sağa 0–1 aralığına çevrilir. Karede üst kenar en açık olduğu için Y ekseninin yönü ters çevrilir.
            double horizontal = Clamp(x / (double)Math.Max(1, Width-1));
            if (HueStrip) Hue = horizontal * 360;
            else { Saturation = horizontal; Value = 1-Clamp(y / (double)Math.Max(1, Height-1)); }
            Changed();
        }
        void Changed()
        {
            if (Edited != null) Edited(this, EventArgs.Empty);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left) { Focus(); Capture = true; Pick(e.X, e.Y); }
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (Capture && e.Button == MouseButtons.Left && e.Location != lastPointer) Pick(e.X, e.Y);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && e.Location != lastPointer) Pick(e.X,e.Y);
            Capture = false;
        }
        protected override bool IsInputKey(Keys key)
        {
            return Arrow(key & Keys.KeyCode) || base.IsInputKey(key);
        }
        static bool Arrow(Keys key) { return key == Keys.Left || key == Keys.Right || key == Keys.Up || key == Keys.Down; }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (!Arrow(e.KeyCode)) return;
            int delta = e.KeyCode == Keys.Right || e.KeyCode == Keys.Up ? 1 : -1;
            if (HueStrip) Hue = (Hue + delta + 360) % 360;
            else if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right) Saturation = Clamp(Saturation + delta*.01);
            else Value = Clamp(Value + delta*.01);
            e.Handled = true;
            Changed();
        }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    }
}
