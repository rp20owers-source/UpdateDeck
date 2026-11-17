# Third-party notices

UpdateDeck source is MIT licensed. It uses redistributed components under their own licenses; no proprietary competitor code or artwork was copied. The `licenses` directory contains explicit copies of the WinGet, CsWinRT, Microsoft.Data.Sqlite, SQLitePCLRaw Apache 2.0, and Inno Setup license texts, alongside runtime-provided notices and the bundled WinGet notices.

| Component | Version | License / notices |
|---|---|---|
| .NET / Windows Desktop runtime | 10.0.15 | [MIT and third-party notices](https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT); runtime license/notice files are included by self-contained publish |
| Microsoft.WindowsPackageManager.ComInterop / InProcCom | 1.29.380 | [MIT](https://github.com/microsoft/winget-cli/blob/master/LICENSE), [NOTICE](https://github.com/microsoft/winget-cli/blob/master/NOTICE.txt); bundled notice files accompany releases |
| Microsoft.Windows.CsWinRT runtime | 2.2.0 | [MIT](https://github.com/microsoft/CsWinRT/blob/master/LICENSE) |
| Microsoft.Data.Sqlite | 10.0.15 | [MIT](https://github.com/dotnet/efcore/blob/main/LICENSE.txt) |
| SQLitePCLRaw | 2.1.13 | [Apache 2.0](https://github.com/ericsink/SQLitePCL.raw/blob/master/LICENSE.TXT) |
| SQLite | packaged by SQLitePCLRaw | [Public domain dedication](https://sqlite.org/copyright.html) |
| Inno Setup compiler / installer engine | 7.1.1 | [License and current commercial-use terms](https://jrsoftware.org/inno-setup); compiler not included in UpdateDeck payload |

NuGet package lockfiles record resolved dependencies, versions and content hashes. Test-only xUnit and Microsoft.NET.Test.Sdk components are not shipped in application release artifacts. Windows fonts, shell icons and local application icons remain under their respective owners' rights and are obtained from the target Windows installation; icons extracted on the development PC are not bundled.
SQLitePCLRaw 2.1.13 package metadata identifies Copyright 2014–2024 SourceGear, LLC, with Eric Sink as author. The exact native SQLite version/source identifier used by the x64 development probe is recorded in the environment audit; that probe is separate from the self-contained application runtime.
Pin locale headers and empty-result phrases are transcribed from the corresponding official WinGet 1.29.380 localization resources under Microsoft's MIT license; the shipped WinGet notices/license attribution applies. Pin-storage schema checks follow the public 1.0 metadata and pin-table definitions and never modify the user's index. Notepad++ metadata and installer downloads are fetched only at runtime from the reviewed official repository; no Notepad++ binary or application artwork is redistributed. The original publisher's installer license and prompts remain in effect.
