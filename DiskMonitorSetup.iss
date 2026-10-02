#define MyAppName "DiskMonitor"
#define MyAppVersion "1.06"
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
OutputBaseFilename=DiskMonitor-Setup-v1.06

SetupIconFile=DiskMonitor.ico

Compression=lzma2
SolidCompression=yes
WizardStyle=modern

CloseApplications=force
CloseApplicationsFilter=*.exe

UninstallDisplayName=DiskMonitor
UninstallDisplayIcon={app}\{#MyAppExeName}

[Tasks]
Name: "desktopicon"; Description: "Create a Desktop shortcut"; GroupDescription: "Additional options:"
Name: "startup"; Description: "Start DiskMonitor with Windows"; GroupDescription: "Additional options:"; Flags: checkedonce
Name: "launchapp"; Description: "Launch DiskMonitor after installation"; GroupDescription: "Additional options:"; Flags: checkedonce

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

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  // Force close any running DiskMonitor instance before installing files
  Exec('taskkill.exe', '/f /im DiskMonitor.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ExePath: String;
  ResultCode: Integer;
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
      Exec('schtasks.exe', '/delete /tn "DiskMonitor" /f', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    end;
  end;
end;

procedure CurUninstallStepChanged(
  CurUninstallStep: TUninstallStep
);
var
  ResultCode: Integer;
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
    Exec('schtasks.exe', '/delete /tn "DiskMonitor" /f', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;
