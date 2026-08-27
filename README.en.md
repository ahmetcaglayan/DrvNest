<div align="center">

<img src="assets/logo.svg" alt="DrvNest logo: a hexagonal nest cell cradling a mint microchip" width="120" height="120">

# DrvNest

**DrvNest is a free, open-source driver updater for Windows 10 and 11.**
It scans every device in the machine, finds and installs the missing and outdated drivers, resumes
after the restarts they need, backs your drivers up before a format and restores them afterwards
with no internet at all. One file, no installer, no adware.

<br>

[![Download DrvNest.exe](https://img.shields.io/badge/⬇️%20DOWNLOAD%20DrvNest.exe-Windows%20x64-2ea043?style=for-the-badge&logo=windows&logoColor=white&labelColor=1a7f37)](https://github.com/ahmetcaglayan/DrvNest/releases/latest/download/DrvNest.exe)

<sub>[🌐 Project site](https://ahmetcaglayan.github.io/DrvNest/) · [All releases and the arm64 build →](../../releases)</sub>

<br>

### No installer. Download, double-click, done.

`Windows 10 1607+ / Windows 11` · `64-bit` · `Administrator rights required`

**No .NET installation. No Visual C++ Redistributable.**
Everything lives inside the executable: the .NET 8 runtime is published self-contained as
a single file, and WPF carries its own `vcruntime140_cor3.dll` / `msvcp140_cor3.dll`.

<br>

[![License: MIT](https://img.shields.io/badge/License-MIT-blue?style=flat-square)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%2F11-0078d4?style=flat-square&logo=windows&logoColor=white)](../../releases)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-512bd4?style=flat-square&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Release](https://img.shields.io/github/v/release/ahmetcaglayan/DrvNest?style=flat-square&label=Release)](../../releases/latest)
[![Downloads](https://img.shields.io/github/downloads/ahmetcaglayan/DrvNest/total?style=flat-square&label=downloads&color=2ea043)](../../releases)
[![Stars](https://img.shields.io/github/stars/ahmetcaglayan/DrvNest?style=flat-square)](../../stargazers)
[![Build](https://img.shields.io/github/actions/workflow/status/ahmetcaglayan/DrvNest/build.yml?style=flat-square&label=Build)](../../actions/workflows/build.yml)

<sub>[🇹🇷 Türkçe](README.md) · 🇬🇧 English</sub>

</div>

---

## 🎯 What is it for?

You just formatted Windows. Device Manager is full of yellow exclamation marks, the
resolution is wrong, there is no sound and — worst of all — no internet, because the
network adapter has no driver either.

DrvNest solves that from a single window:

- Lists **every PnP device** in the machine and tells you which ones have no driver.
- Finds missing and upgradable drivers in the **Windows Update catalog** or in a
  **local folder on a USB stick**.
- Queues them, downloads them, installs them and **carries on where it left off** after
  every restart it needs.
- Exports your current drivers **before** a format and restores them **afterwards**
  with no internet involved.

One file, no installer, no background service, no telemetry.

---

## ✨ Features

| Feature | What it does |
| --- | --- |
| 🔍 **Full device scan** | Enumerates every present PnP device through SetupAPI + CfgMgr32. No WMI, so it also works on a freshly installed machine or one with a broken WMI repository. |
| ⚠️ **Missing-driver detection** | Reads Configuration Manager problem codes; 28 (`CM_PROB_FAILED_INSTALL`), 1 and 19 mean "no driver". 22 is disabled, 14 is waiting for a restart. |
| ☁️ **Windows Update driver catalog** | Talks to Microsoft Update through the Windows Update Agent COM API (WUApiLib). No extra service, no extra download, no extra dependency — `wuapi.dll` ships with Windows. |
| 💾 **Local / offline INF repository** | Parses `.inf` packages in folders and matches them by hardware id. A USB stick, a network share or a DrvNest backup all work as a source. |
| ⚡ **Parallel downloads + serialized installs** | Downloads overlap (3 by default, configurable 1–8). Installs run one at a time. That is not a shortcut: Windows Update returns `WU_E_OPERATIONINPROGRESS` for a second concurrent install and the PnP subsystem serializes anyway. Pretending otherwise would only produce spurious failures. |
| 🔄 **Resume after reboot** | The queue is written to `session.json` after every state change; a logon-triggered `schtasks` task (with an HKLM `RunOnce` fallback) relaunches DrvNest with `--resume` and it continues exactly where it stopped. |
| 🛡️ **System restore point** | Creates a driver-type restore point through `srclient.dll` before the first install of a session. |
| ↩️ **Pre-update backup** | The package about to be replaced is exported immediately before the install, and its path is stored in the history record so it can be restored if something goes wrong. |
| 📦 **Driver backup / restore** | Exports every third-party driver package with `pnputil /export-driver` into a folder or ZIP, and restores with `pnputil /add-driver ... /subdirs /install`. |
| 📊 **Update history** | A permanent record kept as one JSON object per line (`history.jsonl`), exportable to CSV in one click. |
| 📄 **Hardware report** | Writes every device and hardware id to a plain text file — carry it on a USB stick to a working computer and look the drivers up by hand. |
| 🆙 **Built-in updater** | Downloads the new release from GitHub, **verifies its SHA-256** (and refuses to install when the release publishes no `checksums.txt`), then swaps the executable in place. |
| 🌍 **Turkish / English UI** | Switches instantly while the app is open. |
| 🎨 **Dark / light theme** | Swaps the palette dictionary; applied without reopening the window. |

---

## 🚑 Post-format recovery

This is why DrvNest exists.

### The chicken-and-egg problem

After a format the **network adapter usually has no driver either**. You need the
internet to download the driver and the driver to reach the internet. Windows Update
cannot help, because you cannot reach it.

The fix: **take your drivers with you before the format.**

### BEFORE the format (5 minutes)

1. Run DrvNest.
2. Go to **Backup & Restore**.
3. Click **Create backup**. Every third-party driver package on the system is exported.
   (Microsoft's own in-box drivers are deliberately skipped — Windows reinstalls those
   itself, and including them would triple the backup size for nothing.)
4. Tick **Compress as ZIP** if you like.
5. Copy the resulting folder **and `DrvNest.exe`** onto the same USB stick.

> 💡 Optional: name the backup folder `Drivers` and keep it next to `DrvNest.exe`.
> DrvNest registers it **automatically** as a local driver repository — no configuration
> needed.

### AFTER the format

1. Plug the stick in and run `DrvNest.exe` (it asks for elevation).
   With no internet, start it as `DrvNest.exe --rescue`: Windows Update is never
   contacted and only local sources are used.
2. **Backup & Restore → Restore** (or **Restore from folder**) and pick your backup.
   Every package is added to the driver store and bound to its devices.
3. Once the network adapter works, press **Scan**.
4. **Dashboard → Post-format recovery** queues everything that is still missing from
   Windows Update.
5. Accept the restart when asked — DrvNest brings itself back at logon and finishes the
   rest of the queue.

> ℹ️ The folder does not have to be a DrvNest backup. Any vendor driver folder you
> downloaded and extracted works with **Restore from folder**; its `.inf` files are
> found recursively.

### Command line

```powershell
DrvNest.exe                 # normal launch
DrvNest.exe --rescue        # offline rescue mode (same as --offline)
DrvNest.exe --resume        # continue an interrupted queue straight away
```

---

## 📸 Screenshots

> Screenshots coming soon — they will live in `assets/screenshots/`.

The nine screens:

- `dashboard.png` — Dashboard: device/missing/update counters, system summary, quick actions
- `devices.png` — Devices: full inventory grouped by class, filters and search
- `updates.png` — Updates: installable packages and selection
- `queue.png` — Activity: live download/install progress
- `backup.png` — Backup & Restore
- `history.png` — History and CSV export
- `logs.png` — Logs
- `settings.png` — Settings
- `about.png` — About and the built-in updater

---

## 🧭 Menus

| Menu | What it does |
| --- | --- |
| **Dashboard** | Device count, missing drivers, available updates, problem devices. OS / machine / CPU / BIOS summary. Quick actions: *Scan now*, *Post-format recovery*, *Update everything*, *Back up drivers*, *Hardware report*. A warning banner appears when no network adapter has a working driver. |
| **Devices** | Every PnP device, grouped by class. Filters: *All / Problems / Missing / Generic driver*. Search by name, manufacturer, version and hardware id; copy a hardware id to the clipboard. |
| **Updates** | Installable packages: both missing drivers and version upgrades. Per-row selection, *Select all / Clear selection*, total selected size, *Install selected*. You can hide one update or ignore a device entirely. |
| **Activity** | The running queue. Download percentage, speed, transferred bytes and the install phase are shown separately for every job. *Cancel all*, *Retry failed*, *Restart now* / *Later*. An interrupted session shows a *Continue* button here. |
| **Backup & Restore** | *Create backup* (optionally zipped), the list of existing backups (package count, size, date), *Restore*, *Restore from folder*, *Open*, *Delete*. |
| **History** | A permanent record of every driver operation. Filter by outcome, search, *Export as CSV*, *Clear history*. If a record's pre-update backup still exists you can open its folder. |
| **Logs** | Live diagnostics. *Copy* puts the log on the clipboard with a version / OS / machine header — exactly what an issue report needs. Open the log file or folder, or clear it. |
| **Settings** | Parallel download count, retry count, scan on startup, restore point, pre-update backup, resume after restart, automatic restart and its delay, offline mode, optional drivers, local driver folders, history retention, theme, language. |
| **About** | Version information, *Check for updates*, *Download and install*, release notes, project page and issue links. |

---

## ⚙️ How it works

```mermaid
flowchart TD
    A["Scan starts"] --> B["Devices<br/>SetupAPI + CfgMgr32"]
    B --> C{"Providers<br/>queried in parallel"}
    C --> D["Windows Update<br/>WUApiLib COM"]
    C --> E["Local INF repository<br/>USB / folder / backup"]
    D --> F["Candidate list<br/>deduplicated"]
    E --> F
    F --> G["User selects"]
    G --> H["Queue"]
    H --> I["Parallel downloads<br/>3 jobs by default"]
    I --> J["Serialized installs<br/>one global lock"]
    J --> K{"Restart<br/>required?"}
    K -->|No| L["Done"]
    K -->|Yes| M["session.json written<br/>+ schtasks ONLOGON"]
    M --> N["Restart"]
    N --> O["DrvNest --resume"]
    O --> H
```

In short:

1. **Scan.** `SetupDiGetClassDevs(DIGCF_PRESENT | DIGCF_ALLCLASSES)` enumerates every
   device physically present; `CM_Get_DevNode_Status` supplies the problem code, and
   `HKLM\SYSTEM\CurrentControlSet\Control\Class\<DriverKey>` supplies the installed
   driver's version, date and provider.
2. **Providers.** Windows Update and the local INF repository are queried at the same
   time. A provider that fails becomes a warning line, never an aborted scan.
3. **Deduplication.** When both sources offer the same package the **local copy wins** —
   it is already on disk and needs no network. If Windows Update offers a version older
   than the installed one, that candidate is dropped.
4. **Queue.** Downloads run behind `SemaphoreSlim(MaxParallelJobs)`; installs run behind
   one global lock. A failed job is retried twice by default.
5. **Resume.** Every state change is written atomically to `session.json`. When a restart
   is needed the queue is parked, and a logon-triggered task brings DrvNest back with
   `--resume`. A session survives at most 10 restarts before it is abandoned as a safety
   valve.

---

## 🔨 Building from source

Irrelevant if you just want the app: **download the exe, double-click, done.**
The source sits in its own folder and bothers nobody.

```
DrvNest/
├── src/                    source code (C#, .NET 8, WPF)
│   ├── DrvNest.Core/       UI-free core: scanning, providers, job engine
│   ├── DrvNest.App/        WPF desktop application (DrvNest.exe)
│   └── DrvNest.Cli/        (reserved) placeholder for a headless front end
├── docs/                   documentation
├── build/                  build scripts
├── assets/                 logo and screenshots
└── .github/workflows/      CI
```

The short version:

```powershell
dotnet publish src/DrvNest.App/DrvNest.App.csproj -c Release -r win-x64 -o publish
```

Details, the arm64 build and an explanation of the WUApiLib COM reference:
**[docs/BUILD.md](docs/BUILD.md)**

Architecture, the `IDriverProvider` abstraction and how to add a new driver source:
**[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)**

---

## 🔐 Security and privacy

- **No telemetry.** No usage data, no device identifiers, no statistics leave the machine.
- Exactly **two** things ever go out:
  1. **Windows Update queries** — straight to Microsoft, through Windows' own Windows
     Update Agent. (Never in offline mode or with `--rescue`.)
  2. **The GitHub Releases API** — only when you press *Check for updates*.
- **Why administrator?** Installing a driver is privileged: `pnputil`, the Windows Update
  installer and System Restore all need an elevated token. DrvNest asks for it up front in
  its application manifest (`requireAdministrator`) rather than failing half way through a
  queue.
- **Safety nets:** a system restore point before the first install of a session, and a
  backup of every driver package it replaces.
- **The updater** compares the download against the release's `checksums.txt` SHA-256;
  a missing or mismatching checksum means the file is deleted and the update refused.
- All state lives under `%ProgramData%\DrvNest`: `settings.json`, `session.json`,
  `history.jsonl`, `logs/`, `backups/`, `cache/`, `reports/`.

Vulnerability reports: **[SECURITY.md](SECURITY.md)**

---

## ❓ FAQ

### Is DrvNest free?

Yes. DrvNest is released under the MIT licence and the full source is in this repository. There is
no paid tier, no trial, no feature that unlocks after payment, no advertising and no bundled
third-party software. The scan and the installs are the same product.

### Do I need to install .NET?

No. The entire .NET 8 runtime is inside `DrvNest.exe` (self-contained, single-file publish) and WPF
carries its own `vcruntime140_cor3.dll` / `msvcp140_cor3.dll`. No Visual C++ Redistributable either.
The only requirement is 64-bit Windows 10 version 1607 (build 14393) or newer.

### Why does SmartScreen or my antivirus warn about it?

Because `DrvNest.exe` is **not code-signed** — certificates cost money. SmartScreen and Smart App
Control warn about any unsigned executable that has not built up reputation, and an app that runs as
administrator, installs drivers and registers a scheduled task looks like malware to a heuristic
scanner. The honest mitigation is to verify the file: compare the output of
`Get-FileHash .\DrvNest.exe -Algorithm SHA256` with the matching line in the release's
`checksums.txt`.

### Can I install drivers after a format with no internet?

Yes — that is what DrvNest was built for. Back your drivers up before the format, put the backup and
`DrvNest.exe` on the same USB stick, then start `DrvNest.exe --rescue` afterwards and press
**Restore**. A folder named `Drivers` next to `DrvNest.exe` is registered automatically as a local
driver repository, and any vendor folder you extracted yourself works too.

### Can I roll a driver back?

Yes, three ways: from the backup DrvNest exports immediately before each install (**Restore from
folder**), from the system restore point created before the first install of a session
(`rstrui.exe`), or with Windows' own *Roll Back Driver* button in Device Manager. Which is why
leaving the restore-point setting on is recommended.

### Does it really resume after a reboot?

Yes. Queue state is written atomically to `session.json` on every change, and a logon-triggered
`DrvNest\ResumeSession` scheduled task (with an HKLM `RunOnce` fallback) brings DrvNest back with
`--resume`. A session survives at most 10 restarts; the task and the registry value are removed once
the queue finishes.

### Does DrvNest collect any data?

No. No telemetry, no usage statistics, no device identifiers. Exactly two things leave the machine:
Windows Update queries, which go straight to Microsoft through Windows' own agent and never happen
in offline mode, and one request to the GitHub Releases API when you press *Check for updates*.

---

"Why is the exe so big?", "Why no WMI?", "Does it work on Windows Server?" and the rest:

**[docs/FAQ.md](docs/FAQ.md)** · Usage guide (Turkish): **[docs/USAGE.md](docs/USAGE.md)** ·
Project site: **[ahmetcaglayan.github.io/DrvNest](https://ahmetcaglayan.github.io/DrvNest/)**

---

## 🤝 Contributing

Contributions are welcome.

- **Bug reports:** [Issues](../../issues) — please attach the log from the *Copy* button
  in the **Logs** menu; it already carries the version and OS header.
- **Code:** fork, branch, open a pull request. Keep the existing style: no NuGet
  dependencies (single-file size and offline operation are deliberate choices), and no UI
  code inside `DrvNest.Core`.
- **Translation:** adding a language means adding one dictionary to
  `src/DrvNest.App/Services/Loc.cs`; see [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

---

## 📄 License

MIT — see [LICENSE](LICENSE).

---

## ⚠️ Disclaimer

Installing drivers carries inherent risk. A wrong or corrupt driver can cause problems up
to and including a machine that will not boot. DrvNest reduces that risk by creating a
system restore point and backing up the drivers it replaces, but it offers no guarantee.

**Leave the restore point setting on.** Keep backups of anything you care about. The
software is provided "as is"; the consequences of using it are the user's responsibility.

---

## Keywords

<sub>
windows driver updater open source · free driver updater no adware · install drivers after format ·
offline driver installer usb · driver backup restore windows · missing driver finder ·
windows 11 driver scanner · pnputil driver export · windows update driver catalog tool ·
device manager yellow exclamation mark fix
</sub>
