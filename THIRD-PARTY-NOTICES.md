# Third-Party Notices

This file lists third-party software that PokeLauncher either publishes itself, downloads
on the user's explicit request, or depends on legally. It is shown in the About/Settings
Pages and must be kept in sync with `emulators.json` and `PokeLauncher.csproj`.

## Emulators (downloaded on request, never bundled)

PokeLauncher **never ships emulator binaries inside the app**. When a user confirms a
download in Settings, the archive is fetched **directly from the emulator project's own
official release source** (GitHub Releases of the project or its documented official site
— never mirrors or aggregator sites), verified by SHA-256, and extracted to the user's
local `%LocalAppData%\PokeLauncher\Emulators\` folder. Each project keeps its own license,
which the user effectively accepts by using the downloaded software.

| Emulator | Covered systems | License | Official source | Downloads used |
|----------|-----------------|---------|-----------------|----------------|
| mGBA | GB, GBC, GBA | MPL-2.0 | https://github.com/mgba-emu/mgba (site: https://mgba.io) | GitHub Release assets only (`mgba-emu/mgba/releases`) |
| melonDS | NDS | GPL-3.0 | https://github.com/melonDS-emu/melonDS (site: https://melonds.kuribo64.net) | GitHub Release assets only (`melonDS-emu/melonDS/releases`) |
| RetroArch (future option, not offered yet) | all (cores) | GPL-3.0 | https://github.com/libretro/RetroArch (site: https://www.retroarch.com) | not yet included in `emulators.json` |

Notes:
- mGBA is published under the Mozilla Public License 2.0. Downloading from the official
  project (rather than redistributing) is the default flow chosen by PokeLauncher.
- melonDS is published under the GNU GPL-3.0. The official release ships the emulator only:
  DS games will not boot until the user supplies BIOS dumps (`bios7.bin`/`bios9.bin`, plus
  `firmware.bin` for firmware-menu boot) from a console they own. PokeLauncher never
  downloads BIOS/firmware; the melonDS source repo's `freebios/` folder is source code and
  is not included in official release archives.
- `melonds.org` is **not** the official melonDS site (it distributes ROMs). It is never
  referenced or linked by PokeLauncher.

## Dependencies bundled inside this application

| Package | License | Source |
|---------|---------|--------|
| Avalonia (12.x) | MIT | https://github.com/AvaloniaUI/Avalonia |
| CommunityToolkit.Mvvm (8.x) | MIT | https://github.com/CommunityToolkit/dotnet |
| Microsoft.Data.Sqlite (10.x) | MIT (part of .NET data tooling) | https://github.com/dotnet/efcore |
| SharpCompress (1.0.0) | MIT | https://github.com/adamhathcock/sharpcompress — used only to extract 7z emulator archives during user-approved downloads |

## How to update emulator entries

Edit `emulators.json` at the project root: bump `version`, refresh the URL, and record a
**newly computed SHA-256** (compute it by downloading the archive from the official URL
yourself). An updated manifest placed at
`%LocalAppData%\PokeLauncher\emulators.json` additionally lets you ship emulator offers
without rebuilding the app; a copy that fails validation is ignored and the bundled one
stays authoritative.
