# Building DrvNest

> **Türkçe özet**
>
> Gerekenler: **.NET 8 SDK** (Windows üzerinde) ve **Windows 10 1607 veya üstü**.
> Tek satırlık yol:
>
> ```powershell
> dotnet publish src/DrvNest.App/DrvNest.App.csproj -c Release -r win-x64 -o publish
> ```
>
> Sonuç `publish\DrvNest.exe` — kendi kendine yeten (self-contained), tek dosya, yaklaşık
> 65 MB. Ayarlar zaten `.csproj` içinde tanımlı, komuta ek bayrak vermeniz gerekmez.
> **Derleme yalnızca Windows'ta yapılabilir**, çünkü Windows Update COM referansı
> işletim sistemindeki tür kitaplığından üretilir.

---

## Prerequisites

| Requirement | Notes |
| --- | --- |
| **.NET 8 SDK** | Windows build of the SDK. `dotnet --version` should report 8.0.x or newer. |
| **Windows 10 1607 (build 14393) or newer** | Also the minimum the published app supports (`SupportedOSPlatformVersion`). |
| Nothing else | No NuGet packages are restored beyond the SDK's own, and there are no submodules. |

Visual Studio is not required. Visual Studio 2022 (17.8+) with the *.NET desktop
development* workload works if you prefer an IDE.

> **The build must run on Windows.** The projects target `net8.0-windows`, use WPF, and
> call SetupAPI, CfgMgr32 and the Windows Update Agent. There is no cross-platform
> substitute, so CI uses a `windows-latest` runner.
>
> The build needs **nothing but the .NET 8 SDK** — no Visual Studio, no Build Tools, no
> NuGet packages at all.

---

## Commands

```powershell
# Restore
dotnet restore src/DrvNest.sln

# Compile everything
dotnet build src/DrvNest.sln -c Release

# Produce the shippable single file (x64)
dotnet publish src/DrvNest.App/DrvNest.App.csproj -c Release -r win-x64 -o publish
```

The publish output is `publish\DrvNest.exe`. Copy it anywhere; it needs no companion files.

If you prefer to work project by project (or the solution file is not present in your
checkout), the same commands work against the project files directly:

```powershell
dotnet restore src/DrvNest.App/DrvNest.App.csproj
dotnet build   src/DrvNest.App/DrvNest.App.csproj -c Release
```

### ARM64

```powershell
dotnet publish src/DrvNest.App/DrvNest.App.csproj -c Release -r win-arm64 -o publish-arm64
```

The release workflow publishes this as `DrvNest-arm64.exe`; `AppInfo.ReleaseAssetArm64`
is the name the built-in updater looks for when it runs on an ARM64 machine.

### Setting the version

`Directory.Build.props` holds the single source of truth (`<Version>1.1.0</Version>`).
CI overrides it from the release tag so the tag and the version shown inside the app can
never drift apart:

```powershell
dotnet publish src/DrvNest.App/DrvNest.App.csproj -c Release -r win-x64 -o publish -p:Version=1.2.3
```

`AppInfo.Version` reads `AssemblyInformationalVersionAttribute` at runtime and strips the
`+<commit>` suffix the SDK appends.

### Regenerating the screenshots

The images in `assets/screenshots/`, which the README and the website both use, are
produced by the application itself rather than taken by hand:

```powershell
# from an elevated prompt, against a build
.\DrvNest.exe --capture .\assets\screenshots --lang en
```

It walks every menu entry, waits for the live pages to fill their charts, writes one PNG
per page (plus a second, scrolled shot of the pages whose table falls below the fold) and
exits. `--lang` pins the interface language, so the published images do not depend on the
display language of whoever regenerated them.

Two things make this worth having rather than a manual chore. DrvNest runs elevated, and
User Interface Privilege Isolation stops the unelevated Snipping Tool from seeing input
aimed at a higher-integrity window — Print Screen over DrvNest does nothing, the same way
it does nothing over Task Manager. And documentation screenshots rot: regenerating them is
one command, so a UI change and its pictures stay in step.

Run it elevated. Unelevated, the network page cannot enable the per-connection byte
counters and the sidebar reports "Not elevated", both of which show up in the image.

Then copy them where the website expects them:

```powershell
Copy-Item .\assets\screenshots\*.png .\docs\site\screenshots\ -Force
```

### `build/check-site.py`

The five language pages are one document in five languages, so everything except the
words has to match. `build/check-site.py` compares each translation against
`docs/site/index.html` element by element and reports any divergence in structure, ids,
classes, image paths, relative links or inline SVG geometry:

```powershell
python build/check-site.py          # every language
python build/check-site.py ru zh    # just these
```

It exits non-zero on the first failure, so it can be wired into CI.

### `build/publish.ps1`

`build/publish.ps1` is the convenience wrapper around the commands above — it publishes the
selected runtime identifier, stamps the version, and prints the SHA-256 and size of the
result. Running the raw `dotnet publish` command does exactly the same build; the script
only automates the chores around it.

`checksums.txt`, which the built-in updater verifies downloads against, is written by
`build/make-release.ps1` and by the release workflow — not by `publish.ps1`, which handles
one runtime at a time.

---

## What the project file already sets

You do **not** need to pass `--self-contained`, `-p:PublishSingleFile=true` or similar on
the command line. `DrvNest.App.csproj` sets all of it:

```xml
<RuntimeIdentifier Condition="'$(RuntimeIdentifier)' == ''">win-x64</RuntimeIdentifier>
<SelfContained>true</SelfContained>
<PublishSingleFile>true</PublishSingleFile>
<IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
<EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
<PublishTrimmed>false</PublishTrimmed>
<PublishReadyToRun>true</PublishReadyToRun>
<DebugType>embedded</DebugType>
<SupportedOSPlatformVersion>10.0.14393.0</SupportedOSPlatformVersion>
```

The `RuntimeIdentifier` is conditional, so passing `-r win-arm64` overrides it cleanly.

Consequences worth knowing:

- **The output is one file of roughly 65 MB.** That is the compressed .NET 8 runtime plus
  WPF plus the application. It is the price of "works on a machine with nothing installed".
- **WPF's runtime pack includes `vcruntime140_cor3.dll` and `msvcp140_cor3.dll`**, so no
  Visual C++ Redistributable is required on the target machine.
- **`DebugType=embedded`** puts the PDB inside the executable: stack traces in the log have
  line numbers, and there is still only one file to ship.
- **First launch extracts native libraries** to a temporary folder, which is why the very
  first start is a little slower than subsequent ones.

### Why trimming and NativeAOT are off

Both are intentionally disabled, and neither should be re-enabled without a plan for the
following:

- **WPF is reflection-heavy.** XAML is resolved to types at runtime, `DynamicResource`
  lookups and data binding both go through reflection, and the trimmer cannot see any of
  it. A trimmed build compiles and then fails at runtime with missing-type errors in views
  that were never exercised during testing.
- **The WUApiLib interop is COM.** `Providers/WuaCallbacks.cs` implements COM callback
  interfaces (`ISearchCompletedCallback`, `IDownloadProgressChangedCallback` and friends)
  and `WindowsUpdateProvider.ReadDriverVersion` uses `dynamic` late binding so that a
  Windows build without `IWindowsDriverUpdate5` degrades gracefully instead of failing the
  whole scan. Trimming and NativeAOT are both hostile to that.
- **NativeAOT does not support WPF** at all.

`PublishReadyToRun` gives most of the startup benefit without any of the risk.

---

## Windows Update Agent interop

DrvNest talks to the Windows Update Agent, which is a COM API. There are two ways to do
that from .NET, and the choice here is deliberate.

### What this project does not do

The obvious approach is a `<COMReference Include="WUApiLib">` with `WrapperTool=tlbimp`,
which generates an interop assembly from the type library registered on the machine.
**That does not work with `dotnet build`:**

```
error MSB4803: The task "ResolveComReference" is not supported on the .NET Core version
of MSBuild. Please use the .NET Framework version of MSBuild.
```

`ResolveComReference` only exists in the .NET Framework build of MSBuild, so a
`COMReference` would mean nobody could compile DrvNest — or run CI — without a full
Visual Studio installation. For a project whose whole premise is "one file, no
prerequisites", that was the wrong trade.

### What this project does instead

Two layers, in `src/DrvNest.Core/Providers/`:

- **`WuaInterop.cs`** declares the five COM interfaces WUA *calls back into*
  (`ISearchCompletedCallback`, `IDownloadProgressChangedCallback`,
  `IDownloadCompletedCallback`, `IInstallationProgressChangedCallback`,
  `IInstallationCompletedCallback`). These need exact GUIDs and an exact vtable layout,
  because Windows invokes them through a function pointer table.

  Every GUID was **read out of `%SystemRoot%\System32\wuapi.dll` with `ITypeLib`**, not
  copied from memory or a blog post. All five derive from `IUnknown` (verified: vtable
  offset 24 on x64) and declare a single `Invoke` method taking two interface pointers.
  Their parameters are declared as `object` with `UnmanagedType.Interface`, so the
  argument types do not have to be declared by hand either.

- **`WindowsUpdateProvider.cs`** makes every *outbound* call through `IDispatch` using
  C# `dynamic`, with objects created from ProgIDs (`Microsoft.Update.Session`,
  `Microsoft.Update.UpdateColl`, `Microsoft.Update.ServiceManager`). No GUIDs, no vtables,
  nothing that can be silently wrong. A property a particular Windows build does not
  expose throws at the binder and is caught, so one missing field never costs the user a
  whole scan.

The enum values used (`ssOthers = 3`, `dpHigh = 3`, `orcSucceeded = 2`,
`asf* = 1|2|4`, …) were likewise read from the type library and are listed in
`WuaConstants`.

The result: `dotnet build` alone compiles the project, on any Windows machine with the
.NET 8 SDK, with zero NuGet packages and zero external tooling.

---

## Continuous integration

## Continuous integration

CI lives in `.github/workflows/` and must run on `windows-latest` for the reason above. A
release build does, in order:

1. `dotnet publish … -r win-x64 -p:Version=<tag>` → `DrvNest.exe`
2. `dotnet publish … -r win-arm64 -p:Version=<tag>` → `DrvNest-arm64.exe`
3. Compute SHA-256 for both and write `checksums.txt` in `sha256sum` format
   (`<hash>  <filename>`, two spaces).
4. Attach all three files to the GitHub release.

Step 3 is not optional. `SelfUpdateService.VerifyChecksumAsync` **refuses to install a
release that publishes no `checksums.txt`**, and refuses one whose hash does not match. A
release without that file will simply never auto-update anyone.

The asset names are fixed by `AppInfo.ReleaseAssetX64`, `AppInfo.ReleaseAssetArm64` and
`AppInfo.ChecksumAsset`. Renaming an asset without changing those constants breaks the
updater silently.

---

## Forking

`Core/AppInfo.cs` holds the two constants the built-in updater uses to find releases:

```csharp
public const string RepositoryOwner = "ahmetcaglayan";
public const string RepositoryName  = "DrvNest";
```

Change them to your own repository in a fork, or your builds will offer your users updates
from someone else's project.

---

## Troubleshooting

| Symptom | Cause and fix |
| --- | --- |
| `MSB4803: ResolveComReference is not supported` | A `COMReference` was added to a project. DrvNest deliberately avoids these - see *Windows Update Agent interop* above. |
| `NETSDK1100` / "Windows is required to build" | Building on Linux or macOS. Both projects set `EnableWindowsTargeting`, but the COM reference still needs real Windows. |
| Published exe is ~150 MB | `EnableCompressionInSingleFile` was overridden. Do not pass `-p:EnableCompressionInSingleFile=false`. |
| Published output is a folder of DLLs | `-r <rid>` was omitted, so the conditional `RuntimeIdentifier` never triggered single-file publish. Always pass an explicit `-r`. |
| App starts without a UAC prompt and installs fail | `app.manifest` was not applied. Check `ApplicationManifest` in `DrvNest.App.csproj`. |
| Missing `Assets\drvnest.ico` warning | Harmless. The `ApplicationIcon` property is wrapped in an `Exists(...)` condition precisely so a clean clone still builds. |
