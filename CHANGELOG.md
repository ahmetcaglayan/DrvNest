# Changelog

All notable changes to Hexnest are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.3.0] - 2026-09-07

### Added

**Hexnest for Mac**

- A macOS application, built on Avalonia over the same `Hexnest.Core`. The system
  monitor, the network monitor, the start-up manager, the cleaner, the logs, the
  settings, all five interface languages and both themes are the same product, not a
  reimplementation: `Hexnest.Core` now multi-targets `net8.0-windows` for the driver
  half and a portable `net8.0` for everything else, and the Windows compilation is
  byte-for-byte what it was.
- **System monitor.** Total and per-logical-core processor load from the mach tick
  counters, including the performance and efficiency clusters on Apple silicon; memory
  using Activity Monitor's own definition, so the two agree; swap; storage; battery;
  uptime from `kern.boottime` rather than a monotonic clock, so a laptop's answer is
  not days out. The process table comes from libproc: processes owned by another user
  are listed and marked, never silently dropped.
- **Network monitor.** Live throughput and per-application traffic from `nettop`, the
  same statistics Activity Monitor's Network tab reads, started with the page and
  stopped with it. Per-adapter counters come from `NET_RT_IFLIST2` alongside.
- **Start-up manager.** Every launchd job from `~/Library/LaunchAgents`,
  `/Library/LaunchAgents` and `/Library/LaunchDaemons`. Toggling writes a `launchctl`
  override, which is the same mechanism macOS itself uses - the property list is never
  moved or edited, so re-enabling restores the job exactly as its author configured it.
  System-wide jobs are shown and marked read-only.
- **Clean-up.** Application caches, developer caches (Xcode derived data, simulator
  caches, Homebrew, npm, pip, Yarn, Gradle, Go, Cargo), logs, saved window state and the
  Trash; plus old downloads, iPhone and iPad backups and leftover Application Support
  folders reviewed item by item. Measured rather than estimated, nothing ticked by
  default, and your own files go to the Trash rather than being erased.
- **A hardware summary on the dashboard**, on both platforms: model, system, processor,
  graphics, memory and storage. On Windows the graphics adapter comes from
  `Win32_VideoController` and the memory from `GlobalMemoryStatusEx`, both read once at
  startup.
- `build/make-mac-app.sh` builds `Hexnest.app` and the disk images, writes the
  `Info.plist`, assembles the `.icns` and signs the bundle ad-hoc. `--capture`
  regenerates the macOS screenshots the same way the Windows build already does.

### Changed

- **The project is now called Hexnest.** It began as a Windows driver updater called
  DrvNest, and the name had become a description of a quarter of what it does. The
  hexagonal nest cell in the logo is unchanged; only the "Drv" has gone.
- The Mac build **never runs as root**, by design. Everything it does lives inside the
  account that launched it: the monitors read public statistics, the cleaner works in
  the user's home folder, and the start-up manager changes that user's own login agents.
  A GUI running as root can delete anything on the machine by accident, and nothing on
  these pages is worth that.
- The string table moved from `Hexnest.App` into `Hexnest.Core`, so both applications
  display the same text in the same five languages from one file rather than two copies
  that would have drifted apart within a release.

### Notes

- macOS has no third-party driver store, so the Mac build has no *Devices*, *Updates*,
  *Activity*, *Backup* or *History* page. Apple ships device support inside the operating
  system; there is nothing there for a tool like this to scan, download or back up, and
  five permanently empty pages would have been worse than five absent ones.
- Processor temperature, per-volume disk throughput and working-set trimming are not
  available to an unprivileged process on macOS. Hexnest shows nothing for them rather
  than an invented number, and says why where each would have appeared.

## [1.2.0] - 2026-09-07

Two maintenance pages, both of which only ever act when you press something.

### Added

**Startup Programs**

- Every autostart entry Hexnest can safely toggle: the `Run` and `RunOnce` keys under
  HKCU and HKLM, the 32-bit `Wow6432Node` view, and both Startup folders. Each row
  carries the program name from the executable's version resource, its publisher, the
  command line, the size and where it starts from.
- A switch per entry. Turning one off writes the same `StartupApproved` value Task
  Manager writes, so the two always agree and **nothing is ever deleted** - the command
  line stays exactly where it is and switching back on restores it unchanged. The
  alternative, deleting the value and remembering it in Hexnest's own settings, is easier
  to write and quietly makes Hexnest load-bearing for someone else's software.
- Entries pointing at a file that no longer exists are flagged: Windows tries to run them
  at every logon and fails silently.
- Security software is marked, and turning one off asks first.
- Filters for on, off and broken, plus search across name, publisher and command line.

**Clean Up**

- Measures, rather than estimates, what each category holds: temporary files, the
  thumbnail and icon cache, browser caches for seven browsers, the Windows Update
  download cache, Delivery Optimization, crash dumps, error reports, DirectX/NVIDIA/AMD/
  Intel shader caches, Windows servicing logs, Hexnest's own driver cache and the
  Recycle Bin. The number next to a box is the space that will actually come back.
- **Nothing is ticked by default.** The page opens with a total of zero.
- Two categories hold the user's own files and are never bulk-selected: old downloads,
  and folders under AppData that match no installed program, no running program and
  nothing in Program Files, and have not been written to for six months. Both are listed
  item by item with a size and an age, each ticked individually, and both go to the
  **Recycle Bin** rather than being deleted outright - a wrong guess is recoverable.
- Every path comes from a well-known folder API, reparse points are never followed, and
  every single deletion is re-checked against its own category's roots before it happens.
  Files that are open are skipped rather than forced.
- A memory trim that says what it actually does: it pages out working sets, physical
  memory in use genuinely drops, and Windows reads those pages straight back as the
  programs are used again. It is useful immediately before something that needs a lot of
  memory at once, and the page says so instead of promising a faster computer.

**Languages**

- All 93 new strings translated into Russian, Simplified Chinese and Hindi alongside
  English and Turkish.

### Fixed

- `SHQUERYRBINFO` had its two 64-bit fields declared in the wrong order, so the Recycle
  Bin reported its byte count as a file count and its file count as a size - "44 B" and
  "1,169,713,882 files" for a bin holding 1.1 GB in 44 items.
- The leftover-folder detector compared raw folder names against raw display names, so
  `%APPDATA%\riot-client-ux` was offered for deletion while Riot Client was installed.
  Names are now normalised before comparison, running processes count as evidence that
  software is installed, and the age test uses the newest file anywhere in the tree
  rather than the folder's own timestamp, which does not change when something several
  levels down is written.

## [1.1.0] - 2026-09-07

Two new pages and an updater that no longer waits to be asked.

### Added

**System Monitor**

- A live panel for the whole machine: total processor load and a bar for every
  logical processor, current clock, process / thread / handle counts and uptime.
- Memory broken down into in use, available, cached and committed, with the
  commit limit, read from `GlobalMemoryStatusEx` and `GetPerformanceInfo`.
- Temperature from the ACPI thermal zones the firmware publishes
  (`root\WMI:MSAcpi_ThermalZoneTemperature`), reached through the WbemScripting
  COM class rather than a NuGet package. Machines that publish no thermal zone -
  most desktops - get an explanation of why, not an invented number.
- Storage: capacity and free space per fixed drive, plus live read and write
  throughput from `IOCTL_DISK_PERFORMANCE`.
- Battery percentage, charge state and remaining time on machines that have one.
- A table of every running process with its processor share, working set, private
  bytes, disk throughput and thread count. Sortable by processor, memory, disk or
  name, searchable by name or pid, and pausable so a row can be read. Processor
  usage is measured the way Task Manager measures it: the change in a process' own
  kernel + user time between two samples over the wall clock and the logical
  processor count.

**Network Monitor**

- Live download and upload for the whole machine from the adapters' own byte
  counters, with totals for this session and since Windows started.
- Every adapter with its type, address, negotiated link speed and current rate;
  disconnected adapters are hidden behind a toggle.
- A per-application table: download and upload rate, session totals, open
  connection count and the remote endpoint, built from `GetExtendedTcpTable` plus
  TCP ESTATS (`GetPerTcpConnectionEStats`).
- The page states plainly that per-application figures cover TCP only, because
  Windows exposes no per-process UDP counter without a kernel driver, and that the
  machine total therefore does not equal the sum of the rows.

**Automatic updates**

- A daily check against the GitHub Releases API, twenty seconds after startup so it
  never competes with the opening scan, showing a count on the *About* menu entry
  when a newer release exists.
- Optional automatic download and install, off by default. The download is verified
  against the release's `checksums.txt` and the swap only happens as Hexnest closes,
  so an update can never land in the middle of a driver queue.
- Both are skipped entirely in offline and rescue mode.
- New settings: automatic check, automatic install, include pre-releases.

**Languages**

- Russian, Simplified Chinese and Hindi, in addition to English and Turkish. They
  are compiled into the executable as JSON, so the single-file publish keeps
  working; a `Languages\<code>.json` next to the executable still overrides or adds
  a translation with no rebuild.

**Documentation**

- `Hexnest.exe --capture <folder> [--lang <code>]` walks the whole menu and writes
  one PNG per page, so the screenshots in the README and on the website are
  regenerated from the build rather than taken by hand.
- The website gained a screenshot carousel, two diagrams, and Russian, Chinese and
  Hindi pages.

### Fixed

- A wrapping notice inside a horizontal `StackPanel` never actually wrapped, because
  a `StackPanel` measures its children with infinite width; long warnings on the
  dashboard were silently clipped at the edge of the card. Both notice banners now
  use a `Grid`.
- A language switch only set `CultureInfo.DefaultThreadCurrent*`, which seeds threads
  created afterwards but not the user interface thread, so the application formatted
  numbers two different ways at once - a handle count rendered as `276.132` on the UI
  thread and `276,132` on a worker thread of the same machine.

### Changed

- Text on the website is no longer capped to a narrow measure, so it lines up with
  the navigation instead of leaving a wide empty column on the right.

## [1.0.0] - 2026-08-27

First release. Hexnest is a Windows driver scanner, installer and updater built
for the first boot after a format, when nothing is installed and the network
adapter may not have a driver either.

### Added

**Deployment**

- Ships as a single self-contained executable with the .NET 8 runtime inside it.
  No .NET install, no Visual C++ redistributable, no internet connection needed to
  start it. Windows 10 1607 or newer.
- Builds for x64 (`Hexnest.exe`) and ARM64 (`Hexnest-arm64.exe`).
- Optional Inno Setup installer for people who would rather have a Start menu
  entry than a file on the desktop.

**Scanning**

- Enumerates every present PnP device through SetupAPI and CfgMgr32 only, so it
  works on a freshly formatted machine with no network and a possibly broken WMI
  repository.
- Classifies each device as healthy, missing a driver, faulty, disabled or
  waiting for a restart, mapped from the Configuration Manager problem codes.
- Flags devices still running a generic in-box Microsoft driver where a vendor
  driver would be better.

**Driver sources**

- **Windows Update** through the Windows Update Agent COM API, registered against
  Microsoft Update so driver offers actually appear. Optional driver offers can be
  included or left out.
- **Local repository**: folders, ZIP archives, network shares, Hexnest backups, or
  a `Drivers` folder dropped next to the executable on a USB stick. This is what
  makes the post-format case work at all - no network driver means no Windows
  Update.
- Both sit behind one `IDriverProvider` interface, so another source can be added
  without touching the job engine or the UI.

**Installing**

- Job queue with configurable parallel downloads (1-8). Installs are serialised
  because Windows permits only one driver installation at a time.
- Automatic retries for failed jobs, with a configurable limit.
- Cancellation that actually stops an in-flight download.

**Safety**

- Creates a System Restore point before the first install of a session, through
  `srclient.dll` directly so it keeps working when WMI does not.
- Exports the current driver package before replacing it, so a bad update can be
  rolled back.
- Backup and restore of installed driver packages via `pnputil`, written as an
  archive with a manifest recording the machine, OS build, architecture and every
  package inside it.

**Reboots**

- Detects when an install needs a restart and keeps the job in a pending state
  rather than claiming success.
- Resumes the queue after a restart through a scheduled task running with the
  highest privileges, falling back to `HKLM\...\RunOnce` when Task Scheduler is
  unavailable. Both hooks are removed as soon as the queue finishes.
- Optional automatic restart with a grace period.

**Interface**

- WPF application with Dashboard, Devices, Updates, Queue, Backup & Restore,
  History, Logs, Settings and About pages.
- Dark and light themes.
- English and Turkish, switchable at runtime.
- Live diagnostic log with a one-click Copy button, because driver failures
  surface as opaque HRESULTs and a bug report needs something to paste.
- Persistent install history with configurable retention.
- Rescue Mode: offline only, no Windows Update calls at all.

**Updating itself**

- Checks GitHub Releases for a newer version and can replace itself in place.
- Every download is verified against the release's `checksums.txt` before it goes
  anywhere near the installed executable, and a release that publishes no checksum
  file is refused rather than trusted.

**State**

- Settings, session, history, logs, backups, cache and reports live under
  `%ProgramData%\Hexnest`, so the resume task finds the same files even when it
  runs as SYSTEM or as a different administrator account. Falls back to the user
  profile when ProgramData is not writable.

[Unreleased]: https://github.com/ahmetcaglayan/Hexnest/compare/v1.3.0...HEAD
[1.3.0]: https://github.com/ahmetcaglayan/Hexnest/compare/v1.2.0...v1.3.0
[1.2.0]: https://github.com/ahmetcaglayan/Hexnest/releases/tag/v1.2.0
[1.1.0]: https://github.com/ahmetcaglayan/Hexnest/releases/tag/v1.1.0
[1.0.0]: https://github.com/ahmetcaglayan/Hexnest/releases/tag/v1.0.0
