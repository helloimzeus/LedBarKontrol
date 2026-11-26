using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace LedKumanda
{
    internal enum Profile { Standard, Legacy, Dob }

    // Lotus Lantern komutları 9 bayttır. İlk bayt (0x7E) paketi açar, son bayt (0xEF) kapatır.
    // Üçüncü bayt işlemi belirtir: 4 güç, 5 renk, 1 parlaklık. Diğer alanlar modele göre değişebilir.
    internal static class Commands
    {
        public static byte[] Power(Profile p, bool on)
        {
            bool dob = p == Profile.Dob;
            byte variant = (byte)(dob ? 7 : p == Profile.Legacy ? 0 : 4);
            return new byte[] { 0x7e, variant, 4, (byte)(on ? (dob ? 255 : 240) : 0),
                0, (byte)(on ? 1 : 0), (byte)(dob ? 2 : 255), (byte)(dob ? 1 : 0), 0xef };
        }
        // R, G ve B değerleri pakete bu sırayla yazılır. Örneğin tam kırmızı için üç bayt FF 00 00 olur.
        public static byte[] Rgb(Profile p, Color c)
        {
            bool legacy = p == Profile.Legacy;
            return new byte[] { 0x7e, (byte)(legacy ? 0 : 7), 5, 3, c.R, c.G, c.B,
                (byte)(legacy ? 0 : 0x10), 0xef };
        }
        // Kaydırıcıdaki yüzde doğrudan dördüncü bayta yazılır. %25 için gönderilen değer 25’tir.
        public static byte[] Brightness(Profile p, int level)
        {
            if (level < 0 || level > 100) throw new ArgumentOutOfRangeException("level");
            if (p == Profile.Dob) return new byte[] { 0x7e, 4, 1, (byte)level, 1, 255, 2, 1, 0xef };
            return new byte[] { 0x7e, (byte)(p == Profile.Legacy ? 0 : 4), 1, (byte)level, 0, 0, 0, 0, 0xef };
        }
    }

    internal class Nearby
    {
        public ulong Address;
        public BluetoothAddressType AddressType;
        public string Name;
        public override string ToString()
        {
            return string.Format("{0}   ·   {1:X12}", string.IsNullOrEmpty(Name) ? "Adsız cihaz" : Name, Address);
        }
    }

    internal static class DeviceMemory
    {
        // Paketteki dosya ilk cihazı tanıtır. Sonraki başarılı seçimler Windows kullanıcısına özel dosyada saklanır.
        public static Nearby Load(string path)
        {
            try
            {
                var parts = File.ReadAllText(path).Trim().Split(new[] { '|' },3);
                ulong address;
                BluetoothAddressType type;
                if (parts.Length != 3 || parts[0].Length != 12 ||
                    !ulong.TryParse(parts[0],NumberStyles.AllowHexSpecifier,CultureInfo.InvariantCulture,out address) || address == 0 ||
                    !Enum.TryParse(parts[1],out type) || !Enum.IsDefined(typeof(BluetoothAddressType),type)) return null;
                return new Nearby { Address = address, AddressType = type, Name = parts[2] };
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }
        public static bool Save(string path, Nearby target, string name)
        {
            name = (name ?? "").Replace('\r',' ').Replace('\n',' ').Trim();
            var old = Load(path);
            if (old != null && old.Address == target.Address && old.AddressType == target.AddressType && old.Name == name) return true;
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                try
                {
                    File.WriteAllText(temp,target.Address.ToString("X12") + "|" + target.AddressType + "|" + name);
                    try { File.Move(temp,path); }
                    catch (IOException) { if (!File.Exists(path)) throw; File.Replace(temp,path,null); }
                }
                finally { File.Delete(temp); }
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }

    // Ana pencere iki dosyaya ayrılmıştır. Studio.cs kullanıcı girişlerini, bu dosya cihaz bağlantısını yönetir.
    internal sealed partial class Remote : Form
    {
        readonly System.Windows.Forms.Timer scanTimer = new System.Windows.Forms.Timer();
        readonly string rememberedFile;
        Nearby automaticTarget;
        CancellationTokenSource connecting;
        BluetoothLEAdvertisementWatcher watcher;
        BluetoothLEDevice led;
        GattDeviceService service;
        GattCharacteristic writer;
        string connectedName;
        bool busy;
        LedOutput output;

        internal Remote() : this(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LedKumanda","son-cihaz.txt"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"son-cihaz.txt")) { }

        internal Remote(string rememberedFile, string initialFile)
        {
            this.rememberedFile = rememberedFile;
            BuildWindow();
            status.Text = "Bağlanmak için sağ üstteki Bluetooth düğmesini aç.";
            Shown += (s,e) => {
                var previous = DeviceMemory.Load(rememberedFile) ?? DeviceMemory.Load(initialFile);
                if (previous != null) StartScan(previous);
            };
            scan.Click += (s, e) => { if (watcher == null) StartScan(); else FinishScan(); };
            // Cihaz zaten Windows'a bağlıysa yayın yapmayabilir. Kısa taramadan sonra kayıtlı adresi doğrudan deneriz.
            scanTimer.Tick += async (s, e) => {
                var previous = automaticTarget;
                if (previous == null) FinishScan(); else await Connect(previous);
            };
            devices.SelectedIndexChanged += (s, e) => RefreshButtons();
            devices.SelectionChangeCommitted += (s,e) => automaticTarget = null;
            link.Click += async (s, e) => {
                if (writer != null) { Disconnect(); status.Text = "Bağlantı kesildi."; RefreshButtons(); }
                else await Connect(devices.SelectedItem as Nearby);
            };
            FormClosed += (s, e) => { if (connecting != null) connecting.Cancel(); StopScan(); scanTimer.Dispose(); tips.Dispose(); Disconnect(); };
            RefreshButtons();
        }

        // Otomatik seçimde adında BLEDOB geçen cihaz Dob profilini kullanır. Kullanıcının menüden seçtiği profil önceliklidir.
        Profile SelectedProfile()
        {
            switch (profiles.SelectedIndex)
            {
                case 1: return Profile.Standard;
                case 2: return Profile.Legacy;
                case 3: return Profile.Dob;
                default: return (connectedName ?? "").IndexOf("BLEDOB", StringComparison.OrdinalIgnoreCase) >= 0 ? Profile.Dob : Profile.Standard;
            }
        }
        void RefreshButtons()
        {
            bool connected = writer != null;
            on.Enabled = off.Enabled = applyLevel.Enabled = connected && !busy;
            state.Text = connected ? "●   " + (string.IsNullOrWhiteSpace(connectedName) ? "Bağlı LED" : connectedName) + "    ▾" : "Bluetooth   ·   Cihaz seç    ▾";
            state.ForeColor = connected ? Color.FromArgb(153,244,205) : Ink;
            devices.Enabled = scan.Enabled = !connected && !busy;
            profiles.Enabled = !busy;
            link.Enabled = !busy && (connected || devices.SelectedItem != null);
            link.Text = connected ? "Bağlantıyı kes" : "Bağlan";
            scan.Text = watcher == null ? "Cihazları tara" : "Taramayı durdur";
        }
        // Bluetooth olayları başka bir iş parçacığında gelebilir. WinForms denetimlerini pencerenin iş parçacığında güncelleriz.
        void OnUi(Action action)
        {
            try { if (IsHandleCreated && !IsDisposed) BeginInvoke(action); }
            catch (InvalidOperationException) { }
        }
        void StartScan(Nearby previous = null)
        {
            devices.Items.Clear();
            automaticTarget = previous;
            scanTimer.Interval = previous == null ? 12000 : 3500;
            try
            {
                watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
                watcher.Received += Advertisement;
                watcher.Stopped += ScanStopped;
                watcher.Start();
                scanTimer.Start();
                status.Text = previous == null ? "Yakındaki cihazlar taranıyor…" : "Kayıtlı LED aranıyor · " + previous;
            }
            catch (Exception ex) { StopScan(); status.Text = "Bluetooth taraması başlatılamadı. " + ex.Message; }
            RefreshButtons();
        }
        void Advertisement(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs e)
        {
            var found = new Nearby { Address = e.BluetoothAddress, AddressType = e.BluetoothAddressType, Name = e.Advertisement.LocalName };
            OnUi(async () => {
                if (sender != watcher) return;
                // Bir cihaz tarama boyunca birçok kez yayın yapabilir. Aynı adresi tek satırda tutup adı sonradan gelirse satırı güncelleriz.
                var existing = devices.Items.Cast<Nearby>().FirstOrDefault(x => x.Address == found.Address);
                if (existing == null) devices.Items.Add(found);
                else if (!string.IsNullOrEmpty(found.Name) && existing.Name != found.Name)
                    devices.Items[devices.Items.IndexOf(existing)] = found;
                // Aynı adla yayın yapan iki şerit olabilir. Otomatik seçimde adres ve adres türü birlikte eşleşmelidir.
                if (automaticTarget != null && found.Address == automaticTarget.Address && found.AddressType == automaticTarget.AddressType)
                {
                    if (string.IsNullOrWhiteSpace(found.Name)) found.Name = automaticTarget.Name;
                    await Connect(found);
                }
            });
        }
        void ScanStopped(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs e)
        {
            var error = e.Error;
            OnUi(() => {
                if (sender != watcher) return;
                FinishScan();
                if (error != BluetoothError.Success) status.Text = "Windows taramayı durdurdu. Bluetooth adaptörünü ve Windows Bluetooth ayarını kontrol et. (" + error + ")";
            });
        }
        void StopScan()
        {
            automaticTarget = null;
            scanTimer.Stop();
            if (watcher == null) return;
            var previous = watcher;
            watcher = null;
            previous.Received -= Advertisement;
            previous.Stopped -= ScanStopped;
            if (previous.Status == BluetoothLEAdvertisementWatcherStatus.Started) previous.Stop();
        }
        void FinishScan()
        {
            StopScan();
            status.Text = devices.Items.Count == 0 ? "Cihaz bulunamadı. PC’de Bluetooth’u aç, LED’e yaklaş ve iPhone bağlantısını kes." : "Tarama tamamlandı. Listeden LED’ini seçip Bağlan’a bas.";
            RefreshButtons();
        }
        static Guid Uuid(int value) { return new Guid(value.ToString("x8") + "-0000-1000-8000-00805f9b34fb"); }
        async Task Connect(Nearby target)
        {
            if (target == null || busy) return;
            busy = true;
            connecting = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var token = connecting.Token;
            StopScan();
            RefreshButtons();
            status.Text = "Bağlanılıyor · " + target;
            try
            {
                led = await BluetoothLEDevice.FromBluetoothAddressAsync(target.Address, target.AddressType).AsTask(token);
                if (led == null) throw new InvalidOperationException("Cihaz erişim alanında görünmüyor.");
                connectedName = string.IsNullOrEmpty(target.Name) ? led.Name : target.Name;
                var result = await led.GetGattServicesAsync(BluetoothCacheMode.Uncached).AsTask(token);
                if (result.Status != GattCommunicationStatus.Success) throw new InvalidOperationException("Servisler okunamadı (" + result.Status + ").");
                try
                {
                    foreach (var candidate in result.Services)
                    {
                        // Her servis bir grup Bluetooth özelliği sunar. Komutu kabul eden yazma özelliği FFF0 servisinde FFF3, FFE5 servisinde FFE9’dur.
                        int id = candidate.Uuid == Uuid(0xfff0) ? 0xfff3 : candidate.Uuid == Uuid(0xffe5) ? 0xffe9 : 0;
                        if (id == 0) continue;
                        var chars = await candidate.GetCharacteristicsForUuidAsync(Uuid(id), BluetoothCacheMode.Uncached).AsTask(token);
                        if (chars.Status != GattCommunicationStatus.Success) continue;
                        writer = chars.Characteristics.FirstOrDefault(x => (x.CharacteristicProperties &
                            (GattCharacteristicProperties.Write | GattCharacteristicProperties.WriteWithoutResponse)) != 0);
                        if (writer != null) { service = candidate; break; }
                    }
                }
                finally { foreach (var candidate in result.Services) if (candidate != service) candidate.Dispose(); }
                if (writer == null) throw new InvalidOperationException("Bu cihazda desteklenen Lotus Lantern kontrol servisi bulunamadı.");
                led.ConnectionStatusChanged += ConnectionChanged;
                if (led.ConnectionStatus != BluetoothConnectionStatus.Connected) throw new InvalidOperationException("Bluetooth bağlantısı kesildi.");
                // Kuyruğu bu bağlantının yazma özelliğine bağlarız. Yeniden bağlanıldığında yeni bir kuyruk oluşturulur.
                var endpoint = writer;
                output = new LedOutput(data => Write(endpoint,data),
                    label => status.Text = label + " komutu gönderildi · " + connectedName,
                    ex => { Disconnect(); RefreshButtons(); status.Text = "Komut gönderilemedi. Yeniden bağlan. " + ex.Message; });
                status.Text = "Bağlandı · " + target;
                // Ayarı yalnızca LED'in yazma özelliği doğrulanınca değiştiririz.
                if (!DeviceMemory.Save(rememberedFile,target,connectedName))
                    status.Text += " · Son cihaz tercihi kaydedilemedi.";
                ShowDevices(false);
            }
            catch (OperationCanceledException) { Disconnect(); if (!IsDisposed) status.Text = "Bağlantı zaman aşımına uğradı. Bluetooth menüsünden yeniden deneyebilirsin."; }
            catch (Exception ex) { Disconnect(); if (!IsDisposed) status.Text = "Bağlanılamadı. " + ex.Message; }
            finally { connecting.Dispose(); connecting = null; busy = false; if (!IsDisposed) RefreshButtons(); }
        }
        void ConnectionChanged(BluetoothLEDevice sender, object args)
        {
            OnUi(() => {
                if (sender != led || led.ConnectionStatus != BluetoothConnectionStatus.Disconnected) return;
                Disconnect();
                RefreshButtons();
                status.Text = "LED bağlantısı kesildi. Tekrar bağlanabilirsin.";
            });
        }
        void Disconnect()
        {
            if (output != null) { output.Stop(); output = null; }
            writer = null;
            if (led != null) led.ConnectionStatusChanged -= ConnectionChanged;
            if (service != null) { service.Dispose(); service = null; }
            if (led != null) { led.Dispose(); led = null; }
        }
        void Send(byte[] data, string label)
        {
            if (output != null) output.Post(data,label);
        }
        static async Task Write(GattCharacteristic endpoint, byte[] data)
        {
            // Cihaz yanıtsız yazmayı destekliyorsa onu kullanırız. Diğer durumda her yazma için Bluetooth yanıtı istenir.
            var mode = endpoint.CharacteristicProperties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse)
                ? GattWriteOption.WriteWithoutResponse : GattWriteOption.WriteWithResponse;
            using (var buffer = new DataWriter())
            {
                buffer.WriteBytes(data);
                var result = await endpoint.WriteValueAsync(buffer.DetachBuffer(),mode);
                if (result != GattCommunicationStatus.Success) throw new InvalidOperationException(result.ToString());
            }
        }
    }
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            // Ana pencereyi açar ve fare, klavye ve pencere olaylarını işleyen döngüyü başlatır.
            Application.Run(new Remote());
        }
    }
}
