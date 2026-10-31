# PokeLauncher — Instructions

How to add your own game files (ROMs), cover art, and your logo — and what to name them.

PokeLauncher never bundles game files, and it never touches your originals:
everything you import is **copied** into its own data folder, so your source files
stay exactly where they are.

---

## 1. Where PokeLauncher keeps things

All of this lives in your data folder:

```
%LocalAppData%\PokeLauncher\
├── library.db        ← the database (library, favorites, playtime, settings)
├── ROMs\
│   ├── Inbox\        ← DROP NEW GAME FILES HERE (see section 2)
│   └── <game-id>\    ← managed copies of your imported files (automatic)
├── Covers\           ← cover art images (see section 3)
├── Saves\            ← automatic save backups, one folder per game
├── Emulators\        ← downloaded emulators (Settings → Emulators)
└── logo.png          ← optional: your own app logo (see section 5)
```

`%LocalAppData%` is a hidden folder — the fastest way in:
**PokeLauncher → Settings → Managed library → Open ROMs folder**.
Or press `Win + R`, type `%LocalAppData%\PokeLauncher`, press Enter.

---

## 2. Adding games (ROM files)

### Option A — the ROM inbox (best for the 16 built-in Gen I–IV games)

1. Open the inbox folder: `%LocalAppData%\PokeLauncher\ROMs\Inbox\`
   (create the `Inbox` folder if it doesn't exist yet)
2. Copy your game files in and name them after the built-in game:

   | File name | Attaches to |
   |---|---|
   | `Pokemon Emerald (U).gba` | Pokémon Emerald |
   | `pokemon red.gb` | Pokémon Red |
   | `Crystal Version.gbc` | Pokémon Crystal |
   | `heartgold.nds` | Pokémon HeartGold |

   Naming rules: capitalization doesn't matter, the "Pokémon" prefix is optional,
   and region tags like `(U)`, `(E)`, `Rev 1` are ignored. Just make sure the
   game title is in there.

3. In the app: **My Library → Check ROM inbox** → review the matches → **Confirm import**.
4. The matched built-in games become playable (their cards change from
   "Not imported" to the file name). The files stay in the Inbox folder —
   delete them from there whenever you like; PokeLauncher already copied them.

### Option B — Import Games (any game, any name)

1. **My Library → Import Games** → pick one or more files.
2. PokeLauncher detects the system from the extension and shows a review screen —
   correct titles/platforms if needed, then confirm.
3. Files whose name matches a built-in game (e.g. `Emerald.gba`) are attached to
   that game automatically instead of creating a duplicate.

**Supported file types:** `.gb`, `.gbc`, `.gba`, `.nds`, `.zip`

### Which built-in games exist

| Game | Id (used for covers) | Console | Typical file |
|---|---|---|---|
| Pokémon Red | `red` | Game Boy | `.gb` |
| Pokémon Blue | `blue` | Game Boy | `.gb` |
| Pokémon Yellow | `yellow` | Game Boy | `.gb` |
| Pokémon Gold | `gold` | Game Boy Color | `.gbc` |
| Pokémon Silver | `silver` | Game Boy Color | `.gbc` |
| Pokémon Crystal | `crystal` | Game Boy Color | `.gbc` |
| Pokémon Ruby | `ruby` | Game Boy Advance | `.gba` |
| Pokémon Sapphire | `sapphire` | Game Boy Advance | `.gba` |
| Pokémon Emerald | `emerald` | Game Boy Advance | `.gba` |
| Pokémon FireRed | `firered` | Game Boy Advance | `.gba` |
| Pokémon LeafGreen | `leafgreen` | Game Boy Advance | `.gba` |
| Pokémon Diamond | `diamond` | Nintendo DS | `.nds` |
| Pokémon Pearl | `pearl` | Nintendo DS | `.nds` |
| Pokémon Platinum | `platinum` | Nintendo DS | `.nds` |
| Pokémon HeartGold | `heartgold` | Nintendo DS | `.nds` |
| Pokémon SoulSilver | `soulsilver` | Nintendo DS | `.nds` |

> **Tip:** **My Library → Missing ROMs** shows exactly which built-in games
> still need a file.

---

## 3. Adding cover art

Cover images live in: `%LocalAppData%\PokeLauncher\Covers\`
(Settings → Managed library shows the path too).

**Naming:** use the game's **Id** from the table above, with an image extension:

| File | Cover for |
|---|---|
| `emerald.png` | Pokémon Emerald |
| `red.jpg` | Pokémon Red |
| `crystal.webp` | Pokémon Crystal |
| `heartgold.png` | Pokémon HeartGold |

Supported image types: `.png`, `.jpg`, `.jpeg`, `.webp`, `.bmp`.
Roughly card-shaped images (2:3 portrait, e.g. 400×600) look best; any size works.

**Easier ways:**
- Open any game's page → **Set cover art…** → pick an image
  (this names and places the file for you). **Remove cover art** undoes it.
- Put the image **next to your ROM file with the same name** before importing
  (`Emerald.gba` + `Emerald.png`) and it's picked up automatically.

Make sure **Settings → Cover art → "Custom cover art (when available)"** is
selected — it's the default.

---

## 4. Playing a game (one-time emulator setup)

1. **Settings → Emulators** — either *Download official build…* (mGBA covers
   Game Boy / Color / Advance; melonDS covers DS) or point the system at an
   emulator .exe you already have.
2. Open a game with a ROM attached → **Play**.
3. Save files live next to each game's managed copy, and PokeLauncher
   **snapshots them automatically before every launch** into `Saves\<game-id>\`.
   Restore any snapshot from the game's page (Saves and backups → Restore).

---

## 5. Your own app logo

1. Save your logo as `logo.png` (square works best) into
   `%LocalAppData%\PokeLauncher\`.
2. Restart PokeLauncher — the sidebar mark, window icon, and taskbar icon all
   use it.
3. To go back to the built-in emblem, delete `logo.png`.

---

## Troubleshooting

- **"No {platform} emulator is configured"** → Settings → Emulators → download
  an official build or browse for your own.
- **"The ROM file … can't be found"** → the file was moved or deleted. Re-import
  it, or use the game's page → Import ROM… to attach it again.
- **A game doesn't appear after an inbox import** → the review screen may have
  flagged it as a duplicate or unsupported — the result toast explains what was
  skipped, and unmatched files stay in the Inbox folder.
- **Icons look stale in Explorer/taskbar** → that's the Windows icon cache;
  restart Explorer or re-pin the app.
- **Nothing plays?** Check **Settings → Emulators** first — PokeLauncher needs
  an emulator per console, exactly like any other launcher.

---

*PokeLauncher never bundles or downloads game files, never modifies your
originals, and never deletes anything you imported without asking. Game files
are yours; PokeLauncher just organizes and launches them.*
