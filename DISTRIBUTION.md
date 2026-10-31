# Distributing PokeLauncher — step by step

Everything you need to build an installer for other people, host updates, and
what happens when someone uninstalls.

---

## 1. One-time setup: install Inno Setup 6

The installer is compiled with Inno Setup 6 (free, well-established).

```
winget install -e --id JRSoftware.InnoSetup
```

After this, `ISCC.exe` (the compiler) is at
`C:\Program Files (x86)\Inno Setup 6\ISCC.exe`.

## 2. Build the installer

From the project root:

```
powershell -ExecutionPolicy Bypass -File .\setup\build-installer.ps1
```

The script:
1. Publishes a **self-contained** Release build — includes the .NET runtime,
   so players don't need anything else installed.
2. Compiles `setup\PokeLauncher.iss` with Inno Setup.
3. Produces **`setup\installer\PokeLauncher-<version>-setup.exe`**.
4. Writes **`setup\installer\update.json`** — the update manifest for step 3,
   already filled with the correct version and the installer's SHA-256.

Optional switches:
- `-Version "0.12.0"` — override the version (default: 0.11.1)
- `-SkipInstaller` — publish only

You can change the logo/icon before building: replace
`PokeLauncher\Assets\app-icon.png` / `.ico` (or use the `logo.png` override).

## 3. Give it to people

Upload `PokeLauncher-<version>-setup.exe` anywhere people can download it
(your website, itch.io, GitHub Releases…). That single .exe is the whole app —
run it, click through, done. Installing needs **no admin rights** (it installs
per-user into `%LocalAppData%\Programs\PokeLauncher`).

## 4. Shipping updates to people who already installed it

1. Build a new installer with a **higher version** (`-Version "0.12.0"`).
2. Upload the new `setup.exe` **and** the generated `update.json` to the same
   place (the URLs must be stable — a GitHub Releases page works well).
3. Point the app at your manifest: in
   `PokeLauncher\Services\UpdateService.cs` set
   `DefaultManifestUrl = "https://your-site.example/update.json"`,
   rebuild, and ship that build once. After that, every future release only
   needs steps 1–2.

What users experience: on launch the app checks your `update.json` (silently).
When a newer version is published they get a toast → **About → Check for
updates → Download update** → the installer is downloaded and **verified by
SHA-256** → they close the app and run it. Nothing installs without a click.

The manifest format (also generated for you by the build script):

```json
{
  "version": "0.12.0",
  "url": "https://your-site.example/PokeLauncher-0.12.0-setup.exe",
  "sha256": "<sha256 of that exe>",
  "notes": "What's new in this release."
}
```

For testing without hosting anything, set the environment variable
`POKELAUNCHER_UPDATE_MANIFEST` to a local file path (e.g.
`C:\test\update.json`) and point `url` at a local file path too.

## 5. Uninstalling removes everything

Running **uninstall** (Windows Settings → Apps, or Setup → uninstall) removes
the program files. It then asks:

> *"Do you also want to delete all PokeLauncher data — your library database,
> managed ROM copies, save backups, covers, downloaded emulators, and anything
> in the ROM inbox?"*

- **Yes** → `%LocalAppData%\PokeLauncher` is deleted completely: database,
  managed ROM copies, save backups, covers, emulators, inbox, logo override.
  The prompt warns that inbox files are originals you dropped in yourself —
  move anything you want to keep first.
- **No** → only the program is removed; the data folder stays on disk
  (re-installing later picks everything up again).

Importantly: files you imported are **copies** — the originals you imported
from (downloads, dumps, wherever) are never touched by installing *or*
uninstalling, in both cases.

---

## Checklist for a release

1. [ ] Update version: AboutViewModel `Version`, and build with `-Version`
2. [ ] Update the "What's new" list on the About page
3. [ ] `powershell -File .\setup\build-installer.ps1 -Version "X.Y.Z"`
4. [ ] Test the built installer on a clean machine/user account
5. [ ] Upload installer + `update.json`
6. [ ] (Optional, recommended for reaching more users) code-sign the installer —
       requires a code-signing certificate; unsigned installers trigger a
       SmartScreen warning on other people's machines
