using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace LedKumanda
{
    // Her Bluetooth bağlantısı kendi kuyruğunu kullanır. Kuyruğa ekleme ve yazma sonrası işlemler arayüz iş parçacığında yürür.
    internal sealed class LedOutput
    {
        sealed class Command
        {
            public byte[] Data;
            public string Label; // Canlı renk seçiminde null, düğme komutunda durum çubuğunda gösterilecek metindir.
        }
        readonly LinkedList<Command> pending = new LinkedList<Command>();
        readonly Func<byte[], Task> write;
        readonly Action<string> sent;
        readonly Action<Exception> failed;
        readonly Stopwatch sinceColor = new Stopwatch();
        bool running, stopped;

        public LedOutput(Func<byte[], Task> write, Action<string> sent, Action<Exception> failed)
        {
            this.write = write;
            this.sent = sent;
            this.failed = failed;
        }
        public void Post(byte[] data, string label = null)
        {
            if (stopped) return;
            // Arka arkaya bekleyen renklerden en güncelini tutarız. Araya giren güç veya parlaklık komutu sırasını korur.
            if (label == null && pending.Last != null && pending.Last.Value.Label == null)
                pending.Last.Value.Data = data;
            else
                pending.AddLast(new Command { Data = data, Label = label });
            if (!running) Drain();
        }
        // Bağlantı kapanırken bekleyen komutları sileriz. Devam eden yazma tamamlanırsa eski bağlantı için durum mesajı gösterilmez.
        public void Stop()
        {
            stopped = true;
            pending.Clear();
        }
        async void Drain()
        {
            running = true;
            try
            {
                while (!stopped && pending.Count != 0)
                {
                    // İlk renk hemen gönderilebilir. Sürükleme sırasında iki renk yazımının başlangıcı arasında en az 40 ms bırakırız.
                    if (pending.First.Value.Label == null && sinceColor.IsRunning && sinceColor.ElapsedMilliseconds < 40)
                    {
                        await Task.Delay(Math.Max(1, 40 - (int)sinceColor.ElapsedMilliseconds));
                        continue;
                    }
                    var next = pending.First.Value;
                    pending.RemoveFirst();
                    if (next.Label == null) sinceColor.Restart();
                    // Bir yazma tamamlanmadan sonraki komuta geçmeyiz. Beklerken arayüz yeni renk seçimlerini kabul edebilir.
                    await write(next.Data);
                    if (!stopped && next.Label != null) sent(next.Label);
                }
            }
            catch (Exception ex)
            {
                if (!stopped) { Stop(); failed(ex); }
            }
            finally { running = false; }
        }
    }
}
