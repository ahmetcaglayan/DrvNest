# Contributing to Hexnest

Thanks for taking the time. Hexnest exists for one moment: the first boot after a
format, when the network adapter has no driver and nothing else on the machine
works either. Every decision in this repository comes back to that.

## Building

You need **Windows** and the **.NET 8 SDK**. That is the whole list.

```powershell
git clone https://github.com/ahmetcaglayan/Hexnest.git
cd Hexnest
dotnet build src/Hexnest.sln -c Release
```

Windows is not negotiable as a build platform:

- The app is WPF, and every project targets `net8.0-windows`.
- `Hexnest.Core` calls SetupAPI, CfgMgr32, srclient and the Windows Update Agent.

Nothing beyond the .NET 8 SDK is needed, though: there are no NuGet packages, and the
COM interop is hand-written rather than generated, so `dotnet build` alone is enough.
See [docs/BUILD.md](docs/BUILD.md#windows-update-agent-interop) for why.

To produce the real artifact - one self-contained file with the .NET runtime
inside it:

```powershell
pwsh build\publish.ps1                       # dist\Hexnest.exe
pwsh build\publish.ps1 -Runtime win-arm64    # dist\Hexnest-arm64.exe
```

`build\make-release.ps1 -Version 1.2.3` does both architectures, the optional
Inno Setup installer, and `checksums.txt` in one go. It never runs git; it prints
the tag commands for you.

## Code style

`.editorconfig` has the details. The short version:

- 4 spaces, file-scoped namespaces, nullable enabled.
- `var` when the type is obvious from the right-hand side, the explicit type when
  it is not.
- Comments explain **why**, not what. If a line needs a comment to say what it
  does, rewrite the line.
- Keep the existing XML doc comments on public API up to date.

Nothing here is enforced by the build. `TreatWarningsAsErrors` is off and every
style rule is a suggestion, on purpose: a contributor on a slightly older SDK
patch should never be blocked from compiling.

## The one hard rule: `Hexnest.Core` stays clean

`Hexnest.Core` has **no NuGet dependencies and no UI**. Not "few". None.

- No `PackageReference` in `Hexnest.Core.csproj`. Everything is BCL, P/Invoke into
  SetupAPI / CfgMgr32 / srclient, or the Windows Update Agent COM API that already
  ships with Windows.
- No `System.Windows`, no WPF types, no `Dispatcher`, no `MessageBox`. Core reports
  through events, return values and `Log`; the UI decides what to show.
- No `Console.WriteLine` either - `Hexnest.Cli` and `Hexnest.App` both consume Core.

This is what lets Hexnest be one 65 MB file that runs on a machine with no
runtime installed, no redistributable and no internet. A single package
reference that drags in a native dependency breaks that promise.

If you need something Core cannot do without a dependency, put it in the app
layer or write the twenty lines by hand.

## Adding a driver provider

A provider is a source of driver packages: a vendor catalog, a network share,
WSUS, a driver pack archive. The job engine and the UI only ever see
`IDriverProvider`, so a new source needs no changes in either.

1. Add a value to `ProviderKind` in `src/Hexnest.Core/Models/Enums.cs`.
2. Implement `IDriverProvider`
   (`src/Hexnest.Core/Abstractions/IDriverProvider.cs`) in
   `src/Hexnest.Core/Providers/`. Read `LocalRepositoryProvider` first - it is
   the simpler of the two existing ones.
3. Register it where the providers are composed in `Hexnest.App/Services/AppHost.cs`.

Contracts you must honour:

- **`SearchAsync` never throws for an expected failure.** No internet, no
  permission, a missing folder: set `UnavailableReason`, return an empty list.
  The UI surfaces the reason as a warning instead of a crash.
- **Implementations are called concurrently** for different jobs during download.
  `InstallAsync` is serialised for you by the job engine, because Windows allows
  only one driver installation at a time.
- **Respect the `CancellationToken`.** A user who presses Cancel expects the
  download to stop now, not after the current 400 MB file.
- **Never mark a job succeeded when a reboot is pending.** Return
  `ProviderResult.Ok(rebootRequired: true)` so the resume machinery can do its job.

## Adding a language

The UI strings live in one file: `src/Hexnest.App/Services/Loc.cs`. There is no
RESX and there are no satellite assemblies, deliberately - satellite assemblies
fight the single-file publish, and this app has a few hundred strings rather than
a few thousand.

1. Copy the `English` dictionary in `Loc.cs` and translate the values. Keys never
   change.
2. Add the language to the `switch` in `SetLanguage` and to the culture mapping
   just below it.
3. Add the option to the language picker in the settings page.
4. Allow the new code in `AppSettings.Normalize()`
   (`src/Hexnest.Core/Models/AppSettings.cs`), which currently clamps `Language`
   to `tr` or `en`.

Missing keys fall back to English and then to the key itself, so a partial
translation is still usable - but please finish the dictionary before opening the
PR. Keep device and driver terminology matching what Windows itself uses in that
language; users will be comparing your text against Device Manager.

## Pull requests

- One change per PR. A driver provider and a UI redesign are two PRs.
- Say **what** broke and **how** you reproduced it. For driver bugs include the
  hardware ID and the relevant lines from `%ProgramData%\Hexnest\logs\hexnest.log`.
- Build Release before pushing. CI runs on `windows-latest` and does a real
  single-file publish, so a change that only breaks under `PublishSingleFile`
  will be caught - but finding it locally is faster.
- Test on a real machine when you touch scanning, installing, backup or the
  resume-after-reboot path. These are hard to reason about and easy to get subtly
  wrong; a VM with a snapshot is fine.
- If you change what the release publishes, check `AppInfo.cs` and
  `SelfUpdateService.cs` first. The updater looks for exactly `Hexnest.exe`,
  `Hexnest-arm64.exe` and `checksums.txt`, and it refuses to install a release
  without a checksum file. Renaming an asset silently breaks updates for everyone
  already running Hexnest.
- New UI strings go through `Loc.T(...)` in both languages. No hardcoded text.

## Reporting bugs

Open an issue with the bug report form. The Logs page has a **Copy** button that
puts the whole diagnostic log on the clipboard - that, plus the hardware ID of
the device involved, answers most of what we would otherwise have to ask.

Security issues do not go in the issue tracker. See [SECURITY.md](SECURITY.md).
