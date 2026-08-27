# Changelog

All notable changes to DrvNest are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

Nothing yet.

## [1.0.0] - 2026-08-27

First release. DrvNest is a Windows driver scanner, installer and updater built
for the first boot after a format, when nothing is installed and the network
adapter may not have a driver either.

### Added

**Deployment**

- Ships as a single self-contained executable with the .NET 8 runtime inside it.
  No .NET install, no Visual C++ redistributable, no internet connection needed to
  start it. Windows 10 1607 or newer.
- Builds for x64 (`DrvNest.exe`) and ARM64 (`DrvNest-arm64.exe`).
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
- **Local repository**: folders, ZIP archives, network shares, DrvNest backups, or
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
  `%ProgramData%\DrvNest`, so the resume task finds the same files even when it
  runs as SYSTEM or as a different administrator account. Falls back to the user
  profile when ProgramData is not writable.

[Unreleased]: https://github.com/ahmetcaglayan/DrvNest/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/ahmetcaglayan/DrvNest/releases/tag/v1.0.0
