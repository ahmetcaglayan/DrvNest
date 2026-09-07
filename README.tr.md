<div align="center">

<img src="assets/logo.svg" alt="Hexnest logo" width="120" height="120">

# Hexnest

**Hexnest, Windows ve macOS için ücretsiz ve açık kaynaklı bir sistem aracıdır:
sürücü güncelleyici, sistem izleyici, ağ izleyici, başlangıç yöneticisi ve disk temizleyici — tek pencerede.**

Windows'ta bilgisayardaki her aygıtı tarar, eksik ve eski sürücüleri bulup kurar, gereken yeniden
başlatmalardan sonra kaldığı yerden devam eder, format öncesi sürücülerinizi yedekler ve sonrasında
internetsiz geri yükler. Her iki platformda da bilgisayarın ve üzerindeki her programın işlemci,
bellek ve bant genişliği olarak ne harcadığını, girişte nelerin başladığını ve diskte yerin nereye
gittiğini canlı gösterir. Windows'ta kurulum yok, hiçbir yerde reklam yazılımı yok.

<br>

[![Windows için indir](https://img.shields.io/badge/⬇️%20DOWNLOAD-Windows%20x64-2ea043?style=for-the-badge&logo=windows&logoColor=white&labelColor=1a7f37)](https://github.com/ahmetcaglayan/Hexnest/releases/latest/download/Hexnest.exe)
[![macOS için indir](https://img.shields.io/badge/⬇️%20DOWNLOAD-macOS%20Apple%20silicon-1D1D1F?style=for-the-badge&logo=apple&logoColor=white&labelColor=000000)](https://github.com/ahmetcaglayan/Hexnest/releases/latest/download/Hexnest-arm64.dmg)

<sub>[🌐 Proje sitesi](https://ahmetcaglayan.github.io/Hexnest/) · [Intel Mac, ARM Windows ve tüm eski sürümler →](../../releases)</sub>

<br>

### Windows: kurulum yok. macOS: Applications klasörüne sürükleyin.

`Windows 10 1607+ / 11` · `macOS 12 Monterey+` · `64-bit` · `Apple silicon ve Intel`

**Önce kurulacak hiçbir şey yok.** Her iki yapı da .NET 8 çalışma zamanını içinde taşır:
Windows'ta .NET indirmesi, Visual C++ Redistributable, Mac'te Homebrew veya Xcode gerekmez.

<br>

[![License: MIT](https://img.shields.io/badge/License-MIT-blue?style=flat-square)](LICENSE)
[![Windows](https://img.shields.io/badge/Windows-10%20%2F%2011-0078D4?style=flat-square&logo=windows&logoColor=white)](../../releases)
[![macOS](https://img.shields.io/badge/macOS-12%2B-1D1D1F?style=flat-square&logo=apple&logoColor=white)](../../releases)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-512bd4?style=flat-square&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Release](https://img.shields.io/github/v/release/ahmetcaglayan/Hexnest?style=flat-square&label=Release)](../../releases/latest)
[![Downloads](https://img.shields.io/github/downloads/ahmetcaglayan/Hexnest/total?style=flat-square&label=downloads&color=2ea043)](../../releases)
[![Stars](https://img.shields.io/github/stars/ahmetcaglayan/Hexnest?style=flat-square)](../../stargazers)
[![Build](https://img.shields.io/github/actions/workflow/status/ahmetcaglayan/Hexnest/build.yml?style=flat-square&label=Build)](../../actions/workflows/build.yml)

<sub>[🇬🇧 English](README.md) · 🇹🇷 Türkçe · [🇷🇺 Русский](README.ru.md) · [🇨🇳 简体中文](README.zh.md) · [🇮🇳 हिन्दी](README.hi.md) · [🇵🇹 Português](README.pt.md) · [🇯🇵 日本語](README.ja.md) · [🇩🇪 Deutsch](README.de.md) · [🇫🇷 Français](README.fr.md) · [🇰🇷 한국어](README.ko.md)</sub>

<br>

<img src="assets/screenshots/dashboard.png" alt="Windows'ta Hexnest genel bakış ekranı: aygıt, eksik sürücü, güncelleme ve sorunlu aygıt sayaçları, hızlı işlemler ve donanım özeti" width="900">

</div>

---

## 🖥️ Hangi özellik nerede çalışıyor

Hexnest, iki pencereli tek bir üründür. Ortak motor — izleyiciler, temizleyici,
başlangıç yöneticisi, ayarlar ve günlük — her iki platformda da aynı koddur. Sürücü tarafı yalnızca
Windows'tadır ve bunun nedeni henüz yazılmamış olması değildir: **macOS'ta taranacak, güncellenecek
veya yedeklenecek üçüncü taraf bir sürücü deposu yoktur.** Apple sürücüleri işletim sisteminin içinde
gönderir, dolayısıyla böyle bir aracın bulacağı bir şey yoktur. Bu sayfalar bu yüzden Mac yapısında
sürekli boş durmak yerine hiç bulunmuyor.

| | <img src="https://img.shields.io/badge/-Windows-0078D4?style=flat-square&logo=windows&logoColor=white" alt="Windows"> | <img src="https://img.shields.io/badge/-macOS-1D1D1F?style=flat-square&logo=apple&logoColor=white" alt="macOS"> |
| --- | :---: | :---: |
| **Genel bakış** ve donanım özeti | ✅ | ✅ |
| **Sistem izleme** — işlemci, çekirdek başına, bellek, depolama, pil | ✅ | ✅ |
| **Uygulama başına işlemci ve bellek** | ✅ | ✅ |
| **Ağ izleme** — canlı hız, toplamlar, bağdaştırıcılar, bağlantılar | ✅ | ✅ |
| **Uygulama başına ağ kullanımı** | ✅ yalnızca TCP | ✅ `nettop` ile |
| **Başlangıç yöneticisi** | ✅ Run anahtarları + klasörler | ✅ launchd ajanları |
| **Temizlik**, tahmin değil ölçüm | ✅ | ✅ |
| **Günlük, ayarlar, on dil, koyu/açık tema** | ✅ | ✅ |
| **İşlemci sıcaklığı** | ✅ ACPI termal bölgeleri | ❌ root olmadan verilmiyor |
| **Birim başına disk trafiği** | ✅ | ❌ birim sayacı yok |
| **Bellek boşaltma** | ✅ | ❌ macOS bunun yerine sıkıştırır |
| **Aygıt taraması / eksik sürücüler** | ✅ | ❌ sürücü deposu yok |
| **Windows Update sürücü kataloğu** | ✅ | ❌ |
| **Çevrimdışı INF deposu** | ✅ | ❌ |
| **Sürücü yedekleme ve geri yükleme** | ✅ | ❌ |
| **Kurulum kuyruğu ve yeniden başlatma sonrası devam** | ✅ | ❌ |
| **Sistem geri yükleme noktası** | ✅ | ❌ Time Machine'in işi |
| **Yönetici / root olarak çalışma** | ⚠️ gerekli | ✅ asla — yalnızca kullanıcı alanı |

Yukarıdaki bir ❌, Hexnest'in atladığı bir şey değil, platformun sahip olmadığı bir
şeydir. Her biri uygulamanın kendi içinde, göründüğü yerde açıklanır.

---

## 🎯 Bu ne işe yarar?

Windows'u formatladınız. Aygıt Yöneticisi sarı ünlem işaretleriyle dolu, ekran çözünürlüğü
yanlış, ses yok ve — en kötüsü — ağ bağdaştırıcısının da sürücüsü olmadığı için internet yok.

Hexnest bu tabloyu tek bir pencereden çözer:

- Sistemdeki **tüm PnP aygıtlarını** listeler ve hangisinin sürücüsü olmadığını söyler.
- Eksik ve güncellenebilir sürücüleri **Windows Update kataloğundan** ya da
  **USB bellekteki yerel bir klasörden** bulur.
- Hepsini bir kuyruğa alır, indirir, kurar; gereken her yeniden başlatmadan sonra
  **kaldığı yerden devam eder**.
- Format **öncesinde** mevcut sürücülerinizi dışa aktarır; format **sonrasında**
  internet olmadan geri yükler.

1.1 sürümünden beri, insanların Görev Yöneticisi'ni açma sebebi olan iki soruyu da yanıtlıyor:

- **Bu bilgisayar ne yapıyor?** Mantıksal çekirdek başına işlemci yükü, bellek dağılımı,
  ürün yazılımının yayınladığı tüm sıcaklık sensörleri, gerçek okuma/yazma hızıyla depolama,
  pil — ve çalışan her programı işlemci payı, bellek kullanımı, özel baytları ve disk hızıyla
  birlikte listeleyen bir tablo.
- **Bağlantımı kim kullanıyor?** Tüm bilgisayar için canlı indirme ve yükleme, bu oturumun ve
  Windows açıldığından beri olan toplamlar, tüm ağ bağdaştırıcıları — ve hangi programın şu
  anda ne aktardığını gösteren, uygulama bazında bir tablo.

1.2 sürümünden beri iki soruyu daha yanıtlıyor:

- **Windows ile birlikte neler başlıyor, bunları istiyor muyum?** Her başlangıç kaydı için
  bir anahtar; kapatmak Görev Yöneticisi'nin yazdığı ayarın aynısını yazar, hiçbir şey
  silinmez.
- **Disk alanımı ne yiyor?** Tahmin edilerek değil ölçülerek bulunan her önbellek; sizin
  yerinize hiçbir şey işaretlenmez, kendi dosyalarınız ayrı tutulur ve Geri Dönüşüm
  Kutusu'na gider.

Tek dosya, kurulum yok, arka planda çalışan servis yok, telemetri yok.

---

## ✨ Öne çıkan özellikler

| Özellik | Platform | Ne yapar |
| --- | --- | --- |
| 🔍 **Tam aygıt taraması** | <img src="https://img.shields.io/badge/-Windows-0078D4?style=flat-square&logo=windows&logoColor=white" alt="Windows"> | Sistemdeki tüm PnP aygıtlarını SetupAPI + CfgMgr32 ile sayar. WMI kullanmaz, bu yüzden WMI deposu bozuk ya da yeni kurulmuş bir makinede de çalışır. |
| ⚠️ **Eksik sürücü tespiti** | <img src="https://img.shields.io/badge/-Windows-0078D4?style=flat-square&logo=windows&logoColor=white" alt="Windows"> | Configuration Manager sorun kodlarını okur; 28 (`CM_PROB_FAILED_INSTALL`), 1 ve 19 "sürücü yok" olarak işaretlenir. 22 devre dışı, 14 yeniden başlatma bekliyor demektir. |
| ☁️ **Windows Update sürücü kataloğu** | <img src="https://img.shields.io/badge/-Windows-0078D4?style=flat-square&logo=windows&logoColor=white" alt="Windows"> | Windows Update Agent COM API'si (WUApiLib) üzerinden Microsoft Update'e bağlanır. Ek servis, ek indirme, ek bağımlılık yok — `wuapi.dll` zaten her Windows'ta var. |
| 💾 **Yerel / çevrimdışı INF havuzu** | <img src="https://img.shields.io/badge/-Windows-0078D4?style=flat-square&logo=windows&logoColor=white" alt="Windows"> | Klasörlerdeki `.inf` paketlerini ayrıştırır ve donanım kimliğine göre eşleştirir. USB bellek, ağ paylaşımı ya da bir Hexnest yedeği kaynak olabilir. |
| ⚡ **Paralel indirme + sıralı kurulum** | <img src="https://img.shields.io/badge/-Windows-0078D4?style=flat-square&logo=windows&logoColor=white" alt="Windows"> | İndirmeler aynı anda (varsayılan 3, ayarlanabilir 1–8). Kurulumlar tek tek. Bu bir eksiklik değil: Windows Update ikinci bir kuruluma `WU_E_OPERATIONINPROGRESS` döner ve PnP alt sistemi zaten sıraya sokar. Aynı anda kurmaya çalışmak sadece sahte hatalar üretir. |
| 🔄 **Yeniden başlatma sonrası devam** | <img src="https://img.shields.io/badge/-Windows-0078D4?style=flat-square&logo=windows&logoColor=white" alt="Windows"> | Kuyruk her adımda `session.json` dosyasına yazılır; `schtasks` ile oturum açılışına bağlı bir görev (yedeği HKLM `RunOnce`) Hexnest'i `--resume` ile geri getirir ve kaldığı yerden devam eder. |
| 🛡️ **Sistem geri yükleme noktası** | <img src="https://img.shields.io/badge/-Windows-0078D4?style=flat-square&logo=windows&logoColor=white" alt="Windows"> | Oturumdaki ilk kurulumdan önce `srclient.dll` ile sürücü tipinde bir geri yükleme noktası oluşturur. |
| ↩️ **Güncelleme öncesi yedek** | <img src="https://img.shields.io/badge/-Windows-0078D4?style=flat-square&logo=windows&logoColor=white" alt="Windows"> | Değiştirilecek sürücü paketi kurulumdan hemen önce dışa aktarılır; yolu geçmiş kaydına yazılır, böylece bir şey ters giderse o klasörden geri yüklenebilir. |
| 📦 **Sürücü yedekleme / geri yükleme** | <img src="https://img.shields.io/badge/-Windows-0078D4?style=flat-square&logo=windows&logoColor=white" alt="Windows"> | `pnputil /export-driver` ile tüm üçüncü parti sürücüleri klasöre veya ZIP'e aktarır; `pnputil /add-driver ... /subdirs /install` ile geri yükler. |
| 📊 **Güncelleme geçmişi** | <img src="https://img.shields.io/badge/-Windows-0078D4?style=flat-square&logo=windows&logoColor=white" alt="Windows"> | Kalıcı kayıt, satır başına bir JSON nesnesi (`history.jsonl`) olarak tutulur; tek tıkla CSV'ye aktarılır. |
| 📄 **Donanım raporu** | <img src="https://img.shields.io/badge/-Windows-0078D4?style=flat-square&logo=windows&logoColor=white" alt="Windows"> | Tüm aygıtları ve donanım kimliklerini düz metin dosyasına yazar. USB bellekle çalışan bir bilgisayara taşıyıp sürücüleri elle arayabilirsiniz. |
| 🆙 **Kendi kendini güncelleme** | <img src="https://img.shields.io/badge/-Windows-0078D4?style=flat-square&logo=windows&logoColor=white" alt="Windows"> | GitHub'dan yeni sürümü indirir, **SHA-256 doğrulaması** yapar (sürüm `checksums.txt` yayımlamamışsa kurulumu reddeder) ve exe'yi yerinde değiştirir. |
| 📈 **Sistem izleme** | <img src="https://img.shields.io/badge/-Windows-0078D4?style=flat-square&logo=windows&logoColor=white" alt="Windows"> <img src="https://img.shields.io/badge/-macOS-1D1D1F?style=flat-square&logo=apple&logoColor=white" alt="macOS"> | Genel ve mantıksal çekirdek başına işlemci yükü (`NtQuerySystemInformation`), önbellek ve ayrılmış bayta kadar inen bellek dağılımı (`GlobalMemoryStatusEx` + `GetPerformanceInfo`), ACPI termal bölgeleri, birim başına okuma/yazma hızı (`IOCTL_DISK_PERFORMANCE`) ve pil durumu. Sayfa açılana kadar hiçbir örnekleme yapılmaz; sayfadan çıktığınız anda da durur. |
| 🧮 **Uygulama bazında kaynak kullanımı** | <img src="https://img.shields.io/badge/-Windows-0078D4?style=flat-square&logo=windows&logoColor=white" alt="Windows"> <img src="https://img.shields.io/badge/-macOS-1D1D1F?style=flat-square&logo=apple&logoColor=white" alt="macOS"> | Her işlem için işlemci payı, bellek kullanımı, özel baytlar, disk hızı ve iş parçacığı sayısı; tam olarak Görev Yöneticisi'nin ölçtüğü yöntemle: işlemin iki örnekleme arasındaki kendi çekirdek + kullanıcı süresi farkı, geçen süreye ve mantıksal işlemci sayısına bölünür. |
| 🌐 **Ağ izleme** | <img src="https://img.shields.io/badge/-Windows-0078D4?style=flat-square&logo=windows&logoColor=white" alt="Windows"> <img src="https://img.shields.io/badge/-macOS-1D1D1F?style=flat-square&logo=apple&logoColor=white" alt="macOS"> | Bağdaştırıcıların kendi sayaçlarından tüm bilgisayarın indirme ve yüklemesi, oturum ve açılıştan beri toplamları, açık bağlantı sayısı ve adresiyle, anlaşılan bağlantı hızıyla birlikte tüm bağdaştırıcılar. |
| 🔎 **Uygulama bazında ağ kullanımı** | <img src="https://img.shields.io/badge/-Windows-0078D4?style=flat-square&logo=windows&logoColor=white" alt="Windows"> <img src="https://img.shields.io/badge/-macOS-1D1D1F?style=flat-square&logo=apple&logoColor=white" alt="macOS"> | Hangi programın ne aktardığı; `GetExtendedTcpTable` ve TCP ESTATS (`GetPerTcpConnectionEStats`) üzerinden. Yalnızca TCP — Windows, çekirdek sürücüsü olmadan işlem başına UDP sayacı sunmaz ve sayfa bunu sessizce eksik göstermek yerine açıkça yazar. |
| 🔁 **Otomatik güncelleme denetimi** | <img src="https://img.shields.io/badge/-Windows-0078D4?style=flat-square&logo=windows&logoColor=white" alt="Windows"> | Günde bir kez GitHub Releases API'sine tek bir istek; yeni sürüm varsa *Hakkında* girdisinde bir sayı belirir. Otomatik indirme ve kurulum isteğe bağlıdır, SHA-256 ile doğrulanır ve yalnızca Hexnest kapanırken uygulanır — asla kuyruğun ortasında değil. macOS'ta denetim elle yapılır — *Hakkında* → *Güncellemeleri Kontrol Et* — ve kurmak yerine indirmeyi açar. |
| 🚀 **Başlangıç yöneticisi** | <img src="https://img.shields.io/badge/-Windows-0078D4?style=flat-square&logo=windows&logoColor=white" alt="Windows"> <img src="https://img.shields.io/badge/-macOS-1D1D1F?style=flat-square&logo=apple&logoColor=white" alt="macOS"> | `Run` / `RunOnce` anahtarlarındaki (HKCU, HKLM ve 32-bit görünüm) ve iki Başlangıç klasöründeki tüm otomatik başlangıç kayıtları, her biri için bir anahtarla. Kapatmak, Görev Yöneticisi'nin yazdığı `StartupApproved` değerinin aynısını yazar; bu yüzden ikisi her zaman aynı şeyi gösterir ve özgün komut satırı hiç silinmez. Artık var olmayan bir dosyayı gösteren kayıtlar işaretlenir. |
| 🧹 **Temizlik** | <img src="https://img.shields.io/badge/-Windows-0078D4?style=flat-square&logo=windows&logoColor=white" alt="Windows"> <img src="https://img.shields.io/badge/-macOS-1D1D1F?style=flat-square&logo=apple&logoColor=white" alt="macOS"> | Geçici dosyalar, küçük resim ve simge önbelleği, yedi tarayıcının önbellekleri, Windows Update indirme önbelleği, Teslim İyileştirme, çökme dökümleri, hata raporları, shader önbellekleri, bakım günlükleri ve Geri Dönüşüm Kutusu — hepsinin boyutu **tahmin edilmez, ölçülür** ve **sizin yerinize hiçbiri işaretlenmez**. |
| 🗂️ **Artık klasörler ve eski indirmeler** | <img src="https://img.shields.io/badge/-Windows-0078D4?style=flat-square&logo=windows&logoColor=white" alt="Windows"> <img src="https://img.shields.io/badge/-macOS-1D1D1F?style=flat-square&logo=apple&logoColor=white" alt="macOS"> | AppData altında; kurulu hiçbir programa, çalışan hiçbir programa ve Program Files içindeki hiçbir şeye uymayan, altı aydır dokunulmamış klasörler; ayrıca İndirilenler klasöründe bir aydan eski arşivler ve kurulum dosyaları. Tek tek listelenir ve doğrudan silinmez, **Geri Dönüşüm Kutusu**'na gönderilir. |
| 🧠 **Bellek boşaltma** | <img src="https://img.shields.io/badge/-Windows-0078D4?style=flat-square&logo=windows&logoColor=white" alt="Windows"> | İşlemlerin çalışma kümelerini disk belleğine alır. Sayfa açıkça yazar: bu işlem fiziksel belleği *şu anda* boşaltır ve hiçbir şeyi hızlandırmaz — bu düğmeye sahip diğer her programın iddia ettiğinin tam tersi. |
| 🌍 **On arayüz dili** | <img src="https://img.shields.io/badge/-Windows-0078D4?style=flat-square&logo=windows&logoColor=white" alt="Windows"> <img src="https://img.shields.io/badge/-macOS-1D1D1F?style=flat-square&logo=apple&logoColor=white" alt="macOS"> | İngilizce, Türkçe, Rusça, Basitleştirilmiş Çince, Hintçe, Portekizce, Japonca, Almanca, Fransızca ve Korece; hepsi tek exe'nin içinde. Uygulama açıkken anında değişir. |
| 🎨 **Koyu / açık tema** | <img src="https://img.shields.io/badge/-Windows-0078D4?style=flat-square&logo=windows&logoColor=white" alt="Windows"> <img src="https://img.shields.io/badge/-macOS-1D1D1F?style=flat-square&logo=apple&logoColor=white" alt="macOS"> | Palet sözlüğü değiştirilir, pencere yeniden açılmadan uygulanır. |

---

## 🚑 Format sonrası kurtarma senaryosu

Bu, Hexnest'in var oluş sebebi.

### Tavuk-yumurta problemi

Formattan sonra genellikle **ağ bağdaştırıcısının sürücüsü de yoktur**. Sürücüyü indirmek
için internet, internete çıkmak için sürücü gerekir. Windows Update bu durumda size yardım
edemez, çünkü ona ulaşamazsınız.

Çözüm: **sürücüleri formattan önce yanınıza almak.**

### Format ÖNCESİ (5 dakika)

1. Hexnest'i çalıştırın.
2. **Yedekle & Geri Yükle** menüsüne gidin.
3. **Yedek Oluştur**'a basın. Sistemdeki tüm üçüncü parti sürücü paketleri dışa aktarılır.
   (Microsoft'un kendi kutu içi sürücüleri bilinçli olarak yedeklenmez — Windows onları
   zaten kendisi kurar, yedeğe eklemek boyutu boşuna üçe katlardı.)
4. İsterseniz **ZIP olarak sıkıştır** kutusunu işaretleyin.
5. Oluşan klasörü **ve `Hexnest.exe`'yi aynı USB belleğe** kopyalayın.

> 💡 İsteğe bağlı: Yedek klasörünü `Hexnest.exe` ile aynı dizinde `Drivers` adıyla
> tutarsanız, Hexnest onu **otomatik olarak** yerel sürücü havuzu olarak kaydeder.
> Hiçbir ayar yapmanız gerekmez.

### Format SONRASI

1. USB belleği takın, `Hexnest.exe`'yi çalıştırın (yönetici onayı ister).
   İnternet yoksa `Hexnest.exe --rescue` ile açın: Windows Update hiç aranmaz,
   yalnızca yerel kaynaklar kullanılır.
2. **Yedekle & Geri Yükle → Geri Yükle** (veya **Klasörden Geri Yükle**) ile yedeğinizi
   seçin. Tüm paketler sürücü deposuna eklenir ve aygıtlara bağlanır.
3. Ağ bağdaştırıcısı çalışmaya başladıktan sonra **Tara**'ya basın.
4. **Genel Bakış → Format Sonrası Kurtarma** butonu, hâlâ eksik olan her şeyi
   Windows Update'ten bulup sıraya alır.
5. Yeniden başlatma istenirse kabul edin — Hexnest oturum açılışında kendini geri çağırır
   ve kuyruğun kalanını tamamlar.

> ℹ️ Yedek klasörü illa Hexnest tarafından üretilmiş olmak zorunda değil. Üreticinin
> sitesinden indirip açtığınız herhangi bir sürücü klasörünü de **Klasörden Geri Yükle**
> ile kurabilirsiniz; içindeki `.inf` dosyaları alt klasörler dahil taranır.

### Komut satırı

```powershell
Hexnest.exe                 # normal başlatma
Hexnest.exe --rescue        # çevrimdışı kurtarma modu (--offline ile aynı)
Hexnest.exe --resume        # kesintiye uğramış kuyruğu doğrudan sürdür
```

---

## 📸 Ekran görüntüleri

Yayınlanan yapının gerçek ekran görüntüleri. Uygulamanın kendisinden yeniden üretilirler
— bkz. [Ekran görüntülerini yeniden üretmek](#ekran-görüntülerini-yeniden-üretmek) — bu yüzden eskiyemezler.

### <img src="https://img.shields.io/badge/-Windows%2011-0078D4?style=flat-square&logo=windows&logoColor=white" alt="Windows 11">

<table>
<tr>
<td width="50%"><img src="assets/screenshots/system.png" alt="Sistem İzleme: işlemci, bellek, sıcaklık ve disk etkinliği canlı grafiklerle, mantıksal çekirdek başına bir çubuk"><br><sub><b>Sistem İzleme</b> — işlemci, bellek, sıcaklık ve disk canlı grafiklerle; mantıksal çekirdek başına bir çubuk.</sub></td>
<td width="50%"><img src="assets/screenshots/network.png" alt="Ağ İzleme: canlı indirme ve yükleme grafikleri, oturum ve açılıştan beri toplamları, bağdaştırıcı listesi"><br><sub><b>Ağ İzleme</b> — tüm bilgisayarın indirme ve yüklemesi, oturum toplamları, tüm bağdaştırıcılar.</sub></td>
</tr>
<tr>
<td width="50%"><img src="assets/screenshots/system-detail.png" alt="Uygulama bazında tablo: her işlem için işlemci payı, bellek kullanımı, özel baytlar, disk hızı ve iş parçacığı sayısı"><br><sub><b>Program başına kullanım</b> — çalışan her işlem için işlemci, bellek, disk ve iş parçacığı.</sub></td>
<td width="50%"><img src="assets/screenshots/network-detail.png" alt="Uygulama bazında ağ tablosu: program başına indirme ve yükleme hızı, oturum toplamları ve açık bağlantı sayısı"><br><sub><b>Program başına trafik</b> — bağlantıyı hangi uygulama, ne kadar kullanıyor.</sub></td>
</tr>
<tr>
<td width="50%"><img src="assets/screenshots/devices.png" alt="Aygıtlar: sınıfa göre gruplanmış tüm PnP aygıtları, canlı sorun kodları ve filtrelerle"><br><sub><b>Aygıtlar</b> — sınıfa göre gruplanmış tüm PnP aygıtları, canlı sorun kodlarıyla.</sub></td>
<td width="50%"><img src="assets/screenshots/updates.png" alt="Güncellemeler: kurulabilir sürücü paketleri, satır bazında seçim ve toplam indirme boyutu"><br><sub><b>Güncellemeler</b> — Windows Update ve yerel INF klasörlerinden kurulabilir paketler.</sub></td>
</tr>
<tr>
<td width="50%"><img src="assets/screenshots/queue.png" alt="İşlemler: çalışan kuyruk; her sürücü için indirme yüzdesi, hız ve kurulum aşaması"><br><sub><b>İşlemler</b> — çalışan kuyruk; her sürücü için hız ve kurulum aşaması.</sub></td>
<td width="50%"><img src="assets/screenshots/backup.png" alt="Yedekle ve Geri Yükle: yedek oluşturma, mevcut yedeklerin listesi, klasörden geri yükleme"><br><sub><b>Yedekle &amp; Geri Yükle</b> — tüm üçüncü parti sürücüleri dışa aktar, çevrimdışı geri yükle.</sub></td>
</tr>
<tr>
<td width="50%"><img src="assets/screenshots/startup.png" alt="Başlangıç Programları: her otomatik başlangıç kaydı bir anahtarla; yayımcısı, komut satırı ve nereden başladığı"><br><sub><b>Başlangıç Programları</b> — kayıt başına bir anahtar; Görev Yöneticisi'nin yazdığı gibi yazılır.</sub></td>
<td width="50%"><img src="assets/screenshots/clean.png" alt="Temizlik: kategori başına ölçülmüş boyutlar, hiçbiri seçili değil, ve bellek boşaltma paneli"><br><sub><b>Temizlik</b> — tahmin edilerek değil ölçülerek; sizin yerinize hiçbir şey işaretlenmez.</sub></td>
</tr>
<tr>
<td width="50%"><img src="assets/screenshots/settings.png" alt="Ayarlar: paralel indirme, güvenlik seçenekleri, otomatik güncelleme, kaynaklar, tema ve dil"><br><sub><b>Ayarlar</b> — paralel indirme, güvenlik, otomatik güncelleme, tema ve dil.</sub></td>
<td width="50%"><img src="assets/screenshots/about.png" alt="Hakkında: sürüm bilgisi, kendi kendini güncelleme ve proje bağlantıları"><br><sub><b>Hakkında</b> — sürüm bilgisi ve kendi kendini güncelleme.</sub></td>
</tr>
</table>

### <img src="https://img.shields.io/badge/-macOS-1D1D1F?style=flat-square&logo=apple&logoColor=white" alt="macOS">

<table>
<tr>
<td width="50%"><img src="assets/screenshots/mac-dashboard.png" alt="macOS'ta Hexnest genel bakış: işlemci, bellek ve depolama kartları, hızlı işlemler ve donanım özeti"><br><sub><b>Genel Bakış</b> — Mac şu anda ne yapıyor ve ne: yonga, ekran kartı, bellek, disk.</sub></td>
<td width="50%"><img src="assets/screenshots/mac-system.png" alt="macOS'ta sistem izleme: işlemci ve bellek grafikleri, mantıksal çekirdek başına bir çubuk, depolama, pil ve süreç tablosu"><br><sub><b>Sistem İzleme</b> — çekirdek başına bir çubuk; Apple silicon'da P ve E kümeleri dahil.</sub></td>
</tr>
<tr>
<td width="50%"><img src="assets/screenshots/mac-network.png" alt="macOS'ta ağ izleme: canlı indirme ve yükleme grafikleri, oturum ve açılıştan beri toplamlar, bağdaştırıcılar ve uygulama başına trafik"><br><sub><b>Ağ İzleme</b> — uygulama başına trafik, Etkinlik Monitörü ile aynı kaynaktan.</sub></td>
<td width="50%"><img src="assets/screenshots/mac-startup.png" alt="macOS'ta başlangıç programları: her launchd ajanı ve servisi bir anahtarla, etiketi, komutu ve nereden başladığı"><br><sub><b>Başlangıç Programları</b> — launchd ajanları için birer anahtar; sistem işleri gösterilir, dokunulmaz.</sub></td>
</tr>
<tr>
<td width="50%"><img src="assets/screenshots/mac-clean.png" alt="macOS'ta temizlik: uygulama önbellekleri, geliştirici önbellekleri, günlükler, Çöp, eski indirmeler ve artıklar için ölçülmüş boyutlar"><br><sub><b>Temizlik</b> — önbellekler, Xcode derived data, iPhone yedekleri. Ölçülmüş ve hiçbiri işaretli değil.</sub></td>
<td width="50%"><img src="assets/screenshots/mac-settings.png" alt="macOS'ta ayarlar: dil, sistemi izleyen tema seçeneği dahil tema, güncelleme seçenekleri ve veri klasörleri"><br><sub><b>Ayarlar</b> — aynı seçenekler, artı gün doğumu ve batımında macOS'u izleyen bir tema.</sub></td>
</tr>
<tr>
<td width="50%"><img src="assets/screenshots/mac-logs.png" alt="macOS'ta günlük: günlük dosyası yolu ile canlı tanılama ve kopyala, göster, temizle eylemleri"><br><sub><b>Günlük</b> — canlı tanılama; hata bildirimi için tek tıkla panoya.</sub></td>
<td width="50%"><img src="assets/screenshots/mac-about.png" alt="macOS'ta hakkında: sürüm, bilgisayar ve işlemci bilgisi, güncelleme denetimi ve proje bağlantıları"><br><sub><b>Hakkında</b> — sürüm, bilgisayar ve indirmeyi açan bir güncelleme denetimi.</sub></td>
</tr>
</table>

### Ekran görüntülerini yeniden üretmek

Yukarıdaki her görüntüyü uygulamanın kendisi üretir; böylece bir arayüz değişikliği
belgelere tek komutla yansıtılabilir:

```powershell
# Windows, derledikten sonra yükseltilmiş bir komut isteminden
.\Hexnest.exe --capture .\assets\screenshots --lang en
```

```bash
# macOS, ./build/make-mac-app.sh çalıştırıldıktan sonra
./artifacts/mac/arm64/Hexnest.app/Contents/MacOS/Hexnest --capture ./shots --lang en
```

İkisi de tüm menüyü dolaşır, canlı sayfaların grafiklerini doldurmasını bekler ve sayfa
başına bir PNG yazar. `--lang` arayüz dilini sabitler; böylece yayımlanan görüntüler onları üretenin
görüntü diline bağlı kalmaz.

> Neden dahili bir yakalama? Windows'ta Hexnest yükseltilmiş çalışır ve Kullanıcı Arayüzü
> Ayrıcalık Yalıtımı, (yükseltilmemiş) Ekran Alıntısı'nın daha yüksek bütünlükteki bir pencereye giden
> girdiyi görmesini engeller — Hexnest, Görev Yöneticisi veya Kayıt Defteri Düzenleyicisi üzerindeyken
> Print Screen hiçbir şey yapmaz. macOS'ta ise bir ekran yakalaması Ekran Kaydı izni ister ve masaüstünde
> ne varsa onu da fotoğraflar. Her iki yapı da bunun yerine kendi görsel ağacını çizer.

---

## 🧭 Menüler

| Menü | Platform | Ne yapar |
| --- | --- | --- |
| **Genel Bakış** | <img src="https://img.shields.io/badge/-Windows-0078D4?style=flat-square&logo=windows&logoColor=white" alt="Windows"> <img src="https://img.shields.io/badge/-macOS-1D1D1F?style=flat-square&logo=apple&logoColor=white" alt="macOS"> | Aygıt sayısı, eksik sürücü sayısı, güncelleme sayısı, sorunlu aygıt sayısı. İşletim sistemi / makine / işlemci / BIOS özeti. Hızlı işlemler: *Şimdi Tara*, *Format Sonrası Kurtarma*, *Tümünü Güncelle*, *Sürücüleri Yedekle*, *Donanım Raporu*. Çalışan sürücüsü olan bir ağ bağdaştırıcısı yoksa uyarı şeridi çıkar. |
| **Aygıtlar** | <img src="https://img.shields.io/badge/-Windows-0078D4?style=flat-square&logo=windows&logoColor=white" alt="Windows"> | Sistemdeki tüm PnP aygıtları, sınıfa göre gruplanmış. Filtreler: *Tümü / Sorunlu / Sürücüsüz / Jenerik Sürücü*. Ada, üreticiye, sürüme ve donanım kimliğine göre arama; donanım kimliğini panoya kopyalama. |
| **Güncellemeler** | <img src="https://img.shields.io/badge/-Windows-0078D4?style=flat-square&logo=windows&logoColor=white" alt="Windows"> | Kurulabilecek paketler: hem eksik sürücüler hem de sürüm yükseltmeleri. Tekli seçim, *Tümünü Seç / Seçimi Temizle*, seçili boyut toplamı, *Seçilenleri Kur*. Bir güncellemeyi gizleyebilir veya bir aygıtı tamamen yoksayabilirsiniz. |
| **İşlemler** | <img src="https://img.shields.io/badge/-Windows-0078D4?style=flat-square&logo=windows&logoColor=white" alt="Windows"> | Çalışan kuyruk. Her iş için indirme yüzdesi, hız, aktarılan bayt ve kurulum aşaması ayrı ayrı görünür. *Tümünü İptal Et*, *Başarısızları Tekrar Dene*, *Şimdi Yeniden Başlat* / *Daha Sonra*. Kesintiye uğramış bir oturum varsa *Devam Et* butonu burada çıkar. |
| **Yedekle & Geri Yükle** | <img src="https://img.shields.io/badge/-Windows-0078D4?style=flat-square&logo=windows&logoColor=white" alt="Windows"> | *Yedek Oluştur* (isteğe bağlı ZIP), mevcut yedeklerin listesi (paket sayısı, boyut, tarih), *Geri Yükle*, *Klasörden Geri Yükle*, *Aç*, *Sil*. |
| **Geçmiş** | <img src="https://img.shields.io/badge/-Windows-0078D4?style=flat-square&logo=windows&logoColor=white" alt="Windows"> | Yapılan tüm sürücü işlemlerinin kalıcı kaydı. Sonuca göre filtre, arama, *CSV Olarak Dışa Aktar*, *Geçmişi Temizle*. Bir kaydın güncelleme öncesi yedeği duruyorsa klasörü açabilirsiniz. |
| **Sistem İzleme** | <img src="https://img.shields.io/badge/-Windows-0078D4?style=flat-square&logo=windows&logoColor=white" alt="Windows"> <img src="https://img.shields.io/badge/-macOS-1D1D1F?style=flat-square&logo=apple&logoColor=white" alt="macOS"> | Genel ve mantıksal çekirdek başına işlemci yükü; kullanımda / kullanılabilir / önbellek / ayrılmış olarak ayrıştırılmış bellek; makine yayınlıyorsa sıcaklık sensörleri; canlı okuma ve yazma hızıyla depolama kapasitesi; pil. Altında çalışan her işlem, işlemci payı, bellek kullanımı, özel baytları, disk hızı ve iş parçacığı sayısıyla — işlemciye, belleğe, diske veya ada göre sıralanabilir, aranabilir ve bir satır gerçekten okunabilsin diye duraklatılabilir. |
| **Ağ İzleme** | <img src="https://img.shields.io/badge/-Windows-0078D4?style=flat-square&logo=windows&logoColor=white" alt="Windows"> <img src="https://img.shields.io/badge/-macOS-1D1D1F?style=flat-square&logo=apple&logoColor=white" alt="macOS"> | Tüm bilgisayarın canlı indirme ve yüklemesi grafik olarak, bu oturumun ve Windows açıldığından beri olan toplamı, açık bağlantı sayısı ve türü, adresi ve bağlantı hızıyla birlikte tüm bağdaştırıcılar. Altında uygulama bazında bir tablo: indirme ve yükleme hızı, oturum toplamları, açık bağlantılar ve karşı uç adresi. |
| **Başlangıç Programları** | <img src="https://img.shields.io/badge/-Windows-0078D4?style=flat-square&logo=windows&logoColor=white" alt="Windows"> <img src="https://img.shields.io/badge/-macOS-1D1D1F?style=flat-square&logo=apple&logoColor=white" alt="macOS"> | Hexnest'in güvenle açıp kapatabildiği tüm otomatik başlangıç kayıtları; program adı exe'nin sürüm kaynağından, yayımcısı, komut satırı, boyutu ve nereden başladığı ile birlikte. Her satırda bir anahtar; kapatmak Görev Yöneticisi'nin yazdığı ayarın aynısını yazar ve hiçbir şey silmez. Artık var olmayan bir dosyayı gösteren kayıtlar işaretlenir, güvenlik yazılımları ayrıca belirtilir ve kapatılmadan önce sorulur; açık, kapalı ve bozuk için filtreler ile bir arama vardır. |
| **Temizlik** | <img src="https://img.shields.io/badge/-Windows-0078D4?style=flat-square&logo=windows&logoColor=white" alt="Windows"> <img src="https://img.shields.io/badge/-macOS-1D1D1F?style=flat-square&logo=apple&logoColor=white" alt="macOS"> | Geçici dosyalar, küçük resim ve simge önbellekleri, yedi tarayıcı, Windows Update indirme önbelleği, Teslim İyileştirme, çökme dökümleri, hata raporları, shader önbellekleri, Windows günlükleri, Hexnest'in kendi önbelleği ve Geri Dönüşüm Kutusu için ölçülmüş boyutlar. Sizin yerinize hiçbir şey işaretlenmez. Eski indirmeler ve artık AppData klasörleri tek tek listelenir ve Geri Dönüşüm Kutusu'na gider. Bir de ne yaptığı konusunda dürüst olan bir bellek boşaltma. |
| **Günlük** | <img src="https://img.shields.io/badge/-Windows-0078D4?style=flat-square&logo=windows&logoColor=white" alt="Windows"> <img src="https://img.shields.io/badge/-macOS-1D1D1F?style=flat-square&logo=apple&logoColor=white" alt="macOS"> | Canlı tanılama akışı. *Kopyala* butonu sürüm, işletim sistemi ve makine başlığıyla birlikte günlüğü panoya alır — hata bildirirken tam olarak bu gerekir. Günlük dosyasını / klasörünü açma ve temizleme. |
| **Ayarlar** | <img src="https://img.shields.io/badge/-Windows-0078D4?style=flat-square&logo=windows&logoColor=white" alt="Windows"> <img src="https://img.shields.io/badge/-macOS-1D1D1F?style=flat-square&logo=apple&logoColor=white" alt="macOS"> | Aynı anda indirme sayısı, tekrar deneme sayısı, açılışta tarama, geri yükleme noktası, güncelleme öncesi yedek, yeniden başlatma sonrası devam, otomatik yeniden başlatma ve gecikmesi, çevrimdışı mod, isteğe bağlı sürücüler, yerel sürücü klasörleri, geçmiş saklama süresi, **otomatik güncelleme denetimi, otomatik kurulum ve ön sürümler**, tema, dil. |
| **Hakkında** | <img src="https://img.shields.io/badge/-Windows-0078D4?style=flat-square&logo=windows&logoColor=white" alt="Windows"> <img src="https://img.shields.io/badge/-macOS-1D1D1F?style=flat-square&logo=apple&logoColor=white" alt="macOS"> | Sürüm bilgisi, *Güncellemeleri Kontrol Et*, *İndir ve Kur*, sürüm notları, proje sayfası ve hata bildirme bağlantıları. |

Mac yapısının kenar çubuğu, yukarıdaki macOS işaretli sekiz satırdan bu sırayla oluşur.
*Aygıtlar*, *Güncellemeler*, *İşlemler*, *Yedekle & Geri Yükle* ve *Geçmiş* orada boş değil, hiç yoktur.

---

## ⚙️ Nasıl çalışır?

```mermaid
flowchart TD
    A["Tarama başlar"] --> B["Aygıtlar<br/>SetupAPI + CfgMgr32"]
    B --> C{"Sağlayıcılar<br/>paralel sorgulanır"}
    C --> D["Windows Update<br/>WUApiLib COM"]
    C --> E["Yerel INF havuzu<br/>USB / klasör / yedek"]
    D --> F["Aday listesi<br/>tekilleştirilir"]
    E --> F
    F --> G["Kullanıcı seçer"]
    G --> H["Kuyruk"]
    H --> I["Paralel indirme<br/>varsayılan 3 iş"]
    I --> J["Sıralı kurulum<br/>tek global kilit"]
    J --> K{"Yeniden başlatma<br/>gerekiyor mu?"}
    K -->|Hayır| L["Bitti"]
    K -->|Evet| M["session.json yazılır<br/>+ schtasks ONLOGON"]
    M --> N["Yeniden başlatma"]
    N --> O["Hexnest --resume"]
    O --> H
```

Kısa teknik özet:

1. **Tarama.** `SetupDiGetClassDevs(DIGCF_PRESENT | DIGCF_ALLCLASSES)` ile sistemde
   fiziksel olarak bulunan her aygıt sayılır; `CM_Get_DevNode_Status` sorun kodunu,
   `HKLM\SYSTEM\CurrentControlSet\Control\Class\<DriverKey>` ise kurulu sürücünün
   sürümünü, tarihini ve sağlayıcısını verir.
2. **Sağlayıcılar.** Windows Update ve yerel INF havuzu aynı anda sorgulanır. Biri
   başarısız olursa bu bir uyarı satırına dönüşür, tarama iptal olmaz.
3. **Tekilleştirme.** İki kaynak aynı paketi önerirse **yerel kopya tercih edilir** —
   zaten diskte olduğu için ağ gerektirmez. Windows Update kurulu olandan daha eski bir
   sürüm önerirse o aday listeden düşer.
4. **Kuyruk.** İndirmeler `SemaphoreSlim(MaxParallelJobs)` ile paralel, kurulumlar tek
   bir global kilidin arkasında sıralıdır. Başarısız bir iş varsayılan olarak 2 kez
   tekrar denenir.
5. **Devam.** Her durum değişikliği `session.json`'a atomik olarak yazılır. Yeniden
   başlatma gerekiyorsa kuyruk park edilir, oturum açılışına bağlı görev Hexnest'i
   `--resume` ile geri getirir. Bir oturum en fazla 10 yeniden başlatma sürer; sonrasında
   güvenlik gereği bırakılır.

---

## 🔨 Kaynak koddan derleme

Sadece programı kullanmak isteyen için burası ilgisiz: **exe'yi indirin, çift tıklayın, bitti.**
Kaynak kod deponun içinde ayrı bir klasörde durur ve kimseyi rahatsız etmez.

```
Hexnest/
├── src/                    kaynak kod (C#, .NET 8, WPF)
│   ├── Hexnest.Core/       arayüzden bağımsız çekirdek: tarama, sağlayıcılar, kuyruk
│   ├── Hexnest.App/        WPF masaüstü uygulaması (Hexnest.exe)
│   ├── Hexnest.Mac/        macOS için Avalonia uygulaması (Hexnest.app)
│   └── Hexnest.Cli/        (ayrılmış) başsız/konsol ön yüz için yer tutucu
├── docs/                   dokümanlar
├── build/                  derleme scriptleri
├── assets/                 logo ve ekran görüntüleri
└── .github/workflows/      CI
```

Kısaca:

```powershell
dotnet publish src/Hexnest.App/Hexnest.App.csproj -c Release -r win-x64 -o publish
```

Bir Mac'te ise, `artifacts/mac/arm64/Hexnest.app` üretir:

```bash
./build/make-mac-app.sh --arch arm64
```

macOS ve disk imajı için `--dmg` ekleyin. Betiğin .NET 8 SDK dışında hiçbir şeye ihtiyacı yoktur: `Info.plist` dosyasını yazar, `.icns` dosyasını `assets/icon-mac-1024.png` üzerinden üretir ve Apple silicon çalıştırsın diye paketi ad-hoc imzalar.


Ayrıntılar, arm64 yapısı ve WUApiLib COM referansının açıklaması için:
**[docs/BUILD.md](docs/BUILD.md)**

Mimari, `IDriverProvider` soyutlaması ve yeni bir sürücü kaynağının nasıl ekleneceği:
**[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)**

---

## 🔐 Güvenlik ve gizlilik

- **Telemetri yok.** Kullanım verisi, aygıt kimliği, istatistik hiçbir yere gönderilmez.
- Makineden dışarı çıkan **yalnızca iki** trafik vardır:
  1. **Windows Update sorguları** — doğrudan Microsoft'a, Windows'un kendi
     Windows Update Agent bileşeni üzerinden. (Çevrimdışı modda veya `--rescue` ile
     hiç yapılmaz.)
  2. **GitHub Releases API** — sadece siz *Güncellemeleri Kontrol Et*'e bastığınızda.
- **Neden yönetici yetkisi?** Sürücü kurmak ayrıcalıklı bir işlemdir: `pnputil`,
  Windows Update kurucusu ve Sistem Geri Yükleme yükseltilmiş bir belirteç ister.
  Hexnest bunu uygulama bildiriminde (`requireAdministrator`) baştan ister — kuyruğun
  ortasında yarıda kalmaktansa dürüst olmayı tercih eder.
- **Güvenlik ağları:** oturumdaki ilk kurulumdan önce sistem geri yükleme noktası,
  her değiştirdiği sürücü paketinin yedeği.
- **Kendi kendini güncelleme** indirilen dosyayı sürümün `checksums.txt` dosyasındaki
  SHA-256 ile karşılaştırır; sağlama toplamı yoksa veya tutmuyorsa dosya silinir ve
  kurulum reddedilir.
- Tüm durum dosyaları `%ProgramData%\Hexnest` altındadır: `settings.json`,
  `session.json`, `history.jsonl`, `logs/`, `backups/`, `cache/`, `reports/`.

Güvenlik açığı bildirimi: **[SECURITY.md](SECURITY.md)**

---

## ❓ Sık Sorulan Sorular

### Hexnest ücretsiz mi?

Evet. Hexnest MIT lisansıyla yayımlanır ve kaynak kodunun tamamı bu depodadır. Ücretli sürüm,
deneme süresi, para ödeyince açılan özellik, reklam ya da yanında gelen üçüncü parti yazılım
yoktur. Tarama ile kurulum aynı programın parçasıdır.

### .NET kurmam gerekiyor mu?

Hayır. .NET 8 çalışma zamanının tamamı `Hexnest.exe`'nin içindedir (self-contained, tek dosya
yayını) ve WPF kendi `vcruntime140_cor3.dll` / `msvcp140_cor3.dll` kopyalarını taşır. Visual C++
Redistributable de gerekmez. Tek gereksinim 64-bit Windows 10 sürüm 1607 (yapı 14393) veya üstüdür.

### Hexnest bir Mac'te sürücü günceller mi?

Hayır, başka hiçbir şey de güncellemez. macOS'ta üçüncü taraf bir sürücü deposu yoktur: Apple aygıt
desteğini işletim sisteminin içinde gönderir ve onu işletim sistemiyle birlikte günceller. Böyle bir
aracın tarayacağı, indireceği veya yedekleyeceği hiçbir şey yoktur; bu yüzden Mac yapısında *Aygıtlar*,
*Güncellemeler*, *İşlemler*, *Yedekle* veya *Geçmiş* sayfası hiç yoktur — asla içi dolmayacak beş sayfa
olmasındansa. Hexnest'in yaptığı diğer her şey orada çalışır.

### Hangi Mac gerekiyor?

macOS 12 Monterey veya daha yenisi; Apple silicon ya da Intel. İki yapı tek bir evrensel ikili yerine
ayrı ayrı yayımlanır (`Hexnest-arm64.dmg` ve `Hexnest-x64.dmg`): her biri kendi .NET çalışma zamanını
taşır ve bunları birleştirmek, indirme sayfasındaki tek bir kararı ortadan kaldırmak için herkesin
indirmesini ikiye katlardı.

### macOS "geliştirici doğrulanamadığı için açılamıyor" diyor

Sürümler ad-hoc imzalanır ama noter onaylı değildir; noter onayı ücretli bir Apple Developer hesabı
gerektirir. Applications içindeki Hexnest'e sağ tıklayın (veya Control ile tıklayın) ve **Aç** deyin;
macOS bir kez sorar ve cevabı hatırlar. Her sürüm, indirmeyi önceden doğrulayabileceğiniz bir
`checksums.txt` yayımlar.

### Hexnest for Mac neden hiç parolamı sormuyor?

Çünkü hiç ihtiyacı yok. İzleyiciler herkese açık istatistikleri okur, temizleyici kendi ev klasörünüzün
içinde çalışır ve başlangıç yöneticisi kendi giriş ajanlarınızı `launchctl` ile değiştirir. Sistem geneli
launchd işleri gösterilir ama salt okunur işaretlenir. Size bir grafik göstermek için yönetici parolası
isteyen bir uygulama, ihtiyacı olandan çok daha fazla güven istiyor demektir.

### SmartScreen / antivirüs neden uyarı veriyor?

Çünkü `Hexnest.exe` **kod imzalama sertifikasıyla imzalanmamıştır** — sertifika ücretlidir.
SmartScreen ve Smart App Control, itibar kazanmamış imzasız her exe için uyarı gösterir; üstelik
yönetici olarak çalışıp sürücü kuran ve zamanlanmış görev oluşturan bir uygulama sezgisel
tarayıcılara kötü amaçlı yazılım gibi görünür. Dürüst çözüm dosyayı doğrulamaktır:
`Get-FileHash .\Hexnest.exe -Algorithm SHA256` çıktısını sürümün `checksums.txt` dosyasındaki
satırla karşılaştırın.

### Formattan sonra internet yokken sürücü kurabilir miyim?

Evet, Hexnest asıl bunun için yazıldı. Format öncesinde sürücülerinizi yedekleyip yedeği ve
`Hexnest.exe`'yi aynı USB belleğe koyun, format sonrasında `Hexnest.exe --rescue` ile açıp
**Geri Yükle**'ye basın. `Hexnest.exe` ile aynı klasördeki `Drivers` adlı klasör otomatik olarak
yerel sürücü havuzu sayılır; üreticiden indirip açtığınız klasörler de kullanılabilir.

### Bozulan bir sürücüyü geri alabilir miyim?

Evet, üç yolu var: Hexnest'in her kurulumdan hemen önce aldığı yedekten **Klasörden Geri Yükle**
ile, oturumun ilk kurulumundan önce oluşturulan sistem geri yükleme noktasından (`rstrui.exe`) ya
da Aygıt Yöneticisi'ndeki *Sürücüyü Geri Al* düğmesiyle. Bu yüzden geri yükleme noktası ayarını
kapatmayın.

### Yeniden başlatmadan sonra gerçekten devam ediyor mu?

Evet. Kuyruk durumu her değişiklikte `session.json`'a atomik olarak yazılır ve oturum açılışına
bağlı `Hexnest\ResumeSession` zamanlanmış görevi (yedeği HKLM `RunOnce`) Hexnest'i `--resume` ile
geri getirir. Bir oturum en fazla 10 yeniden başlatma taşır; kuyruk bitince görev ve kayıt silinir.

### Bir başlangıç programını kapatmak bir şey siliyor mu?

Hayır. Windows açık/kapalı bilgisini ayrı bir anahtarda tutar —
`...\CurrentVersion\Explorer\StartupApproved\Run` ve onunla aynı düzeydeki iki anahtar —
Hexnest'in yazdığı tek şey de budur. `Run` değeri ya da Başlangıç klasöründeki kısayol tam
olarak olduğu yerde kalır; bu yüzden kaydı tekrar açtığınızda özgün komut satırı bayt bayt
aynı şekilde çalışır.

Bu aynı zamanda Görev Yöneticisi ile Hexnest'in birbiriyle aynı şeyi göstermesi demektir:
birinde kapattığınızı diğeri de kapalı gösterir. Ayrıca Hexnest'i ileride silerseniz
bilgisayarda başlangıç programlarının yarısı eksik kalmaz, çünkü hiçbiri zaten bir yere
gitmemiştir.

### Temizlik güvenli mi?

Öyle olacak şekilde tasarlandı ve tasarım, güvenmenizi istemek yerine bunu nasıl yaptığını
anlatıyor:

- **Sizin yerinize hiçbir şey işaretlenmez.** Sayfa, toplamı sıfır olarak açılır.
- Her yol, elle yazılmış bir yol metninden değil, Windows'un bilinen klasör API'sinden
  gelir. Bir kategorinin kendi köklerinin dışına asla dokunulmaz ve her silme işlemi
  gerçekleşmeden hemen önce o köklerin içinde kalıp kalmadığı yeniden denetlenir.
- Yeniden ayrıştırma noktaları asla izlenmez. `%LOCALAPPDATA%` junction'larla doludur ve
  birinin içine dalmak, "önbelleği temizle" diyen bir özelliğin sonunda birinin belgelerini
  silmesine giden yoldur.
- Açık olan dosyalar zorlanmaz, atlanır. Atlanan dosya sayısı bildirilir.
- Kendi dosyalarınız — eski indirmeler, artık klasörler — asla toplu olarak seçilmez.
  Boyutu ve yaşıyla tek tek listelenir ve **Geri Dönüşüm Kutusu**'na gider.

Artık klasör tespiti, Hexnest'in tahmin yürüttüğü tek yerdir; bu bir tahmin, kesin bilgi
değil — ve satırın kendisi bunu yazar.

### "Bellek boşalt" gerçekten bir işe yarıyor mu?

Fiziksel belleği o an için boşaltır ve yaptığı tek şey budur.

Her işlem için `EmptyWorkingSet` çağırır; bu, Windows'tan o işlemin çalışma kümesini disk
belleğine almasını ister. Kullanımdaki bellek gerçekten düşer. Ama o sayfalar yok olmaz —
diskteler ve program o belleğe bir daha dokunduğu anda Windows onları geri okur, ki bu
onları oldukları yerde bırakmaktan daha yavaştır. Kullanılmayan bellek boşa giden bellek
değildir; Windows onu zaten kullanılabilir tutuyordu.

Yani bu bir performans özelliği değil ve Hexnest onu öyle sunmuyor. Büyük bir bellek
ayırması gerektiren bir işe başlamadan hemen önce ya da bellek sızdıran bir programın
gerçekte ne kadar tuttuğunu görmek için gerçekten işe yarar. Bu düğmeye sahip diğer her
program aksini iddia eder.

### Sıcaklık kartı neden sensör olmadığını yazıyor?

Çünkü o bilgisayarda Windows'un okuyabileceği bir sensör yok. Windows'un sürücü olmadan
sunduğu tek sıcaklık değeri, ürün yazılımının kendi fan denetimi için tanımladığı ACPI termal
bölgesidir (`root\WMI:MSAcpi_ThermalZoneTemperature`) ve pek çok masaüstü anakart hiç
tanımlamaz. Çekirdek başına ve ekran kartı sıcaklıkları, SMBus üzerinden üreticinin sensör
yongasından gelir; bunun için imzalı bir çekirdek sürücüsü gerekir — HWiNFO ve Open Hardware
Monitor tam olarak bunu kurar. Hexnest bir sayıyı doldurmak için çekirdek sürücüsü kurmaz;
bu yüzden makul görünen bir 45 °C uydurmak yerine sensörün olmadığını söyler.

### Program başına ağ kullanımı neden bilgisayar toplamıyla uyuşmuyor?

Çünkü ikisi farklı ölçülür ve ikisi de doğrudur.

Bilgisayar geneli değer, ağ bağdaştırıcılarının kendi bayt sayaçlarının toplamıdır; yani her
şeyi kapsar: TCP, UDP, QUIC, yayın trafiği. Uygulama bazındaki değer ise
`GetPerTcpConnectionEStats` üzerinden TCP ESTATS'tan (RFC 4898) gelir; Windows'un çekirdek
sürücüsü olmadan sunduğu tek işlem başına bayt sayacı budur — ve yalnızca TCP'yi kapsar.
Görüntülü aramalar, oyun trafiğinin büyük kısmı ve DNS bu yüzden birinci sayıya dahildir,
ikincisine değil. Sayfa bunu sessizce eksik göstermek yerine açıkça yazar.

ESTATS'ı etkinleştirmek yükseltilmiş bir belirteç gerektirir. Hexnest'te bu her zaman vardır;
yine de reddedilirse tablo işlem başına bağlantı sayısına düşer ve nedenini yazar.

### Hexnest kendini arka planda güncelliyor mu?

Günde bir kez **denetler** ve sonucu *Hakkında* menü girdisinde gösterir. **Ayarlar →
Güncellemeler** altından açmadığınız sürece hiçbir şey indirmez ve kurmaz; açtığınızda bile:

- indirilen dosya güvenilir sayılmadan önce sürümün `checksums.txt` dosyasıyla doğrulanır,
- değiştirme işlemi Hexnest **kapanırken** yapılır, asla bir sürücü kuyruğu çalışırken değil,
- çevrimdışı ve kurtarma modunda hem denetim hem kurulum tamamen atlanır.

Denetimi tamamen kapatabilirsiniz; *Güncellemeleri Kontrol Et* düğmesi yine çalışır.

### İzleme sayfaları arka planda çalışan bir servis mi?

Hayır. İki izleme sayfası da siz açmadan hiçbir şey örneklemez ve başka bir sayfaya geçtiğiniz
anda ikisi de durur. Hexnest yine hiçbir servis, sürücü ve başlangıç girdisi kurmaz — kaydettiği
tek şey, yarıda kalan bir sürücü kuyruğunu sürdüren oturum açılışı görevidir; o da kuyruk bitince
kendini siler.

### Hexnest veri topluyor mu?

Hayır. Telemetri, kullanım istatistiği, aygıt kimliği yok. Makineden dışarı yalnızca iki trafik
çıkar: Windows'un kendi bileşeni üzerinden Microsoft'a giden Windows Update sorguları (çevrimdışı
modda hiç yapılmaz) ve *Güncellemeleri Kontrol Et*'e bastığınızda GitHub Releases API'sine giden
tek bir istek.

---

"Exe neden bu kadar büyük?", "Neden WMI kullanılmıyor?", "Windows Server'da çalışır mı?" gibi
soruların cevapları ve tam liste:

**[docs/FAQ.md](docs/FAQ.md)** · Kullanım rehberi: **[docs/USAGE.md](docs/USAGE.md)** ·
Proje sayfası: **[ahmetcaglayan.github.io/Hexnest](https://ahmetcaglayan.github.io/Hexnest/tr/)**

---

## 🤝 Katkıda bulunma

Katkılar memnuniyetle karşılanır.

- **Hata bildirimi:** [Issues](../../issues) — lütfen **Günlük** menüsündeki *Kopyala*
  butonuyla aldığınız günlüğü ekleyin, sürüm ve işletim sistemi bilgisi otomatik olarak
  başına yazılır.
- **Kod:** depoyu çatallayın, bir dal açın, değişikliğinizi gönderin. Mevcut kod stilini
  koruyun: NuGet bağımlılığı eklemeyin (tek dosya boyutu ve çevrimdışı çalışabilirlik
  bilinçli bir tercihtir), `Hexnest.Core` içine arayüz kodu koymayın.
- **Çeviri:** yeni bir dil eklemek `src/Hexnest.Core/Languages/` klasörüne bir JSON
  dosyası bırakmaktan ibarettir; `build/check-languages.py` onu İngilizceyle karşılaştırır.
  Ayrıntılar [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) içinde.

---

## 📄 Lisans

MIT — bkz. [LICENSE](LICENSE).

---

## ⚠️ Sorumluluk reddi

Sürücü kurmak doğası gereği risklidir. Yanlış ya da bozuk bir sürücü, sistemin
açılmamasına kadar varan sorunlara yol açabilir. Hexnest bu riski azaltmak için
sistem geri yükleme noktası oluşturur ve değiştirdiği sürücüleri yedekler, ancak
hiçbir garanti vermez.

**Geri yükleme noktası ayarını kapatmayın.** Önemli verilerinizin yedeği olsun.
Yazılım "olduğu gibi" sunulur; kullanımından doğacak sonuçların sorumluluğu
kullanıcıya aittir.

---

## Anahtar kelimeler

<sub>
sürücü güncelleme programı · format sonrası driver yükleme · eksik sürücü bulma programı ·
driver yedekleme programı · ücretsiz driver güncelleyici · windows sürücü tarama ·
çevrimdışı sürücü kurulumu · usb ile driver yükleme · açık kaynak sürücü güncelleyici ·
windows 10 / windows 11 sürücü kurma, ücretsiz sistem izleme programı, işlemci ram sıcaklık takibi, program bazında ağ kullanımı windows, program bazında bant genişliği takibi, açık kaynak görev yöneticisi alternatifi ·
aygıt yöneticisi sarı ünlem çözümü
</sub>
