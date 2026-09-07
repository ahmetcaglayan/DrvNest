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

English is the master and lives in `src/Hexnest.Core/Localization/Loc.cs`, as a
plain `Dictionary<string, string>`. There is no RESX and there are no satellite
assemblies, deliberately - satellite assemblies fight the single-file publish, and
this app has a few hundred strings rather than a few thousand.

Adding a language does not mean touching any code:

1. Copy an existing pack from `src/Hexnest.Core/Languages/` to `<code>.json` and
   translate the values. Keys never change. Keep `_name` (the language's own name,
   which is what the picker shows) and `_englishName`.
2. Run `python3 build/check-languages.py`. It compares your pack against English
   and fails on missing keys, on `{0}` placeholders that did not survive the
   translation, and on multi-line strings that were flattened into one paragraph.
3. Build. That is all: the pack is embedded by a wildcard in `Hexnest.Core.csproj`,
   discovered at startup by `LoadEmbeddedPacks`, and sorted into the picker by the
   name you put in `_name`. Both applications pick it up, because both read the
   same `Loc`.

A pack does not even have to be built in. A `Languages/<code>.json` dropped next to
the executable, or into the data folder, is merged over the built-in one at startup -
which is how a translation can be corrected without a rebuild.

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
