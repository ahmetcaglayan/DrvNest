# Kullanım Rehberi

DrvNest'in dokuz menüsü, her butonun ne yaptığı ve tipik akışlar.

---

## İçindekiler

1. [İlk çalıştırma](#1-ilk-çalıştırma)
2. [Genel Bakış](#2-genel-bakış)
3. [Aygıtlar](#3-aygıtlar)
4. [Güncellemeler](#4-güncellemeler)
5. [İşlemler — kuyruğu izlemek](#5-işlemler--kuyruğu-izlemek)
6. [Yeniden başlatma akışı](#6-yeniden-başlatma-akışı)
7. [Yedekle & Geri Yükle](#7-yedekle--geri-yükle)
8. [Çevrimdışı / kurtarma modu](#8-çevrimdışı--kurtarma-modu)
9. [Geçmiş ve CSV dışa aktarma](#9-geçmiş-ve-csv-dışa-aktarma)
10. [Günlük — hata bildirirken](#10-günlük--hata-bildirirken)
11. [Ayarların anlamı](#11-ayarların-anlamı)
12. [Hakkında ve kendi kendini güncelleme](#12-hakkında-ve-kendi-kendini-güncelleme)
13. [Komut satırı bayrakları](#13-komut-satırı-bayrakları)
14. [Dosyalar nerede?](#14-dosyalar-nerede)

---

## 1. İlk çalıştırma

`DrvNest.exe`'yi çift tıklayın. Windows bir **yönetici onayı (UAC)** penceresi gösterir;
onaylamanız gerekir. Sürücü kurmak ayrıcalıklı bir işlemdir ve uygulama bunu baştan ister —
kuyruğun ortasında yetki hatası almaktansa dürüst olmayı tercih eder.

Onay vermezseniz uygulama açılmaz. Yükseltilmemiş bir şekilde çalıştığı istisnai bir
durumda durum çubuğunda *"Yönetici değil"* yazar ve kurulum denemeleri başarısız olur.

Açılışta:

- Uygulama **Genel Bakış** ekranına gelir.
- **Açılışta otomatik tara** ayarı açıksa (varsayılan: açık) tarama hemen başlar.
- Önceki bir oturum yarıda kalmışsa doğrudan **İşlemler** ekranına gidilir ve
  bir **Devam Et** butonu gösterilir.

> Aynı anda yalnızca **tek bir DrvNest** çalışabilir. İkinci bir kopya açmaya çalışırsanız
> "DrvNest zaten çalışıyor" uyarısı alırsınız. Bu bilinçli: iki kopyanın aynı anda sürücü
> kurması gerçekten zarar verici bir senaryodur.

---

## 2. Genel Bakış

Uygulamanın ana ekranı. Dört sayaç:

| Sayaç | Anlamı |
| --- | --- |
| **Aygıt** | Sistemde fiziksel olarak bulunan toplam PnP aygıt sayısı |
| **Sürücüsü Eksik** | Hiç sürücüsü olmayan aygıtlar için bulunan kurulum adayı sayısı |
| **Güncelleme** | Sürücüsü olan ama daha yenisi bulunan aygıt sayısı |
| **Sorunlu Aygıt** | Sürücüsü eksik veya hata durumundaki aygıtların toplamı |

Altında sistem özeti (işletim sistemi, makine, işlemci, mimari, BIOS) yer alır.

### Hızlı İşlemler

| Buton | Ne yapar |
| --- | --- |
| **Şimdi Tara** | Aygıt taramasını ve tüm kaynak sorgularını yeniden çalıştırır. |
| **Format Sonrası Kurtarma** | *Yalnızca eksik olan* sürücüleri sıraya alır ve kurmaya başlar. Formattan sonraki tek tıklık akış budur. Eksik sürücü yoksa buton pasiftir. |
| **Tümünü Güncelle** | Listedeki tüm adayları (eksikler + güncellemeler) sıraya alır. |
| **Sürücüleri Yedekle** | *Yedekle & Geri Yükle* menüsüne gider. |
| **Donanım Raporu** | Tüm aygıtları ve donanım kimliklerini bir metin dosyasına yazar ve dosyayı Gezgin'de gösterir. |

### Uyarı şeridi

Ekranın üstünde kırmızı/sarı bir şerit çıkabilir. En önemlisi:

> **"Çalışan bir ağ sürücüsü yok. Windows Update'e ulaşılamaz — yerel klasör veya yedek kullanın."**

Bu mesajı görüyorsanız internet üzerinden hiçbir şey indiremezsiniz.
[Çevrimdışı / kurtarma modu](#8-çevrimdışı--kurtarma-modu) bölümüne bakın.

---

## 3. Aygıtlar

Sistemdeki tüm PnP aygıtlarının, **aygıt sınıfına göre gruplanmış** tam listesi.
Her satırda ad, sınıf, üretici, kurulu sürücünün sağlayıcısı ve sürümü görünür.

### Filtreler

| Filtre | Gösterdiği |
| --- | --- |
| **Tümü** | Her şey |
| **Sorunlu** | Sürücüsü eksik olan ve hata durumundaki aygıtlar |
| **Sürücüsüz** | Hiç sürücüsü olmayan aygıtlar (Aygıt Yöneticisi'ndeki sarı ünlemler) |
| **Jenerik Sürücü** | Microsoft'un kutu içi sürücüsüyle çalışan aygıtlar — genellikle üretici sürücüsü daha iyidir |

**Arama kutusu** ada, sınıfa, üreticiye, sürücü sağlayıcısına, sürüme ve donanım kimliğine
göre süzer.

**Donanım kimliğini kopyala:** bir satırın donanım kimliğini panoya alır. İnterneti olmayan
bir makinede bu kimliği not edip başka bir bilgisayarda arattığınızda doğru sürücüyü
bulursunuz.

---

## 4. Güncellemeler

Kurulabilecek paketlerin listesi. Her satırda:

- **EKSİK** veya **GÜNCELLEME** rozeti,
- aygıt adı ve sınıfı, üretici,
- sürüm geçişi (`31.0.15.3623` ya da `31.0.15.3600 → 31.0.15.3623`),
- indirme boyutu,
- **Kaynak**: *Windows Update* veya *Yerel klasör*.

### Butonlar

| Buton | Ne yapar |
| --- | --- |
| **Tümünü Seç / Seçimi Temizle** | Toplu seçim |
| **Seçilenleri Kur** | Seçili paketleri kuyruğa alır ve İşlemler ekranına geçer |

Alt kısımda *"N seçili · toplam boyut"* özeti bulunur.

### Satır menüsü

| Seçenek | Ne yapar |
| --- | --- |
| **Bu güncellemeyi gizle** | Sadece o paketi bir daha listelemez (`HiddenUpdateIds`) |
| **Bu aygıtı yoksay** | Aygıtın donanım kimliklerini yoksayma listesine ekler; o aygıt için bir daha öneri gelmez (`IgnoredHardwareIds`) |

Her ikisi de ayarlara kaydedilir. Fikrinizi değiştirirseniz **Ayarlar → Varsayılanlara Dön**
listeleri temizler.

> **Neden bazı aygıtlar listede yok?** Windows Update kurulu olandan **daha eski** bir sürüm
> önerdiğinde o aday otomatik olarak elenir; DrvNest bilerek sürüm düşürmez.

---

## 5. İşlemler — kuyruğu izlemek

Çalışan kuyruğun canlı görünümü. Üstte toplam ilerleme ve `tamamlanan/toplam` sayacı,
altta her iş için ayrı bir satır.

Her satırda görülenler:

- **Durum:** Sırada → İndiriliyor → İndirildi → Kuruluyor → Tamamlandı / Başarısız
- **İndirme yüzdesi**, aktarılan/toplam bayt ve anlık **hız**
- **Kurulum aşaması** ayrı gösterilir

> Toplam ilerleme ağırlıklı hesaplanır: indirme %60, kurulum %40.

### İndirmeler neden paralel, kurulumlar neden sırayla?

Ekranda birkaç iş aynı anda inerken kurulumların tek tek yapıldığını göreceksiniz.
Bu bir eksiklik değil:

- **İndirme ağ işidir**, üst üste binmesi gerçekten hızlandırır. Varsayılan olarak
  3 iş aynı anda iner (Ayarlar'dan 1–8 arası değiştirilebilir).
- **Kurulum işletim sistemi işidir.** Windows Update ikinci bir kuruluma
  `WU_E_OPERATIONINPROGRESS` hatası döner ve PnP alt sistemi `pnputil` çağrılarını
  zaten sıraya sokar. Aynı anda kurmaya çalışmak hiçbir şeyi hızlandırmaz; sadece
  sahte hata mesajları üretir.

Kurulum sırasında Windows'un kendisi başka bir güncelleme kuruyorsa DrvNest
*"Windows Update'in boşalması bekleniyor"* diyerek 10 saniye aralıklarla 6 kez tekrar dener.

### Butonlar

| Buton | Ne yapar |
| --- | --- |
| **Tümünü İptal Et** | Kuyruğu durdurur. İndirmeler kesilir; **başlamış bir kurulum tamamlanır** — yarıda kesmek aygıtı daha kötü bir durumda bırakır. |
| **Başarısızları Tekrar Dene** | Başarısız ve iptal edilmiş işleri yeni bir kuyruk olarak yeniden başlatır. |
| **Devam Et** | Yalnızca yarıda kalmış bir oturum varken görünür. |
| **Şimdi Yeniden Başlat / Daha Sonra** | Yeniden başlatma gerektiğinde çıkar. |

Başarısız bir iş zaten otomatik olarak tekrar denenmiştir (varsayılan 2 kez, aralarda
3 saniye bekleyerek). Buton bundan sonraki manuel denemeler içindir.

---

## 6. Yeniden başlatma akışı

Bazı sürücüler etkinleşmek için yeniden başlatma ister. Akış şöyledir:

1. Kuyruk biter veya bir iş "önce yeniden başlatma gerekiyor" der.
2. DrvNest tüm kuyruk durumunu `session.json`'a yazar ve
   oturum açılışına bağlı bir **zamanlanmış görev** kaydeder
   (`DrvNest\ResumeSession`, en yüksek yetkiyle, `DrvNest.exe --resume` komutuyla).
   Görev oluşturulamazsa `RunOnce` kayıt defteri anahtarı yedek olarak kullanılır.
3. **Şimdi Yeniden Başlat**'a basarsanız Windows'un standart geri sayımlı yeniden başlatma
   uyarısı çıkar (varsayılan 60 saniye). Fikrinizi değiştirirseniz **Daha Sonra** ile
   iptal edebilirsiniz.
4. Makine açıldığında DrvNest kendiliğinden gelir ve kuyruğun kalanını tamamlar.

Notlar:

- **Otomatik yeniden başlatma varsayılan olarak kapalıdır.** Ayarlar'dan açabilirsiniz;
  o zaman kuyruk bittiğinde makine kendiliğinden yeniden başlar.
- Uygulamayı elle açtığınızda yarıda kalmış oturum **kendiliğinden devam etmez** —
  size **Devam Et** butonu gösterilir. Pencere açılır açılmaz sessizce sürücü kurmak
  savunulabilir bir varsayılan değildir.
- Bir oturum en fazla **10 yeniden başlatma** taşır. Bu sınırı aşarsa oturum bırakılır;
  sonsuz döngüye karşı bir emniyet valfidir.
- Kuyrukta iş kalmadığında zamanlanmış görev ve `RunOnce` kaydı **silinir**. DrvNest
  makinede kalıcı bir iz bırakmaz.

---

## 7. Yedekle & Geri Yükle

### Yedek Oluştur

Sistemdeki **tüm üçüncü parti sürücü paketlerini** dışa aktarır
(`pnputil /export-driver * <klasör>`). Microsoft'un kutu içi sürücüleri bilinçli olarak
dışarıda bırakılır — Windows onları zaten kendisi kurar.

- **ZIP olarak sıkıştır** kutusunu işaretlerseniz sonuç tek bir `.zip` dosyası olur.
- Yedeğin içine `drvnest-backup.json` adında bir künye yazılır: makine adı, üretici/model,
  Windows sürümü, mimari, paket sayısı, toplam boyut ve paketlerin listesi.
- Varsayılan konum: `%ProgramData%\DrvNest\backups\`. Ayarlar'dan değiştirilebilir.

### Mevcut Yedekler

Yedek kökündeki **ve ayarlardaki yerel sürücü klasörlerindeki** tüm yedekler listelenir.
Her satırda ad, paket sayısı, boyut ve tarih görünür.

| Buton | Ne yapar |
| --- | --- |
| **Geri Yükle** | Seçili yedekteki tüm paketleri kurar ve aygıtları yeniden tarar |
| **Klasörden Geri Yükle** | Diskte istediğiniz bir klasörü seçip kurar |
| **Aç** | Yedeği Gezgin'de gösterir |
| **Sil** | Yedeği kalıcı olarak siler |
| **Yenile** | Listeyi yeniden okur |

> **Klasörden Geri Yükle** yalnızca DrvNest yedekleri için değildir. Üreticinin sitesinden
> indirip açtığınız herhangi bir sürücü klasörünü de kurabilirsiniz; alt klasörler dahil
> tüm `.inf` dosyaları taranır.

### Bir sürücüyü geri almak

**Güncellemeden önce mevcut sürücüyü yedekle** ayarı açıkken (varsayılan: açık), her
güncellemeden hemen önce değiştirilecek paket dışa aktarılır ve klasör yolu geçmiş
kaydına yazılır.

Bir sürücü sorun çıkarırsa:

1. **Geçmiş** menüsünde ilgili kaydı bulun ve yedek klasörünü açın.
2. **Yedekle & Geri Yükle → Klasörden Geri Yükle** ile o klasörü seçin.

Daha ağır durumlar için **sistem geri yükleme noktası** vardır: Windows'un kendi
*Sistem Geri Yükleme* sihirbazından (`rstrui.exe`) kurulum öncesine dönebilirsiniz.

---

## 8. Çevrimdışı / kurtarma modu

Ağ kartının sürücüsü yoksa internet de yoktur. Kurtarma modu tam bu durum içindir.

### Hazırlık (format öncesi)

1. **Yedek Oluştur** ile sürücüleri dışa aktarın.
2. Bir USB belleğe şunları koyun:

```
USB:\
├── DrvNest.exe
└── Drivers\          ← yedek klasörünün içeriği
```

`DrvNest.exe` ile aynı dizindeki **`Drivers`** klasörü, uygulama tarafından
**otomatik olarak** yerel sürücü havuzu olarak kaydedilir. Hiçbir ayar yapmanız gerekmez.

### Kullanım (format sonrası)

```powershell
DrvNest.exe --rescue
```

`--rescue` (eş anlamlısı `--offline`) modunda Windows Update **hiç aranmaz**. Tarama
yalnızca aygıtları ve yerel klasörleri kullanır, böylece internet olmayan bir makinede
zaman aşımı beklemezsiniz.

Aynı davranışı kalıcı olarak **Ayarlar → Çevrimdışı mod** ile de açabilirsiniz.

Sonra:

1. **Geri Yükle** ile yedeği kurun — ya da doğrudan **Tara**'ya basıp
   **Güncellemeler** listesinden *Yerel klasör* kaynaklı paketleri seçin.
2. Ağ çalışmaya başlayınca çevrimdışı modu kapatın ve yeniden tarayın; kalan sürücüleri
   Windows Update tamamlar.

### Başka bir bilgisayardan sürücü bulmak

İnternetiniz hiç yoksa: **Genel Bakış → Donanım Raporu**. Oluşan metin dosyasını USB'ye
kopyalayıp çalışan bir bilgisayarda açın; içinde her aygıtın donanım kimliği yazar.
O kimliklerle üreticinin sitesinden doğru paketi indirip USB ile geri getirebilirsiniz.

---

## 9. Geçmiş ve CSV dışa aktarma

Yapılan **her** sürücü işleminin kalıcı kaydı: tarih, aygıt, sınıf, üretici, sürüm geçişi,
kaynak, sonuç, süre ve varsa hata mesajı.

| Buton | Ne yapar |
| --- | --- |
| **Tümü / Başarılı / Başarısız** | Sonuca göre filtre |
| **Arama** | Aygıt adı, başlık, üretici, sınıf ve hedef sürümde arar |
| **CSV Olarak Dışa Aktar** | `%ProgramData%\DrvNest\reports\` altına CSV yazar ve dosyayı Gezgin'de gösterir |
| **Geçmişi Temizle** | Tüm kayıtları siler |
| **Yenile** | Listeyi yeniden okur |

CSV dosyası **UTF-8 BOM** ile yazılır, böylece Excel Türkçe aygıt adlarını doğru gösterir.
Sütunlar: `Date, Device, Class, Manufacturer, From, To, Provider, Outcome, Duration(s),
Reboot, Error`.

Bir kaydın güncelleme öncesi yedeği hâlâ diskteyse, o kayıttan **yedek klasörünü açabilir**
ve [7. bölümdeki](#bir-sürücüyü-geri-almak) adımlarla geri yükleyebilirsiniz.

Geçmiş dosyası satır başına bir JSON nesnesi olarak tutulur (`history.jsonl`), bu yüzden
bir çökme en fazla son kaydı etkiler.

---

## 10. Günlük — hata bildirirken

Uygulamanın canlı tanılama akışı. Sürücü hataları neredeyse her zaman anlamsız bir HRESULT
olarak gelir; bu ekran onu anlamlı kılan bağlamı tutar.

| Buton | Ne yapar |
| --- | --- |
| **Kopyala** | Günlüğü panoya alır |
| **Temizle** | Ekrandaki ve bellekteki kayıtları siler |
| **Dosyayı Aç** | `drvnest.log` dosyasını açar |
| **Klasörü Aç** | Günlük klasörünü açar |
| **Otomatik kaydır** | Yeni satır geldikçe aşağı kayar |

### Hata bildirirken tam olarak şunu yapın

1. Sorunu tekrar edin.
2. **Günlük → Kopyala**.
3. [GitHub Issues](https://github.com/ahmetcaglayan/DrvNest/issues) üzerinde yeni bir kayıt
   açın ve panoyu yapıştırın.

*Kopyala* butonu günlüğün başına DrvNest sürümünü, işletim sistemi sürümünü, mimariyi ve
makine bilgisini otomatik ekler — ayrıca yazmanıza gerek yoktur.

Günlük dosyası `%ProgramData%\DrvNest\logs\drvnest.log` adresindedir ve 8 MB'a ulaşınca
kendini devirir.

---

## 11. Ayarların anlamı

Değişiklikler **Kaydet**'e basınca uygulanır. (Tema ve dil istisnadır: anında uygulanır.)

### Genel

| Ayar | Varsayılan | Anlamı |
| --- | --- | --- |
| **Aynı anda indirilecek sürücü sayısı** | 3 | Kaç işin aynı anda indirileceği (1–8). Yavaş bağlantıda 1–2, hızlı bağlantıda 4–6 mantıklıdır. **Bu ayar kurulumları etkilemez** — kurulumlar her zaman sırayla yapılır (bkz. aşağıdaki not). |
| **Başarısız işlemde tekrar deneme sayısı** | 2 | Bir iş kaç kez otomatik tekrar denenecek (0–5). Toplam deneme = bu sayı + 1. |
| **Açılışta otomatik tara** | Açık | Uygulama açılır açılmaz taramayı başlatır |
| **Geçmiş saklama süresi (gün)** | 0 | 0 = sınırsız. Değer verilirse açılışta daha eski kayıtlar silinir. |

> **Kurulumlar neden sıralı?** Bu ayarlanabilir bir şey değil, Windows'un davranışı.
> Windows Update aynı anda ikinci bir kuruluma `WU_E_OPERATIONINPROGRESS` döner ve PnP alt
> sistemi `pnputil` çağrılarını zaten kuyruklar. DrvNest bunu tek bir global kilitle
> dürüstçe modeller: aynı anda en fazla bir kurulum çalışır. "Aynı anda indirme sayısı"
> ayarını yükseltmek indirmeleri hızlandırır, kurulumu değil.

### Güvenlik

| Ayar | Varsayılan | Anlamı |
| --- | --- | --- |
| **Kurulumdan önce sistem geri yükleme noktası oluştur** | Açık | Oturumdaki ilk kurulumdan önce bir kez oluşturulur. Sistem Geri Yükleme kapalıysa veya sürüm desteklemiyorsa (Windows Server) atlanır — kurulum yine devam eder. **Kapatmanız önerilmez.** |
| **Güncellemeden önce mevcut sürücüyü yedekle** | Açık | Değiştirilen paketi `backups\rollback\` altına aktarır ve yolunu geçmişe yazar |
| **Yeniden başlatmadan sonra kaldığı yerden devam et** | Açık | Zamanlanmış görevi/`RunOnce` kaydını oluşturur |
| **Gerektiğinde otomatik yeniden başlat** | Kapalı | Kuyruk bitince yeniden başlatma gerekiyorsa makineyi kendiliğinden yeniden başlatır |
| **Yeniden başlatma gecikmesi (saniye)** | 60 | Geri sayım süresi (5–3600). Bu sürede vazgeçebilirsiniz. |

### Kaynaklar

| Ayar | Varsayılan | Anlamı |
| --- | --- | --- |
| **Çevrimdışı mod** | Kapalı | Windows Update'e hiç bağlanılmaz; yalnızca yerel klasörler kullanılır. `--rescue` ile açmakla aynı etki. |
| **İsteğe bağlı sürücü güncellemelerini de göster** | Açık | Kapalıyken yalnızca Microsoft'un önerdiği (gizli olmayan, otomatik seçilen) paketler listelenir |
| **Yerel sürücü klasörleri** | — | `.inf` paketlerinin aranacağı klasörler. **Klasör Ekle** ile ekleyin. `DrvNest.exe` yanındaki `Drivers` klasörü zaten otomatik eklenir. |

### Görünüm

| Ayar | Varsayılan | Anlamı |
| --- | --- | --- |
| **Tema** | Koyu | Koyu / Açık. Anında uygulanır. |
| **Dil** | Türkçe | Türkçe / English. Anında uygulanır. |

**Varsayılanlara Dön** tüm ayarları sıfırlar; gizlenen güncellemeler ve yoksayılan aygıtlar
listesi de temizlenir.

**Veri klasörünü aç** butonu `%ProgramData%\DrvNest` klasörünü Gezgin'de açar.

---

## 12. Hakkında ve kendi kendini güncelleme

Sürüm bilgisi, çalışma zamanı ve işletim sistemi özeti, proje sayfası ve hata bildirme
bağlantıları burada.

### Güncelleme adımları

1. **Güncellemeleri Kontrol Et** — GitHub Releases API'sine tek bir istek gider ve en son
   sürümün numarası mevcut sürümle karşılaştırılır.
2. Yeni sürüm varsa numarası ve sürüm notları gösterilir.
3. **İndir ve Kur**:
   - Makinenizin mimarisine uygun dosya indirilir
     (`DrvNest.exe` veya ARM64'te `DrvNest-arm64.exe`).
   - Dosyanın **SHA-256** özeti, sürümle birlikte yayımlanan `checksums.txt` içindeki
     değerle karşılaştırılır.
   - **Sağlama toplamı yoksa ya da tutmuyorsa indirilen dosya silinir ve kurulum
     reddedilir.** Bu dosya sürücüleri yönetici yetkisiyle kuran programın kendisidir;
     doğrulanmadan yerine konması kabul edilemez.
   - Doğrulama geçerse çalışan exe `.old` uzantısıyla yeniden adlandırılır, yenisi yerine
     kopyalanır ve uygulama yeniden başlatılır. Kalan `.old` dosyası bir sonraki açılışta
     silinir.

Notlar:

- **Kuyruk çalışırken güncelleme yapılamaz.** Sürücü kurarken exe'yi değiştirmek makineyi
  bozmanın etkileyici bir yoludur; uygulama buna izin vermez.
- Güncelleme **hiçbir zaman kendiliğinden uygulanmaz** — her zaman sizin tıklamanız gerekir.
- Bir şey ters giderse orijinal exe geri konur; asla exe'siz kalmazsınız.
- İsterseniz güncellemeyi elle de indirebilirsiniz:
  [Releases sayfası](https://github.com/ahmetcaglayan/DrvNest/releases).

---

## 13. Komut satırı bayrakları

```powershell
DrvNest.exe [--resume | --rescue | --offline | --updated]
```

| Bayrak | Ne yapar |
| --- | --- |
| `--resume` | Kesintiye uğramış oturumu **doğrudan sürdürür**. Yeniden başlatma sonrası zamanlanmış görevin kullandığı bayrak budur. Bayraksız açılışta oturum yüklenir ama kullanıcı **Devam Et**'e basana kadar başlamaz. |
| `--rescue` | Çevrimdışı kurtarma modu: Windows Update sağlayıcısı devre dışı bırakılır, yalnızca yerel klasörler kullanılır. |
| `--offline` | `--rescue` ile aynı. |
| `--updated` | Kendi kendini güncelleme sonrasında yeni exe'yi başlatırken kullanılan işaret. Uygulama bunu normal bir açılış gibi ele alır; elle vermenizi gerektiren bir durum yoktur. |

Bayraklar `-resume`, `/resume` gibi yazılabilir; büyük-küçük harf farkı yoktur.
Tanınmayan bir bayrak yok sayılır ve uygulama normal modda açılır.

---

## 14. Dosyalar nerede?

Her şey `%ProgramData%\DrvNest` altındadır (genellikle `C:\ProgramData\DrvNest`).
`%AppData%` yerine burası seçilmiştir: yeniden başlatma sonrası devam görevi başka bir
yönetici hesabı veya SYSTEM olarak çalışabilir ve aynı oturum dosyasını bulabilmelidir.

| Yol | İçerik |
| --- | --- |
| `settings.json` | Ayarlarınız |
| `session.json` | Yarıda kalan kuyruk (kuyruk bitince silinir) |
| `history.jsonl` | Güncelleme geçmişi, satır başına bir kayıt |
| `logs\drvnest.log` | Günlük dosyası (8 MB'da devrilir) |
| `backups\` | Sürücü yedekleri; `backups\rollback\` güncelleme öncesi yedekler |
| `cache\` | Geçici: hazırlanan paketler, indirilen sürümler, açılan ZIP'ler |
| `reports\` | Donanım raporları ve CSV dışa aktarımları |
| `<exe klasörü>\Drivers` | Taşınabilir sürücü havuzu (varsa otomatik kaydedilir) |

`%ProgramData%` yazılabilir değilse uygulama `%LocalAppData%\DrvNest` klasörüne geçer.

Ayarlar dosyasını elle silmek uygulamayı varsayılanlara döndürür. Kalıcı olarak kaldırmak
isterseniz: `DrvNest.exe`'yi silin ve `%ProgramData%\DrvNest` klasörünü kaldırın —
kayıt defterinde iz kalmaz, çünkü zamanlanmış görev ve `RunOnce` kaydı kuyruk bittiğinde
zaten silinir.
