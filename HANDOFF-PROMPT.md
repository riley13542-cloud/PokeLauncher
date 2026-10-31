# PokeLauncher — New Chat Handoff Prompt

Copy everything below the line into a new chat as your first message.

---

# PokeLauncher — Continuation of Development (Phase 2, next milestone)

We are continuing development of **PokeLauncher**, a Windows desktop Pokémon game launcher
built with C# / .NET 10 / Avalonia UI 12 / MVVM. This is an **existing project — do not
recreate it from scratch.**

Before doing anything, read `AGENT_RULES.md` and `notes.md` in the project root
(`C:\Users\riley\OneDrive\Documents\PokeRomLauncher`). `notes.md` documents the current state,
conventions, and what has been tested. Note there is no `AGENTS.md` — the rules file is
`AGENT_RULES.md`.

## Where the project stands (as of 2026-10-29)

- Full UI shell is complete and polished: Home, My Library, ROM Hack Browser, Favorites,
  Recently Played, Collections, Settings, About, plus Game/RomHack detail pages. All
  generation labels use Roman numerals (Gen I–IV). Covers are generated gradient
  placeholders (`Controls/CoverPlaceholder` + `Controls/PokeBallMark`); no image assets.
- **Local persistence works (milestone 0.5.0):** `Services/AppDatabase` — SQLite via
  Microsoft.Data.Sqlite 10.0.14 at `%LocalAppData%\PokeLauncher\library.db` (WAL, foreign
  keys). Seeds 16 official Gen I–IV catalog games, 4 collections, and 4 emulator-system rows
  on first run. Persists: favorites, rom_path, last_played, playtime_hours, hack bookmarks,
  collections, settings key/value (`ui.accent`, `library.gridView`, `library.folderPath`),
  and `emulator_configs` (one row per system key `GB`/`GBC`/`GBA`/`NDS`: `executable_path` +
  `arguments` supporting a `{rom}` placeholder). The DB layer fails soft: on failure it sets
  `LoadError` and runs in memory without overwriting data. All 16 behavioral checks passed
  (temp console harness at `C:\Users\riley\AppData\Local\Temp\opencode\DbTest`, disposable).
- Build: `dotnet build PokeLauncher\PokeLauncher.csproj` — currently 0 warnings, 0 errors.
  The project does NOT enable ImplicitUsings; new files need explicit `using` directives.
- Settings page now has **per-system emulator cards** with an exe Browse dialog and arguments
  field, plus a Data Storage card showing the DB path.
- Not yet implemented: ROM import (the Import button intentionally shows an honest "later
  phase" message; DB schema already has `games.rom_path`), emulator launching, playtime
  tracking, custom-collection creation, metadata editing, save backups, patching.
- Hard constraints from me: **no copyrighted ROM files ever bundled; no emulator binaries
  distributed without respecting their licenses; ROM hacks get creator/legitimate sources
  only; no automatic ROM downloads.**

## This session's main feature: automatic emulator download & configuration

I want to eventually distribute PokeLauncher so that when a user installs it, the app **can
download and configure all required emulators itself** — the user should not have to hunt for
emulator binaries. Implement this in a legally sound, modular way:

### Design direction (agreed reasoning)

- **Do not pre-bundle emulator binaries with the app by default.** Instead, ship a
  **manifest** (`emulators.json`, kept in the repo and/or fetchable from a URL I control)
  describing each emulator: system key, name, license + source link, official download URL,
  archive format, SHA-256 checksum, exe path inside the archive, ready-made launch arguments.
- **Download on demand from official sources only**, with explicit user consent in the UI
  (a "Download mGBA" style button in Settings per system, plus optionally a first-run setup
  dialog). Never use mirror/aggregator sites.
- Verify every download by **SHA-256** before extraction; extract to
  `%LocalAppData%\PokeLauncher\Emulators\<system-key>\<name>-<version>\`; then write the
  discovered executable path into the existing `emulator_configs` table so the already-built
  config UI and future launcher service use it unchanged.
- Add new columns or a related table (e.g. `emulator_downloads`: version, sha256,
  downloaded_at, source url, install folder) rather than hacking strings into
  `emulator_configs.arguments`.
- Extraction: .zip via System.IO.Compression; .7z needs **SharpCompress** (MIT) — check
  package compatibility and licensing before adding it, per the project rules.

### Verified emulator facts (researched 2026-10-29 — re-verify versions/URLs at build time)

- **mGBA** (covers Game Boy, Game Boy Color, AND Game Boy Advance with one emulator):
  MPL-2.0, official site https://mgba.io, official GitHub releases
  `https://github.com/mgba-emu/mgba/releases` (e.g. `mGBA-0.10.5-win64.7z` portable
  Windows 64-bit build). MPL-2.0 permits redistribution with license notice; a
  download-from-official-source flow is the recommended default either way.
- **melonDS** (Nintendo DS): GPL-3.0, repository `melonDS-emu/melonDS` on GitHub (releases
  there), official site is **https://melonDS.kuribo64.net (a.k.a. melonDS.io)**.
  **WARNING: `melonds.org` is NOT the official site — it distributes ROMs. Never link or
  download from it.** melonDS normally needs NEC DS BIOS/firmware dumps (copyrighted); check
  whether the current release supports BIOS-less "Direct Boot" or bundles an open FreeBIOS —
  if not, show an honest message telling the user DS boot requires their own firmware dump.
- **Fallback / all-in-one option:** RetroArch (GPL-3.0) with the Gambatte/mGBA/melonDS cores —
  one download covers all systems, but adds configuration complexity; treat as a secondary
  option, not the first choice.
- Licenses to respect when this ships to users: keep a third-party notices file listing
  mGBA/melonDS/RetroArch licenses and source links, shown in the About/Settings UI.

### Suggested milestone order for the emulator-downloader feature

1. Manifest loader (`Models/EmulatorManifest` + `Services/EmulatorCatalog`) with schema
   validation and tests using a local file.
2. `Services/EmulatorDownloadService`: consent-driven download (progress UI), SHA-256 check,
   atomic extraction (temp dir + rename), then auto-configure `emulator_configs`.
3. Settings UI: per-system "Download official build" button + status (not installed /
   downloading / installed vX.Y at path), reusing the existing per-system cards.
4. Verify end-to-end: download mGBA (only emulator needed for GB/GBC/GBA), confirm config row
   update, confirm deletion cleanup path, and test checksum-mismatch failure handling.
5. Only afterwards build the actual game launching service that consumes these configs.

## Broader remaining roadmap (after / alongside the above, one milestone per session)

1. ROM import: file + folder pickers (Avalonia `StorageProvider`, already used in Settings),
   extension filter .gb/.gbc/.gba/.nds (+zip), identify against the 16 catalog entries,
   write `rom_path`, honor the library folder setting, "remove from library" without deleting
   the original file.
2. Game launching: `LauncherService` mapping `Platform` → system key via
   `EmulatorSystems.SystemKeyForPlatform`, `{rom}` substitution, error surfaces for missing
   exe/ROM, playtime + last played updates (`SaveGamePlayState` already exists).
3. Save management backups (never overwrite without confirmation + recovery safeguards).
4. Collections creation/editing, game metadata editing, game details polish.
5. Full testing & refinement pass. Update `notes.md` after every meaningful milestone.

## How to work with me

- Take initiative with implementation rather than asking me to perform routine tasks.
- Explain important technical decisions in plain language; ask before major architecture or
  scope changes; never silently remove functionality.
- Test actual behavior (build + run + UIA scripts via PowerShell
  `System.Windows.Automation` work well on this app), not just compilation.
- Distinguish clearly in your reports what is implemented, tested at runtime, and unverified.
