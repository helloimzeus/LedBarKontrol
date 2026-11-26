# LED Kumanda

Lotus Lantern ile kullanılan ELK-BLEDOM tipi Bluetooth LE şerit LED’leri Windows üzerinden kontrol eden Türkçe masaüstü uygulaması.

**[Windows uygulamasını indir](https://github.com/helloimzeus/LedBarKontrol/raw/refs/heads/main/indir/LED-Kumanda-Windows.zip)**

## İlk bağlantı

1. Windows 10 veya 11’de Bluetooth’u aç. İndirdiğin ZIP’i bir klasöre çıkar ve `LedKumanda.exe` dosyasını çalıştır.
2. Sağ üstteki Bluetooth menüsünü aç, cihazları tara ve kendi LED kontrolcünü seçerek bağlan. Sonraki açılışlarda uygulama son bağlandığın cihazı arar.
3. Renk tonunu seç ve renk alanında tıkla veya sürükle. LED rengi anında değişir. Tam bir ton için RGB veya HEX değeri de girebilirsin.

Hazır renkler arasında sıcak sarı (`#FFFF64`) bulunur. Parlaklığı değiştirmek için kaydırıcıyı ayarlayıp **Uygula** düğmesine bas. Aynı pencereden ışığı açıp kapatabilirsin.

Telefon LED’e bağlıysa ve taramada cihaz görünmüyorsa Lotus Lantern bağlantısını kapatıp tekrar tara. Bluetooth komutları kontrolcü modeline göre değişebilir.

## Kaynaktan derleme

Windows’ta .NET Framework 4.8 Developer Pack ve Windows 10 veya 11 SDK kurulu olmalıdır. Proje klasöründe Windows PowerShell ile çalıştır:

```powershell
.\build.ps1
```

Çalıştırılabilir dosya `build\LedKumanda.exe` yoluna yazılır. Uygulama son başarılı bağlantıyı Windows kullanıcı profilinde saklar.
