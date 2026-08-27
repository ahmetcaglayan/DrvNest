# SSS / FAQ

🇹🇷 [Türkçe](#türkçe) · 🇬🇧 [English](#english)

---

## Türkçe

### Kurulum ve gereksinimler

<details open>
<summary><b>.NET kurmam gerekiyor mu?</b></summary>

**Hayır.** .NET 8 çalışma zamanının tamamı `DrvNest.exe`'nin içindedir (self-contained,
tek dosya yayını). Makinede hiç .NET olmasa bile çalışır.

Aynı şekilde **Visual C++ Redistributable de gerekmez**: WPF'in çalışma zamanı paketi
kendi özel `vcruntime140_cor3.dll` ve `msvcp140_cor3.dll` kopyalarını beraberinde taşır.

Tek gereksinim: **Windows 10 sürüm 1607 (yapı 14393) veya üstü**, 64-bit.
</details>

<details>
<summary><b>Kurulum sihirbazı yok mu?</b></summary>

Yok, gerekmiyor. `DrvNest.exe` tek dosyadır: indirin, istediğiniz yere koyun, çift tıklayın.
Kaldırmak için dosyayı silin ve isterseniz `%ProgramData%\DrvNest` klasörünü kaldırın.
</details>

<details>
<summary><b>Windows Server'da çalışır mı?</b></summary>

Büyük ölçüde evet. Aygıt taraması, Windows Update sorguları, yerel INF kurulumu, yedekleme
ve geri yükleme normal çalışır.

**Sistem Geri Yükleme Server SKU'larında bulunmaz.** DrvNest bunu bir hata olarak görmez:
geri yükleme noktası oluşturma denemesi başarısız olur, günlüğe bir uyarı yazılır ve kuyruk
devam eder. Bu durumda **Güncellemeden önce mevcut sürücüyü yedekle** ayarını açık tutmanız
daha da önemlidir; geri dönüş yolunuz o olur.

Windows 10 1607'den eski sürümler ve 32-bit Windows desteklenmez.
</details>

### Güvenlik ve yetkiler

<details>
<summary><b>Neden yönetici yetkisi istiyor?</b></summary>

Sürücü kurmak ayrıcalıklı bir işlemdir. DrvNest'in kullandığı üç mekanizmanın **üçü de**
yükseltilmiş bir belirteç ister:

- `pnputil` ile sürücü deposuna paket eklemek/dışa aktarmak,
- Windows Update kurucusunu çalıştırmak,
- `srclient.dll` üzerinden sistem geri yükleme noktası oluşturmak.

Ayrıca yeniden başlatma sonrası devam için zamanlanmış görev (`HKLM` altında) kaydedilir.

Uygulama bunu bildirimde `requireAdministrator` olarak baştan ister. Alternatifi, kuyruğun
ortasında "erişim reddedildi" ile durmaktı; bu daha dürüst.
</details>

<details>
<summary><b>Antivirüs / SmartScreen uyarı veriyor. Güvenli mi?</b></summary>

Bu beklenen bir durum ve sebebi teknik:

- `DrvNest.exe` **kod imzalama sertifikasıyla imzalanmamıştır** (sertifika ücretlidir).
  Windows SmartScreen imzasız ve henüz "itibar" kazanmamış her exe için
  *"Windows bilgisayarınızı korudu"* ekranını gösterir.
- Dosya **tek dosya, kendi kendine yeten** bir yayındır: çalışırken içindeki yerel
  kütüphaneleri geçici klasöre açar. Bazı sezgisel antivirüs motorları bu davranışı
  şüpheli sayar.
- Uygulama **yönetici olarak çalışır, sürücü kurar ve zamanlanmış görev oluşturur** —
  yani gerçekten de kötü amaçlı yazılımların yaptığı işleri yapar. Sezgisel bir tarayıcının
  ikisini ayırt etmesi zordur.

SmartScreen'i geçmek için: *Daha fazla bilgi* → *Yine de çalıştır*.

**Smart App Control (Akıllı Uygulama Denetimi) hakkında.** Windows 11'in temiz
kurulumlarında varsayılan olarak açık olan bu özellik SmartScreen'den daha katıdır ve
imzasız uygulamaları uyarmak yerine doğrudan **engeller**
(`0x800711C7 — Uygulama Denetimi ilkesi bu dosyayı engelledi`). Bu, format sonrası
senaryosunda karşınıza çıkabilecek gerçek bir durumdur.

DrvNest'in **tek dosya** olarak yayımlanmasının bir sebebi de budur: birden çok gevşek
`.dll` dosyası içeren bir yayın Smart App Control tarafından engellenir, tek dosyalı
sürüm ise çalışır (bu davranış Windows 11 25H2 üzerinde test edilerek doğrulanmıştır).
Yine de engellenirse:

- *Ayarlar → Gizlilik ve güvenlik → Windows Güvenliği → Uygulama ve tarayıcı denetimi →
  Akıllı Uygulama Denetimi ayarları* yolundan kapatabilirsiniz. **Not:** Smart App Control
  bir kez kapatıldığında, Windows'u yeniden kurmadan tekrar açılamaz.
- Kalıcı çözüm kod imzalama sertifikasıdır. Seçenekler, maliyetleri ve CI kurulumu
  [docs/SIGNING.md](SIGNING.md) dosyasında ayrıntılı olarak anlatılıyor.

**İndirdiğiniz dosyanın gerçekten yayımlanan dosya olduğunu doğrulayabilirsiniz.**
Her sürümle birlikte bir `checksums.txt` yayımlanır. PowerShell'de:

```powershell
Get-FileHash .\DrvNest.exe -Algorithm SHA256
```

Çıkan değeri sürüm sayfasındaki `checksums.txt` içindeki `DrvNest.exe` satırıyla
karşılaştırın. Tutuyorsa dosya birebir yayımlanan dosyadır.

Aynı doğrulamayı uygulamanın kendi güncelleyicisi de otomatik yapar ve tutmazsa kurulumu
reddeder.

Yine de emin olmak isterseniz kaynak kod açıktır ve [kendiniz derleyebilirsiniz](BUILD.md).
</details>

<details>
<summary><b>Verilerim nereye gidiyor?</b></summary>

Hiçbir yere. Telemetri, kullanım istatistiği, cihaz kimliği toplanmaz ve gönderilmez.

Makineden dışarı çıkan yalnızca iki trafik vardır:

1. **Windows Update sorguları** — Windows'un kendi bileşeni üzerinden doğrudan Microsoft'a.
   Çevrimdışı modda veya `--rescue` ile hiç yapılmaz.
2. **GitHub Releases API'sine tek bir istek** — yalnızca siz *Güncellemeleri Kontrol Et*'e
   bastığınızda.

Tüm kayıtlar (`settings.json`, `session.json`, `history.jsonl`, günlükler, yedekler)
`%ProgramData%\DrvNest` altında, sizin makinenizde kalır.
</details>

### Kullanım

<details>
<summary><b>Neden kurulumlar aynı anda yapılmıyor?</b></summary>

Çünkü Windows buna izin vermiyor.

- Windows Update, bir kurulum sürerken ikincisi başlatıldığında
  `WU_E_OPERATIONINPROGRESS` (0x80240016) hatası döner.
- PnP alt sistemi `pnputil` çağrılarını zaten kendi içinde sıraya sokar.

DrvNest bunu tek bir global kilitle dürüstçe modeller: aynı anda en fazla bir kurulum.
Aksini yapmak daha güzel görünen bir ilerleme çubuğu ve bir yığın sahte hata üretirdi.

**İndirmeler ise gerçekten paraleldir** (varsayılan 3, ayarlanabilir 1–8), çünkü indirme
ağ işidir ve üst üste binmesi işe yarar.
</details>

<details>
<summary><b>İnternet yokken ne yapmalıyım?</b></summary>

Bu, DrvNest'in asıl tasarlandığı senaryo.

**En iyisi — formattan önce hazırlanmak:**

1. Format öncesi **Yedekle & Geri Yükle → Yedek Oluştur**.
2. Yedeği ve `DrvNest.exe`'yi aynı USB belleğe koyun.
3. Format sonrası `DrvNest.exe --rescue` ile açın ve **Geri Yükle**'ye basın.

`DrvNest.exe` ile aynı klasördeki **`Drivers`** adlı klasör otomatik olarak yerel sürücü
havuzu sayılır — ayar yapmanız gerekmez.

**Hazırlanamadıysanız:**

1. **Genel Bakış → Donanım Raporu** ile tüm donanım kimliklerini metin dosyasına yazın.
2. Dosyayı USB ile çalışan bir bilgisayara taşıyın.
3. Kimliklerle üreticinin sitesinden doğru sürücüleri indirin, arşivleri açın.
4. Klasörleri USB ile geri getirin ve **Klasörden Geri Yükle** ile kurun (ya da klasörü
   Ayarlar'dan yerel sürücü klasörü olarak ekleyip **Tara**'ya basın).

Öncelik ağ kartı sürücüsündedir; o çalıştığı anda gerisini Windows Update halleder.
</details>

<details>
<summary><b>Sürücü bozulursa geri alabilir miyim?</b></summary>

Evet, üç ayrı yol var:

1. **DrvNest'in aldığı yedekten.** *Güncellemeden önce mevcut sürücüyü yedekle* ayarı açıksa
   (varsayılan), değiştirilen paket kurulumdan hemen önce dışa aktarılır ve yolu geçmiş
   kaydına yazılır. **Geçmiş** ekranından yedek klasörünü açın, sonra
   **Yedekle & Geri Yükle → Klasörden Geri Yükle** ile kurun.
2. **Sistem geri yükleme noktasından.** Oturumdaki ilk kurulumdan önce bir tane oluşturulur.
   Windows'un *Sistem Geri Yükleme* sihirbazıyla (`rstrui.exe`) kurulum öncesine dönün.
3. **Aygıt Yöneticisi'nden.** Aygıt → Özellikler → Sürücü → *Sürücüyü Geri Al*. Windows'un
   kendi mekanizmasıdır ve DrvNest'ten bağımsız çalışır.

Bu yüzden geri yükleme noktası ayarını kapatmamanız önerilir.
</details>

<details>
<summary><b>Yeniden başlatma sonrası gerçekten devam ediyor mu?</b></summary>

Evet. Kuyruğun durumu her değişiklikte `session.json`'a atomik olarak yazılır ve yeniden
başlatmadan önce zorla diske aktarılır. DrvNest ayrıca oturum açılışına bağlı bir
zamanlanmış görev (`DrvNest\ResumeSession`, en yüksek yetkiyle) kaydeder; Görev Zamanlayıcı
kullanılamıyorsa `HKLM\...\RunOnce` yedeğe geçer.

Makine açıldığında görev DrvNest'i `--resume` ile başlatır ve kalan işler devam eder.

Sınırlar: bir oturum en fazla **10 yeniden başlatma** taşır, sonrasında güvenlik gereği
bırakılır. Oturum dosyası başka bir makinede oluşturulmuşsa yok sayılır. Kuyruk bittiğinde
zamanlanmış görev ve `RunOnce` kaydı **silinir**.
</details>

<details>
<summary><b>Uygulamayı elle açtım, yarım kalan kuyruk kendiliğinden başlamadı.</b></summary>

Bu kasıtlı. Yarıda kalmış bir oturum **İşlemler** ekranında bir **Devam Et** butonuyla
gösterilir ama otomatik başlamaz. Kuyruk yalnızca zamanlanmış görev `--resume` bayrağıyla
başlattığında kendiliğinden devam eder.

Pencere açılır açılmaz sessizce sürücü kurmaya başlamak savunulabilir bir varsayılan değil.
</details>

<details>
<summary><b>Bazı aygıtlar için güncelleme çıkmıyor.</b></summary>

Muhtemel sebepler:

- **Zaten günceldir.** Windows Update kurulu olandan eski bir sürüm önerdiğinde DrvNest o
  adayı otomatik eler; bilerek sürüm düşürmez.
- **Windows Update o aygıt için bir şey sunmuyordur.** Ekran kartı ve yonga seti
  sürücülerinin en yenisi çoğu zaman yalnızca üreticinin sitesindedir. Bu paketleri
  indirip **Klasörden Geri Yükle** ile kurabilirsiniz.
- **Gizlemiş veya yoksaymış olabilirsiniz.** Ayarlar → **Varsayılanlara Dön** listeleri
  temizler.
- **İsteğe bağlı güncellemeler kapalı olabilir.** Ayarlar → *İsteğe bağlı sürücü
  güncellemelerini de göster*.
</details>

<details>
<summary><b>Otomatik güncelleme nasıl çalışıyor?</b></summary>

**Hakkında** menüsünden, her zaman sizin tıklamanızla:

1. *Güncellemeleri Kontrol Et* → GitHub Releases API'sine bir istek; sürüm numaraları
   karşılaştırılır.
2. *İndir ve Kur* → mimarinize uygun dosya indirilir (`DrvNest.exe` veya
   `DrvNest-arm64.exe`).
3. Dosyanın **SHA-256** özeti sürümün `checksums.txt` dosyasıyla karşılaştırılır.
   **Sağlama toplamı yoksa veya tutmuyorsa dosya silinir ve kurulum reddedilir.**
4. Doğrulama geçerse çalışan exe `.old` uzantısıyla yeniden adlandırılır (Windows çalışan
   bir exe'nin adını değiştirmeye izin verir ama üzerine yazmaya izin vermez), yenisi yerine
   kopyalanır ve uygulama yeniden başlatılır. Artık dosya bir sonraki açılışta silinir.

Arka planda sessizce güncelleme **yapılmaz**. Kuyruk çalışırken güncelleme **yapılamaz**.
Kopyalama başarısız olursa orijinal exe geri konur.
</details>

### Teknik detaylar

<details>
<summary><b>Exe neden ~70 MB?</b></summary>

Çünkü içinde **.NET 8 çalışma zamanının ve WPF'in tamamı** var. "Hiçbir şey kurulu olmayan
bir makinede çalışsın" hedefinin bedeli budur.

Sıkıştırma açık olduğu için dosya ~65 MB civarındadır; kapalı olsaydı ~150 MB olurdu.

Alternatif, "framework-dependent" bir yayın olurdu: ~2 MB'lık bir exe artı makineye ayrıca
kurulması gereken bir .NET çalışma zamanı. Formattan yeni çıkmış, interneti olmayan bir
makinede bu tam olarak işe yaramaz.
</details>

<details>
<summary><b>Neden WMI kullanılmıyor?</b></summary>

Üç sebep:

1. **Hedef makine yeni formatlanmıştır.** WMI deposunun hâlâ oluşuyor ya da bozuk olması
   en çok orada olasıdır, üstelik `Win32_PnPEntity` sorguları yavaştır.
2. **SetupAPI daha fazla bilgi verir.** Çok değerli donanım/uyumluluk kimlikleri, sürücü
   anahtarı ve canlı Configuration Manager sorun kodu — sürücü eşleştirmesi için gereken
   tam olarak bunlar.
3. **Servis bağımlılığı yoktur.** SetupAPI, CfgMgr32 ve kayıt defteri her Windows'ta bulunan
   DLL'lerdir ve çalışan bir servise ihtiyaç duymazlar.

Aynı mantıkla sistem geri yükleme noktası da WMI yerine `srclient.dll` üzerinden
oluşturulur.
</details>

<details>
<summary><b>Uygulamayı kaldırınca geride ne kalıyor?</b></summary>

`DrvNest.exe` dosyasını silin. Geriye kalanlar:

- `%ProgramData%\DrvNest` klasörü (ayarlar, geçmiş, günlükler, yedekler) — elle silin.
- Kayıt defterinde **hiçbir şey kalmaz**: zamanlanmış görev ve `RunOnce` kaydı, kuyruk
  tamamlandığında uygulama tarafından zaten silinir.

Kuyruk yarıda kalmışken uygulamayı sildiyseniz, zamanlanmış görevi elle kaldırabilirsiniz:

```powershell
schtasks /Delete /TN "DrvNest\ResumeSession" /F
```
</details>

---

## English

### Installation and requirements

<details open>
<summary><b>Do I need to install .NET?</b></summary>

**No.** The entire .NET 8 runtime is inside `DrvNest.exe` (self-contained, single-file
publish). It runs on a machine with no .NET at all.

**No Visual C++ Redistributable either**: WPF's runtime pack carries its own private
`vcruntime140_cor3.dll` and `msvcp140_cor3.dll`.

The only requirement is **Windows 10 version 1607 (build 14393) or newer**, 64-bit.
</details>

<details>
<summary><b>Is there an installer?</b></summary>

No, and none is needed. `DrvNest.exe` is one file: download it, put it wherever you like,
double-click. To remove it, delete the file and optionally the `%ProgramData%\DrvNest`
folder.
</details>

<details>
<summary><b>Does it work on Windows Server?</b></summary>

Mostly yes. Device scanning, Windows Update queries, local INF installs, backup and restore
all work normally.

**System Restore does not exist on Server SKUs.** DrvNest does not treat that as an error:
the restore-point attempt fails, a warning goes to the log, and the queue continues. On
Server it is therefore even more important to leave **Back up the current driver before
updating** switched on — that is your way back.

Windows releases older than 10 1607, and 32-bit Windows, are not supported.
</details>

### Security and privileges

<details>
<summary><b>Why does it require administrator rights?</b></summary>

Installing a driver is a privileged operation. All three mechanisms DrvNest uses need an
elevated token:

- adding to and exporting from the driver store with `pnputil`,
- driving the Windows Update installer,
- creating a system restore point through `srclient.dll`.

It also registers a scheduled task (under `HKLM`) so the queue can resume after a restart.

The application manifest requests `requireAdministrator` up front. The alternative was
failing with "access denied" in the middle of a queue; asking honestly is better.
</details>

<details>
<summary><b>My antivirus / SmartScreen warns about it. Is it safe?</b></summary>

Expected, and the reasons are technical:

- `DrvNest.exe` is **not code-signed** (certificates cost money). SmartScreen shows its
  *"Windows protected your PC"* screen for any unsigned executable that has not yet built
  up reputation.
- It is a **single-file self-contained** publish, so it extracts its native libraries to a
  temporary folder at startup. Some heuristic engines find that suspicious.
- It **runs as administrator, installs drivers and creates a scheduled task** — genuinely
  the same set of actions malware performs. A heuristic scanner has a hard time telling
  them apart.

To get past SmartScreen: *More info* → *Run anyway*.

**About Smart App Control.** On clean Windows 11 installations this is on by default, and
it is stricter than SmartScreen: instead of warning, it **blocks** unsigned applications
outright (`0x800711C7 — this file was blocked by an Application Control policy`). That is
a real possibility in exactly the post-format scenario DrvNest targets.

It is also one of the reasons DrvNest ships as a **single file**: a publish made of many
loose `.dll` files gets blocked by Smart App Control, while the single-file build runs
(verified on Windows 11 25H2). If it is still blocked:

- Turn it off under *Settings → Privacy & security → Windows Security → App & browser
  control → Smart App Control settings*. **Note:** once switched off, Smart App Control
  cannot be turned back on without reinstalling Windows.
- The permanent fix is code signing. The options, what they cost and how to wire one
  into CI are laid out in [docs/SIGNING.md](SIGNING.md).

**You can verify that your download is exactly the published file.** Every release ships a
`checksums.txt`. In PowerShell:

```powershell
Get-FileHash .\DrvNest.exe -Algorithm SHA256
```

Compare the result with the `DrvNest.exe` line in the release's `checksums.txt`. A match
means the file is byte-for-byte the published one.

The built-in updater performs the same check automatically and refuses to install anything
that fails it.

If you would rather not trust a binary at all, the source is open and you can
[build it yourself](BUILD.md).
</details>

<details>
<summary><b>Where does my data go?</b></summary>

Nowhere. No telemetry, no usage statistics, no device identifiers are collected or sent.

Exactly two things leave the machine:

1. **Windows Update queries** — straight to Microsoft through Windows' own agent. Never in
   offline mode or with `--rescue`.
2. **One request to the GitHub Releases API** — only when you press *Check for updates*.

Everything else (`settings.json`, `session.json`, `history.jsonl`, logs, backups) stays in
`%ProgramData%\DrvNest` on your machine.
</details>

### Usage

<details>
<summary><b>Why aren't installs done in parallel?</b></summary>

Because Windows does not allow it.

- Windows Update returns `WU_E_OPERATIONINPROGRESS` (0x80240016) when a second installation
  starts while one is running.
- The PnP subsystem serializes `pnputil` calls internally regardless.

DrvNest models that honestly with a single global lock: at most one install at a time.
Doing otherwise would produce a prettier progress bar and a pile of spurious failures.

**Downloads really are parallel** (3 by default, configurable 1–8), because downloading is
network-bound and genuinely benefits from overlapping.
</details>

<details>
<summary><b>What do I do without an internet connection?</b></summary>

This is the scenario DrvNest was built for.

**Best case — prepare before the format:**

1. Before formatting: **Backup & Restore → Create backup**.
2. Copy the backup and `DrvNest.exe` onto the same USB stick.
3. After the format, run `DrvNest.exe --rescue` and press **Restore**.

A folder named **`Drivers`** next to `DrvNest.exe` is automatically registered as a local
driver repository — no configuration required.

**If you did not prepare:**

1. **Dashboard → Hardware report** writes every hardware id to a text file.
2. Carry it on a USB stick to a working computer.
3. Look the ids up, download the right drivers from the vendor, extract them.
4. Bring the folders back and use **Restore from folder** (or add the folder in Settings as
   a local driver folder and press **Scan**).

Start with the network adapter; once that works, Windows Update handles the rest.
</details>

<details>
<summary><b>Can I roll a driver back if it breaks something?</b></summary>

Yes, three ways:

1. **From DrvNest's own backup.** With *Back up the current driver before updating* on (the
   default), the package being replaced is exported immediately before the install and its
   path is stored in the history record. Open the backup folder from **History**, then use
   **Backup & Restore → Restore from folder**.
2. **From the system restore point.** One is created before the first install of a session.
   Use Windows' *System Restore* wizard (`rstrui.exe`).
3. **From Device Manager.** Device → Properties → Driver → *Roll Back Driver*. This is
   Windows' own mechanism and works independently of DrvNest.

Which is why leaving the restore-point setting on is recommended.
</details>

<details>
<summary><b>Does it really resume after a reboot?</b></summary>

Yes. Queue state is written atomically to `session.json` on every change and force-flushed
before a restart is scheduled. DrvNest registers a logon-triggered scheduled task
(`DrvNest\ResumeSession`, highest privileges), falling back to `HKLM\...\RunOnce` when Task
Scheduler is unavailable.

At logon the task starts DrvNest with `--resume` and the remaining jobs continue.

Limits: a session survives at most **10 restarts** before it is abandoned as a safety valve,
and a session file created on a different machine is ignored. When the queue finishes, the
scheduled task and the `RunOnce` value are **removed**.
</details>

<details>
<summary><b>I launched it manually and the interrupted queue did not start by itself.</b></summary>

That is deliberate. An interrupted session is shown on the **Activity** page with a
**Continue** button but does not start on its own. It only resumes automatically when the
process was launched by the resume task with `--resume`.

Silently installing drivers the moment a window opens is not a defensible default.
</details>

<details>
<summary><b>Some devices never get an update offered.</b></summary>

Likely reasons:

- **It is already current.** When Windows Update offers a version older than the installed
  one, DrvNest drops that candidate; it never downgrades on purpose.
- **Windows Update simply has nothing for that device.** The newest GPU and chipset drivers
  often exist only on the vendor's own site. Download them and install with **Restore from
  folder**.
- **You hid or ignored it.** Settings → **Reset to defaults** clears both lists.
- **Optional updates are switched off.** Settings → *Show optional driver updates too*.
</details>

<details>
<summary><b>How does the built-in updater work?</b></summary>

From the **About** page, always on your click:

1. *Check for updates* → one request to the GitHub Releases API; versions are compared.
2. *Download and install* → the file matching your architecture is fetched (`DrvNest.exe`
   or `DrvNest-arm64.exe`).
3. Its **SHA-256** is compared against the release's `checksums.txt`. **A missing or
   mismatching checksum means the file is deleted and the update refused.**
4. On success the running executable is renamed with a `.old` suffix (Windows allows a
   running exe to be renamed but not overwritten), the new one is copied into its place and
   the app relaunches. The leftover is deleted at the next start.

There is no silent background update. Updating is blocked while the queue is running. If
the copy fails, the original executable is put back.
</details>

### Technical

<details>
<summary><b>Why is the exe ~70 MB?</b></summary>

Because it contains **the whole .NET 8 runtime and WPF**. That is the price of "runs on a
machine with nothing installed".

With single-file compression enabled it lands around 65 MB; without it, roughly 150 MB.

The alternative would be a framework-dependent publish: a ~2 MB executable plus a .NET
runtime that has to be installed separately. On a freshly formatted machine with no
internet, that is precisely useless.
</details>

<details>
<summary><b>Why no WMI?</b></summary>

Three reasons:

1. **The target machine was just formatted.** That is exactly where a WMI repository is most
   likely to still be building or subtly broken, and `Win32_PnPEntity` queries are slow on
   top of it.
2. **SetupAPI carries more information.** Multi-valued hardware and compatible ids, the
   driver key, and the live Configuration Manager problem code — precisely what driver
   matching needs.
3. **No service dependency.** SetupAPI, CfgMgr32 and the registry are DLLs present on every
   Windows install and need nothing running.

The same reasoning applies to restore points, created through `srclient.dll` rather than the
WMI `SystemRestore` class.
</details>

<details>
<summary><b>What is left behind if I remove it?</b></summary>

Delete `DrvNest.exe`. What remains:

- The `%ProgramData%\DrvNest` folder (settings, history, logs, backups) — delete it manually.
- **Nothing in the registry**: the scheduled task and the `RunOnce` value are removed by the
  application as soon as a queue finishes.

If you deleted the application while a queue was still pending, remove the task by hand:

```powershell
schtasks /Delete /TN "DrvNest\ResumeSession" /F
```
</details>
