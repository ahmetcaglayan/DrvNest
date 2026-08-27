# Security Policy

## Reporting a vulnerability

**Please do not open a public issue for a security problem.**

Report it privately through GitHub Security Advisories:

<https://github.com/ahmetcaglayan/DrvNest/security/advisories/new>

That form is private between you and the maintainers, and it lets us prepare a
fix and a release before anything becomes public.

Useful things to include:

- What an attacker can do, and what they need in order to do it (local user?
  administrator already? a file on disk? a network position?).
- Steps to reproduce, ideally with the DrvNest version and Windows build.
- Relevant lines from `%ProgramData%\DrvNest\logs\drvnest.log`.
- Whether you would like to be credited in the advisory, and under what name.

You should get an acknowledgement within a few days. This is a volunteer project,
so please be patient if it takes a little longer; we will keep you updated on the
fix and coordinate disclosure with you.

If you are unsure whether something is a security issue, treat it as one and use
the private form. A normal bug filed privately costs nothing; a vulnerability
filed publicly cannot be taken back.

For anything that is clearly **not** a security issue, the normal tracker is the
right place: <https://github.com/ahmetcaglayan/DrvNest/issues>

## Supported versions

| Version | Supported |
| ------- | --------- |
| Latest release | Yes |
| Anything older | No |

DrvNest ships as a single self-contained executable with a built-in updater.
There are no maintenance branches: fixes go into the next release, and the
in-app updater moves everyone forward. If you are not on the latest release,
please update before reporting.

## Threat model

Be aware of what this program is before you assess a finding.

**DrvNest runs elevated and installs kernel-mode drivers.** Its manifest requires
administrator rights, it calls SetupAPI and `pnputil`, it drives the Windows
Update Agent, it creates System Restore points through `srclient.dll`, and it can
register a scheduled task that runs at the highest privileges to resume work
after a reboot. A driver installed by DrvNest runs in the kernel.

That means the interesting consequences here are not "DrvNest crashes" but
"DrvNest can be made to install something". Findings we care about most:

- **Anything that gets an attacker-controlled driver package installed.** A
  malicious or tampered `.inf`/`.cat` in a local repository path, a path that
  escapes its intended folder, an archive that extracts outside its destination.
- **Tampering with the update path.** The self-updater downloads a release asset
  from GitHub and verifies its SHA-256 against the release's `checksums.txt`
  before it goes anywhere near the installed executable, and it refuses outright
  to install a release that publishes no checksum file. A way around that check
  is a serious bug.
- **Privilege escalation via the files DrvNest writes.** State lives in
  `%ProgramData%\DrvNest` so that the resume task can find it after a restart. A
  way for a non-administrator to influence what the elevated process then reads
  or executes matters.
- **The resume mechanism.** The scheduled task and the `HKLM\...\RunOnce`
  fallback both launch an executable with high privileges. Anything that lets an
  unprivileged user point either of them somewhere else is in scope.
- **Backup and restore.** Restoring a driver backup installs whatever the archive
  contains. Manifest or path handling that can be abused is in scope.

Out of scope, because they are how the program is meant to work:

- DrvNest requires administrator rights and shows a UAC prompt. That is by design.
- An administrator can already install any driver on the machine without DrvNest.
- Drivers offered by Microsoft Update are Microsoft's to vouch for, not ours.
- DrvNest does not bypass Windows driver signature enforcement, and will not be
  changed to.

## Verifying a download

Every release publishes `checksums.txt` alongside the executables. Check your
download before you run it - this is a program you are about to give
administrator rights to.

```powershell
Get-FileHash .\DrvNest.exe -Algorithm SHA256
```

Compare the result with the line for `DrvNest.exe` in `checksums.txt` from the
same release. The file is in standard `sha256sum` format:

```
<hash>  DrvNest.exe
<hash>  DrvNest-arm64.exe
```

The hashes there are lowercase and PowerShell prints uppercase; the comparison is
not case sensitive. If they do not match, do not run the file, and please tell us.

Downloads must come from the releases page of this repository:

<https://github.com/ahmetcaglayan/DrvNest/releases>

DrvNest is not code-signed - a certificate costs more than this project has - so
SmartScreen will warn you the first time. That is expected, and it is exactly why
the checksums are published.
