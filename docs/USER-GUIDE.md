This maintenance build fixes over-broad restart blocking: queued file operations are advisory, while Windows Update/servicing restart requirements and unreadable restart state still block installations. Installer-reported restart results still stop subsequent work, and history reconciliation retains all pending-restart signals. Unknown inventory and publisher workflows no longer inflate deferred-update counts. The latest hotfix report records current validation; earlier long-idle/installer observations describe the preceding preview.

This PC appears directly above Settings. It reports motherboard brand/model/revision, CPU model/socket/cores/threads/clocks/cache, each RAM module and slot/type/capacity/reported speed/voltage, GPU/driver/dedicated-memory metadata, disk model/capacity/bus/media/firmware, network adapters, displays, BIOS and Windows details. Data is read locally from bounded Windows CIM queries and display-driver registry metadata. No hardware code is loaded and no serial numbers, passwords or license keys are queried. Missing fields remain Not reported; clocks are Windows snapshots, not live benchmarks. Standard Windows inventory does not expose every GPU clock, temperature, power-supply or chipset specification. New profiles default to Dark; saved appearance choices remain supported.

Related desktop entries are grouped into a single collapsed family/brand header with a visible count in Updates and Installed Apps. Expand a group (for example .NET or Microsoft Visual C++) to inspect each installed version, architecture, status and individual action. Single entries remain ordinary rows. Search and filters apply before the count is shown. Grouping changes presentation only: it never merges installations, combines update operations or removes versions.

Games has its own navigation entry after Installed Apps. Steam and GOG installed registrations and local Epic installed-game manifests appear there. Launcher cards cover Steam, Epic, Battle.net, EA, Ubisoft Connect, GOG GALAXY, Riot, Xbox, Rockstar, Amazon Games and itch.io. Existing desktop clients require Windows Authenticode and an expected publisher; otherwise the card opens a fixed official website. These are launcher handoffs, not account/library synchronization. Other vendors and unregistered libraries must be managed manually. Game downloads, game/launcher updates, sign-in, licenses and anti-cheat stay with the platform. Vendor Updaters is an option inside Updates, beside Scan now, rather than a sidebar entry. Games and game launchers appear only in Games. They are excluded from Updates, desktop update counts, Update All, previews and deferred-update lists, including stale or conflicting gaming inventory.

# Using UpdateDeck

UpdateDeck brings installed software, update sources and publisher workflows into one Windows window. This release is an unsigned engineering preview. Keep important work saved and review compatibility-sensitive updates through their publishers.

## Start with a scan

Open `UpdateDeck.exe` from the complete extracted portable folder, or use the installed shortcut. Startup reads installed software and checks connected sources when startup checking is enabled. **Scan now** or **F5** performs a fresh check. Missing sources leave other inventory available.

Use **Installed Apps** to search by name, publisher, package ID or provider. **Ctrl+F** focuses search; on History it focuses history search. Filters separate available updates, verified current results, vendor-managed applications, attention, ignored software, unknown status and optional audio-plugin modules. Column headers change sorting direction. Installed/available versions use numeric ordering where their format is understood.

Select a row to inspect its installed version, raw version evidence, publisher, source, installation scope, architecture, catalog query/refresh time and recent history. **Escape** closes those details. A release being listed does not mean every installation is eligible to update.

| Status | Meaning |
|---|---|
| Update available / Review update | A newer release was reported; installation policy still applies |
| Verified current | A recent trusted-source check found no newer applicable release |
| Cached result / Recheck needed | The saved result has aged out; check again before relying on it |
| Vendor managed | Continue through the application's established updater |
| Needs attention | Identity, version evidence, source or policy needs inspection |
| Cannot verify | Installed software was discovered, but its release status is unknown |
| Pinned / Ignored | The package manager's pin or your saved ignore policy blocks updating |

## Review and run updates

1. Select **Preview plan** to refresh and inspect routine operations, publisher handoffs and deferral reasons. Preview does not start installers or managers.
2. Save work and close applications you intend to update. Resolve a pending Windows restart before another installation.
3. Use **Update All**, **Update selected**, or an individual **Update** action. These refresh and evaluate the plan again before execution.
4. Eligible routine applications install one at a time. An individual **Review update** or **Review installer** action opens a review for supported interactive updates. Complete required publisher agreements and Windows elevation yourself.
5. Inspect the summary and History. Verified work refreshes the inventory. After completing a publisher workflow, use **Recheck remaining** or **Scan now**.

Unknown scope/architecture, stale sources, pins, ignored applications, running software, insufficient space, conflicting versions and sensitive changes are deferred with reasons. UpdateDeck does not override these conditions. A pending restart can legitimately produce an empty automatic queue even while newer releases remain listed.

**Pause** waits at the next safe boundary. **Cancel pending** stops later work; the active installer finishes normally. **Skip next** skips the next pending item. **Retry failures** refreshes inventory and applies policy again. These controls never terminate an installer or restart Windows.

## Resolve an uncertain result

History distinguishes verified success, failure, interaction, publisher handoff and results needing inspection. Installer exit code zero alone is insufficient evidence of success.

If a queue stops with **Needs inspection**, check the publisher's windows, Windows elevation prompts and any child installer. The original command ending does not prove that every child has finished. Once you have inspected the situation, select that History entry and use **Confirm installer ended**. Confirmation clears the monitoring hold; it does not mark the update successful. Run a new scan so the installed version can be checked at the original identity and location before retrying.

Do not retry while an installer may still be active. Restart Windows manually if a publisher or Windows requires it. UpdateDeck preserves interrupted history and does not automatically rerun an interrupted installer.

## Publisher, driver and game workflows

Vendor-managed applications remain with their original manager. Supported actions open a validated installed manager or a fixed official guidance page. Opening a manager is a handoff, not a successful update.

The saved default can open up to two non-driver, non-game managers after Update All. Turn this off in Settings if you prefer to open them individually. Additional managers remain available on **Vendor Updaters**.

**Drivers** shows graphics information observed through Windows. This preview does not independently establish the newest applicable NVIDIA/OEM driver, change driver branches, install firmware or perform unattended driver updates. Use the official vendor/OEM workflow and review hardware compatibility yourself.

Steam/Epic game content stays in its launcher. Audio plugins are metadata inventory only; projects, presets, samples, licenses and individual instruments inside a plugin module are not migrated or updated.

## Save preferences and protect portable data

Settings are a draft until **Save preferences**. A saved installation pause also prevents automatic publisher handoffs; explicit manager actions remain available. Appearance, startup checking, tray behavior, notifications, installation pause, manager handoffs, daily/weekly while-open checks and quiet hours are available. Quiet hours use whole local hours from 0 to 23. Scheduled checks respect connection cost, energy saver and offline state, never install software, and back off after incomplete checks. No closed-app task or background service is installed.

Use selected portable folders or optional plugin discovery when needed. Whole-drive scans are excluded. Folder exclusions apply after saving and scanning. Plugin discovery reads metadata without loading plugin code.

Portable storage normally lives in `Data` beside the executable. Keep the full program folder together, back up Data before moving removable media, and let active work finish before disconnecting a drive. If the folder is read-only, UpdateDeck shows its local fallback storage location in Settings and pauses installations and automatic handoffs. Inspect saved ignores and any unfinished installer from the original portable location before resuming. Each launch that still needs fallback pauses again because the unavailable store's policy/history cannot be verified. The installer edition stores state in your local application data and preserves it on upgrade/uninstall.

## Preview a new-PC inventory

Use **Export inventory** in Settings to save readable application names, recognized package IDs, provider preference, categories, scope/architecture, ignore state and selected preferences. It excludes installation paths, credentials and license keys.

On the target PC, select **Import preview**, search the saved list and choose up to 25 public package IDs for **Check selected IDs**. Recognized local IDs are identified separately. Results show missing IDs, publisher/manual workflows, unavailable sources or installer metadata applicable to this PC. Saved ARM64 metadata can produce an x64 installer result on an x64 target; this is a current-PC lookup, not a claim of cross-machine compatibility.

Inspect the evidence and source refresh time. Catalog applicability does not prove publisher trust, license rights, plugin/project compatibility or OEM driver suitability. **Save preview** saves the results. Import never installs software, changes your update policy or transfers a license.

## Keep diagnostics private

**Export diagnostics** saves application/version history and source capability information. Local databases and command-line diagnostic reports can also contain installation paths. Review these files before sharing them. UpdateDeck does not automatically upload diagnostics or collect license keys and passwords.

See [verification and limits](VERIFICATION.md), [security](SECURITY.md) and [release instructions](RELEASE.md) for the engineering details and remaining production validation.

