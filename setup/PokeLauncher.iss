; PokeLauncher installer - Inno Setup 6 script
; Build: install Inno Setup (winget install JRSoftware.InnoSetup), then either
;   .\setup\build-installer.ps1
; or open this file in the Inno Setup Compiler and press Compile.
; The [Files] entry expects a Release publish at:
;   PokeLauncher\bin\Release\net10.0\win-x64\publish\
; build-installer.ps1 passes the version as /DAppVersion.
;
; This build is FRAMEWORK-DEPENDENT: the app needs the free .NET 10 Desktop
; Runtime, and this installer checks for it, offers to download it from
; Microsoft's official site, and installs it before continuing.

#define AppName "PokeLauncher"
#ifndef AppVersion
#define AppVersion "0.12.0"
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
Source: "..\PokeLauncher\bin\Release\net10.0\win-x64\publish\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut (recommended)"; GroupDescription: "Additional icons:"; Flags: checkedonce

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[Code]
// ---------------------------------------------------------------------------
// .NET 10 Desktop Runtime detection (registry is the documented location for
// .NET 5+ runtimes; checked for per-machine and per-user installs).
// ---------------------------------------------------------------------------

function DesktopRuntime10Installed: Boolean;
var
  Keys: array of String;
  Names: TArrayOfString;
  I, J: Integer;
begin
  Result := False;
  SetArrayLength(Keys, 2);
  Keys[0] := 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App';
  Keys[1] := 'SOFTWARE\dotnet\Setup\InstalledVersions\ARM64\sharedfx\Microsoft.WindowsDesktop.App';

  for I := 0 to 1 do
  begin
    // per-machine
    if RegGetValueNames(HKLM64, Keys[I], Names) then
    begin
      for J := 0 to GetArrayLength(Names) - 1 do
      begin
        if Copy(Names[J], 1, 3) = '10.' then
        begin
          Result := True;
          Exit;
        end;
      end;
    end;
    // per-user
    if RegGetValueNames(HKCU64, Keys[I], Names) then
    begin
      for J := 0 to GetArrayLength(Names) - 1 do
      begin
        if Copy(Names[J], 1, 3) = '10.' then
        begin
          Result := True;
          Exit;
        end;
      end;
    end;
  end;
end;

function InstallDesktopRuntime: Boolean;
var
  RuntimeExe, PsParams: String;
  ResultCode: Integer;
begin
  Result := False;
  if MsgBox(
       'PokeLauncher needs the free .NET 10 Desktop Runtime from Microsoft.' #13#10 +
       'It is not installed on this computer.' #13#10 #13#10 +
       'Download and install it now? (about 60 MB, from the official Microsoft site)',
       mbConfirmation, MB_YESNO) <> IDYES then
    Exit;

  // Download the official runtime installer with Windows' built-in BITS service.
  RuntimeExe := ExpandConstant('{tmp}\windowsdesktop-runtime-10.0-win-x64.exe');
  PsParams := '-NoProfile -ExecutionPolicy Bypass -Command "Start-BitsTransfer ' +
              '-Source ''https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe'' ' +
              '-Destination ''" + RuntimeExe + "''"';
  Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), PsParams,
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  if (ResultCode <> 0) or not FileExists(RuntimeExe) then
  begin
    MsgBox('The runtime download failed. Check your internet connection and try again.',
           mbError, MB_OK);
    Exit;
  end;

  // Run Microsoft's installer interactively - the user confirms and UAC elevates it.
  if not ShellExec('open', RuntimeExe, '', '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
  begin
    MsgBox('The runtime installer could not be started.', mbError, MB_OK);
    Exit;
  end;

  if DesktopRuntime10Installed then
    Result := True
  else
    MsgBox('The .NET 10 Desktop Runtime still was not detected. ' +
           'If the runtime installer was cancelled, run this setup again and retry.',
           mbError, MB_OK);
end;

// ---------------------------------------------------------------------------
// Existing-install + running-app checks
// ---------------------------------------------------------------------------

function PreviousInstallVersion: String;
begin
  Result := GetPreviousData('AppVersion', '');
end;

procedure CloseRunningApp;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/f /im PokeLauncher.exe',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function InitializeSetup(): Boolean;
var
  PrevVersion: String;
begin
  Result := True;

  // --- existing install? (Inno detects the same AppId automatically)
  PrevVersion := PreviousInstallVersion;
  if PrevVersion <> '' then
  begin
    if PrevVersion = '{#AppVersion}' then
    begin
      if MsgBox(
           'PokeLauncher ' + PrevVersion + ' is already installed on this computer.' #13#10 +
           'Do you want to reinstall it? (Your library, saves, and covers are kept.)',
           mbConfirmation, MB_YESNO) <> IDYES then
      begin
        Result := False;
        Exit;
      end;
    end
    else
    begin
      MsgBox('An existing PokeLauncher install (version ' + PrevVersion + ') was found.' #13#10 +
             'This setup will upgrade it - your library, saves, and covers are preserved.',
             mbInformation, MB_OK);
    end;
  end;

  // --- app running? close it so files can be replaced
  if CheckForMutexes('PokeLauncherRunningMutex') then
    CloseRunningApp;
end;

// ---------------------------------------------------------------------------
// Uninstall: offer to remove ALL PokeLauncher data (database, managed ROM copies,
// save backups, covers, downloaded emulators, inbox, logo override).
// The user decides; No keeps the data so a later reinstall picks up where they left off.
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

  // Silent/unattended uninstalls NEVER delete data. A suppressed message box
  // answers with its default button (Yes), which once wiped a user's data
  // during an automated cleanup uninstall. Data deletion is interactive-only
  // and always opt-in.
  if WizardSilent then
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
