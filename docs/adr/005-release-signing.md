# ADR 005 — Release MSIX Signing: Ephemeral Per-Build Certificate

**Status**: Accepted

## Context

Publishing a GitHub Actions release workflow requires an installable, signed `.msix` (Windows
rejects unsigned MSIX installs, even in Developer Mode). This isn't going through the Microsoft
Store or WinGet, so there's no free third-party signing available - the realistic choices were:

1. **Long-lived self-signed cert as a GitHub Actions secret** - generate once, store the base64
   PFX + password as a repo secret, reuse across releases. Users trust the publisher cert once;
   every later release installs/upgrades cleanly.
2. **Ephemeral per-build cert** - generate a throwaway self-signed cert inside the workflow run,
   sign with it, discard it. No secrets to manage, but every release has a different signing
   identity, so users must re-trust (and can't upgrade in place) each version.

Checked `davidegiacometti/CmdPal-Extensions` (a real, actively-maintained multi-extension CmdPal
repo) for precedent: its CI manages no persistent signing secret at all. It treats the CmdPal
in-app store and WinGet as the primary install paths (both sidestep the self-signed trust
problem), with a raw MSIX-from-GitHub-Releases as a secondary "manual" option.

Also discovered empirically while spiking the packaging command locally: `dotnet build
-p:PackageCertificateKeyFile=... -p:PackageCertificatePassword=...` fails to import *any*
password-protected PFX (`APPX0105`/`APPX0107`), regardless of the password value or the
certificate's key provider - a known `dotnet build`/`dotnet publish` limitation
([dotnet/msbuild#9830](https://github.com/dotnet/msbuild/issues/9830),
[microsoft/WindowsAppSDK#4255](https://github.com/microsoft/WindowsAppSDK/issues/4255)). This is
why every official single-project-MSIX CI example uses classic `msbuild.exe` instead. Exporting
the PFX with **no password** and omitting `PackageCertificatePassword` entirely sidesteps the bug,
so plain `dotnet build` (consistent with this repo's existing local dev build command) still
works.

## Decision

Use an ephemeral per-build self-signed certificate (`CN=BtBattery`, matching the manifest
`Publisher`) in `.github/workflows/release.yml`:

- Generated fresh inside each `package` matrix leg (one per platform) via
  `New-SelfSignedCertificate`, exported as a **no-password** PFX (works around the `dotnet build`
  PFX-import bug above), used to sign, then deleted from the runner's cert store.
- The public `.cer` is uploaded alongside its `.msix` as a release asset, so users can trust that
  specific release.
- No GitHub Actions secret is created or managed for signing.

Verified end-to-end locally before committing to this: built a signed `x64` MSIX this way,
imported the resulting `.cer` into `LocalMachine\TrustedPeople`, ran `Add-AppxPackage`, and
confirmed CmdPal's `commandProviderCache.json` picked up the extension
(`SignatureKind: Developer`, `IsDevelopmentMode: False`) - same outcome a real user's install
would produce.

## Consequences

- Zero secrets to provision or rotate; the release workflow has no setup dependency beyond the
  repo itself.
- Every release has a distinct signing identity: installing release N+1 after release N requires
  re-trusting the new `.cer` (`Add-AppxPackage` will reject the upgrade with an untrusted-chain
  error otherwise). This is called out in each release's body.
- If this project ever needs seamless in-place upgrades for manual MSIX installs, or gets
  distributed via WinGet, revisit this ADR - a persistent cert-as-secret (or Store/WinGet
  distribution, which avoids the problem entirely) is the fix at that point.
