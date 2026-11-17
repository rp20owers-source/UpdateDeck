This maintenance build fixes over-broad restart blocking: queued file operations are advisory, while Windows Update/servicing restart requirements and unreadable restart state still block installations. Installer-reported restart results still stop subsequent work, and history reconciliation retains all pending-restart signals. Unknown inventory and publisher workflows no longer inflate deferred-update counts. The latest hotfix report records current validation; earlier long-idle/installer observations describe the preceding preview.

This PC appears directly above Settings. It reports motherboard brand/model/revision, CPU model/socket/cores/threads/clocks/cache, each RAM module and slot/type/capacity/reported speed/voltage, GPU/driver/dedicated-memory metadata, disk model/capacity/bus/media/firmware, network adapters, displays, BIOS and Windows details. Data is read locally from bounded Windows CIM queries and display-driver registry metadata. No hardware code is loaded and no serial numbers, passwords or license keys are queried. Missing fields remain Not reported; clocks are Windows snapshots, not live benchmarks. Standard Windows inventory does not expose every GPU clock, temperature, power-supply or chipset specification. New profiles default to Dark; saved appearance choices remain supported.

Related desktop entries are grouped into a single collapsed family/brand header with a visible count in Updates and Installed Apps. Expand a group (for example .NET or Microsoft Visual C++) to inspect each installed version, architecture, status and individual action. Single entries remain ordinary rows. Search and filters apply before the count is shown. Grouping changes presentation only: it never merges installations, combines update operations or removes versions.

Games has its own navigation entry after Installed Apps. Steam and GOG installed registrations and local Epic installed-game manifests appear there. Launcher cards cover Steam, Epic, Battle.net, EA, Ubisoft Connect, GOG GALAXY, Riot, Xbox, Rockstar, Amazon Games and itch.io. Existing desktop clients require Windows Authenticode and an expected publisher; otherwise the card opens a fixed official website. These are launcher handoffs, not account/library synchronization. Other vendors and unregistered libraries must be managed manually. Game downloads, game/launcher updates, sign-in, licenses and anti-cheat stay with the platform. Vendor Updaters is an option inside Updates, beside Scan now, rather than a sidebar entry. Games and game launchers appear only in Games. They are excluded from Updates, desktop update counts, Update All, previews and deferred-update lists, including stale or conflicting gaming inventory.

# Provider and execution architecture

UpdateDeck uses C# / WPF with MVVM-style observable state, SQLite and asynchronous providers. .NET 10 self-contained Windows Desktop builds support native x64 and ARM64 payloads. SDK targeting is 26100; the technical runtime floor is Windows build 19041. Native WPF was chosen for predictable portable deployment, established accessibility and virtualized tables. WinUI 3 was considered; its deployment bootstrapper adds complexity to this portable-first application. Visual tokens and artwork are original.

## Provider boundaries

Discovery, recognition, update checking, eligibility, execution and independent verification are separate stages. `IInventorySource`, `IUpdateExecutor`, `ISupervisedUpdateExecutor` and `IVendorLauncher` isolate those responsibilities. A capability flag describes an implemented provider operation, not universal support for every discovered package.

| Source | Inventory/check | Execution |
|---|---|---|
| Registry | HKCU/HKLM 32/64 uninstall keys; registered file metadata; read-only cached MSI Summary Template architecture | No registry-driven arbitrary commands |
| WinGet | Official in-process COM installed/community catalog; raw versions, product codes, scope/channel evidence | Exact installed Microsoft CLI; sequential routine or explicitly reviewed interactive action |
| Store/MSIX | Current-user launchable package discovery; framework/resource/system exclusions; signature-kind ownership | Registered Microsoft Store update interface |
| Scoop/Chocolatey | Bounded local installed JSON/XML manifests | Official guidance; no manager installation or ownership migration |
| Portable | Opt-in selected directory and one child level; executable resources/PE architecture | Inventory only |
| Audio plugins | Optional VST2/VST3/CLAP locations; bounded static PE export/resource parsing | Inventory only; never loads DLLs |
| Reviewed vendors | Built-in product/publisher definitions and expected-signature launcher paths | Validated executable or exact official guidance URL |
| Official installer | Narrow Notepad++ fallback for uncovered, signed machine installations | Individually reviewed signed interactive installer; never Update All |

Provider tasks have time limits and fail independently. Work is invoked off the UI thread even if a provider blocks before returning its Task. One in-flight query is retained per source, and native WinGet ownership/pin leases last until real completion even when the caller stops waiting. A missing package manager does not remove other inventory. Windows package discovery owns MSIX filtering so the desktop catalog cannot reintroduce frameworks as ordinary applications. No `Win32_Product` query is used.

## Identity and versions

Product codes/family IDs or a path/name/publisher conjunction provide identity evidence. Names alone do not merge records. Scope, architecture and location preserve separate installations. Durable continuity keeps ignore/history identity across normal upgrades; ambiguous current matches remain separate and blocked. Positive Scoop/Chocolatey/Steam ownership takes precedence over catalog resemblance. Steam games require their registration, protocol and steamapps path evidence and remain manual game-content workflows.

Candidate indexes narrow comparisons to shared record IDs, product codes, families, package IDs or path/publisher/name conjunctions; the complete confidence and ambiguity checks still decide every merge. Continuity uses indexed prior identities and reverse match counts. This preserves installation policy while avoiding all-pairs version parsing.

Raw installed version values remain visible. A registry/catalog disagreement for one exact registration produces Attention rather than an automatic mapped-version assumption. Numeric and semantic versions, dotted dates and arbitrary-size numeric components are compared conservatively. Unknown syntax is not current or an upgrade. Beta/RC/stable track changes and explicit channels without a reviewed CLI selector are deferred. Current badges age out after 30 minutes. WinGet also requires source-index freshness within that period, independently of query time. Unicode digit scripts and empty prerelease/build identifiers are unsupported rather than passed to numeric parsing. Conflicting raw evidence remains blocked even if a later presentation state changes.

Installed architecture is recovered only from related registered application resources/PE headers or a read-only cached MSI template. Uninstall/setup executables and registry view alone are not architecture proof. Unknown architecture/scope disables automatic work. The application never loads discovered plugin DLLs or executes files to learn their version.

## WinGet trust and exact updates

The bundled `Microsoft.WindowsPackageManager.ComInterop` and `InProcCom` packages are version 1.29.380. The architecture-matched in-process factory is deployed as `Microsoft.Management.Deployment.dll` beside the matching engine/manifest. Indexed vector access follows the official projection's supported contract. Package list/upgrade console tables are not parsed.

The installed CLI must be a physical `winget.exe`, Windows-trusted with the Microsoft Corporation signer and matching App Installer version resources. PATH name alone does not grant trust. Source export must identify the reviewed Microsoft community cache/type. Every operation supplies exact package ID, source, target version, installed scope and architecture. Routine operations use silent mode; individually reviewed routine/Caution operations use interactive mode. No `--all`, force, unknown-version, security/hash bypass, agreement acceptance or reboot flag is supplied.

The COM contract has no pin-list API. The narrow CLI adapter accepts reviewed official locale headers/empty results and rejects unknown, truncated or malformed output. Before COM/CLI reads, existing packaged and unpackaged pin storage is held read-only against deletion, checked for SQLite integrity, supported 1.0 metadata and readable pin columns. Unsupported/corrupt storage is preserved and blocks the provider. This is necessary because WinGet itself can delete an index it cannot open. Prior known pins survive failed fresh verification. The direct installer checks WinGet pins too, before metadata/download and immediately before launch.

Immediately before each update, fresh installed catalog plus registry correlation must identify exactly one unchanged installation. Process/restart/target-volume-space policy is checked again. After execution a fresh installed record must report the target or a supported newer version. Exit code zero alone never becomes Verified.

## Official installer fallback

Only Notepad++ is reviewed. Established WinGet/Store/MSIX/Scoop/Chocolatey ownership wins. The existing machine-scope application must have an expected Windows-trusted signer, product/file version, known architecture and safe original path. Duplicate scope/architecture registrations are blocked.

Latest stable release metadata comes from the exact official GitHub repository. Exact architecture asset name/URI, SHA-256 and bounded size are required. Downloads use HTTPS with a small exact redirect-host list. The resulting payload must match its hash, Windows-trusted NOTEPAD++ signer and file version. A deny-write/delete handle remains open through installer completion. NSIS is launched interactively with only its documented original-directory argument; licenses and elevation remain the user's responsibility. The original signed installed executable is checked independently afterward. HTTPS transfer limits cover headers, the whole body and read inactivity. Successful/failed pre-launch downloads are cleaned up. If installer monitoring becomes uncertain, the payload lease is retained until actual monitored completion; unknown/faulted monitoring retains protection until UpdateDeck exits instead of deleting a potentially active payload. No publisher binary is redistributed in UpdateDeck packages.

## Queue and recovery

Update All refreshes, plans, then executes individual routine operations sequentially. One process census is used during planning and fresh checks run before each operation. Caution software requires individual review. System-sensitive/high-risk software remains in publisher workflows. Saved ignores, pins, pause, pending restart, ambiguity, stale results, running/protected processes and low space defer work. At most two non-driver/non-game vendor handoffs can follow under the saved preference; opening a manager is recorded as Handoff only.

All pending operations are durable before any side effect. Pause and cancel operate at safe boundaries and never kill an installer. One retry applies only to recognized transient download failure, not interactive installation. Reboot or uncertain monitoring stops subsequent work. Running and Verifying states preserve an inspection guard until verification completes; recovery treats both as potentially active at interruption. An exact recorded PID/start time can establish that the parent command ended; it cannot clear a possible child-installer hold. Explicit user inspection in History clears that hold without claiming success. A later fresh observation must match the original provider, package/publisher, path, scope, architecture and channel before reconciling an ended operation. Relaunch never blindly reruns interrupted work. A failed post-execution verification stops subsequent work, even after exit code zero. Late UI progress callbacks cannot overwrite the final summary, and verified work refreshes the visible inventory under the same maintenance ownership.

SQLite uses FULL synchronization, transactions, schema checks, recovery copies and bounded logs. Retention prunes only 2,000 finished history records, preserving all pending/active/unverified operations and inspection holds. Malformed history JSON is preserved and cannot break pruning or make a hold disappear. Corrupt database/policy recovery preserves evidence and pauses installations. Malformed history creates an inspection hold. A future schema is not rewritten. Storage write failure prevents new installer work. Portable mode uses adjacent Data, with a visible local fallback when that location is read-only.

The GUI owns presentation and scheduling. Settings are a draft until Save, preserving live ignores/check times/view/window placement. Daily/weekly checks run only while open/in tray, respect quiet hours/metered/battery/offline policy, and never install. Scheduled failures back off 15/30/60/120/240/480 minutes; the successful-check time advances only with completed, timestamped catalog evidence. Clock rollback does not suppress future checks indefinitely. There is no background service or startup task. Window bounds are physical monitor coordinates, clamped to the nearest current work area on restore.

## Migration preview and accessibility

Manifest schema 2 captures names, IDs, provider preference, kind/risk, scope/architecture/channel, ignore state and a small non-executable preference subset. It excludes installation paths and credentials. Parsing supports legacy schema 1, rejects ambiguous properties/types, and bounds UTF-8 size, depth, strings and item count. Import never applies policy or invokes an installer.

The metadata-only migration adapter uses the predefined community source, an exact ID filter, a two-result limit and `GetApplicableInstaller` for the current OS and preferred scope. It never calls package install/download APIs or accepts source agreements. Up to 25 selected IDs are checked sequentially; cancellation/timeouts stop the batch, retaining single-flight ownership until native completion. Returned applicability is metadata evidence, not a license, publisher-trust or cross-machine compatibility guarantee.

Native table controls expose named checkboxes and rows. Status regions raise coalesced polite `LiveRegionChanged` events only when an accessibility listener exists; no repeating idle timer is used for announcements. Window minimums follow current monitor work area/DPI. Full screen-reader and actual multi-monitor DPI audits remain open.

## Primary references and research

- [WPF live-region announcements](https://learn.microsoft.com/en-us/accessibility-tools-docs/items/wpf/text_livesetting), [WinGet package-manager IDL](https://github.com/microsoft/winget-cli/blob/v1.29.380/src/Microsoft.Management.Deployment/PackageManager.idl).
- [WinGet API and samples](https://github.com/microsoft/winget-cli/tree/v1.29.380/samples/MinimalCallers), [upgrade](https://learn.microsoft.com/en-us/windows/package-manager/winget/upgrade), [pins](https://learn.microsoft.com/en-us/windows/package-manager/winget/pinning).
- [PinningIndex source](https://github.com/microsoft/winget-cli/blob/v1.29.380/src/AppInstallerRepositoryCore/Microsoft/PinningIndex.cpp), [1.0 pin table](https://github.com/microsoft/winget-cli/blob/v1.29.380/src/AppInstallerRepositoryCore/Microsoft/Schema/Pinning_1_0/PinTable.cpp).
- [Notepad++ releases](https://github.com/notepad-plus-plus/notepad-plus-plus/releases), [documented installer switches](https://npp-user-manual.org/docs/command-prompt/).
- [.NET support](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core), [Windows App SDK](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/).
- [NVIDIA App](https://www.nvidia.com/en-us/software/nvidia-app/) and [Image-Line updates](https://www.image-line.com/fl-studio-learning/fl-studio-online-manual/html/app_update.htm): supported publisher workflows rather than invented APIs.

Public UniGetUI, Patch My PC Home Updater and Ninite documentation informed breadth, approachable maintenance and batch-flow decisions. No competitor implementation, proprietary catalog or artwork was copied. Provider ownership, uncertainty and independent verification govern this implementation.

