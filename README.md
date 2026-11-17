# UpdateDeck 2.0.3

This PC appears directly above Settings. It reports motherboard brand/model/revision, CPU model/socket/cores/threads/clocks/cache, each RAM module and slot/type/capacity/reported speed/voltage, GPU/driver/dedicated-memory metadata, disk model/capacity/bus/media/firmware, network adapters, displays, BIOS and Windows details. Data is read locally from bounded Windows CIM queries and display-driver registry metadata. No hardware code is loaded and no serial numbers, passwords or license keys are queried. Missing fields remain Not reported; clocks are Windows snapshots, not live benchmarks. Standard Windows inventory does not expose every GPU clock, temperature, power-supply or chipset specification. New profiles default to Dark; saved appearance choices remain supported.

Related desktop entries are grouped into a single collapsed family/brand header with a visible count in Updates and Installed Apps. Expand a group (for example .NET or Microsoft Visual C++) to inspect each installed version, architecture, status and individual action. Single entries remain ordinary rows. Search and filters apply before the count is shown. Grouping changes presentation only: it never merges installations, combines update operations or removes versions.

Games has its own navigation entry after Installed Apps. Steam and GOG installed registrations and local Epic installed-game manifests appear there. Launcher cards cover Steam, Epic, Battle.net, EA, Ubisoft Connect, GOG GALAXY, Riot, Xbox, Rockstar, Amazon Games and itch.io. Existing desktop clients require Windows Authenticode and an expected publisher; otherwise the card opens a fixed official website. These are launcher handoffs, not account/library synchronization. Other vendors and unregistered libraries must be managed manually. Game downloads, game/launcher updates, sign-in, licenses and anti-cheat stay with the platform. Vendor Updaters is an option inside Updates, beside Scan now, rather than a sidebar entry. Games and game launchers appear only in Games. They are excluded from Updates, desktop update counts, Update All, previews and deferred-update lists, including stale or conflicting gaming inventory.

A native Windows software maintenance application with real discovery, structured WinGet catalog checks, policy-aware update queues, and official vendor handoffs.

**This is an unsigned engineering preview, not a fully validated production release.** See [the verification report](docs/VERIFICATION.md) for tested behavior and open release gates. No unrelated application was updated to validate this build.

## Run

Choose the release matching your PC's architecture.

- **Portable:** extract the complete `UpdateDeck-2.0.3-win-x64-Portable.zip` or `win-arm64` ZIP, then run `UpdateDeck.exe`. Keep all bundled files together. A .NET SDK or separately installed .NET runtime is not required.
- **Installer:** run the matching `Setup.exe`. Defaults to a per-user install; all-users installation uses normal Windows elevation. Shortcuts and an uninstall entry are provided. Settings survive upgrades and uninstall.

Minimum technical OS: Windows 10 build **19041** (version 2004) or newer. Windows 11 is the primary target. Windows 10 compatibility does not imply Microsoft security support: use an edition / ESU / LTSC configuration still serviced by Microsoft. LTSC 2021 meets the technical floor; LTSC 2019 and older do not meet this product's floor. ARM64 is built but has not been run on ARM64 hardware. x86 is not distributed. macOS and Linux are not supported.

The ARM64 application and native dependencies are ARM64. Its Inno installer bootstrapper is x86 and relies on Windows' supported x86 emulation; it does not require an x64 runtime.

## Use

Restart detection distinguishes Windows Update/servicing requirements from queued file operations. File operations alone show a restart recommendation; they do not universally block unrelated application updates. Update plans list actual available updates and conflicting evidence, while unknown inventory and vendor workflows remain on their own pages.

1. Open UpdateDeck. It reads local inventory first, then checks the catalog when enabled and connected.
2. Use **Installed Apps** to search, filter, sort, inspect raw version/channel/identity evidence, or ignore an application. Click column headers for ascending/descending numeric version ordering.
3. **Preview plan** refreshes metadata and shows eligible operations, publisher handoffs and deferral reasons without launching anything. **Update All**, selected checkboxes and individual **Update** buttons use the same policy engine and refresh again before execution. No global `winget upgrade --all` is used.
4. Routine eligible applications install sequentially. Unknown versions, architectures or scopes, ambiguous identities, pins, channel changes, sensitive software, Windows Update/servicing restart requirements, running apps, and insufficient disk space defer installation. A registered redistributable/bootstrapper icon is not treated as proof of the installed application's architecture; strict comma-separated numeric Windows version resources are normalized only when matching executable evidence.
5. The default policy opens at most two non-driver vendor handoffs after automatic work. Additional managers are available individually. Change this in Settings. A handoff is never recorded as a successful installation.
6. Use **Recheck remaining** after the publisher finishes. History distinguishes verified success, installer execution awaiting verification, failure, interaction, interruption and handoff.

Pause affects the next safe boundary. Cancel stops pending work. Neither action kills an active installer or restarts Windows. Retry refreshes metadata and re-evaluates policy before creating a new operation, including before a transient-download retry. Restart requirements and unverified installer outcomes stop subsequent work. A recorded parent process ending does not prove that its child installer has ended. Inspect publisher dialogs in History before clearing a monitoring hold; confirmation alone never marks the update successful. After verified work, the visible inventory refreshes automatically.

Some installers require UAC, publisher agreements, or authentication. UpdateDeck neither accepts unfamiliar agreements nor bypasses these interactions. Use the individual **Review installer** action for eligible routine/Caution WinGet packages. Its interactive route keeps all pin, identity, scope, architecture and machine checks; system-sensitive/high-risk software remains in the original publisher workflow.

## Sources

- Registry uninstall keys: user/machine, 32/64-bit views; no `Win32_Product` enumeration.
- Store: supported package enumeration of launchable current-user apps; official Store update handoff.
- WinGet: bundled official COM API for structured correlation and available versions; Microsoft's installed WinGet CLI is required for execution and pin checks.
- Scoop and Chocolatey: local installed manifests and official guidance handoffs. Automatic updates and online availability checks are not implemented for these managers.
- Portable folders: optional executable metadata scanning of selected folders and one child level. This finds candidates, not verified releases. No entire-drive scans or plugin code loading.
- Audio plugins: optional VST2/VST3/CLAP module inventory using bounded static PE exports and resources. No plugin DLL is loaded and no plugin release availability is invented.
- Official installer fallback: reviewed, signed, uncovered machine-scope Notepad++ only. Official metadata, exact architecture asset, SHA-256, size, Windows signer and file version are required. Individual interactive review only; established managers and pins retain ownership.
- Vendor managers: NVIDIA App, Creative Cloud, Native Access, IK Product Manager, Steam, Epic, Visual Studio Installer. Known installed executables must pass Windows Authenticode validation and expected-publisher matching. Otherwise the action opens reviewed official guidance. Image-Line, Serato, rekordbox, MOTU and BOSS use official download/guidance pages. Steam game content and driver managers are excluded from automatic handoffs.

Missing providers do not prevent other discovery. A timed-out native query retains its ownership and protective storage lease until its actual work finishes; repeated scans do not pile up more queries. Unverifiable software remains labeled **Cannot verify**. Names alone never merge installation identities. Existing manager ownership is preserved when a later source cannot confirm it, and potential conflicts disable automatic operations. WinGet automatic eligibility requires both a recent query and a catalog-source refresh within 30 minutes; old cache results stay visible without fresh-current claims.

## Settings and storage

Installer mode stores state in `%LOCALAPPDATA%\UpdateDeck`. Portable mode is selected by `portable.mode` beside the executable and uses `Data` beside it. If the portable folder is read-only, startup falls back to `%LOCALAPPDATA%\UpdateDeck` and shows that location in Settings. That transition pauses installations and automatic handoffs until policy and unfinished installers from the unavailable store have been reviewed. The pause applies on each launch that still needs this fallback; an unrelated local policy is never silently treated as the portable policy.

SQLite uses transactions, full synchronization, a rollback journal, integrity checks, schema versioning, and retention of 2,000 finished history records. Pending work, unverified results and unresolved inspection holds are retained beyond that limit. Corrupt databases are preserved with `.corrupt-<timestamp>` before a fresh database is created. Recovery preserves damaged policy/history evidence and pauses installations until policy review. Uncertain installer history creates a monitoring hold; confirming it ended never marks an update successful. Abrupt media loss during a run stops state writes and prevents new work when operation persistence fails; an active third-party installer can continue. No filesystem can guarantee recovery from failed hardware.

Settings support Dark / Light / System, startup checks, daily/weekly checks while the app is open, quiet hours, optional tray, completion notifications, opt-in folders, and ignores. Settings are a draft until **Save preferences**. The saved installation pause also suppresses automatic publisher handoffs; you can still open a publisher workflow explicitly. Invalid quiet hours disable Save; saving preserves current ignore policy, check times and window placement. There is no service, login task, PATH modification or automatic startup registration. Background checks respect metered connections, battery saver, quiet hours and offline state. Incomplete scheduled checks retry with increasing delays from 15 minutes up to 8 hours; they do not advance the successful-check time. Scheduled checks never install software.

Ctrl+F opens Installed Apps search (or History search when on History); F5 checks inventory and updates; Escape closes application details. Native window controls and keyboard navigation are retained. Interface animations are limited to indeterminate progress; no fabricated percentages are displayed.

Launch with `UpdateDeck.exe --compact` for a 1000 × 700 starting window and compact navigation. Window size, physical position and maximized state are normally remembered on close and restored inside a current monitor work area. Windows reduced-motion preferences disable indeterminate progress animation; the activity text remains visible.

Export a readable inventory manifest from Settings, including recognized IDs, categories, scope/architecture, saved ignores and selected preferences. The import preview reads both legacy and current manifests, searches saved applications, and can check up to 25 exact public IDs against the Microsoft community catalog. It reports installer metadata applicable to **this PC**, the selected scope, catalog freshness, missing IDs and publisher/manual workflows. Catalog applicability does not prove driver, plug-in, project or license compatibility. Preview results can be saved. Import never installs software, applies saved policy or transfers licenses. No installation paths, license keys, credentials, or personal document contents are exported in an inventory manifest. Local diagnostic exports contain application/version history: review before sharing.

See the [user guide](docs/USER-GUIDE.md) for ordinary maintenance, queue recovery, portable storage and migration workflows.

## Build and test

Requires PowerShell 7.4+ for packaging and .NET SDK 10.0.402 or a compatible 10.0 servicing SDK and Windows SDK targeting support, fetched by NuGet. Dependencies are pinned in project files and lockfiles. Build on Windows.

```powershell
dotnet restore UpdateDeck.sln --locked-mode
dotnet build src/UpdateDeck.App/UpdateDeck.App.csproj -c Release
dotnet test tests/UpdateDeck.Tests/UpdateDeck.Tests.csproj -c Release
```

Release publishing bundles .NET **10.0.15**, including Windows Desktop. A framework-dependent build needs that runtime installed to launch.

```powershell
.\scripts\Build-Release.ps1 -IsccPath 'path\to\Inno Setup 7\ISCC.exe'
```

Produces x64/ARM64 installer EXEs, portable ZIPs, `SHA256SUMS.txt`, and `release.json` in `../releases`. Use `scripts/Build-Source.ps1` after release construction to create matching source and per-file index. The release records a source fingerprint and refuses inputs changed during construction. See [release instructions](docs/RELEASE.md). The supplied artifacts are unsigned. No self-signed trust claim or first-party release feed is provided.

For an explicit **read-only** integration report:

```powershell
.\UpdateDeck.exe --diagnose 'C:\chosen-writable-folder\scan.json'
```

This writes an inventory report and a `diagnostic-data` database in the chosen folder. Reports contain installation paths and should remain private unless deliberately shared. Diagnostic mode does not launch installers or vendor managers.

## Project structure

`src/UpdateDeck.Core`: portable models, version comparison, identity, policies, queue, SQLite, logs. `src/UpdateDeck.Windows`: Windows discovery, safe process execution, WinGet, vendor trust, machine capabilities. `src/UpdateDeck.App`: WPF/MVVM presentation, themes, tray, scheduler and dialogs. `tests`: automated tests and a harmless version-file fixture. `installer` and `scripts`: architecture-specific release infrastructure.

See [architecture and research](docs/ARCHITECTURE.md), [security](docs/SECURITY.md), and [verification](docs/VERIFICATION.md). UpdateDeck source is MIT licensed; bundled third-party components retain their licenses.


Shared-release checks require an exact normalized publisher identity and product-word boundaries for vendor routing; substring lookalikes are rejected. A known vendor workflow does not claim a verified release. Update/review actions require fresh catalog evidence, a newer version on the same track/channel, known architecture/scope and a local absolute installation path. Stale metadata removes the action and updates dashboard counts; per-operation process, disk, restart and identity checks still run immediately before execution.
