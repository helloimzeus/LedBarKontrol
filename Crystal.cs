using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LedKumanda
{
    internal static class Glass
    {
        // Düğmenin yuvarlak köşelerinin dışında kalan pikselleri alttaki panelden alırız.
        // Böylece düğmeye tıklandığında etrafında dikdörtgen bir iz kalmaz.
        public static void PaintBehind(Control child, Graphics g)
        {
            var panel = child.Parent as CrystalPanel;
            if (panel == null) { g.Clear(Color.FromArgb(13,17,31)); return; }
            panel.CopySurface(g, child.Bounds);
        }
        public static GraphicsPath Outline(RectangleF r, float radius)
        {
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            var p = new GraphicsPath();
            p.AddArc(r.Left,r.Top,d,d,180,90);
            p.AddArc(r.Right-d,r.Top,d,d,270,90);
            p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);
            p.AddArc(r.Left,r.Bottom-d,d,d,90,90);
            p.CloseFigure();
            return p;
        }
        public static void Glow(Graphics g, RectangleF r, Color color)
        {
            using (var p = new GraphicsPath())
            {
                p.AddEllipse(r);
                using (var b = new PathGradientBrush(p))
                {
                    b.CenterColor = color;
                    b.SurroundColors = new[] { Color.FromArgb(0,color) };
                    g.FillPath(b,p);
                }
            }
        }
    }

    // Panelin cam görünümü bir bitmap üzerinde hazırlanır. Çocuk denetimler kendi arkalarına denk gelen bölümü bu resimden alır.
    internal sealed class CrystalPanel : Panel
    {
        public bool Frame;
        Bitmap surface;
        Color accent = Color.MediumSlateBlue;
        Rectangle AccentBounds { get { return new Rectangle(Width/3,Height-5,Width/3,5); } }
        public void SetAccent(Color value)
        {
            if (accent == value) return;
            accent = value;
            // Seçilen renk yalnızca alt kenardaki yansımayı etkiler. Bu dar alanı yenilemek düğmelerin titremesini önler.
            Invalidate(AccentBounds, false);
        }
        public CrystalPanel()
        {
            SetStyle(ControlStyles.Opaque | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(13,17,31);
        }
        public void CopySurface(Graphics g, Rectangle source)
        {
            if (Width < 4 || Height < 4) return;
            if (surface == null || surface.Size != ClientSize)
            {
                if (surface != null) surface.Dispose();
                surface = new Bitmap(Width,Height,System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                using (var canvas = Graphics.FromImage(surface)) PaintSurface(canvas);
            }
            // Negatif kaydırma, çocuk denetimin sol üst köşesini paneldeki doğru pikselle hizalar.
            g.DrawImageUnscaled(surface,-source.X,-source.Y);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            CopySurface(e.Graphics,ClientRectangle);
            if (Frame && Width >= 12 && e.ClipRectangle.IntersectsWith(AccentBounds))
            {
                var r = AccentBounds;
                using (var b = new LinearGradientBrush(r,Color.Transparent,accent,0f))
                {
                    var clear = Color.FromArgb(0,accent);
                    b.InterpolationColors = new ColorBlend { Colors = new[] { clear,accent,clear }, Positions = new[] { 0f,.5f,1f } };
                    e.Graphics.FillRectangle(b,r.X,Height-3,r.Width,2);
                }
            }
            base.OnPaint(e);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && surface != null) { surface.Dispose(); surface = null; }
            base.Dispose(disposing);
        }
        void PaintSurface(Graphics g)
        {
            Glass.PaintBehind(this,g);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new RectangleF(1,1,Width-3,Height-3);
            using (var shape = Glass.Outline(rect,Frame ? 24 : 16))
            using (var fill = new LinearGradientBrush(rect,Frame ? Color.FromArgb(28,43,71) : Color.FromArgb(38,49,78),
                Frame ? Color.FromArgb(15,19,38) : Color.FromArgb(29,31,55), 55f))
            using (var edge = new LinearGradientBrush(rect,Frame ? Color.FromArgb(117,215,246) : Color.FromArgb(105,124,156),
                Frame ? Color.FromArgb(205,Color.MediumSlateBlue) : Color.FromArgb(66,70,100),35f))
            using (var pen = new Pen(edge,Frame ? 1.8f : 1f))
            {
                var saved = g.Save();
                g.SetClip(shape,CombineMode.Intersect);
                g.FillPath(fill,shape);
                Glass.Glow(g,new RectangleF(-Width*.32f,-Height*.49f,Width*.85f,Height*.98f),Color.FromArgb(Frame ? 120 : 46,25,153,209));
                Glass.Glow(g,new RectangleF(Width*.50f,-Height*.36f,Width*.90f,Height*1.05f),Color.FromArgb(Frame ? 94 : 43,Color.MediumSlateBlue));
                g.Restore(saved);
                g.DrawPath(pen,shape);
            }
        }
    }
    internal sealed class CrystalLabel : Label
    {
        public CrystalLabel()
        {
            SetStyle(ControlStyles.Opaque | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer, true);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Glass.PaintBehind(this,e.Graphics);
            base.OnPaint(e);
        }
    }
    internal sealed class CrystalLevel : Control
    {
        const int RailInset = 11;
        int value;
        bool dragging;
        sealed class SliderAccessibility : ControlAccessibleObject
        {
            readonly CrystalLevel slider;
            public SliderAccessibility(CrystalLevel slider) : base(slider) { this.slider = slider; }
            public override string Value
            {
                get { return slider.Value + "%"; }
                set { int number; if (int.TryParse((value ?? "").TrimEnd('%'),out number)) slider.Value = number; }
            }
        }
        protected override AccessibleObject CreateAccessibilityInstance() { return new SliderAccessibility(this); }
        // Fare, klavye ve erişilebilirlik araçları aynı Value özelliğini değiştirir. Yüzde etiketi ValueChanged olayını dinler.
        public event EventHandler ValueChanged;
        public int Value
        {
            get { return value; }
            set
            {
                int next = Math.Max(0, Math.Min(100, value));
                if (this.value == next) return;
                this.value = next;
                Invalidate();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
                AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
            }
        }
        public CrystalLevel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque |
                ControlStyles.Selectable | ControlStyles.ResizeRedraw, true);
            TabStop = true;
            AccessibleRole = AccessibleRole.Slider;
        }
        int RailWidth { get { return Math.Max(1, Width - 2 * RailInset); } }
        // İki uçtaki boşluğu hesaba katarak fare konumunu yüzdeye çeviririz. Çizginin ortası %50’ye karşılık gelir.
        void Pick(int x) { Value = (int)Math.Round((x - RailInset) * 100.0 / RailWidth); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            // Fareyi yakalamak, sürükleme sırasında imleç çizginin dışına çıksa bile hareketleri almamızı sağlar.
            if (e.Button == MouseButtons.Left) { Focus(); Capture = true; dragging = true; Pick(e.X); }
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (Capture && dragging) Pick(e.X);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left) { Pick(e.X); Capture = false; }
        }
        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture) dragging = false;
        }
        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Left: case Keys.Right: case Keys.Up: case Keys.Down:
                case Keys.Home: case Keys.End: case Keys.PageUp: case Keys.PageDown: return true;
                default: return base.IsInputKey(keyData);
            }
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            switch (e.KeyCode)
            {
                case Keys.Left: case Keys.Down: Value--; break;
                case Keys.Right: case Keys.Up: Value++; break;
                case Keys.PageUp: Value += 10; break;
                case Keys.PageDown: Value -= 10; break;
                case Keys.Home: Value = 0; break;
                case Keys.End: Value = 100; break;
                default: return;
            }
            e.SuppressKeyPress = true;
        }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Value += e.Delta / SystemInformation.MouseWheelScrollDelta;
            var handled = e as HandledMouseEventArgs;
            if (handled != null) handled.Handled = true;
        }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Glass.PaintBehind(this,g);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int y = Height / 2;
            float x = RailInset + RailWidth * Value / 100f;
            using (var track = new Pen(Color.FromArgb(78,83,115),6))
            using (var fill = new Pen(Color.FromArgb(151,107,255),6))
            using (var shadow = new SolidBrush(Color.FromArgb(75,0,0,0)))
            {
                track.StartCap = track.EndCap = fill.StartCap = fill.EndCap = LineCap.Round;
                g.DrawLine(track,RailInset,y,RailInset+RailWidth,y);
                if (x > RailInset) g.DrawLine(fill,RailInset,y,x,y);
                g.FillEllipse(shadow,x-8,y-6,17,17);
                g.FillEllipse(Brushes.WhiteSmoke,x-8,y-8,16,16);
            }
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g,ClientRectangle);
        }
    }
    internal sealed class CrystalButton : Button
    {
        // Chip doluysa düğme dairesel bir hazır rengi gösterir. Boşsa açma, kapatma veya bağlantı gibi bir işlem için metin gösterir.
        public Color Chip = Color.Empty;
        bool hover, pressed;
        public CrystalButton()
        {
            SetStyle(ControlStyles.Opaque | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            ForeColor = Color.White;
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
        }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = pressed = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); pressed = true; Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); pressed = false; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width < 4 || Height < 4) return;
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            Glass.PaintBehind(this,g);
            var rect = new RectangleF(1,1,Width-3,Height-3);
            var first = Color.FromArgb(62,77,110);
            var last = Color.FromArgb(39,47,73);
            if (!Chip.IsEmpty) { first = ControlPaint.Light(Chip,.16f); last = Chip; }
            if (!Enabled) { first = Color.FromArgb(50,57,78); last = Color.FromArgb(43,45,66); }
            if (Enabled && hover) first = ControlPaint.Light(first,.14f);
            if (pressed) first = last;
            using (var shape = Glass.Outline(rect, Chip.IsEmpty ? 11 : Height/2f))
            using (var fill = new LinearGradientBrush(rect,first,last,85f))
            using (var border = new Pen(Color.FromArgb(Enabled ? 115 : 55,209,220,255)))
            {
                g.FillPath(fill,shape); g.DrawPath(border,shape);
            }
            if (Chip.IsEmpty) TextRenderer.DrawText(g,Text,Font,Rectangle.Inflate(ClientRectangle,-7,-2),
                Enabled ? ForeColor : Color.FromArgb(174,180,203),
                TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues)
            {
                using (var focus = Glass.Outline(Rectangle.Inflate(ClientRectangle,-4,-4),Chip.IsEmpty ? 8 : Height/2f))
                using (var pen = new Pen(Color.White,1) { DashStyle = DashStyle.Dot })
                    g.DrawPath(pen,focus);
            }
        }
    }
}
