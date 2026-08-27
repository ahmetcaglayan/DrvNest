# Code signing

## The problem

`DrvNest.exe` is not signed, so Windows shows **"Unknown publisher"** on the UAC prompt and
SmartScreen shows *"Windows protected your PC"* on first run. On a clean Windows 11 install,
Smart App Control may block it outright.

None of that is a bug, and no change to the build can fix it. Windows is reporting a fact:
nobody has vouched for who produced this file. The only fix is a code-signing certificate
issued to a verified identity.

Until then, users can verify a download is exactly what was published:

```powershell
Get-FileHash .\DrvNest.exe -Algorithm SHA256
```

and compare it against `checksums.txt` in the same release.

---

## The options, cheapest first

| Route | Cost | Removes "Unknown publisher" | Instant SmartScreen trust | Notes |
|---|---|---|---|---|
| **[SignPath Foundation](https://signpath.io/open-source)** | Free | Yes | No, builds over time | For OSS projects. Requires an application and a review; the project must be public, have a clear licence and a reproducible CI build. DrvNest meets all three. |
| **[Azure Trusted Signing](https://learn.microsoft.com/azure/trusted-signing/)** | ~$10 / month | Yes | Yes | Microsoft's own service, by far the cheapest paid route. Needs a verified identity: an organisation, or an individual with three years of verifiable history. |
| **OV certificate** (Sectigo, DigiCert…) | ~$200–400 / year | Yes | No, builds over time | The traditional route. A file-based `.pfx` you hold yourself. |
| **EV certificate** | ~$400–700 / year | Yes | Yes | Requires a hardware token, which makes CI signing awkward. Rarely worth it over Azure Trusted Signing now. |

**Self-signed certificates do not work.** Windows will not trust one unless it is installed in
the machine's Trusted Publishers store, which no ordinary user will do — and asking them to
would be indistinguishable from what malware asks for.

---

## Wiring it up

The release workflow already has the signing step. It is skipped while the secrets are absent,
so nothing changes until you add them.

For **SignPath** or an **OV certificate** (a `.pfx` file):

1. Base64-encode the certificate:

   ```powershell
   [Convert]::ToBase64String([IO.File]::ReadAllBytes('certificate.pfx')) | Set-Clipboard
   ```

2. In the repository: **Settings → Secrets and variables → Actions → New repository secret**

   | Secret | Value |
   |---|---|
   | `SIGNING_CERTIFICATE_BASE64` | the base64 string from step 1 |
   | `SIGNING_CERTIFICATE_PASSWORD` | the `.pfx` password |

3. Tag the next release as usual. The workflow signs every `.exe` in `dist` before the
   checksums are generated, so the published hashes cover the signed files.

For **Azure Trusted Signing**, replace the `Sign the executables` step with
`azure/trusted-signing-action@v0` and its own credentials — the rest of the workflow is
unchanged.

---

## What signing does not do

Signing proves who published the file. It does not make the file safe, and it does not
by itself clear SmartScreen: a brand-new OV certificate still has no reputation, and the
warning fades as downloads accumulate. An EV certificate or Azure Trusted Signing skips
that waiting period.
