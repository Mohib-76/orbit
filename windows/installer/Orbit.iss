; Inno Setup 6 script for Orbit for Windows.
; Built by build.ps1 -Installer (or CI): ISCC /DAppVersion=1.0.0 /DSourceDir=<publish dir> Orbit.iss
; Per-user install (no admin rights needed), self-contained .NET build.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\..\dist\windows"
#endif

[Setup]
AppId={{6C1D5F0E-0B7A-4C8E-9E57-0F7B2C5A9D31}
AppName=Orbit
AppVersion={#AppVersion}
AppPublisher=Orbit contributors
AppPublisherURL=https://github.com/Mohib-76/orbit
AppSupportURL=https://github.com/Mohib-76/orbit/issues
DefaultDirName={localappdata}\Programs\Orbit
DefaultGroupName=Orbit
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputBaseFilename=OrbitSetup-{#AppVersion}
SetupIconFile=..\Orbit.Windows\Assets\orbit.ico
UninstallDisplayIcon={app}\Orbit.exe
LicenseFile=..\..\LICENSE
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes

[Tasks]
Name: "startup"; Description: "Start Orbit when I sign in to Windows"; GroupDescription: "Options:"
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Options:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Orbit"; Filename: "{app}\Orbit.exe"
Name: "{autodesktop}\Orbit"; Filename: "{app}\Orbit.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Orbit"; \
    ValueData: """{app}\Orbit.exe"" --tray"; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\Orbit.exe"; Description: "Open Orbit"; Flags: nowait postinstall skipifsilent
Filename: "{app}\Orbit.exe"; Parameters: "--tray"; Flags: nowait; Check: RelaunchAfterSilentUpdate

[UninstallRun]
Filename: "{cmd}"; Parameters: "/c taskkill /im Orbit.exe /f"; Flags: runhidden; RunOnceId: "KillOrbit"
Filename: "{app}\adb\adb.exe"; Parameters: "kill-server"; Flags: runhidden skipifdoesntexist; RunOnceId: "KillAdb"

[Code]
var
  OrbitWasRunning: Boolean;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Dir, Ps: String;
  Code: Integer;
begin
  { Updating: Orbit's adb server keeps adb\*.dll locked and Restart Manager can't close it, so the update
    would abort. Stop Orbit (it would restart the server) and adb, but only the copies in this install folder.
    The exit code says whether Orbit was running, so a silent update can start it again. }
  Dir := ExpandConstant('{app}');
  if FileExists(Dir + '\adb\adb.exe') then
  begin
    StringChangeEx(Dir, '''', '''''', True);
    Ps := '$d=''' + Dir + '\''; $p=@(Get-Process Orbit,adb -ErrorAction SilentlyContinue | ' +
          'Where-Object { $_.Path -and $_.Path.StartsWith($d, ''OrdinalIgnoreCase'') }); ' +
          '$o=@($p | Where-Object Name -eq ''Orbit'').Count; $p | Stop-Process -Force; exit $o';
    if Exec('powershell.exe', '-NoProfile -NonInteractive -Command "' + Ps + '"', '', SW_HIDE, ewWaitUntilTerminated, Code) then
      OrbitWasRunning := Code > 0;
  end;
  Result := '';
end;

function RelaunchAfterSilentUpdate: Boolean;
begin
  Result := WizardSilent and OrbitWasRunning;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    { The app writes its own Run entry when "Start with Windows" is on. }
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Orbit');
    if (not UninstallSilent) and
       (MsgBox('Also delete Orbit''s settings, pairing and logs?' + #13#10 +
               '(Files you received are kept.)', mbConfirmation, MB_YESNO) = IDYES) then
      DelTree(ExpandConstant('{userappdata}\Orbit'), True, True, True);
  end;
end;
