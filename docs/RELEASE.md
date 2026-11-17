# Release construction and signing

Version 2.0.3 is an unsigned development preview. Build on Windows with PowerShell 7.4+, the pinned .NET SDK 10.0.402 (or a compatible SDK permitted by global.json), and an appropriately licensed Inno Setup 7 compiler. Development artifacts used Inno Setup 7.1.1. Commercial builders must obtain any license required by [Inno Setup's current terms](https://jrsoftware.org/inno-setup); the compiler is not shipped in the application.

This update also recognizes strict comma-separated numeric Windows executable version resources when they represent the same dotted registry version. Malformed or nonnumeric resources remain untrusted.

## Build from a stable source tree

```powershell
dotnet restore UpdateDeck.sln --locked-mode
dotnet test tests/UpdateDeck.Tests/UpdateDeck.Tests.csproj -c Release
.\scripts\Build-Release.ps1 -IsccPath 'C:\licensed-tools\Inno Setup 7\ISCC.exe'
.\scripts\Build-Source.ps1
```

The release script repeats locked restore/tests, publishes fresh uniquely named staging directories for x64 and ARM64, audits native dependencies, bundles notices/docs, constructs setup before adding the portable marker, creates ZIPs and hashes, validates packages, and only then promotes them. It never reuses a directory that may contain a launched app's Data. Source inputs are hashed before and after construction; changes during the build abort promotion. Avoid editing source/docs until it finishes.

The default release destination is `../releases`:

- `UpdateDeck-2.0.3-win-x64-Setup.exe`
- `UpdateDeck-2.0.3-win-arm64-Setup.exe`
- `UpdateDeck-2.0.3-win-x64-Portable.zip`
- `UpdateDeck-2.0.3-win-arm64-Portable.zip`
- `SHA256SUMS.txt` and `release.json`

`release.json` records the source tree SHA-256, build ID, exact framework runtime, compatibility, architecture, signing state and expected file list. Files are copied through hash-checked temporary names; metadata is promoted last. `Verify-Release.ps1` audits hashes, safe Windows archive paths, file/directory collisions, links, absence of local/development files, every portable EXE/DLL architecture (including neutral managed IL), nine named native dependency machines, setup bootstrapper and exactly the Core/Desktop 10.0.15 framework pair. Every decompressed entry is bounded and checked for actual size and CRC-32; the report includes per-payload SHA-256 values. Metadata parsing rejects duplicate/case-aliased JSON properties and invalid numeric/signing types. A source archive includes `SOURCE-INDEX.json` with per-file hashes; source packaging refuses a tree different from the recorded release. `Verify-Source.ps1` checks it independently, requiring exact indexed paths and complete per-file coverage, with bounded CRC-checked reads. Dependency lockfiles record NuGet content hashes.

```powershell
.\scripts\Verify-Release.ps1 -ReleaseDirectory '..\releases'
.\scripts\Verify-Source.ps1 -ArchivePath '..\UpdateDeck-2.0.3-Source.zip'
.\scripts\Test-ArtifactGuards.ps1 -ReleaseDirectory '..\releases' `
  -SourceArchive '..\UpdateDeck-2.0.3-Source.zip' `
  -WorkDirectory 'C:\chosen-scratch-folder' -ReportPath 'C:\chosen-scratch-folder\guards.json'
```

Artifact guard testing covers 42 baseline/adversarial cases and creates new disposable mutated copies in the chosen scratch directory. It never changes the supplied archives. Retain or remove those scratch copies after review. `artifacts/`, `bin/`, `obj/`, test results, local Data and generated binaries are excluded from the source archive. The development-only Probe and UiProbe projects are included as source and are never bundled into application payloads.

The scripts were exercised on PowerShell 7.6.9 / .NET 10.0.14 on this host; 7.4 is the declared minimum, not a separately tested shell configuration. The small `ArchiveIntegrity.cs` helper compiles into the packaging process only and is not an application dependency. CRC is checked explicitly rather than relying on read-time ZIP behavior in earlier .NET versions ([runtime guidance](https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/11/ziparchive-entry-crc32-validation)).

Both .NET Core and Windows Desktop runtimes are pinned to 10.0.15, so launch requires no separate .NET install. Servicing requires an intentional version change and republish. Managed deterministic compilation is enabled; ZIP timestamps, native toolchain compression and signatures are not claimed to be byte-for-byte reproducible.

## Installer behavior

Setup defaults to per-user scope, with normal elevation for explicitly selected all-users scope. It creates Start-menu/optional desktop shortcuts with an explicit stacked-deck icon and uninstall registration. It preserves LocalAppData state on upgrade/uninstall. Running app mutex detection prevents replacing the current session's active application. Close all users' sessions before a machine-wide upgrade; cross-session coordination is not validated. User and machine registrations are checked independently: either newer registered UpdateDeck version blocks downgrade, including silent setup. An unreadable version string blocks replacement pending inspection. No runtime download or development-machine path is required.

The x64 setup is a native x64 bootstrapper and rejects ARM64. ARM64 setup requires ARM64 Windows and uses Inno's x86 bootstrapper through Windows emulation; app, .NET, WPF, WinGet and SQLite payloads are native ARM64. No x86 edition is advertised. Cross-building/PE validation does not prove ARM64 runtime operation.

## Trusted signing

Supply `-SignToolPath <absolute-signtool.exe> -SigningThumbprint <real-certificate-thumbprint>`. The script signs all four first-party files (`UpdateDeck.exe`, `UpdateDeck.dll`, `UpdateDeck.Core.dll`, `UpdateDeck.Windows.dll`) before setup, signs setup after construction, applies SHA-256/RFC3161 timestamping, and verifies signatures. Signed-release validation checks Windows trust and the exact expected signer thumbprint. Real keys stay in the certificate store/hardware provider, never source. A CI signing service needs a reviewed credential integration. Signed uninstaller configuration remains a production follow-up.

See [Microsoft signing options](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options). No credentials were available here: supplied metadata says `signed:false`, and checksums are not a trust certificate. Do not bypass Windows or organizational warnings.

Before public production release, complete actual ARM64, clean Windows 10/11, standard-account/elevation, multi-user, isolated real-upgrade, proxy/policy/media fault and accessibility validation. Establish controlled signed publication. There is no operational UpdateDeck self-update feed or rollback mechanism.
