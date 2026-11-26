using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace LedKumanda
{
    // Ana pencerenin arayüzü. SetRgb ve FromPicker farklı girişlerden gelen rengi SyncColor içinde birleştirir.
    internal sealed partial class Remote
    {
        static readonly Color Page = Color.FromArgb(13,17,31), Ink = Color.FromArgb(246,247,255),
            Muted = Color.FromArgb(194,202,226), Input = Color.FromArgb(34,40,66);
        readonly ComboBox devices = ListBoxField(), profiles = ListBoxField();
        readonly CrystalButton scan = new CrystalButton(), link = new CrystalButton(), on = new CrystalButton(),
            off = new CrystalButton(), applyLevel = new CrystalButton();
        readonly CrystalPanel body = new CrystalPanel { Frame = true }, studio = new CrystalPanel(), connection = new CrystalPanel();
        readonly Label status = new CrystalLabel(), preview = new Label(), percent = new CrystalLabel(), hexError = new CrystalLabel();
        readonly CrystalLevel level = new CrystalLevel { Value = 65 };
        readonly Spectrum field = new Spectrum(false), hue = new Spectrum(true);
        readonly TextBox hex = new TextBox { MaxLength = 7, BorderStyle = BorderStyle.FixedSingle };
        readonly NumericUpDown[] channels = new NumericUpDown[3];
        readonly CrystalButton state = new CrystalButton();
        readonly ToolTip tips = new ToolTip();
        Color selected;
        bool updating;
        static ComboBox ListBoxField() { return new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Input, ForeColor = Ink }; }
        static void Place(Control parent, Control child, int x, int y, int w, int h)
        {
            child.SetBounds(x,y,w,h);
            parent.Controls.Add(child);
        }
        static Label LabelStyle(float size, bool bold)
        {
            return new CrystalLabel { Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
                ForeColor = bold ? Ink : Muted, TextAlign = ContentAlignment.MiddleLeft };
        }
        Label Caption(Control parent, string text, int x, int y, int w, int h, float size, bool bold)
        {
            var l = LabelStyle(size, bold);
            l.Text = text;
            Place(parent,l,x,y,w,h);
            return l;
        }
        void ShowDevices(bool open)
        {
            connection.Visible = open;
            if (open) { connection.BringToFront(); if (scan.Enabled) scan.Focus(); else link.Focus(); }
            else state.Focus();
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape && connection.Visible) { ShowDevices(false); return true; }
            return base.ProcessCmdKey(ref msg,keyData);
        }
        void BuildWindow()
        {
            Text = "LED Kumanda";
            // Pencere ve görev çubuğu, EXE içine gömülen Kristal simgesini kullanır.
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(typeof(Remote).Assembly.Location);
            Font = new Font("Segoe UI",10);
            BackColor = Page;
            AutoScaleDimensions = new SizeF(96,96);
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(880,588);
            MinimumSize = new Size(640,460);
            var view = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            Controls.Add(view);
            body.Size = new Size(852,558);
            view.Controls.Add(body);
            Action center = () => {
                body.Left = Math.Max(0,(view.ClientSize.Width-body.Width)/2);
                body.Top = Math.Max(0,(view.ClientSize.Height-body.Height)/2);
            };
            view.Resize += (s,e) => center();
            Shown += (s,e) => center();
            Caption(body,"LED Kumanda",26,13,390,44,23,true);
            Caption(body,"Lotus Lantern  /  Bluetooth LE",28,56,330,22,10,false);
            Place(body,state,567,24,256,41);
            tips.SetToolTip(state,"Bluetooth cihazlarını ve bağlantı ayarlarını aç");
            state.Click += (s,e) => ShowDevices(!connection.Visible);
            Place(body,studio,19,92,814,399);
            Caption(studio,"Renk skalası",20,13,245,28,13,true);
            Place(studio,field,22,54,448,228);
            Caption(studio,"Renk tonu",22,291,160,22,9,false);
            Place(studio,hue,22,317,448,24);
            Caption(studio,"Hazır renkler",22,355,110,24,9,false);
            var presets = new[] { Color.Red,Color.FromArgb(255,127,0),Color.Gold,Color.FromArgb(255,255,100),Color.Lime,Color.Cyan,Color.Blue,Color.Fuchsia,Color.White };
            for (int i=0; i<presets.Length; i++)
            {
                Color color = presets[i];
                var b = new CrystalButton { Chip = color };
                b.AccessibleName = "#" + Hex(color);
                tips.SetToolTip(b,b.AccessibleName);
                b.Click += (s,e) => SetRgb(color);
                Place(studio,b,140+i*37,353,29,29);
            }
            Caption(studio,"Seçilen renk",505,13,272,28,13,true);
            Place(studio,preview,508,55,46,39);
            hex.Font = new Font("Consolas",17); hex.BackColor = Input; hex.ForeColor = Ink;
            hex.AccessibleName = "HEX"; Place(studio,hex,567,57,216,37);
            string[] tags = { "R","G","B" };
            for (int i=0;i<3;i++)
            {
                int x = 508 + i*95;
                Caption(studio,tags[i],x,113,70,20,9,false);
                var n = new NumericUpDown { Maximum = 255, BorderStyle = BorderStyle.FixedSingle, BackColor = Input, ForeColor = Ink };
                channels[i] = n; Place(studio,n,x,137,84,29);
                n.ValueChanged += (s,e) => { if (!updating) SetRgb(Color.FromArgb((int)channels[0].Value,(int)channels[1].Value,(int)channels[2].Value)); };
            }
            hexError.Font = new Font(Font.FontFamily,9);
            hexError.ForeColor = Color.FromArgb(255,181,184);
            Place(studio,hexError,508,165,280,22);
            hexError.Visible = false;
            // Hem gökkuşağı şeridi hem de renk karesi seçim değiştiğinde FromPicker’ı çağırır.
            field.Edited += (s,e) => FromPicker(); hue.Edited += (s,e) => FromPicker();
            hex.TextChanged += (s,e) => {
                if (updating) return;
                var text = hex.Text.StartsWith("#") ? hex.Text.Substring(1) : hex.Text;
                int value;
                // Yazım sırasında eksik kalan HEX değerini LED’e göndermemek için altı hanenin tamamını doğrularız.
                if (text.Length == 6 && int.TryParse(text,NumberStyles.AllowHexSpecifier,CultureInfo.InvariantCulture,out value))
                    SetRgb(Color.FromArgb(255,Color.FromArgb(value)),false);
                else hexError.Visible = true;
            };
            Caption(studio,"Parlaklık",508,207,180,24,10,true);
            percent.TextAlign = ContentAlignment.MiddleRight; percent.ForeColor = Ink;
            Place(studio,percent,719,207,64,24);
            Place(studio,level,505,241,168,32);
            // Kaydırıcı hareket ederken yüzde ekranda hemen değişir. Uygula düğmesi aynı değeri Bluetooth komutuna yazar.
            percent.Text = level.Value + "%"; level.ValueChanged += (s,e) => percent.Text = level.Value + "%";
            applyLevel.Text = "Uygula"; Place(studio,applyLevel,684,240,99,34);
            applyLevel.AccessibleName = "Parlaklığı uygula";
            applyLevel.Click += (s,e) => Send(Commands.Brightness(SelectedProfile(),level.Value),"Parlaklık");
            on.Text = "Işığı aç"; off.Text = "Kapat";
            Place(studio,on,508,305,132,39); Place(studio,off,651,305,132,39);
            on.Click += (s,e) => Send(Commands.Power(SelectedProfile(),true),"Açma");
            off.Click += (s,e) => Send(Commands.Power(SelectedProfile(),false),"Kapatma");
            status.ForeColor = Muted;
            status.TextAlign = ContentAlignment.MiddleLeft; Place(body,status,29,500,793,46);

            Place(body,connection,492,72,337,249);
            Caption(connection,"Bluetooth bağlantısı",17,10,240,37,14,true);
            var close = new CrystalButton { Text = "×", AccessibleName = "Bağlantı ayarlarını kapat" };
            Place(connection,close,283,12,37,34); close.Click += (s,e) => ShowDevices(false);
            Caption(connection,"Cihaz",17,53,280,20,9,false);
            Place(connection,devices,17,77,303,30);
            devices.DropDownWidth = 360;
            Place(connection,scan,17,114,145,37);
            Place(connection,link,173,114,147,37);
            Caption(connection,"Komut profili",17,165,290,20,9,false);
            Place(connection,profiles,17,192,303,29);
            connection.Visible = false;
            profiles.Items.AddRange(new object[] { "Otomatik (cihaz adına göre)", "ELK-BLEDOM", "ELK-BLEDOM (alternatif)", "ELK-BLEDOB" });
            profiles.SelectedIndex = 0;
            profiles.AccessibleName = "Komut profili"; devices.AccessibleName = "Bluetooth cihazı";
            hue.AccessibleName = "Renk tonu"; field.AccessibleName = "Doygunluk ve ışıklılık";
            level.AccessibleName = "LED parlaklığı";
            channels[0].AccessibleName = "Kırmızı"; channels[1].AccessibleName = "Yeşil"; channels[2].AccessibleName = "Mavi";
            hexError.Text = "Altı haneli HEX gir. Örnek: #A84CF0";
            SetRgb(Color.FromArgb(134,87,244));
        }
        static string Hex(Color c) { return (c.ToArgb() & 0xffffff).ToString("X6"); }
        void SetRgb(Color c, bool updateHex = true)
        {
            double max = Math.Max(c.R,Math.Max(c.G,c.B)), min = Math.Min(c.R,Math.Min(c.G,c.B));
            // Gri tonlarında üç kanal eşittir ve renk tonu belirsizdir. Doygunluk yeniden artırıldığında önceki tona dönebilmek için onu koruruz.
            if (max > min) hue.Hue = c.GetHue();
            field.Hue = hue.Hue;
            field.Value = max/255;
            field.Saturation = max == 0 ? 0 : (max-min)/max;
            SyncColor(c,updateHex);
        }
        void FromPicker()
        {
            field.Hue = hue.Hue;
            SyncColor(Spectrum.FromHsv(hue.Hue,field.Saturation,field.Value),true);
        }
        void SyncColor(Color c,bool updateHex)
        {
            // HEX veya RGB alanını kodla değiştirmek de giriş olaylarını tetikler. Bu bayrak aynı seçimin tekrar işlenmesini önler.
            updating = true;
            try
            {
                selected = c;
                hexError.Visible = false;
                if (updateHex) hex.Text = "#" + Hex(c);
                channels[0].Value = c.R; channels[1].Value = c.G; channels[2].Value = c.B;
                preview.BackColor = c;
                body.SetAccent(c);
                field.UpdateSelection(); hue.UpdateSelection();
                // Önizleme bağlantısız da çalışır. LED bağlıysa seçilen RGB değerini canlı gönderim kuyruğuna ekleriz.
                if (output != null) output.Post(Commands.Rgb(SelectedProfile(),c));
            }
            finally { updating = false; }
        }
    }
}
