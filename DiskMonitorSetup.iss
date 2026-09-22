#define MyAppName "DiskMonitor"
#define MyAppVersion "1.05"
#define MyAppPublisher "DgLogiQ"
#define MyAppExeName "DiskMonitor.exe"

[Setup]
AppId={{4DDA3865-5FD4-47F2-AB90-4803BCE34DB1}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} v{#MyAppVersion}
AppPublisher={#MyAppPublisher}

DefaultDirName={localappdata}\DgLogiQ\DiskMonitor
DefaultGroupName=DiskMonitor

PrivilegesRequired=lowest
DisableProgramGroupPage=yes

OutputDir=Installer
OutputBaseFilename=DiskMonitor-Setup-v1.05

SetupIconFile=DiskMonitor.ico

Compression=lzma2
SolidCompression=yes
WizardStyle=modern

UninstallDisplayName=DiskMonitor
UninstallDisplayIcon={app}\{#MyAppExeName}

[Tasks]
Name: "desktopicon"; Description: "Create a Desktop shortcut"; GroupDescription: "Additional options:"
Name: "startup"; Description: "Start DiskMonitor with Windows"; GroupDescription: "Additional options:"
Name: "launchapp"; Description: "Launch DiskMonitor after installation"; GroupDescription: "Additional options:"

[Files]
Source: "bin\Release\net48\DiskMonitor.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\DiskMonitor"; Filename: "{app}\DiskMonitor.exe"
Name: "{autodesktop}\DiskMonitor"; Filename: "{app}\DiskMonitor.exe"; Tasks: desktopicon
Name: "{userstartup}\DiskMonitor"; Filename: "{app}\DiskMonitor.exe"; WorkingDir: "{app}"; Tasks: startup

[Run]
Filename: "{app}\DiskMonitor.exe"; Tasks: launchapp; Flags: nowait skipifsilent

[Code]
const
  StartupKey =
    'Software\Microsoft\Windows\CurrentVersion\Run';

  StartupValue =
    'DiskMonitor';

  StartupApprovedPath =
    'Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run';

  StartupApprovedFolder =
    'Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder';

procedure CurStepChanged(CurStep: TSetupStep);
var
  ExePath: String;
begin
  if CurStep = ssPostInstall then
  begin
    ExePath :=
      ExpandConstant(
        '{app}\DiskMonitor.exe'
      );

    if WizardIsTaskSelected('startup') then
    begin
      RegWriteStringValue(
        HKCU,
        StartupKey,
        StartupValue,
        '"' + ExePath + '"'
      );
    end
    else
    begin
      RegDeleteValue(
        HKCU,
        StartupKey,
        StartupValue
      );
      RegDeleteValue(
        HKCU,
        StartupApprovedPath,
        StartupValue
      );
      RegDeleteValue(
        HKCU,
        StartupApprovedFolder,
        'DiskMonitor.lnk'
      );
    end;
  end;
end;

procedure CurUninstallStepChanged(
  CurUninstallStep: TUninstallStep
);
begin
  if CurUninstallStep = usUninstall then
  begin
    RegDeleteValue(
      HKCU,
      StartupKey,
      StartupValue
    );
    RegDeleteValue(
      HKCU,
      StartupApprovedPath,
      StartupValue
    );
    RegDeleteValue(
      HKCU,
      StartupApprovedFolder,
      'DiskMonitor.lnk'
    );
  end;
end;
