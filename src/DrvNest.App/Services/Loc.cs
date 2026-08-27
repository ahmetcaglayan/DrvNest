using System.Globalization;
using System.IO;
using System.Text.Json;
using DrvNest.Core.Diagnostics;
using DrvNest.Core.Persistence;

namespace DrvNest.App.Services;

/// <summary>A language DrvNest can display, for the settings picker.</summary>
public sealed record LanguageOption(string Code, string NativeName, string EnglishName)
{
    /// <summary>The pseudo-language that follows the machine's own UI language.</summary>
    public const string AutoCode = "auto";

    public bool IsAuto => Code == AutoCode;

    public override string ToString() => NativeName;
}

/// <summary>
/// The string table.
///
/// English is the base language and the permanent fallback: every key exists in it,
/// and any key missing from another language falls back to English rather than
/// showing a raw identifier. Turkish ships built in.
///
/// Extra languages do not require a rebuild. Drop a JSON file next to the executable
/// under <c>Languages\&lt;code&gt;.json</c> (or into %ProgramData%\DrvNest\Languages)
/// shaped like:
///
///   {
///     "_name":        "Deutsch",
///     "_englishName": "German",
///     "nav.dashboard": "Ubersicht",
///     ...
///   }
///
/// and it appears in the settings picker automatically.
/// </summary>
public static class Loc
{
    /// <summary>The fallback language. Never changes.</summary>
    public const string DefaultLanguage = "en";

    private const string PackFolderName = "Languages";
    private const string NameKey = "_name";
    private const string EnglishNameKey = "_englishName";

    private static readonly Dictionary<string, Dictionary<string, string>> Packs =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly List<LanguageOption> Options = new();

    // Assigned by the static constructor, not by a field initializer: initializers run
    // in declaration order, and English is declared at the bottom of the file, so a
    // field initializer here would capture null.
    private static Dictionary<string, string> _active = null!;

    static Loc()
    {
        _active = English;

        Packs[DefaultLanguage] = English;
        Packs["tr"] = Turkish;

        Options.Add(new LanguageOption(LanguageOption.AutoCode, "System / Sistem", "System"));
        Options.Add(new LanguageOption("en", "English", "English"));
        Options.Add(new LanguageOption("tr", "Türkçe", "Turkish"));

        LoadExternalPacks();
    }

    /// <summary>The language currently displayed, e.g. "en". Never "auto".</summary>
    public static string Language { get; private set; } = DefaultLanguage;

    /// <summary>What the user picked, which may be "auto".</summary>
    public static string Preference { get; private set; } = LanguageOption.AutoCode;

    /// <summary>Everything the picker should offer, "System" first.</summary>
    public static IReadOnlyList<LanguageOption> Available => Options;

    /// <summary>Raised after a language switch so views can be rebuilt.</summary>
    public static event Action? LanguageChanged;

    // =====================================================================================
    // Selection
    // =====================================================================================

    /// <summary>
    /// Applies a language preference.
    /// Pass "auto" to follow the machine's UI language, falling back to English when
    /// no pack matches it.
    /// </summary>
    public static void SetLanguage(string? preference)
    {
        Preference = string.IsNullOrWhiteSpace(preference)
            ? LanguageOption.AutoCode
            : preference!.Trim().ToLowerInvariant();

        var resolved = Resolve(Preference);

        _active = Packs.TryGetValue(resolved, out var pack) ? pack : English;
        Language = resolved;

        ApplyCulture(resolved);

        Log.Info($"Language: preference '{Preference}' resolved to '{Language}'.");
        LanguageChanged?.Invoke();
    }

    /// <summary>
    /// Turns a preference into a language that actually exists.
    ///
    /// "auto" looks at the operating system's UI culture: exact match first
    /// ("pt-BR"), then the neutral language ("pt"), then English.
    /// </summary>
    public static string Resolve(string? preference)
    {
        if (!string.IsNullOrWhiteSpace(preference) &&
            !preference!.Equals(LanguageOption.AutoCode, StringComparison.OrdinalIgnoreCase))
        {
            var wanted = preference.Trim().ToLowerInvariant();
            if (Packs.ContainsKey(wanted)) return wanted;

            // "de-DE" was requested but only "de" is installed.
            var neutral = NeutralOf(wanted);
            if (neutral is not null && Packs.ContainsKey(neutral)) return neutral;

            return DefaultLanguage;
        }

        return DetectSystemLanguage();
    }

    /// <summary>The machine's UI language if a pack exists for it, otherwise English.</summary>
    public static string DetectSystemLanguage()
    {
        try
        {
            var culture = CultureInfo.InstalledUICulture;

            var full = culture.Name.ToLowerInvariant();
            if (full.Length > 0 && Packs.ContainsKey(full)) return full;

            var neutral = culture.TwoLetterISOLanguageName.ToLowerInvariant();
            if (Packs.ContainsKey(neutral)) return neutral;

            // CurrentUICulture can differ from InstalledUICulture on a machine whose
            // user overrode the display language; honour that too.
            var current = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
            if (Packs.ContainsKey(current)) return current;
        }
        catch (Exception ex)
        {
            Log.Debug($"Could not detect the system language: {ex.Message}");
        }

        return DefaultLanguage;
    }

    // =====================================================================================
    // Lookup
    // =====================================================================================

    /// <summary>Looks a key up, falling back to English and then to the key itself.</summary>
    public static string T(string key)
    {
        if (_active.TryGetValue(key, out var value)) return value;
        if (English.TryGetValue(key, out var fallback)) return fallback;
        return key;
    }

    public static string T(string key, params object[] args)
    {
        try { return string.Format(T(key), args); }
        catch { return T(key); }
    }

    // =====================================================================================
    // External language packs
    // =====================================================================================

    /// <summary>
    /// Loads <c>Languages\*.json</c> from next to the executable and from the data
    /// folder. A malformed pack is skipped with a log line; it can never stop the
    /// application from starting.
    /// </summary>
    private static void LoadExternalPacks()
    {
        foreach (var folder in new[]
                 {
                     Path.Combine(AppContext.BaseDirectory, PackFolderName),
                     Path.Combine(AppPaths.Root, PackFolderName)
                 })
        {
            if (!Directory.Exists(folder)) continue;

            foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
            {
                try
                {
                    LoadPack(file);
                }
                catch (Exception ex)
                {
                    Log.Warn($"Skipping language pack {Path.GetFileName(file)}: {ex.Message}");
                }
            }
        }
    }

    private static void LoadPack(string path)
    {
        var code = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        if (code.Length is 0 or > 12) return;

        var json = File.ReadAllText(path);
        var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

        if (entries is null || entries.Count == 0) return;

        // A built-in language may be extended, but the built-in strings win only where
        // the pack has nothing, so a pack can also correct a translation.
        if (Packs.TryGetValue(code, out var existing))
        {
            foreach (var pair in entries) existing[pair.Key] = pair.Value;
            Log.Info($"Merged {entries.Count} string(s) into the '{code}' language.");
            return;
        }

        Packs[code] = new Dictionary<string, string>(entries, StringComparer.Ordinal);

        var native = entries.TryGetValue(NameKey, out var n) && n.Length > 0 ? n : code;
        var english = entries.TryGetValue(EnglishNameKey, out var e) && e.Length > 0 ? e : native;

        Options.Add(new LanguageOption(code, native, english));
        Log.Info($"Loaded language pack '{code}' ({native}) with {entries.Count} string(s).");
    }

    private static void ApplyCulture(string language)
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo(language);
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
        }
        catch (CultureNotFoundException)
        {
            // A pack can exist for a code Windows does not know; formatting simply
            // stays on the invariant culture, which is harmless.
            Log.Debug($"No CultureInfo for '{language}'; keeping the current culture.");
        }
    }

    private static string? NeutralOf(string code)
    {
        int dash = code.IndexOf('-');
        return dash > 0 ? code[..dash] : null;
    }

    // =====================================================================================
    // Built-in strings
    // =====================================================================================

    /// <summary>
    /// English. The base language: every key must exist here, because every other
    /// language falls back to it.
    /// </summary>
    private static readonly Dictionary<string, string> English = new(StringComparer.Ordinal)
    {
        // Navigation
        ["nav.dashboard"] = "Dashboard",
        ["nav.devices"] = "Devices",
        ["nav.updates"] = "Updates",
        ["nav.queue"] = "Activity",
        ["nav.backup"] = "Backup & Restore",
        ["nav.history"] = "History",
        ["nav.logs"] = "Logs",
        ["nav.settings"] = "Settings",
        ["nav.about"] = "About",

        // Common
        ["common.scan"] = "Scan",
        ["common.rescan"] = "Rescan",
        ["common.cancel"] = "Cancel",
        ["common.close"] = "Close",
        ["common.save"] = "Save",
        ["common.retry"] = "Retry",
        ["common.refresh"] = "Refresh",
        ["common.selectAll"] = "Select all",
        ["common.selectNone"] = "Clear selection",
        ["common.search"] = "Search...",
        ["common.all"] = "All",
        ["common.yes"] = "Yes",
        ["common.no"] = "No",
        ["common.ok"] = "OK",
        ["common.open"] = "Open",
        ["common.delete"] = "Delete",
        ["common.remove"] = "Remove",
        ["common.export"] = "Export",
        ["common.none"] = "None",
        ["common.loading"] = "Loading...",
        ["common.device"] = "Device",
        ["common.version"] = "Version",
        ["common.size"] = "Size",
        ["common.status"] = "Status",
        ["common.date"] = "Date",
        ["common.provider"] = "Source",
        ["common.class"] = "Class",
        ["common.manufacturer"] = "Manufacturer",
        ["common.progress"] = "Progress",
        ["common.speed"] = "Speed",
        ["common.copy"] = "Copy",

        // Dashboard
        ["dash.title"] = "Dashboard",
        ["dash.subtitle"] = "Driver health of this machine",
        ["dash.devices"] = "Devices",
        ["dash.missing"] = "Missing drivers",
        ["dash.updates"] = "Updates",
        ["dash.problems"] = "Problem devices",
        ["dash.scanNow"] = "Scan now",
        ["dash.scanning"] = "Scanning...",
        ["dash.lastScan"] = "Last scan",
        ["dash.never"] = "Not scanned yet",
        ["dash.quickActions"] = "Quick actions",
        ["dash.fullRecovery"] = "Post-format recovery",
        ["dash.fullRecoveryDesc"] = "Find and install every missing driver",
        ["dash.updateAll"] = "Update everything",
        ["dash.updateAllDesc"] = "Queue and install every available update",
        ["dash.backupNow"] = "Back up drivers",
        ["dash.backupNowDesc"] = "Export every driver before you format",
        ["dash.exportReport"] = "Hardware report",
        ["dash.exportReportDesc"] = "Write hardware ids to a text file",
        ["dash.system"] = "System",
        ["dash.healthy"] = "Everything looks good",
        ["dash.healthyDesc"] = "Every device has a driver and nothing needs updating.",

        // Devices
        ["dev.title"] = "Devices",
        ["dev.subtitle"] = "Every PnP device present in this machine",
        ["dev.filterAll"] = "All",
        ["dev.filterProblem"] = "Problems",
        ["dev.filterMissing"] = "No driver",
        ["dev.filterGeneric"] = "Generic driver",
        ["dev.noDriver"] = "No driver",
        ["dev.count"] = "{0} devices",
        ["dev.copyId"] = "Copy hardware ID",
        ["dev.otherDevices"] = "Other devices",
        ["dev.rescanHardware"] = "Scan for hardware changes",
        ["dev.rescanning"] = "Asking Windows to re-detect hardware...",
        ["dev.searchOnline"] = "Search the web for this hardware ID",
        ["dev.filterUpdatable"] = "Updatable",
        ["dev.update"] = "Update",
        ["dev.updateAll"] = "Update all ({0})",
        ["dev.notChecked"] = "No driver source could be reached, so DrvNest cannot tell whether these drivers are current. Connect to the internet, or add a local driver folder under Settings.",
        ["state.noDriver"] = "NO DRIVER",
        ["state.noDriverHint"] = "This device has no driver installed.",
        ["state.updateAvailable"] = "UPDATE",
        ["state.vendorAvailable"] = "VENDOR DRIVER",
        ["state.updateAvailableHint"] = "A newer driver is available for this device.",
        ["state.upToDate"] = "UP TO DATE",
        ["state.upToDateHint"] = "No newer driver was offered by any source DrvNest checked (Windows Update and your local folders). The manufacturer's own site may still have a newer one.",
        ["state.unknown"] = "NOT CHECKED",
        ["state.unknownHint"] = "No driver source was reachable, so this driver has not been checked for updates.",
        ["dev.empty"] = "No devices to show. Run a scan first.",

        // Updates
        ["upd.title"] = "Updates",
        ["upd.subtitle"] = "Driver packages that can be installed",
        ["upd.installSelected"] = "Install selected",
        ["upd.selected"] = "{0} selected · {1}",
        ["upd.empty"] = "No installable drivers were found.",
        ["upd.emptyHint"] = "Run a scan first.",
        ["upd.missingBadge"] = "MISSING",
        ["upd.updateBadge"] = "UPDATE",
        ["upd.vendorBadge"] = "VENDOR DRIVER",
        ["upd.genericToVendor"] = "Windows generic driver  →  {0} {1}",
        ["upd.hide"] = "Hide this update",
        ["upd.ignoreDevice"] = "Ignore this device",

        // Queue
        ["queue.title"] = "Activity",
        ["queue.subtitle"] = "Download and install queue",
        ["queue.empty"] = "Nothing in the queue.",
        ["queue.emptyHint"] = "Pick some updates and start them.",
        ["queue.cancelAll"] = "Cancel all",
        ["queue.retryFailed"] = "Retry failed",
        ["queue.continue"] = "Continue",
        ["queue.overall"] = "Overall progress",
        ["queue.parallelNote"] = "Downloads run in parallel; installs run one at a time (a Windows restriction).",
        ["queue.rebootNeeded"] = "A restart is required to finish installing.",
        ["queue.rebootNow"] = "Restart now",
        ["queue.cancelRestart"] = "Cancel restart",
        ["queue.willResume"] = "DrvNest will continue where it left off after the restart.",
        ["queue.resumed"] = "Continuing the previous session.",
        ["queue.pendingFound"] = "An interrupted session was found.",

        // Backup
        ["bk.title"] = "Backup & Restore",
        ["bk.subtitle"] = "Export before a format, restore afterwards",
        ["bk.create"] = "Create backup",
        ["bk.createDesc"] = "Exports every third-party driver package on this machine.",
        ["bk.compress"] = "Compress to ZIP",
        ["bk.restore"] = "Restore",
        ["bk.restoreFolder"] = "Restore from folder",
        ["bk.existing"] = "Existing backups",
        ["bk.empty"] = "No backups yet.",
        ["bk.packages"] = "{0} packages",
        ["bk.location"] = "Backup folder",
        ["bk.tip"] = "Tip: put the backup and DrvNest.exe on the same USB stick. After a format you can restore every driver with no internet at all.",

        // History
        ["hist.title"] = "History",
        ["hist.subtitle"] = "Every driver operation DrvNest performed",
        ["hist.empty"] = "No records yet.",
        ["hist.exportCsv"] = "Export as CSV",
        ["hist.clear"] = "Clear history",
        ["hist.success"] = "Success",
        ["hist.failed"] = "Failed",
        ["hist.cancelled"] = "Cancelled",
        ["hist.rolledBack"] = "Rolled back",
        ["hist.filterAll"] = "All",
        ["hist.openBackup"] = "Open backup folder",

        // Logs
        ["log.title"] = "Logs",
        ["log.subtitle"] = "Diagnostics for this session",
        ["log.copy"] = "Copy everything",
        ["log.clear"] = "Clear",
        ["log.openFile"] = "Open log file",
        ["log.openFolder"] = "Open log folder",
        ["log.autoScroll"] = "Follow new entries",
        ["log.copied"] = "Copied to the clipboard.",
        ["log.screenshot"] = "Screenshot",
        ["log.screenshotHint"] = "Save a picture of this window. Print Screen does nothing over an app running as administrator, so DrvNest captures itself instead. The Print Screen key works here too.",
        ["shot.saved"] = "Screenshot saved: {0}",
        ["shot.savedAndCopied"] = "Screenshot copied to the clipboard and saved: {0}",
        ["shot.failed"] = "The screenshot could not be taken.",

        // Settings
        ["set.title"] = "Settings",
        ["set.subtitle"] = "Behaviour and safety options",
        ["set.general"] = "General",
        ["set.safety"] = "Safety",
        ["set.sources"] = "Sources",
        ["set.appearance"] = "Appearance",
        ["set.parallel"] = "Drivers downloaded at the same time",
        ["set.parallelHint"] = "Windows always installs drivers one at a time.",
        ["set.retries"] = "Retry attempts for a failed job",
        ["set.scanOnStartup"] = "Scan automatically on startup",
        ["set.restorePoint"] = "Create a system restore point before installing",
        ["set.backupBefore"] = "Back up the current driver before updating",
        ["set.resume"] = "Continue where it left off after a restart",
        ["set.autoReboot"] = "Restart automatically when required",
        ["set.autoRebootDelay"] = "Restart delay (seconds)",
        ["set.offline"] = "Offline mode (never contact Windows Update)",
        ["set.optional"] = "Show optional driver updates too",
        ["set.repos"] = "Local driver folders",
        ["set.reposHint"] = "Searched in offline mode. A \"Drivers\" folder next to DrvNest.exe is added automatically.",
        ["set.addFolder"] = "Add folder",
        ["set.theme"] = "Theme",
        ["set.themeDark"] = "Dark",
        ["set.themeLight"] = "Light",
        ["set.language"] = "Language",
        ["set.languageHint"] = "\"System\" follows the Windows display language and falls back to English.",
        ["set.reset"] = "Reset to defaults",
        ["set.saved"] = "Settings saved.",
        ["set.historyRetention"] = "History retention (days, 0 = forever)",
        ["set.dataFolder"] = "Open data folder",

        // About / updater
        ["about.title"] = "About",
        ["about.subtitle"] = "Version information and updates",
        ["about.version"] = "Version",
        ["about.checkUpdates"] = "Check for updates",
        ["about.checking"] = "Checking...",
        ["about.upToDate"] = "You are on the latest version.",
        ["about.available"] = "A new version is available: {0}",
        ["about.download"] = "Download and install",
        ["about.downloading"] = "Downloading...",
        ["about.verifying"] = "Verifying...",
        ["about.restartToApply"] = "The update is ready. DrvNest will restart.",
        ["about.autoCheck"] = "Check for updates on startup",
        ["about.repo"] = "Project page",
        ["about.issues"] = "Report an issue",
        ["about.releaseNotes"] = "Release notes",
        ["about.license"] = "License",
        ["about.notConfigured"] = "The GitHub repository is not configured (AppInfo.cs).",
        ["app.tagline"] = "Windows driver scanner, installer and updater",
        ["about.noReleases"] = "No release has been published yet.",
        ["about.networkError"] = "GitHub could not be reached. Check your internet connection.",
        ["about.checkFailed"] = "The update check failed.",
        ["about.checksumNote"] = "Every download is verified against the release checksums.txt (SHA-256) before anything is replaced.",
        ["about.licenseText"] = "MIT License. DrvNest is free and open source software, provided without warranty. Installing device drivers carries inherent risk; a system restore point is created before each run when System Restore is enabled.",
        ["win.minimize"] = "Minimize",
        ["win.maximize"] = "Maximize",

        // Status / errors
        ["status.ready"] = "Ready",
        ["status.admin"] = "Administrator",
        ["status.notAdmin"] = "Not elevated",
        ["status.offline"] = "Offline",
        ["err.needAdmin"] = "This action requires administrator rights.",
        ["health.healthy"] = "OK",
        ["health.missing"] = "NO DRIVER",
        ["health.faulty"] = "ERROR",
        ["health.disabled"] = "DISABLED",
        ["health.restart"] = "RESTART",
        ["prob.1"] = "This device is not configured correctly.",
        ["prob.10"] = "This device cannot start.",
        ["prob.14"] = "A restart is required to finish setting up this device.",
        ["prob.19"] = "The driver needs to be reinstalled.",
        ["prob.22"] = "This device is disabled.",
        ["prob.24"] = "This device is not present or has been removed.",
        ["prob.28"] = "The drivers for this device are not installed.",
        ["prob.31"] = "Windows could not load a driver for this device.",
        ["prob.32"] = "The driver service is disabled.",
        ["prob.39"] = "The driver is corrupted or missing.",
        ["prob.43"] = "The driver reported a device failure.",
        ["prob.45"] = "This device is not currently connected.",
        ["prob.52"] = "The driver signature could not be verified.",
        ["scan.devices"] = "Scanning devices...",
        ["scan.devicesDone"] = "{0} devices found",
        ["scan.sources"] = "Querying driver sources {0}/{1}... ({2}s)",
        ["scan.finalizing"] = "Preparing results...",
        ["status.summaryMissing"] = "{0} device(s) have no driver",
        ["status.summaryProblems"] = "{0} device(s) need attention",
        ["status.summaryReady"] = "{0} package(s) ready to install",
        ["warn.noNetwork"] = "No network adapter has a working driver, so Windows Update is unreachable. Use a local folder or a backup."
    };

    /// <summary>Turkish, shipped in the box.</summary>
    private static readonly Dictionary<string, string> Turkish = new(StringComparer.Ordinal)
    {
        // Navigation
        ["nav.dashboard"] = "Genel Bakış",
        ["nav.devices"] = "Aygıtlar",
        ["nav.updates"] = "Güncellemeler",
        ["nav.queue"] = "İşlemler",
        ["nav.backup"] = "Yedekle & Geri Yükle",
        ["nav.history"] = "Geçmiş",
        ["nav.logs"] = "Günlük",
        ["nav.settings"] = "Ayarlar",
        ["nav.about"] = "Hakkında",

        // Common
        ["common.scan"] = "Tara",
        ["common.rescan"] = "Yeniden Tara",
        ["common.cancel"] = "İptal",
        ["common.close"] = "Kapat",
        ["common.save"] = "Kaydet",
        ["common.retry"] = "Tekrar Dene",
        ["common.refresh"] = "Yenile",
        ["common.selectAll"] = "Tümünü Seç",
        ["common.selectNone"] = "Seçimi Temizle",
        ["common.search"] = "Ara...",
        ["common.all"] = "Tümü",
        ["common.yes"] = "Evet",
        ["common.no"] = "Hayır",
        ["common.ok"] = "Tamam",
        ["common.open"] = "Aç",
        ["common.delete"] = "Sil",
        ["common.remove"] = "Kaldır",
        ["common.export"] = "Dışa Aktar",
        ["common.none"] = "Yok",
        ["common.loading"] = "Yükleniyor...",
        ["common.device"] = "Aygıt",
        ["common.version"] = "Sürüm",
        ["common.size"] = "Boyut",
        ["common.status"] = "Durum",
        ["common.date"] = "Tarih",
        ["common.provider"] = "Kaynak",
        ["common.class"] = "Sınıf",
        ["common.manufacturer"] = "Üretici",
        ["common.progress"] = "İlerleme",
        ["common.speed"] = "Hız",
        ["common.copy"] = "Kopyala",

        // Dashboard
        ["dash.title"] = "Genel Bakış",
        ["dash.subtitle"] = "Sisteminizin sürücü durumu",
        ["dash.devices"] = "Aygıt",
        ["dash.missing"] = "Sürücüsü Eksik",
        ["dash.updates"] = "Güncelleme",
        ["dash.problems"] = "Sorunlu Aygıt",
        ["dash.scanNow"] = "Şimdi Tara",
        ["dash.scanning"] = "Taranıyor...",
        ["dash.lastScan"] = "Son tarama",
        ["dash.never"] = "Henüz taranmadı",
        ["dash.quickActions"] = "Hızlı İşlemler",
        ["dash.fullRecovery"] = "Format Sonrası Kurtarma",
        ["dash.fullRecoveryDesc"] = "Eksik olan tüm sürücüleri bul ve kur",
        ["dash.updateAll"] = "Tümünü Güncelle",
        ["dash.updateAllDesc"] = "Bulunan tüm güncellemeleri sıraya al ve kur",
        ["dash.backupNow"] = "Sürücüleri Yedekle",
        ["dash.backupNowDesc"] = "Formattan önce tüm sürücüleri dışa aktar",
        ["dash.exportReport"] = "Donanım Raporu",
        ["dash.exportReportDesc"] = "Donanım kimliklerini metin dosyasına yaz",
        ["dash.system"] = "Sistem",
        ["dash.healthy"] = "Her şey yolunda",
        ["dash.healthyDesc"] = "Tüm aygıtların sürücüsü kurulu ve güncel.",

        // Devices
        ["dev.title"] = "Aygıtlar",
        ["dev.subtitle"] = "Sistemde bulunan tüm PnP aygıtları",
        ["dev.filterAll"] = "Tümü",
        ["dev.filterProblem"] = "Sorunlu",
        ["dev.filterMissing"] = "Sürücüsüz",
        ["dev.filterGeneric"] = "Jenerik Sürücü",
        ["dev.noDriver"] = "Sürücü yok",
        ["dev.count"] = "{0} aygıt",
        ["dev.copyId"] = "Donanım kimliğini kopyala",
        ["dev.otherDevices"] = "Diğer aygıtlar",
        ["dev.rescanHardware"] = "Donanım Değişikliklerini Tara",
        ["dev.rescanning"] = "Windows'tan donanımı yeniden algılaması isteniyor...",
        ["dev.searchOnline"] = "Bu donanım kimliğini internette ara",
        ["dev.filterUpdatable"] = "Güncellenebilir",
        ["dev.update"] = "Güncelle",
        ["dev.updateAll"] = "Tümünü Güncelle ({0})",
        ["dev.notChecked"] = "Hiçbir sürücü kaynağına ulaşılamadı; bu yüzden sürücülerin güncel olup olmadığı bilinmiyor. İnternete bağlanın veya Ayarlar'dan yerel bir sürücü klasörü ekleyin.",
        ["state.noDriver"] = "SÜRÜCÜ YOK",
        ["state.noDriverHint"] = "Bu aygıtın kurulu sürücüsü yok.",
        ["state.updateAvailable"] = "GÜNCELLEME VAR",
        ["state.vendorAvailable"] = "ÜRETİCİ SÜRÜCÜSÜ VAR",
        ["state.updateAvailableHint"] = "Bu aygıt için daha yeni bir sürücü mevcut.",
        ["state.upToDate"] = "GÜNCEL",
        ["state.upToDateHint"] = "DrvNest'in kontrol ettiği kaynaklarda (Windows Update ve yerel klasörleriniz) daha yeni bir sürücü bulunamadı. Üreticinin kendi sitesinde daha yenisi olabilir.",
        ["state.unknown"] = "KONTROL EDİLMEDİ",
        ["state.unknownHint"] = "Hiçbir sürücü kaynağına ulaşılamadığı için bu sürücü güncellik açısından kontrol edilmedi.",
        ["dev.empty"] = "Gösterilecek aygıt yok. Önce bir tarama yapın.",

        // Updates
        ["upd.title"] = "Güncellemeler",
        ["upd.subtitle"] = "Kurulabilecek sürücü paketleri",
        ["upd.installSelected"] = "Seçilenleri Kur",
        ["upd.selected"] = "{0} seçili · {1}",
        ["upd.empty"] = "Kurulabilecek yeni sürücü bulunamadı.",
        ["upd.emptyHint"] = "Önce bir tarama yapın.",
        ["upd.missingBadge"] = "EKSİK",
        ["upd.updateBadge"] = "GÜNCELLEME",
        ["upd.vendorBadge"] = "ÜRETİCİ SÜRÜCÜSÜ",
        ["upd.genericToVendor"] = "Windows jenerik sürücüsü  →  {0} {1}",
        ["upd.hide"] = "Bu güncellemeyi gizle",
        ["upd.ignoreDevice"] = "Bu aygıtı yoksay",

        // Queue
        ["queue.title"] = "İşlemler",
        ["queue.subtitle"] = "İndirme ve kurulum kuyruğu",
        ["queue.empty"] = "Kuyrukta işlem yok.",
        ["queue.emptyHint"] = "Güncellemelerden seçim yapıp başlatın.",
        ["queue.cancelAll"] = "Tümünü İptal Et",
        ["queue.retryFailed"] = "Başarısızları Tekrar Dene",
        ["queue.continue"] = "Devam Et",
        ["queue.overall"] = "Toplam ilerleme",
        ["queue.parallelNote"] = "İndirmeler paralel, kurulumlar sırayla yapılır (Windows kısıtlaması).",
        ["queue.rebootNeeded"] = "Kurulumu tamamlamak için yeniden başlatma gerekiyor.",
        ["queue.rebootNow"] = "Şimdi Yeniden Başlat",
        ["queue.cancelRestart"] = "Yeniden Başlatmayı İptal Et",
        ["queue.willResume"] = "Yeniden başlatmadan sonra kaldığı yerden devam edecek.",
        ["queue.resumed"] = "Önceki oturum kaldığı yerden devam ediyor.",
        ["queue.pendingFound"] = "Yarım kalmış bir oturum bulundu.",

        // Backup
        ["bk.title"] = "Yedekle & Geri Yükle",
        ["bk.subtitle"] = "Format öncesi dışa aktar, format sonrası geri yükle",
        ["bk.create"] = "Yedek Oluştur",
        ["bk.createDesc"] = "Sistemdeki tüm üçüncü parti sürücü paketlerini dışa aktarır.",
        ["bk.compress"] = "ZIP olarak sıkıştır",
        ["bk.restore"] = "Geri Yükle",
        ["bk.restoreFolder"] = "Klasörden Geri Yükle",
        ["bk.existing"] = "Mevcut Yedekler",
        ["bk.empty"] = "Henüz yedek yok.",
        ["bk.packages"] = "{0} paket",
        ["bk.location"] = "Yedek klasörü",
        ["bk.tip"] = "İpucu: Yedeği ve DrvNest.exe'yi aynı USB belleğe koyun. Format sonrası internet olmadan tüm sürücüleri geri yükleyebilirsiniz.",

        // History
        ["hist.title"] = "Geçmiş",
        ["hist.subtitle"] = "Yapılan tüm sürücü işlemleri",
        ["hist.empty"] = "Henüz kayıt yok.",
        ["hist.exportCsv"] = "CSV Olarak Dışa Aktar",
        ["hist.clear"] = "Geçmişi Temizle",
        ["hist.success"] = "Başarılı",
        ["hist.failed"] = "Başarısız",
        ["hist.cancelled"] = "İptal",
        ["hist.rolledBack"] = "Geri Alındı",
        ["hist.filterAll"] = "Tümü",
        ["hist.openBackup"] = "Yedek klasörünü aç",

        // Logs
        ["log.title"] = "Günlük",
        ["log.subtitle"] = "Bu oturumun tanılama kayıtları",
        ["log.copy"] = "Tümünü Kopyala",
        ["log.clear"] = "Temizle",
        ["log.openFile"] = "Günlük Dosyasını Aç",
        ["log.openFolder"] = "Günlük Klasörünü Aç",
        ["log.autoScroll"] = "Yeni kayıtları takip et",
        ["log.copied"] = "Panoya kopyalandı.",
        ["log.screenshot"] = "Ekran Görüntüsü",
        ["log.screenshotHint"] = "Bu pencerenin görüntüsünü kaydeder. Yönetici olarak çalışan bir uygulamanın üzerinde Print tuşu çalışmaz; bu yüzden DrvNest kendi görüntüsünü alır. Print tuşu burada da çalışır.",
        ["shot.saved"] = "Ekran görüntüsü kaydedildi: {0}",
        ["shot.savedAndCopied"] = "Ekran görüntüsü panoya kopyalandı ve kaydedildi: {0}",
        ["shot.failed"] = "Ekran görüntüsü alınamadı.",

        // Settings
        ["set.title"] = "Ayarlar",
        ["set.subtitle"] = "Davranış ve güvenlik seçenekleri",
        ["set.general"] = "Genel",
        ["set.safety"] = "Güvenlik",
        ["set.sources"] = "Kaynaklar",
        ["set.appearance"] = "Görünüm",
        ["set.parallel"] = "Aynı anda indirilecek sürücü sayısı",
        ["set.parallelHint"] = "Kurulumlar Windows tarafından her zaman sırayla yapılır.",
        ["set.retries"] = "Başarısız işlemde tekrar deneme sayısı",
        ["set.scanOnStartup"] = "Açılışta otomatik tara",
        ["set.restorePoint"] = "Kurulumdan önce sistem geri yükleme noktası oluştur",
        ["set.backupBefore"] = "Güncellemeden önce mevcut sürücüyü yedekle",
        ["set.resume"] = "Yeniden başlatmadan sonra kaldığı yerden devam et",
        ["set.autoReboot"] = "Gerektiğinde otomatik yeniden başlat",
        ["set.autoRebootDelay"] = "Yeniden başlatma gecikmesi (saniye)",
        ["set.offline"] = "Çevrimdışı mod (Windows Update kullanma)",
        ["set.optional"] = "İsteğe bağlı sürücü güncellemelerini de göster",
        ["set.repos"] = "Yerel sürücü klasörleri",
        ["set.reposHint"] = "Çevrimdışı modda taranır. DrvNest.exe yanındaki \"Drivers\" klasörü otomatik eklenir.",
        ["set.addFolder"] = "Klasör Ekle",
        ["set.theme"] = "Tema",
        ["set.themeDark"] = "Koyu",
        ["set.themeLight"] = "Açık",
        ["set.language"] = "Dil",
        ["set.languageHint"] = "\"Sistem\" seçeneği Windows görüntü dilini izler, eşleşme yoksa İngilizce kullanır.",
        ["set.reset"] = "Varsayılanlara Dön",
        ["set.saved"] = "Ayarlar kaydedildi.",
        ["set.historyRetention"] = "Geçmiş saklama süresi (gün, 0 = sınırsız)",
        ["set.dataFolder"] = "Veri Klasörünü Aç",

        // About / updater
        ["about.title"] = "Hakkında",
        ["about.subtitle"] = "Sürüm bilgisi ve güncellemeler",
        ["about.version"] = "Sürüm",
        ["about.checkUpdates"] = "Güncellemeleri Kontrol Et",
        ["about.checking"] = "Kontrol ediliyor...",
        ["about.upToDate"] = "En güncel sürümü kullanıyorsunuz.",
        ["about.available"] = "Yeni sürüm mevcut: {0}",
        ["about.download"] = "İndir ve Kur",
        ["about.downloading"] = "İndiriliyor...",
        ["about.verifying"] = "Doğrulanıyor...",
        ["about.restartToApply"] = "Güncelleme hazır. Uygulama yeniden başlatılacak.",
        ["about.autoCheck"] = "Açılışta güncellemeleri kontrol et",
        ["about.repo"] = "Proje Sayfası",
        ["about.issues"] = "Hata Bildir",
        ["about.releaseNotes"] = "Sürüm notları",
        ["about.license"] = "Lisans",
        ["about.notConfigured"] = "GitHub deposu yapılandırılmamış (AppInfo.cs).",
        ["app.tagline"] = "Windows sürücü tarayıcı, kurucu ve güncelleyici",
        ["about.noReleases"] = "Henüz yayınlanmış bir sürüm yok.",
        ["about.networkError"] = "GitHub'a ulaşılamadı. İnternet bağlantınızı kontrol edin.",
        ["about.checkFailed"] = "Güncelleme kontrolü başarısız oldu.",
        ["about.checksumNote"] = "İndirilen her dosya, değiştirme işleminden önce sürümün checksums.txt dosyasındaki SHA-256 özeti ile doğrulanır.",
        ["about.licenseText"] = "MIT Lisansı. DrvNest özgür ve açık kaynaklı bir yazılımdır, hiçbir garanti verilmez. Sürücü kurmak doğası gereği risk taşır; Sistem Geri Yükleme açıksa her işlemden önce bir geri yükleme noktası oluşturulur.",
        ["win.minimize"] = "Simge durumuna küçült",
        ["win.maximize"] = "Ekranı kapla",

        // Status / errors
        ["status.ready"] = "Hazır",
        ["status.admin"] = "Yönetici",
        ["status.notAdmin"] = "Yönetici değil",
        ["status.offline"] = "Çevrimdışı",
        ["err.needAdmin"] = "Bu işlem için yönetici yetkisi gerekiyor.",
        ["health.healthy"] = "SORUNSUZ",
        ["health.missing"] = "SÜRÜCÜ YOK",
        ["health.faulty"] = "HATA",
        ["health.disabled"] = "DEVRE DIŞI",
        ["health.restart"] = "YENİDEN BAŞLAT",
        ["prob.1"] = "Bu aygıt doğru yapılandırılmamış.",
        ["prob.10"] = "Bu aygıt başlatılamıyor.",
        ["prob.14"] = "Kurulumun tamamlanması için yeniden başlatma gerekiyor.",
        ["prob.19"] = "Sürücünün yeniden kurulması gerekiyor.",
        ["prob.22"] = "Bu aygıt devre dışı bırakılmış.",
        ["prob.24"] = "Bu aygıt takılı değil veya kaldırılmış.",
        ["prob.28"] = "Bu aygıtın sürücüleri kurulu değil.",
        ["prob.31"] = "Windows bu aygıt için sürücü yükleyemedi.",
        ["prob.32"] = "Sürücü hizmeti devre dışı.",
        ["prob.39"] = "Sürücü bozuk veya eksik.",
        ["prob.43"] = "Sürücü bir aygıt hatası bildirdi.",
        ["prob.45"] = "Bu aygıt şu anda bağlı değil.",
        ["prob.52"] = "Sürücünün dijital imzası doğrulanamadı.",
        ["scan.devices"] = "Aygıtlar taranıyor...",
        ["scan.devicesDone"] = "{0} aygıt bulundu",
        ["scan.sources"] = "Sürücü kaynakları sorgulanıyor {0}/{1}... ({2} sn)",
        ["scan.finalizing"] = "Sonuçlar hazırlanıyor...",
        ["status.summaryMissing"] = "{0} aygıtın sürücüsü eksik",
        ["status.summaryProblems"] = "{0} aygıt sorunlu",
        ["status.summaryReady"] = "{0} paket kurulmaya hazır",
        ["warn.noNetwork"] = "Çalışan bir ağ sürücüsü yok. Windows Update'e ulaşılamaz — yerel klasör veya yedek kullanın."
    };
}
