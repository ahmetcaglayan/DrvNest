# What this changes

<!-- One or two sentences. Link the issue it fixes: "Fixes #123". -->

## How it was tested

<!--
Which Windows version, and on real hardware or a VM? For driver changes, say
which device and hardware ID you tried it against.
-->

## Checklist

- [ ] Builds clean: `dotnet build src/Hexnest.sln -c Release`
- [ ] The single-file publish still works: `pwsh build\publish.ps1`
- [ ] `Hexnest.Core` gained no NuGet dependency and no UI reference
- [ ] New user-visible strings go through `Loc.T(...)` in **both** English and Turkish
- [ ] Tested on a real machine if this touches scanning, installing, backup or
      resume-after-reboot
- [ ] `CHANGELOG.md` updated under `## [Unreleased]`
- [ ] Release asset names are unchanged, or `AppInfo.cs`, `SelfUpdateService.cs`
      and `.github/workflows/release.yml` were all updated together

## Notes for the reviewer

<!-- Anything you are unsure about, or deliberately left out. -->
