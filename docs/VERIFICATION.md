# Verification — UpdateDeck 2.0.3 Preview

The 2.0.3 live investigation found a valid registered executable whose PE version resource used comma-separated numeric components, while the registry used dotted components. The old comparison rejected this otherwise matching product/version evidence, leaving its native architecture unknown and preventing a routine update. Executable metadata matching now normalizes only strict comma-separated numeric resource versions; malformed or nonnumeric resource strings remain rejected. The read-only scan and plan preview started no installers or vendor managers. The automated suite passes 456 tests.

## Historical 2.0.2 preview evidence

The 2.0.2 maintenance build fixed over-broad restart blocking: queued file operations are advisory, while Windows Update/servicing restart requirements and unreadable restart state still block installations. Installer-reported restart results still stop subsequent work, and history reconciliation retains all pending-restart signals. Unknown inventory and publisher workflows no longer inflate deferred-update counts. The latest hotfix report records current validation; earlier long-idle/installer observations describe the preceding preview.

## 2.0.2 verification

This is a working native Windows engineering preview. It implements real discovery and update execution paths, with explicit gaps before production distribution. Testing never mass-updated or silently changed unrelated software on the development PC.

## Environment and evidence

Development host: Windows x64, build 26300; .NET SDK 10.0.402; installed WinGet 1.29.380. Published applications bundle .NET Core and Windows Desktop 10.0.15. Machine-readable evidence accompanies the delivery in `verification/`; it is outside the application/source archives so later validation does not change the recorded source fingerprint.

The automated xUnit suite covers version/channel ordering, strong identity and continuity, raw-version disagreements, pin parsing and storage preservation, source/CLI validation, exact execution arguments, ownership and risk policy, supervised selection, queue boundaries, retries and verification uncertainty, real harmless child processes, Unicode paths, durable history, corruption recovery, storage failure, optional plugin PE parsing, and settings preservation. The delivered TRX and `test-summary.json` give the final test count. The 2.0.2 pre-release suite contains 451 passing cases; release construction runs it again and records actual results. New regression tests prevent redistributable bootstrapper icons from defining installed architecture, preserve already-verified architecture evidence, and distinguish publisher-only system-sensitive updates from individually reviewed caution updates. This includes 4,000 deterministic malformed/Unicode version inputs within a robustness test, large mixed-provider inventories, native-query retention, post-execution uncertainty, payload locks and history beyond the finished-record retention limit.

`artifact-guard-tests.json` records 42 baseline/adversarial cases against copied artifacts: checksums, duplicate/case-alias entries, traversal, Windows device/stream/trailing-dot/space paths, private Data leakage, wrong native architecture, incomplete Desktop runtime, malformed signing metadata, duplicate JSON fields, actual-size/CRC mismatches, file/directory collisions, wrong additional native/managed architecture, and source tampering/unindexed entries/missing index/case-aliased indexes. Original artifacts are never mutated. `release-validation.json` records hashes, nine named native dependencies per edition, the architecture of every portable EXE/DLL (distinguishing neutral managed IL), setup bootstrapper architecture, clean payload, actual expansion/CRC checks and the exact runtime pair. Every portable file has a recorded SHA-256. `source-validation.json` records per-file hashes and the source tree matching the release build. Hashes provide integrity comparison, not publisher authentication.

## Live read-only checks

An earlier GUI scan observed 154 registry registrations, 42 launchable Windows packages, and 139 desktop WinGet catalog records. Reconciliation produced 184 applications and 30 newer catalog releases in a warm scan; one earlier stability candidate completed its startup refresh in 1.6 seconds. Other full refreshes ranged from roughly 2 to 4 seconds. The delivered timestamped reports give current counts and timings. These are observations of this host, not hardcoded coverage or performance promises. Removing MSIX framework entries from the desktop catalog intentionally reduced earlier inventory counts. Scoop and Chocolatey were absent. Windows reported a pending restart, so ordinary automatic work remained deferred.

`read-only-plan.json` independently checks candidates against a fresh catalog and registry correlation; consult that timestamped report for current counts. In an earlier probe, all 27 available desktop candidates matched one unchanged installation, and fourteen lacked reliable installed architecture evidence and were deferred. The provider does not infer architecture from registry view alone. MSI cache Summary Template metadata recovered 28 architecture values without invoking configuration or repair. Mapped Python registration/catalog version differences remain Attention with both raw values retained. Catalog availability changes over time without any application being installed.

The 2.0.1 host scan shown in the update queue found 190 applications, 25 catalog update candidates and 5 registrations with conflicting version evidence. All 30 candidates were deferred and no installer started: 14 lacked verified architecture, 10 required individual review for compatibility/system risk, 5 had conflicting versions, and 1 lacked a verified install folder. These holds are intentional; “0 routine updates ready” is not a failed installer or a reason to weaken policy. Inspection also found two x64 Visual C++ registrations whose display icons were `vcredist_x64.exe` / `VC_redist.x64.exe`; those publisher bootstrapper files are x86 and do not prove the installed runtime architecture. Version 2.0.2 ignores these bootstrapper icons, reads cached MSI architecture before executable icons, and never lets an icon override an already verified architecture.

`official-source-probe.json` records an actual 7,048,536-byte Notepad++ 8.9.9.1 x64 download from the reviewed publisher repository, matching SHA-256, expected Windows-trusted NOTEPAD++ signer and embedded version. The temporary binary was deleted and never executed. The host's older installed Notepad++ failed Windows chain trust; the fallback correctly did not claim it was eligible. Its established WinGet ownership remains subject to normal policy.

`audio-plugin-probe.json` records optional static discovery of one VST3 module without loading plugin code. No individual instruments, sample libraries, licenses or plugin release availability were inferred. NVIDIA graphics and raw installed driver metadata were read from Windows CIM. NVIDIA App was opened and visually verified in an earlier handoff check; no driver install or branch change was initiated.

The migration probe queries exact public IDs through the official COM catalog and selects applicable installer metadata for this PC. The live UI imported an owned ARM64 fixture on this x64 host, reported applicable x64 7-Zip metadata and a deliberately missing ID, disabled manual/plugin checkboxes, and saved the preview without creating an update operation. No migration install/download API is called.

## GUI and packaging checks

The native WPF app was inspected using actual screenshots and Windows accessibility state: compact 1000×700 and maximized layouts, dark/light appearance, navigation, search, keyboard focus, details, ignored apps, refreshed source status, and expanded queue/deferred reasons. The current settings draft leaves appearance unchanged until Save; out-of-range quiet hours show validation and disable Save. A saved installation pause and disabled automatic handoffs produced a zero-execution Update All plan with readable reasons. Official/WinGet review dialogs have developer preview modes that never call an executor. Native window placement is saved and restored within the nearest current monitor work area; minimums follow monitor work area/DPI. The plan header/footer remain outside the bounded details scroll area. Named migration checkboxes operate with one click. Polite status announcements raise coalesced live-region events; full screen-reader validation remains open.

The own-app x64 per-user installer was installed, upgraded at the same version, launched with real providers, and uninstalled on this host. Settings were preserved and app/uninstall entries removed. Final-artifact smoke results are recorded in `installer-final-smoke.json` when run, including payload integrity and newer/malformed-version rejection. These are development-host tests, not clean-machine certification. No security feature, UAC prompt, license condition or organization policy was bypassed.

## Resource observations

`inventory-benchmark.json` uses synthetic owned data, not a claim about every PC. The indexed resolver reconciled 5,000 input records into 2,500 applications in about 40 ms with approximately 28 MB of transient allocation, preserving every ID. The earlier all-pairs implementation took about 12 seconds and allocated about 26.9 GB over that same-sized fixture. A 20,000-record / 10,000-application run completed reconciliation in about 250 ms. The report gives exact measurements and a Core binary hash. These are single-host observations with normal JIT/caching effects.

`idle-stability.json` and its samples record the final candidate's extended visible-dashboard interval, CPU, memory, handles and exact first-party binary hashes. Check that report for the completed duration and observations; it is delivered outside the source tree so results do not mutate the release fingerprint. An earlier inspection-affected interval showed continuous external WPF UI Automation polling. Its stack evidence identified that client activity; the inspection kernel was reset before the clean candidate interval. The agent made no computer-use calls against the measured process during that interval. Concurrent development and ordinary Windows effects remain possible. A long quiet interval does not establish universal resource use or leak freedom.

An earlier 384-case suite passed on an isolated .NET Core 10.0.15 test host assembled from the pinned runtime pack under the development workspace. The runner and its test host used the explicit private runtime root; test-host diagnostics confirm the actual 10.0.15 framework location, and that root had no other framework version available. Harmless child-process fixtures used the same private host through that test process's PATH. No machine-wide runtime was installed. This supplements the normal SDK-driven release test pass and the actual self-contained WPF launch.

The dependency audit lists no NuGet vulnerability entries reported by the configured public source at the time of that query. It does not audit every native component or prove the absence of vulnerabilities; pinned dependencies and servicing still need review.

## Capabilities and remaining gates

| Area | Implemented and verified scope | Limit |
|---|---|---|
| WinGet | Structured live discovery; exact source/ID/version/scope/architecture commands; pins and fresh installation checks; fixture and queue tests | A real third-party upgrade was not run on this PC |
| Official installer | Reviewed Notepad++ fallback; actual metadata/download/trust validation; mocked safe execution tests; individual review | No real third-party installer execution; one curated product only |
| Vendor managers | Expected-publisher executable checks or fixed official guidance; NVIDIA handoff observed | No unsupported APIs or automated vendor UI; handoff never equals update success |
| Store/MSIX | Launchable current-user inventory, signature-kind ownership, Store update handoff | No unattended Store update control |
| Scoop/Chocolatey | Local manifests and ownership, official guidance | Online discovery/execution not implemented; managers absent on host |
| Portable/plugins | Explicit folders; bounded metadata/static PE scans; separate plugin filter | No whole-drive scan, plugin loading or verified portable release feed |
| Recovery | All pending work durable before launch, including queues beyond 2,000 entries; corruption recovery pauses; Running/Verifying interruptions and unverified outcomes retain holds | Actual power loss, failed media and hostile multi-user races untested |
| Scheduling | Daily/weekly while open or in tray; bounded failure backoff, clock-change handling and connection/power/quiet-hours policy; never installs | No closed-app OS task/service |
| Migration | Versioned safe manifest; searchable 25-ID metadata preview, current-PC architecture/scope applicability, missing/manual results and saved preview | No reinstall, policy import, license transfer, driver/OEM or project-compatibility guarantee |
| Drivers | GPU/version observation and reviewed manual workflow | No independently verified latest-driver feed, unattended driver or firmware install |
| Windows x64 | Self-contained builds, actual GUI/providers, own-app setup lifecycle | Clean Windows 10/11, restricted-account/proxy and multi-user matrix open |
| Windows ARM64 | Cross-built app/native dependencies and installer; PE audit | Actual ARM64 hardware/runtime testing open |
| Accessibility | Native controls, named rows/checkboxes, keyboard focus, live-region status, system high contrast/reduced motion and monitor-fitting minimums | Full screen-reader and real multi-monitor DPI audits open |
| Signing/self-update | Honest unsigned metadata and real signing hooks | No trusted signing credentials, first-party feed, operational self-update or rollback |

Production priorities: isolated real-update verification on disposable PCs; clean Windows 10/11 and ARM64 hardware testing; accessibility/restricted-environment/storage-fault validation; trusted signing and controlled publication. A signed first-party feed and additional reviewed provider execution are worthwhile later work. Cross-publishing and unit tests do not substitute for those release gates.



The Games maintenance build adds queue exclusion tests, reviewed platform coverage and installed Epic metadata parsing tests. Earlier installer, idle and private runtime observations belong to the previous build; consult current release metadata and maintenance evidence for the new binaries.

Grouping tests cover related runtime versions, distinct Visual Studio families, shared brands and paint.net remaining separate from .NET runtimes. Gaming ownership and legacy/conflict records are excluded from every desktop update-plan section.

Hardware tests cover motherboard/CPU/RAM/storage parsing, DDR type codes, unavailable sources and absent GPU memory remaining unreported. The real local hardware query is read-only.

Dark-mode controls use explicit theme foregrounds for expanders, labels, tooltips and menus. The footer shows only the version. Vendor manager navigation is inside Updates.

Publisher workflow coverage includes additional IK products, NVIDIA HD Audio, ME-90 IR Loader and .NET SDKs explicitly registered as from Visual Studio. Routing requires the reviewed product/publisher pair and never claims a newer release or successful installation. Gaming unknown states are excluded from the desktop dashboard count.

Shared-release checks require an exact normalized publisher identity and product-word boundaries for vendor routing; substring lookalikes are rejected. A known vendor workflow does not claim a verified release. Update/review actions require fresh catalog evidence, a newer version on the same track/channel, known architecture/scope and a local absolute installation path. Stale metadata removes the action and updates dashboard counts; per-operation process, disk, restart and identity checks still run immediately before execution.
