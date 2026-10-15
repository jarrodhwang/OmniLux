#ifndef ProductVersion
  #define ProductVersion "0.2.0"
#endif
#ifndef PayloadDir
  #error Pass /DPayloadDir to the compiler.
#endif
#ifndef OutputDirectory
  #define OutputDirectory "..\dist"
#endif

[Setup]
AppId={{CD3AAB20-7D7C-4F10-BEA8-A5A317833C76}
AppName=OmniLux
AppVersion={#ProductVersion}
AppPublisher=Jarrod Hwang
AppPublisherURL=https://github.com/jarrodhwang/OmniLux
AppSupportURL=https://github.com/jarrodhwang/OmniLux/issues
DefaultDirName={localappdata}\Programs\OmniLux
DefaultGroupName=OmniLux
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
UninstallDisplayIcon={app}\OmniLux.exe
LicenseFile=..\LICENSE
SetupIconFile=..\src\OmniLux\Assets\OmniLux.ico
WizardStyle=modern
DisableProgramGroupPage=yes
DisableWelcomePage=no
OutputDir={#OutputDirectory}
OutputBaseFilename=OmniLux-{#ProductVersion}-Setup
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
CloseApplicationsFilter=OmniLux.exe
RestartApplications=no
UsePreviousAppDir=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked
Name: "startup"; Description: "Start OmniLux when I sign in to Windows"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\OmniLux"; Filename: "{app}\OmniLux.exe"
Name: "{autodesktop}\OmniLux"; Filename: "{app}\OmniLux.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "OmniLux"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "OmniLux"; ValueData: """{app}\OmniLux.exe"" --tray"; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\OmniLux.exe"; Description: "Launch OmniLux"; Flags: nowait postinstall skipifsilent

[Code]
var StartupPreferenceLoaded: Boolean;

function HasTaskOverride(): Boolean;
var Index: Integer; Argument: String;
begin
  Result := False;
  for Index := 1 to ParamCount do begin
    Argument := Uppercase(ParamStr(Index));
    if (Pos('/TASKS=', Argument) = 1) or (Pos('/MERGETASKS=', Argument) = 1) then Result := True;
  end;
end;

procedure LoadStartupPreference();
var ExistingCommand: String;
begin
  if StartupPreferenceLoaded or HasTaskOverride() then Exit;
  StartupPreferenceLoaded := True;
  if RegKeyExists(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{CD3AAB20-7D7C-4F10-BEA8-A5A317833C76}_is1') then begin
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'OmniLux', ExistingCommand) and (ExistingCommand <> '') then begin
      WizardSelectTasks('startup');
      Log('OmniLux: preserving enabled startup');
    end else begin
      WizardSelectTasks('!startup');
      Log('OmniLux: preserving disabled startup');
    end;
  end;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpSelectTasks then LoadStartupPreference();
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and not WizardIsTaskSelected('startup') then
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'OmniLux');
end;

procedure StopOmniLux();
var ExitCode: Integer;
begin
  if FileExists(ExpandConstant('{app}\OmniLux.exe')) then begin
    Exec(ExpandConstant('{app}\OmniLux.exe'), '--exit', '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
    Sleep(1000);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  LoadStartupPreference();
  Log('OmniLux selected tasks: ' + WizardSelectedTasks(False));
  StopOmniLux();
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  StopOmniLux();
  Result := True;
end;
