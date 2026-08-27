<div align="center">

<img src="assets/logo.svg" alt="DrvNest logosu: bir mikroçipi kucaklayan altıgen petek gözü" width="120" height="120">

# DrvNest

**DrvNest, Windows 10 ve 11 için ücretsiz ve açık kaynak bir sürücü güncelleme programıdır.**
Sistemdeki tüm aygıtları tarar, eksik ve eski sürücüleri bulup kurar, gereken yeniden
başlatmalardan sonra kaldığı yerden devam eder; format öncesinde sürücülerinizi yedekler ve
format sonrasında internet olmadan geri yükler. Tek dosya, kurulum yok, reklam yok.

<br>

[![DrvNest.exe indir](https://img.shields.io/badge/⬇️%20DrvNest.exe%20İNDİR-Windows%20x64-2ea043?style=for-the-badge&logo=windows&logoColor=white&labelColor=1a7f37)](https://github.com/ahmetcaglayan/DrvNest/releases/latest/download/DrvNest.exe)

<sub>[🌐 Proje sayfası](https://ahmetcaglayan.github.io/DrvNest/tr/) · [Tüm sürümler ve arm64 yapısı →](../../releases)</sub>

<br>

### Kurulum gerekmez. İndir, çift tıkla, çalışır.

`Windows 10 1607+ / Windows 11` · `64-bit` · `Yönetici yetkisi gerekir`

**.NET kurulumu gerekmez. Visual C++ Redistributable gerekmez.**
Her şey exe'nin içinde: .NET 8 çalışma zamanı self-contained ve tek dosya olarak paketlenir,
WPF de kendi `vcruntime140_cor3.dll` / `msvcp140_cor3.dll` kopyalarını beraberinde taşır.

<br>

[![Lisans: MIT](https://img.shields.io/badge/Lisans-MIT-blue?style=flat-square)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%2F11-0078d4?style=flat-square&logo=windows&logoColor=white)](../../releases)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-512bd4?style=flat-square&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Sürüm](https://img.shields.io/github/v/release/ahmetcaglayan/DrvNest?style=flat-square&label=S%C3%BCr%C3%BCm)](../../releases/latest)
[![İndirme](https://img.shields.io/github/downloads/ahmetcaglayan/DrvNest/total?style=flat-square&label=indirme&color=2ea043)](../../releases)
[![Yıldız](https://img.shields.io/github/stars/ahmetcaglayan/DrvNest?style=flat-square)](../../stargazers)
[![Derleme](https://img.shields.io/github/actions/workflow/status/ahmetcaglayan/DrvNest/build.yml?style=flat-square&label=Derleme)](../../actions/workflows/build.yml)

<sub>🇹🇷 Türkçe · [🇬🇧 English](README.en.md)</sub>

</div>

---

## 🎯 Bu ne işe yarar?

Windows'u formatladınız. Aygıt Yöneticisi sarı ünlem işaretleriyle dolu, ekran çözünürlüğü
yanlış, ses yok ve — en kötüsü — ağ kartının sürücüsü olmadığı için internet de yok.

DrvNest bu tabloyu tek bir pencereden çözer:

- Sistemdeki **tüm PnP aygıtlarını** listeler ve hangisinin sürücüsü olmadığını söyler.
- Eksik ve güncellenebilir sürücüleri **Windows Update kataloğundan** ya da
  **USB'deki yerel bir klasörden** bulur.
- Hepsini bir kuyruğa alır, indirir, kurar; gereken yeniden başlatmalardan sonra
  **kaldığı yerden devam eder**.
- Format **öncesinde** mevcut sürücülerinizi dışa aktarır; format **sonrasında**
  internet olmadan geri yükler.

Tek dosya, kurulum yok, arka planda çalışan servis yok, telemetri yok.

---

## ✨ Öne çıkan özellikler

| Özellik | Ne yapar |
| --- | --- |
| 🔍 **Tam aygıt taraması** | Sistemdeki tüm PnP aygıtlarını SetupAPI + CfgMgr32 ile sayar. WMI kullanmaz, bu yüzden WMI deposu bozuk ya da yeni kurulmuş bir makinede de çalışır. |
| ⚠️ **Eksik sürücü tespiti** | Configuration Manager sorun kodlarını okur; 28 (`CM_PROB_FAILED_INSTALL`), 1 ve 19 "sürücü yok" olarak işaretlenir. 22 devre dışı, 14 yeniden başlatma bekliyor demektir. |
| ☁️ **Windows Update sürücü kataloğu** | Windows Update Agent COM API'si (WUApiLib) üzerinden Microsoft Update'e bağlanır. Ek servis, ek indirme, ek bağımlılık yok — `wuapi.dll` zaten her Windows'ta var. |
| 💾 **Yerel / çevrimdışı INF havuzu** | Klasörlerdeki `.inf` paketlerini ayrıştırır ve donanım kimliğine göre eşleştirir. USB bellek, ağ paylaşımı ya da bir DrvNest yedeği kaynak olabilir. |
| ⚡ **Paralel indirme + sıralı kurulum** | İndirmeler aynı anda (varsayılan 3, ayarlanabilir 1–8). Kurulumlar tek tek. Bu bir eksiklik değil: Windows Update ikinci bir kuruluma `WU_E_OPERATIONINPROGRESS` döner ve PnP alt sistemi zaten sıraya sokar. Aynı anda kurmaya çalışmak sadece sahte hatalar üretir. |
| 🔄 **Yeniden başlatma sonrası devam** | Kuyruk her adımda `session.json` dosyasına yazılır; `schtasks` ile oturum açılışına bağlı bir görev (yedeği HKLM `RunOnce`) DrvNest'i `--resume` ile geri getirir ve kaldığı yerden devam eder. |
| 🛡️ **Sistem geri yükleme noktası** | Oturumdaki ilk kurulumdan önce `srclient.dll` ile sürücü tipinde bir geri yükleme noktası oluşturur. |
| ↩️ **Güncelleme öncesi yedek** | Değiştirilecek sürücü paketi kurulumdan hemen önce dışa aktarılır; yolu geçmiş kaydına yazılır, böylece bir şey ters giderse o klasörden geri yüklenebilir. |
| 📦 **Sürücü yedekleme / geri yükleme** | `pnputil /export-driver` ile tüm üçüncü parti sürücüleri klasöre veya ZIP'e aktarır; `pnputil /add-driver ... /subdirs /install` ile geri yükler. |
| 📊 **Güncelleme geçmişi** | Kalıcı kayıt, satır başına bir JSON nesnesi (`history.jsonl`) olarak tutulur; tek tıkla CSV'ye aktarılır. |
| 📄 **Donanım raporu** | Tüm aygıtları ve donanım kimliklerini düz metin dosyasına yazar. İnterneti olmayan makineden USB ile taşıyıp çalışan bir bilgisayarda sürücü aramak için. |
| 🆙 **Kendi kendini güncelleme** | GitHub Releases'ten yeni sürümü indirir, **SHA-256 doğrulaması** yapar (`checksums.txt` yoksa kurulumu reddeder) ve exe'yi yerinde değiştirir. |
| 🌍 **Türkçe / İngilizce arayüz** | Uygulama açıkken anında değişir. |
| 🎨 **Koyu / açık tema** | Palet sözlüğü değiştirilir, pencere yeniden açılmadan uygulanır. |

---

## 🚑 Format sonrası kurtarma senaryosu

Bu, DrvNest'in var oluş sebebi.

### Tavuk-yumurta problemi

Formattan sonra genellikle **ağ kartının sürücüsü de yoktur**. Sürücüyü indirmek için
internet, internete çıkmak için sürücü gerekir. Windows Update bu durumda size yardım
edemez, çünkü ona ulaşamazsınız.

Çözüm: **sürücüleri formattan önce yanınıza almak.**

### Format ÖNCESİ (5 dakika)

1. DrvNest'i çalıştırın.
2. **Yedekle & Geri Yükle** menüsüne gidin.
3. **Yedek Oluştur**'a basın. Sistemdeki tüm üçüncü parti sürücü paketleri dışa aktarılır.
   (Microsoft'un kendi kutu içi sürücüleri bilinçli olarak yedeklenmez — Windows onları
   zaten kendisi kurar, yedeğe eklemek boyutu boşuna üçe katlardı.)
4. İsterseniz **ZIP olarak sıkıştır** kutusunu işaretleyin.
5. Oluşan klasörü **ve `DrvNest.exe`'yi aynı USB belleğe** kopyalayın.

> 💡 İsteğe bağlı: Yedek klasörünü USB'de `DrvNest.exe` ile aynı dizinde `Drivers` adıyla
> tutarsanız, DrvNest onu **otomatik olarak** yerel sürücü havuzu olarak kaydeder.
> Hiçbir ayar yapmanız gerekmez.

### Format SONRASI

1. USB belleği takın, `DrvNest.exe`'yi çalıştırın (yönetici onayı ister).
   İnternet yoksa `DrvNest.exe --rescue` ile açın: Windows Update hiç aranmaz,
   yalnızca yerel kaynaklar kullanılır.
2. **Yedekle & Geri Yükle → Geri Yükle** (veya **Klasörden Geri Yükle**) ile yedeğinizi
   seçin. Tüm paketler sürücü deposuna eklenir ve aygıtlara bağlanır.
3. Ağ kartı çalışmaya başladıktan sonra **Tara**'ya basın.
4. **Genel Bakış → Format Sonrası Kurtarma** butonu, hâlâ eksik olan her şeyi
   Windows Update'ten bulup sıraya alır.
5. Yeniden başlatma istenirse kabul edin — DrvNest açılışta kendini geri çağırır ve
   kuyruğun kalanını tamamlar.

> ℹ️ Yedek klasörü illa DrvNest tarafından üretilmiş olmak zorunda değil. Üreticinin
> sitesinden indirip açtığınız herhangi bir sürücü klasörünü de **Klasörden Geri Yükle**
> ile kurabilirsiniz; içindeki `.inf` dosyaları alt klasörler dahil taranır.

### Komut satırı

```powershell
DrvNest.exe                 # normal başlatma
DrvNest.exe --rescue        # çevrimdışı kurtarma modu (--offline ile aynı)
DrvNest.exe --resume        # kesintiye uğramış kuyruğu doğrudan sürdür
```

---

## 📸 Ekran görüntüleri

> Ekran görüntüleri yakında — `assets/screenshots/` klasörüne eklenecek.

Uygulamadaki dokuz ekran:

- `dashboard.png` — Genel Bakış: aygıt/eksik/güncelleme sayıları, sistem özeti, hızlı işlemler
- `devices.png` — Aygıtlar: sınıfa göre gruplanmış tam envanter, filtreler ve arama
- `updates.png` — Güncellemeler: kurulabilir paket listesi ve seçim
- `queue.png` — İşlemler: canlı indirme/kurulum ilerlemesi
- `backup.png` — Yedekle & Geri Yükle
- `history.png` — Geçmiş ve CSV dışa aktarma
- `logs.png` — Günlük
- `settings.png` — Ayarlar
- `about.png` — Hakkında ve kendi kendini güncelleme

---

## 🧭 Menüler

| Menü | Ne yapar |
| --- | --- |
| **Genel Bakış** | Aygıt sayısı, eksik sürücü sayısı, güncelleme sayısı, sorunlu aygıt sayısı. İşletim sistemi / makine / işlemci / BIOS özeti. Hızlı işlemler: *Şimdi Tara*, *Format Sonrası Kurtarma*, *Tümünü Güncelle*, *Sürücüleri Yedekle*, *Donanım Raporu*. Çalışan ağ sürücüsü yoksa uyarı şeridi çıkar. |
| **Aygıtlar** | Sistemdeki tüm PnP aygıtları, sınıfa göre gruplanmış. Filtreler: *Tümü / Sorunlu / Sürücüsüz / Jenerik Sürücü*. Ada, üreticiye, sürüme ve donanım kimliğine göre arama; donanım kimliğini panoya kopyalama. |
| **Güncellemeler** | Kurulabilecek paketler: hem eksik sürücüler hem de sürüm yükseltmeleri. Tekli seçim, *Tümünü Seç / Seçimi Temizle*, seçili boyut toplamı, *Seçilenleri Kur*. Bir güncellemeyi gizleyebilir veya bir aygıtı tamamen yoksayabilirsiniz. |
| **İşlemler** | Çalışan kuyruk. Her iş için indirme yüzdesi, hız, aktarılan bayt ve kurulum aşaması ayrı ayrı görünür. *Tümünü İptal Et*, *Başarısızları Tekrar Dene*, *Şimdi Yeniden Başlat* / *Daha Sonra*. Kesintiye uğramış bir oturum varsa *Devam Et* butonu burada çıkar. |
| **Yedekle & Geri Yükle** | *Yedek Oluştur* (isteğe bağlı ZIP), mevcut yedeklerin listesi (paket sayısı, boyut, tarih), *Geri Yükle*, *Klasörden Geri Yükle*, *Aç*, *Sil*. |
| **Geçmiş** | Yapılan tüm sürücü işlemlerinin kalıcı kaydı. Sonuca göre filtre, arama, *CSV Olarak Dışa Aktar*, *Geçmişi Temizle*. Bir kaydın güncelleme öncesi yedeği duruyorsa klasörü açabilirsiniz. |
| **Günlük** | Canlı tanılama akışı. *Kopyala* butonu sürüm, işletim sistemi ve makine başlığıyla birlikte günlüğü panoya alır — hata bildirirken tam olarak bu gerekir. Günlük dosyasını / klasörünü açma ve temizleme. |
| **Ayarlar** | Aynı anda indirme sayısı, tekrar deneme sayısı, açılışta tarama, geri yükleme noktası, güncelleme öncesi yedek, yeniden başlatma sonrası devam, otomatik yeniden başlatma ve gecikmesi, çevrimdışı mod, isteğe bağlı sürücüler, yerel sürücü klasörleri, geçmiş saklama süresi, tema, dil. |
| **Hakkında** | Sürüm bilgisi, *Güncellemeleri Kontrol Et*, *İndir ve Kur*, sürüm notları, proje sayfası ve hata bildirme bağlantıları. |

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
    N --> O["DrvNest --resume"]
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
   başlatma gerekiyorsa kuyruk park edilir, oturum açılışına bağlı görev DrvNest'i
   `--resume` ile geri getirir. Bir oturum en fazla 10 yeniden başlatma sürer; sonrasında
   güvenlik gereği bırakılır.

---

## 🔨 Kaynak koddan derleme

Normal kullanıcı için burası ilgisiz: **exe'yi indirin, çift tıklayın, bitti.**
Kaynak kod deponun içinde ayrı bir klasörde durur ve kimseyi rahatsız etmez.

```
DrvNest/
├── src/                    kaynak kod (C#, .NET 8, WPF)
│   ├── DrvNest.Core/       arayüzden bağımsız çekirdek: tarama, sağlayıcılar, kuyruk
│   ├── DrvNest.App/        WPF masaüstü uygulaması (DrvNest.exe)
│   └── DrvNest.Cli/        (ayrılmış) başsız/konsol ön yüz için yer tutucu
├── docs/                   dokümanlar
├── build/                  derleme scriptleri
├── assets/                 logo ve ekran görüntüleri
└── .github/workflows/      CI
```

Kısaca:

```powershell
dotnet publish src/DrvNest.App/DrvNest.App.csproj -c Release -r win-x64 -o publish
```

Ayrıntılar, arm64 yapısı ve WUApiLib COM referansının açıklaması için:
**[docs/BUILD.md](docs/BUILD.md)**

Mimari, `IDriverProvider` soyutlaması ve yeni bir sürücü kaynağının nasıl ekleneceği:
**[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)**

---

## 🔐 Güvenlik ve gizlilik

- **Telemetri yok.** Kullanım verisi, cihaz kimliği, istatistik hiçbir yere gönderilmez.
- Makineden dışarı çıkan **yalnızca iki** trafik vardır:
  1. **Windows Update sorguları** — doğrudan Microsoft'a, Windows'un kendi
     Windows Update Agent bileşeni üzerinden. (Çevrimdışı modda veya `--rescue` ile
     hiç yapılmaz.)
  2. **GitHub Releases API** — sadece siz *Güncellemeleri Kontrol Et*'e bastığınızda,
     yeni sürüm olup olmadığına bakmak için.
- **Neden yönetici yetkisi?** Sürücü kurmak ayrıcalıklı bir işlemdir: `pnputil`,
  Windows Update kurucusu ve Sistem Geri Yükleme yükseltilmiş bir belirteç ister.
  DrvNest bunu uygulama bildiriminde (`requireAdministrator`) baştan ister — kuyruğun
  ortasında yarıda kalmaktansa dürüst olmayı tercih eder.
- **Güvenlik ağları:** oturumdaki ilk kurulumdan önce sistem geri yükleme noktası,
  her güncellemeden önce değiştirilen sürücünün yedeği.
- **Kendi kendini güncelleme** indirilen dosyayı sürümün `checksums.txt` dosyasındaki
  SHA-256 ile karşılaştırır; sağlama toplamı yoksa veya tutmuyorsa dosya silinir ve
  kurulum reddedilir.
- Tüm durum dosyaları `%ProgramData%\DrvNest` altındadır: `settings.json`,
  `session.json`, `history.jsonl`, `logs/`, `backups/`, `cache/`, `reports/`.

Güvenlik açığı bildirimi: **[SECURITY.md](SECURITY.md)**

---

## ❓ Sık Sorulan Sorular

### DrvNest ücretsiz mi?

Evet. DrvNest MIT lisansıyla yayımlanır ve kaynak kodunun tamamı bu depodadır. Ücretli sürüm,
deneme süresi, para ödeyince açılan özellik, reklam ya da yanında gelen üçüncü parti yazılım
yoktur. Tarama ile kurulum aynı programın parçasıdır.

### .NET kurmam gerekiyor mu?

Hayır. .NET 8 çalışma zamanının tamamı `DrvNest.exe`'nin içindedir (self-contained, tek dosya
yayını) ve WPF kendi `vcruntime140_cor3.dll` / `msvcp140_cor3.dll` kopyalarını taşır. Visual C++
Redistributable de gerekmez. Tek gereksinim 64-bit Windows 10 sürüm 1607 (yapı 14393) veya üstüdür.

### SmartScreen / antivirüs neden uyarı veriyor?

Çünkü `DrvNest.exe` **kod imzalama sertifikasıyla imzalanmamıştır** — sertifika ücretlidir.
SmartScreen ve Smart App Control, itibar kazanmamış imzasız her exe için uyarı gösterir; üstelik
yönetici olarak çalışıp sürücü kuran ve zamanlanmış görev oluşturan bir uygulama sezgisel
tarayıcılara kötü amaçlı yazılım gibi görünür. Dürüst çözüm dosyayı doğrulamaktır:
`Get-FileHash .\DrvNest.exe -Algorithm SHA256` çıktısını sürümün `checksums.txt` dosyasındaki
satırla karşılaştırın.

### Formattan sonra internet yokken sürücü kurabilir miyim?

Evet, DrvNest asıl bunun için yazıldı. Format öncesinde sürücülerinizi yedekleyip yedeği ve
`DrvNest.exe`'yi aynı USB belleğe koyun, format sonrasında `DrvNest.exe --rescue` ile açıp
**Geri Yükle**'ye basın. `DrvNest.exe` ile aynı klasördeki `Drivers` adlı klasör otomatik olarak
yerel sürücü havuzu sayılır; üreticiden indirip açtığınız klasörler de kullanılabilir.

### Bozulan bir sürücüyü geri alabilir miyim?

Evet, üç yolu var: DrvNest'in kurulumdan hemen önce aldığı yedekten **Klasörden Geri Yükle** ile,
oturumun ilk kurulumundan önce oluşturulan sistem geri yükleme noktasından (`rstrui.exe`) ya da
Aygıt Yöneticisi'ndeki *Sürücüyü Geri Al* düğmesiyle. Bu yüzden geri yükleme noktası ayarını
kapatmayın.

### Yeniden başlatmadan sonra gerçekten devam ediyor mu?

Evet. Kuyruk durumu her değişiklikte `session.json`'a atomik olarak yazılır ve oturum açılışına
bağlı `DrvNest\ResumeSession` zamanlanmış görevi (yedeği HKLM `RunOnce`) DrvNest'i `--resume` ile
geri getirir. Bir oturum en fazla 10 yeniden başlatma taşır; kuyruk bitince görev ve kayıt silinir.

### DrvNest veri topluyor mu?

Hayır. Telemetri, kullanım istatistiği, cihaz kimliği yok. Makineden dışarı yalnızca iki trafik
çıkar: Windows'un kendi bileşeni üzerinden Microsoft'a giden Windows Update sorguları (çevrimdışı
modda hiç yapılmaz) ve *Güncellemeleri Kontrol Et*'e bastığınızda GitHub Releases API'sine giden
tek bir istek.

---

"Exe neden bu kadar büyük?", "Neden WMI kullanılmıyor?", "Windows Server'da çalışır mı?" gibi
soruların cevapları ve tam liste:

**[docs/FAQ.md](docs/FAQ.md)** · Kullanım rehberi: **[docs/USAGE.md](docs/USAGE.md)** ·
Proje sayfası: **[ahmetcaglayan.github.io/DrvNest](https://ahmetcaglayan.github.io/DrvNest/tr/)**

---

## 🤝 Katkıda bulunma

Katkılar memnuniyetle karşılanır.

- **Hata bildirimi:** [Issues](../../issues) — lütfen **Günlük** menüsündeki *Kopyala*
  butonuyla aldığınız günlüğü ekleyin, sürüm ve işletim sistemi bilgisi otomatik olarak
  başına yazılır.
- **Kod:** depoyu çatallayın, bir dal açın, değişikliğinizi gönderin. Mevcut kod stilini
  koruyun: NuGet bağımlılığı eklemeyin (tek dosya boyutu ve çevrimdışı çalışabilirlik
  bilinçli bir tercihtir), `DrvNest.Core` içine arayüz kodu koymayın.
- **Çeviri:** yeni bir dil eklemek `src/DrvNest.App/Services/Loc.cs` içine bir sözlük
  eklemekten ibarettir; ayrıntılar [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) içinde.

---

## 📄 Lisans

MIT — bkz. [LICENSE](LICENSE).

---

## ⚠️ Sorumluluk reddi

Sürücü kurmak doğası gereği risklidir. Yanlış ya da bozuk bir sürücü, sistemin
açılmamasına kadar varan sorunlara yol açabilir. DrvNest bu riski azaltmak için
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
windows 10 / windows 11 sürücü kurma · aygıt yöneticisi sarı ünlem çözümü
</sub>
