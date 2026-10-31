; PokeLauncher installer - Inno Setup 6 script
; Build: install Inno Setup (winget install JRSoftware.InnoSetup), then either
;   .\setup\build-installer.ps1
; or open this file in the Inno Setup Compiler and press Compile.
; The [Files] entry expects a Release publish at:
;   PokeLauncher\bin\Release\net10.0\win-x64\publish\
; build-installer.ps1 passes the version as /DAppVersion.

#define AppName "PokeLauncher"
#ifndef AppVersion
#define AppVersion "0.11.1"
#endif
#define AppPublisher "PokeLauncher"
#define AppExe "PokeLauncher.exe"

[Setup]
AppId={{8E5A2C4E-1B7D-4F3A-9C6D-5A2B1E4C7D90}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=installer
OutputBaseFilename=PokeLauncher-{#AppVersion}-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UninstallDisplayIcon={app}\{#AppExe}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "..\PokeLauncher\bin\Release\net10.0\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional icons:"

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[Code]
// ---------------------------------------------------------------------------
// Uninstall: offer to remove ALL PokeLauncher data (database, managed ROM copies,
// save backups, covers, downloaded emulators, inbox, logo override).
// The app's data folder is %LocalAppData%\PokeLauncher. The user decides; No keeps
// the data so a later reinstall picks up where they left off.
// ---------------------------------------------------------------------------

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: string;
begin
  if CurUninstallStep <> usUninstall then
    Exit;

  DataDir := ExpandConstant('{localappdata}\PokeLauncher');
  if not DirExists(DataDir) then
    Exit;

  if MsgBox(
       'Do you also want to delete all PokeLauncher data?' #13#10 #13#10 +
       'This removes your library database, managed ROM copies, save backups, ' +
       'covers, downloaded emulators, and anything in the ROM inbox.' #13#10 #13#10 +
       'Files you imported are copies - the originals you imported from are never touched. ' +
       'If you want to keep anything from the inbox, move it out first.',
       mbConfirmation, MB_YESNO) = IDYES then
  begin
    try
      DelTree(DataDir, True, True, True);
    except
      // best effort: locked files (e.g. an emulator still running) are reported below
    end;

    if DirExists(DataDir) then
      MsgBox('Some files in ' + DataDir + ' could not be deleted because they were still in use.' #13#10 +
             'Close any running emulators and delete the folder manually.',
             mbError, MB_OK);
  end;
end;
