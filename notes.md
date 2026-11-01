# PokeLauncher — Project Notes

## Purpose
Windows desktop Pokémon game launcher: emulator launching, ROM library, and ROM hack catalog.
Phase 2 (functional launcher) complete through milestone **0.12.0**: release readiness.

## Stack & conventions
- C# / .NET 10.0 (
et10.0`), Avalonia UI 12.1.4, MVVM via CommunityToolkit.Mvvm 8.4.2
- Persistence: **Microsoft.Data.Sqlite 10.0.14** (MIT). Database at
  `%LocalAppData%\PokeLauncher\library.db` (WAL, foreign keys). Deliberately outside the
  OneDrive-synced project folder to avoid sync conflicts.
- Desktop app only. Template layout: `Views/`, `ViewModels/`, `Models/`, `Data/` (seed catalog),
  `Services/` (persistence/launch/import), `Styles/` (Tokens/Controls/Cards/ClassStyles), `Controls/`.
- Project does NOT enable ImplicitUsings — new files need explicit `using` directives.
- CommunityToolkit `[ObservableProperty]` partial-property (C# 13) syntax is used in places;
  generated backing fields are NOT assignable from user code — initialize via property with an
  `_initializing`/`_suppress*` guard when ctor-time writes must not trigger persistence handlers.
- **Gotchas learned in 0.7.0 (keep following these):**
  - Avalonia names StackPanel-content buttons "Avalonia.Controls.StackPanel" in UIA — give
    interactive buttons explicit `AutomationProperties.Name` (cards, Import button have them now).
  - ComboBox `SelectedItem` TwoWay can push a transient **null** into the VM while `ItemsSource`
    resolves (especially with `$parent` bindings) — use null-tolerant setters for selection
    properties and keep choice lists on the item VM itself (see ImportRowViewModel).
  - Computed labels need explicit `[NotifyPropertyChangedFor(...)]` (bit us twice).
  - Avalonia modal dialogs appear *under the main window* in the UIA tree, not as root children.
  - NEVER name an async relay method so its command ends in `…AsyncCommand` blindly:
    `PlayAsync`/`PlayAsyncCommand` triggered a flaky AVLN2000 compiled-binding error (~2/3 of
    clean builds); `Play`/`PlayCommand` is stable. If a similar flake appears, rename the command.
  - When a build "passes", double-check no stray PokeLauncher.exe is locking the exe — a locked
    copy step makes builds fail while old binaries keep running (cost us a debugging loop).
  - OneDrive can briefly hold file handles; retries exist in the emulator install path.
- Agent rules live in `AGENT_RULES.md` (there is no `AGENTS.md`).

## State (verified 2026-10-31)
- `dotnet build`: 0 warnings, 0 errors. Version **0.12.0-distribution** (About page).
- **Managed library (0.9.0)**: imported ROMs are **copied** into
  `%LocalAppData%\PokeLauncher\ROMs\<game-id>\` (`Services/ManagedLibrary`); `rom_path`
  points at the managed copy; originals are never moved/modified. Attaching a ROM to a
  catalog game and details-page imports also copy. Detaching a ROM or removing an
  imported game deletes PokeLauncher's managed copy + save backups (disclosed in the
  dialog text) but never the user's original file. Emulators therefore write their saves
  inside PokeLauncher's data folder, next to each managed ROM. The old placeholder
  "Game library folder" setting (`library.folderPath`) was replaced by a "Managed
  library" Settings card (ROMs + Saves paths, open-folder buttons via a window-wired
  `OpenFolderHandler`).
- **Save backups (0.9.0)**: `Services/SaveBackupService` — before **every** launch the
  game's current save files (`.sav/.srm/.dsv/.sa1/.sa2/.sav2/.rtc/.ss0-.ss9` next to the
  ROM) are snapshotted to `Saves\<game-id>\yyyy-MM-dd_HH-mm-ss\` (additive, same-second
  collisions get " (n)" suffixes that the lister still parses; newest-first ordering is by
  folder name). Details page "Saves and backups" section: list with day/time + file count
  + size, **Back up now**, per-backup **Restore** (two-step confirm; takes a safety
  snapshot of the current saves first) and **Delete** (single click, redundant copy only).
  Backups are blocked while the emulator is running; the last 20 per game are kept
  (pruned on creation). A failed pre-launch backup warns but does not block launching.
- **Persistence schema (0.7.0)**: `games` gained `is_imported` (migrated in place via
  `AppDatabase.MigrateSchema`, additive only); new `play_sessions` table (game_id FK cascade,
  started_at/ended_at/duration_seconds) with an index. Migration verified against a real 0.6
  database (the user's own DB migrated on first 0.7.0 launch; 16 catalog rows preserved).
- **Collections (milestone 0.8.0)**: fully user-manageable — create / rename / re-member /
  delete via `EditCollectionDialog` (member editor = one checkbox per game with a filter box;
  delete uses the two-step confirm). DB methods `AddCollection` / `SaveCollection` (name+desc +
  member replace, transactional) / `DeleteCollection` / `LoadCollectionNamesForGame`.
  `collections` seed exactly once (`collections.seeded` settings flag) so a deliberately
  emptied collections list survives restarts; in persistent mode `LoadCollections` returns the
  user's real data even when empty (no mock fallback). Collections page: chips + "New
  collection" button, per-selected-collection "Edit…", honest empty state when none exist.
- **Session history (0.8.0)**: details page "Recent sessions" section — last sessions formatted
  via `GameViewModel.FormatHours` (shared with PlaytimeLabel) + humanized day, "N sessions
  recorded" caption, backed by `LoadPlaySessionsForGame` / `CountPlaySessions`; reloaded on
  SetGame, after each session end, and after collection commits. Details page also shows the
  game's collection membership ("Collections" row).
- **ROM import (milestone 0.7.0)**: `Services/RomImportService` — extension → platform/generation
  detection (.gb/.gbc/.gba/.nds/.zip), conservative filename→catalog matching (normalized equality
  only; "Randomized FireRed" does NOT attach), duplicate detection by exact rom path and by
  imported title+platform twins, unsupported/empty/missing-file reasons. UI: file picker →
  `ImportGamesDialog` review rows (editable title/platform/gen for new entries, attach vs
  duplicate chips) → commit (`AddImportedGame` / `SaveGameRomPath`). Import entry points: My
  Library "Import Games" (multi-file) and Game Details "Import ROM…" (single attach; if the file
  clearly matches a different catalog game it links there and says so).
- **Launching**: `Services/GameLauncherService` — per-platform config read live from
  `emulator_configs` each launch, `{rom}` substitution (quoted; appended when the template lacks
  the placeholder), `Process` started non-blocking (UseShellExecute=false), exit observed via
  `process.Exited` + a one-shot guard (fires even if the process exits before the hook), double
  launch refused, `IsGameRunning` drives a "Now playing" chip and a "Playing…" button label.
  Errors are specific (missing ROM / vanished ROM / unconfigured / missing exe / start failure)
  and surface both as toasts and an inline strip on Game Details with an "Open Settings" shortcut
  when emulator-related. Home hero "Play" launches the featured game through the same pipeline.
- **Playtime**: on launch success `last_played` is stamped immediately; on process exit the
  session duration is added to `games.playtime_hours`, a `play_sessions` row is inserted, and the
  UI updates live (GameViewModel is now the mutable state holder — `ApplyPlayState`,
  `ApplyRomPath`, `ApplyMetadata`, `Snapshot()`). Failed launches create no sessions or playtime.
  Emulators exiting <3s trigger an honest "closed almost immediately" toast; non-zero exits are
  reported but still recorded. Short sessions display as seconds/minutes (PlaytimeLabel).
- **Metadata editing**: `EditGameDialog` (title/platform/generation/description) persists via
  `SaveGameMetadata`; imported games can be removed (two-step confirm; deletes row + cascades,
  never the file) and any game can detach its ROM. Catalog entries cannot be deleted.
- Game cards, hero, and details chips handle unknown years ("Year unknown") and missing ROM files
  ("ROM file missing" chip + re-attach hint strip).
- Pages recompute live on play-state changes (Recently Played, Library sorts, Home Continue
  Playing + stats) and accept imports/removals (`AddGames`/`RemoveGame` everywhere).
- **Tested (0.9.0)**: console harness M090 (`%TEMP%\opencode\M090Test`, 20/20: managed copy
  semantics, backup/restore/prune/safety-snapshot, foreign-path refusal, launch integration
  with a save-writing fake emulator) + regressions M080 (22/22) and M070 (56/56) + UIA against
  the real app and real DB (`%TEMP%\opencode\M070Uia\M070Uia.ps1`, with
  `%TEMP%\opencode\M070Seed` for surgical snapshot/seed/cleanup; the seed tool also
  snapshots/restores collections and manages the test game's managed ROM + saves folders, and
  has a `rompaths` mode): 0.9 Phase F (UI import lands a managed copy; rom_path verified under
  ROMs\) 6/6, Phase G (back up now → modify save → two-step restore reverts file content →
  play auto-backs up, 3 total) 11/11; 0.8 Phase D 17/17, Phase E 7/7; 0.7 Phases A 20/20, B
  9/9, C 9/9 (0.9.0 version check). DB + managed folders returned to pre-test state (verified).
- Emulator download flow (0.6.0) unchanged; mGBA 0.10.5 is installed on this machine via it.

## Not yet implemented (explicit)
- Cover artwork for ROM hacks (games support custom covers; hacks keep generated art), ROM
  patching, save-state cloud sync (future milestones). **Real Gen I–IV ROM files are NEVER
  bundled** (copyright) — the ROM inbox + import flows attach the user's own files instead.
- "Add to collection" quick-action on game cards / from the details page (membership editing
  currently lives in the Collections page's edit dialog).
- 0.7.0-era imports whose rom_path points OUTSIDE the managed library keep working (launch +
  backups read from the ROM's own folder) but are not auto-migrated into ROMs\<game-id>\;
  re-importing or re-attaching a ROM copies it in.
- Git repo not initialized (not requested). Backups: `Backups/2026-10-29_Pre-EmulatorDownloader/`,
  `Backups/2026-10-30_Pre-Import-Launch/`, `Backups/2026-10-30_Post-0.7.0_Pre-Collections/`,
  `Backups/2026-10-30_Post-0.8.0_Pre-Saves/` (the last one is a full 0.8.0 recovery point).

## Requirements / constraints (from user)
- No copyrighted ROMs or emulator binaries bundled/downloaded. Creator/legitimate sources only.
- Do not overwrite/delete existing files without permission (import/removal never touch ROM files).
- Keep emulator integration modular (embedded cores possible later).
- Windows desktop; C#/.NET; Avalonia; MVVM; explain major architectural decisions.

## Session log
- 2026-10-29: Project setup completed and verified.
- 2026-10-29: App shell + navigation phase completed and verified.
- 2026-10-29: Phase 2 continuation handoff written to `HANDOFF-PROMPT.md`. Includes the
  agreed direction for automatic emulator download: manifest-driven (`emulators.json`),
  consent-based download from official sources only, SHA-256 verification, install under
  `%LocalAppData%\PokeLauncher\Emulators\`. Verified facts: mGBA = MPL-2.0 (one build covers
  GB/GBC/GBA); melonDS = GPL-3.0 (melonds.org is unofficial - never link it); melonDS may need
  user-provided BIOS/firmware. Distribution decision: prefer on-demand download-from-official-source.
- 2026-10-29: Milestone "persistence foundation + settings" (0.5.0): SQLite store added; mock
  user state replaced with honest seeds; favorites/bookmarks/emulator settings persisted;
  per-system emulator config UI. Verified via temp console harness (16/16) + UIA.
- 2026-10-29: Milestone "emulator setup" (0.6.0): manifest-driven consent downloads
  (`emulators.json`), SHA-256 verification, atomic extraction, auto-configuration of
  `emulator_configs`, per-system download UI + removal. mGBA 0.10.5 installed via the flow.
- 2026-10-30: User-facing instructions written to `INSTRUCTIONS.md` (ROM inbox + import,
  cover art naming, logo swap, emulator setup, troubleshooting).
- 2026-10-30: **Incident**: OneDrive sync turned the project folder into a reparse point and
  de-hydrated/wiped the working tree mid-session. All work was recovered; the user moved the
  project out of OneDrive to `C:\Users\riley\Documents\PokeRomLauncher` (outside sync).
  Lesson: keep this project OUT of OneDrive; backups in `Backups\` were the safety net.
- 2026-10-30: **Milestone "core game functionality" (0.7.0)** - ROM import system
  (RomImportService + ImportGamesDialog review flow + duplicate protection), game launching
  (GameLauncherService + per-platform configs + specific error surfaces), playtime tracking
  (play_sessions table + live GameViewModel state), metadata editing and imported-game removal
  (EditGameDialog), live page refresh everywhere. Verified: 56/56 harness, UIA A 20/20, B 9/9,
  C 9/9; real-DB migration preserved the user's data. Bugs fixed: missing `process.Exited` hook,
  ComboBox TwoWay null-push into import rows ("Value must be set." SQLite error), missing
  PropertyChanged for RemoveButtonLabel, flaky AVLN2000 from `PlayAsyncCommand` (renamed),
  review dialog focusing Confirm on open (now focuses a text field).
- 2026-10-30: **Milestone "managed library + save backups" (0.9.0)** - imported ROMs are copied
  into `%LocalAppData%\PokeLauncher\ROMs\<game-id>\` (ManagedLibrary; attach flows copy too;
  originals untouched), so emulator saves land inside PokeLauncher's data folder automatically;
  SaveBackupService snapshots saves before every launch (additive, 20-per-game prune, same-second
  suffixes), details-page "Saves and backups" section (list + Back up now + two-step restore with
  a safety snapshot + delete), Settings "Managed library" card. Verified: M090 20/20, M080 22/22,
  M070 56/56, UIA F 6/6, G 11/11, D 17/17, E 7/7, C 9/9, A 20/20, B 9/9.
- 2026-10-30: **Milestone "collections + history" (0.8.0)** - custom collections
  (create/rename/re-member/delete via EditCollectionDialog + checkbox member editor + filter;
  two-step delete), LoadCollections no longer falls back to seeds in persistent mode,
  details-page "Recent sessions" section + collection membership row, shared FormatHours.
  Verified: M080 22/22, M070 56/56, UIA D 17/17, E 7/7, C rerun 9/9.
- 2026-10-30: **Milestone "logo, covers, inbox + polish" (0.10.0)** - brand logo (generated
  app-icon.png/.ico; a user-supplied %LocalAppData%\PokeLauncher\logo.png overrides the sidebar
  mark + window/taskbar icon), per-game custom cover art (Covers\<game-id> via ManagedLibrary,
  details-page Set/Remove cover art, sidecar "<romname>.png" auto-attach on import, Settings
  toggle, GameCover control used by cards/rows/hero/details), ROM inbox (ROMs\Inbox staging
  folder + "Check ROM inbox" review flow), "Missing ROMs" library filter, import dates
  (games.imported_at), collection toggle chips on details (SetGameMembership), backup-retention
  setting, About "What's new" card. **Bundling real Gen I-IV ROMs was declined (copyright)** -
  the inbox + import flows attach the user's own files instead. The user supplied their own app
  icon (Assets/app-icon.png/.ico; avalonia-logo.ico removed as unreferenced). Verified: M100
  16/16 (imported_at incl. pre-0.10 migration, covers, inbox matching, retention), M090 20/20,
  M080 22/22, M070 56/56, UIA H 13/13, G 11/11, F 6/6, E 7/7, D 17/17, C 9/9, B 9/9, A 20/20.
  Bugs fixed: stale _coverImage when a cover was deleted, FindGameByRomPath column mismatch
  (regression suite caught it), MissingRomsOnly missing its Recalculate hook, duplicate sidecar
  helper.
- 2026-10-30: **Milestone "distribution + hack library" (0.11.0)** - details-page overlap fixed
  (Avalonia Grid needs explicit RowDefinitions for Grid.Row; rows had stacked into one since
  0.7), info-strip text overflow fixed (horizontal StackPanels measure children with infinite
  width - replaced with Grid columns). Built-in covers: the user's 16 Assets\Covers\<game-id>
  images are embedded and shown for the built-in catalog. ROM hack browser is now a real user
  library: rom_hacks table + CRUD, Add/Edit dialog (base game from the 16 catalog games,
  patched-ROM browse, genre/difficulty/status), launch through the base game's emulator, empty
  state + Add buttons, fictional seed hacks removed. Installer: setup\PokeLauncher.iss (Inno
  Setup 6, per-user install) + setup\build-installer.ps1. Updates: Services\UpdateService -
  manifest (version/url/sha256/notes) checked at startup and from About; download verified by
  SHA-256; the user runs the downloaded installer themselves. Manifest URL:
  UpdateService.DefaultManifestUrl (empty = checks disabled, UI says so honestly) or
  POKELAUNCHER_UPDATE_MANIFEST env override. Verified: M100 16/16, M090 20/20, M080 22/22,
  M070 56/56, UIA H 13/13, I 9/9, Release publish smoke, real-DB verify re-baselined to the
  user's own 3 play sessions (kept - their data).
- 2026-10-31: **Release readiness (0.11.1)** - uninstall now OFFERS full data removal: Inno
  Setup [Code] prompts to delete all PokeLauncher data and DelTree's %LocalAppData%\PokeLauncher
  on Yes (No keeps data for reinstall); inbox caveat disclosed in the prompt. DISTRIBUTION.md
  written (installer build, hosting, update.json format, release checklist).
  setup\build-installer.ps1 defaults to SELF-CONTAINED publish, takes -Version (passed to ISCC
  /DAppVersion), and generates setup\installer\update.json for the update checker.
- 2026-10-31: GitHub is live: repo github.com/riley13542-cloud/PokeLauncher (public, main
  pushed), release v0.11.1 with setup.exe + update.json attached. DefaultManifestUrl set to the
  stable releases/latest/download/update.json URL. Privacy option discussed: public repo =
  source always downloadable (cannot be disabled); for private source + public releases use a
  second public repo for release assets and point DefaultManifestUrl at it. Gotcha: winget
  installs Inno Setup PER-USER (%LocalAppData%\Programs\Inno Setup 6) - build script checks
  that path.
- 2026-10-31: **Milestone "release polish" (0.11.2)** - details info grid got explicit
  RowDefinitions (Avalonia Grid ignores Grid.Row without them; rows stacked into one spot,
  overlapping the collection picker - broken silently since 0.7, reported by the user with a
  screenshot) and info strips were reworked to Grid columns (text inside a horizontal
  StackPanel never wraps - it measured 1052px and overflowed the window). New Import-from-folder
  flow on My Library (pick a folder, one confirmed step attaches every matched game; files are
  only read). Version 0.11.2. Verified: M100 16/16, M090 20/20, M080 22/22, M070 56/56.
- 2026-10-31: **Release readiness (0.12.0)** - installer is now FRAMEWORK-DEPENDENT (11 MB vs
  89 MB) and checks for the .NET 10 Desktop Runtime when setup starts: registry-based detection
  (HKLM64/HKCU64 dotnet Setup sharedfx Microsoft.WindowsDesktop.App 10.*, per-machine and
  per-user), prompt to download from the official aka.ms/dotnet/10.0 link (BITS download via
  PowerShell + interactive Microsoft installer + re-check), aborts honestly if still missing.
  Existing-install detection via GetPreviousData (upgrade preserves data; same-version reinstall
  asks first), running PokeLauncher.exe closed before file replacement. The .NET-missing branch
  is code-reviewed only (this machine has the runtime). setup\installer now holds the 0.12.0
  setup.exe + update.json (url points at releases/download/v0.12.0/). GitHub: main pushed to
  20153f6. Release process documented in DISTRIBUTION.md. Bug fixed: the melonDS TOML
  reader didn't track [section] headers (bare keys inside [Keyboard] read as -1) -
  fixed with section tracking; round-trip verified.
- 2026-10-31: Version 0.12.1 - Settings customization shipped (startup page, inbox toast
  toggle, per-emulator fullscreen launch option for mGBA/melonDS) and the installer
  rebuilt + verified (silent install produces all files, app launches). The 0.12.0
  local artifacts were removed after the v0.12.0 GitHub release; 0.12.1 is the current
  distributable.
- 2026-10-31: **Milestone "one-click updates" (0.12.2)** - IsNewerVersion fixed: Version.TryParse
  failed on the -distribution suffix (e.g. 0.11.1-distribution vs 0.12.1 always reported
  up-to-date - THE root cause of the reported update.json not working); now parses the
  numeric prefix. About page: one-click install - Check for updates, Download update
  (SHA-256 verified), then Install update now runs the verified installer and closes
  PokeLauncher (data preserved; Inno /SILENT /CLOSEAPPLICATIONS). Verified: M070 56/56,
  M080 22/22, M090 20/20, M100 16/16; live check vs the hosted v0.12.1 manifest reports
  available=True for a 0.11.1 client; installer runs from the one-click path. Version
  0.12.2-distribution; setup\installer has the 0.12.2 setup.exe + update.json (double-slash
  url fixed). PENDING: the update.json attached to release v0.12.1 still has the wrong url
  + BOM - replace it or supersede with the v0.12.2 release.
- 2026-10-31: **0.12.3 - GBA emulator-config pollution fixed** - the user reported
  placeholder/auto-filled paths in Settings > GBA. Root cause: M090Test used
  new AppDatabase() (the REAL library.db) and wrote a PowerShell path into
  emulator_configs.GBA on every run. Fixed the user DB (fixgba), re-baselined the seed
  snapshot (snapemu mode added; restoreemu available), and fixed M090Test to use a temp
  DB (root\library.db) - verified 2 consecutive clean runs with no pollution. M070Seed
  also gained emulator_configs snapshot/restore modes. Installer: 0.12.3 built
  (setup\installer\PokeLauncher-0.12.3-setup.exe + update.json, sha 9d17a7ec...).
- 2026-11-01: **0.12.4 - keyboard-dialog Save fixed + installer data-loss bug fixed**.
  (1) USER-REPORTED: Save controls in the keyboard dialog did nothing for melonDS/mGBA -
  root cause: ControlsDialog.OnOpened had an EMPTY wiring block (lost in an earlier
  cleanup), so SaveRequested/RequestClose had no listeners and the dialog never closed.
  Fixed: Save now closes the dialog with the map and MainViewModel writes the emulator
  config. Verified end-to-end via UIA on both mGBA (config.ini keyA=88 written on disk)
  and melonDS (melonDS.toml A=88, Start=16777220).
  (2) INCIDENT + FIX: an automated uninstall run with /SILENT /SUPPRESSMSGBOXES
  auto-answered the "delete all data?" prompt (default Yes) and DelTree'd
  %LocalAppData%\PokeLauncher - emulators, managed ROM copies, save backups, and the
  DB WAL were deleted. The .iss now SKIPS data deletion entirely when WizardSilent
  (interactive-only opt-in). Recovered: both emulators re-downloaded via the app's own
  SHA-256-verified pipeline (EmulatorDownloadService probe), all four emulator_configs
  rows repointed and verified; standard keyboard maps written (mGBA: X/Z/A/S/arrows,
  melonDS: adds X=S/Y=A/L=Q/R=W). LOST (in the WAL, unrecoverable): the 9 play-session
  records. User's own ROM files outside %LocalAppData% were never touched.
  Installer: 0.12.4 built (sha 499fa0ab...); all 4 harnesses green after the fix;
  M090 temp-DB fix held (no emulator-config pollution across runs).
- 2026-11-01: **0.12.5 - Cancel crash in keyboard dialog fixed** - USER-REPORTED: pressing
  Cancel crashed the app. Root cause: Cancel closed the dialog with result=false (bool),
  but MainViewModel awaits ShowDialog<IReadOnlyDictionary<string,int>> - Avalonia casts
  the result to T, bool->dictionary throws InvalidCastException on resume, app dies.
  Cancel now closes with null (dialog result type matches). Also: Esc during assignment
  now DISARMS the row (the tip always said it would - the code was assigning Esc as the
  key instead). Verified via UIA: cancel closes dialog + app survives, esc disarms, save
  still writes config.ini (keyA=88). All 4 harnesses green. Installer 0.12.5 built
  (sha 499fa0ab->replaced; new sha in update.json); installed locally.
- 2026-11-01: **0.12.6 - Appearance settings expanded** - three new options in Settings >
  Appearance, all following the accent-style runtime resource swap (no restart):
  (1) Theme: Dark / Light / Midnight (OLED) via new ThemeService - swaps the surface/text/
  border/input tokens + sets RequestedThemeVariant so built-in controls follow; the two
  hardcoded dark colors (chip bg, card hover) became InputBackgroundBrush/SurfaceHoverBrush
  tokens so light mode reads correctly. Hero overlay stays dark on purpose (text over
  covers). (2) Cover size: Small/Medium/Large - Cards.axaml now binds the grid card panel
  + GameCover to CardCoverWidth/Height resources (176x208 default). (3) Interface scale:
  90/100/115/130% - MainWindow content wrapped in LayoutTransformControl; MainViewModel
  raises UiScaleChanged, window applies ScaleTransform. All three persist
  (appearance.theme/coverSize/uiScale) and apply on startup. UIA-verified live: cover
  width grows with Large, content scales exactly x1.15 at 115%, theme flips without
  crash. All 4 harnesses green. Installer 0.12.6 built; installed locally.
- 2026-11-01: **0.12.7 - Stats dashboard, screenshots gallery, Discord Rich Presence**.
  (1) Stats page (new nav item): total/week/month playtime cards, most-played bar list,
  recent sessions across all games - new AppDatabase aggregates (LoadPlaytimeByGame with
  games JOIN, LoadRecentPlaySessions, LoadPlaytimeTotals) + StatsViewModel (IRefreshablePage)
  + StatsView (FuncValueConverter bar widths). (2) Screenshots gallery on the game details
  page: ScreenshotService scans emulator folders recursively for images whose filename
  contains the ROM base name (mGBA/melonDS naming), thumbnails via Bitmap.DecodeToWidth,
  click opens the file; hint text mentions F12 in mGBA. (3) Discord Rich Presence via
  Discord's local IPC pipe (discord-ipc-0; op 0 handshake / op 1 SET_ACTIVITY / op 3 ping,
  dedicated drain-reader task): Settings > Integrations toggle + application ID (user
  creates a free app at discord.com/developers), presence set on session start, cleared on
  end, silent no-op when Discord is absent (verified no-crash). Probe-verified: aggregates
  (5 sessions -> 160min total, week/month split correct), screenshot discovery, UIA:
  Stats page renders, Integrations card persists (discord.enabled). All 4 harnesses green.
  Installer 0.12.7 built; installed locally.
- PENDING USER ACTION: the update.json attached to release v0.11.1 has a wrong download url
  (missing /releases/download/v0.11.1/) + a BOM - replace that release asset with
  setup\installer\update.json (delete + re-drag on the release edit page), or supersede it by
  publishing the 0.12.0 release.