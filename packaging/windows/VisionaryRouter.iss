#define AppName "Visionary Router"
#define ServiceName "VisionaryRouter"
#define FirewallRuleName "Visionary Router Web UI"

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#ifndef SourceDir
  #define SourceDir "..\..\artifacts\publish\VisionaryRouter-win-x64"
#endif

#ifndef OutputDir
  #define OutputDir "..\..\artifacts"
#endif

[Setup]
AppId={{E1E5EBAD-8620-46C8-8A91-780478284482}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Visionary Router
DefaultDirName={autopf}\Visionary Router
DefaultGroupName=Visionary Router
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=VisionaryRouter-Setup-win-x64
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\VisionaryRouter.exe
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
UsePreviousAppDir=yes
UsePreviousTasks=yes

[Tasks]
Name: "lanaccess"; Description: "Allow phones and tablets on this private local network"; GroupDescription: "Network access:"; Flags: unchecked
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[INI]
Filename: "{group}\Open Visionary Router.url"; Section: "InternetShortcut"; Key: "URL"; String: "{code:GetLocalUrl}"
Filename: "{autodesktop}\Visionary Router.url"; Section: "InternetShortcut"; Key: "URL"; String: "{code:GetLocalUrl}"; Tasks: desktopicon

[Icons]
Name: "{group}\Uninstall Visionary Router"; Filename: "{uninstallexe}"

[Run]
; binPath must reach sc.exe as one argument holding a quoted path ("C:\Program Files\...").
; Inno collapses """" to nothing, so the inner quotes are backslash-escaped for the C runtime.
Filename: "{sys}\sc.exe"; Parameters: "config {#ServiceName} binPath= ""\""{app}\VisionaryRouter.exe\"""" start= auto DisplayName= ""{#AppName}"""; Flags: runhidden waituntilterminated; Check: ServiceExists
Filename: "{sys}\sc.exe"; Parameters: "create {#ServiceName} binPath= ""\""{app}\VisionaryRouter.exe\"""" start= auto DisplayName= ""{#AppName}"""; Flags: runhidden waituntilterminated; Check: not ServiceExists
Filename: "{sys}\sc.exe"; Parameters: "description {#ServiceName} ""Controls Visionary Solutions AV-over-IP routing and hosts its local Web UI."""; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "failure {#ServiceName} reset= 86400 actions= restart/5000/restart/15000/none/0"; Flags: runhidden waituntilterminated
; The service is started from CurStepChanged(ssPostInstall), not here: [Run] entries execute
; before ssPostInstall, which would start it ahead of its settings and firewall rule.
Filename: "{code:GetLocalUrl}"; Description: "Open Visionary Router"; Flags: shellexec postinstall nowait skipifsilent

[UninstallRun]
Filename: "{sys}\net.exe"; Parameters: "stop {#ServiceName} /y"; Flags: runhidden waituntilterminated; RunOnceId: "StopService"
Filename: "{sys}\sc.exe"; Parameters: "delete {#ServiceName}"; Flags: runhidden waituntilterminated; RunOnceId: "DeleteService"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""{#FirewallRuleName}"""; Flags: runhidden waituntilterminated; RunOnceId: "DeleteFirewallRule"

[Code]
var
  WebPortPage: TInputQueryWizardPage;
  PasswordPage: TInputQueryWizardPage;
  RemoveDataOnUninstall: Boolean;

function DataDirectory(Param: String): String;
begin
  Result := ExpandConstant('{commonappdata}\VisionaryRouter');
end;

function PasswordFilePath: String;
begin
  Result := DataDirectory('') + '\admin-password.txt';
end;

function ProductionSettingsPath: String;
begin
  Result := ExpandConstant('{app}\appsettings.Production.json');
end;

function GetWebPort(Param: String): String;
begin
  Result := Trim(WebPortPage.Values[0]);
  if Result = '' then
    Result := '8088';
end;

function GetLocalUrl(Param: String): String;
begin
  Result := 'http://localhost:' + GetWebPort('');
end;

function ServiceExists: Boolean;
begin
  Result := RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\{#ServiceName}');
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = PasswordPage.ID) and not WizardIsTaskSelected('lanaccess');
end;

procedure InitializeWizard;
begin
  WebPortPage := CreateInputQueryPage(
    wpSelectTasks,
    'Web UI port',
    'Choose the address used by the browser',
    'Port 8088 is recommended unless another application already uses it.');
  WebPortPage.Add('TCP port:', False);
  WebPortPage.Values[0] := '8088';

  PasswordPage := CreateInputQueryPage(
    WebPortPage.ID,
    'Local network password',
    'Protect access from phones, tablets and other computers',
    'Sign in with username "admin". Use at least 8 characters. On an upgrade, leave both fields blank to keep the existing password.');
  PasswordPage.Add('Administrator password:', True);
  PasswordPage.Add('Confirm password:', True);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Port: Integer;
  PasswordExists: Boolean;
begin
  Result := True;

  if CurPageID = WebPortPage.ID then
  begin
    Port := StrToIntDef(Trim(WebPortPage.Values[0]), -1);
    if (Port < 1024) or (Port > 65535) then
    begin
      MsgBox('Enter a TCP port from 1024 through 65535.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
  end;

  if (CurPageID = PasswordPage.ID) and WizardIsTaskSelected('lanaccess') then
  begin
    PasswordExists := FileExists(PasswordFilePath);

    if (PasswordPage.Values[0] = '') and (PasswordPage.Values[1] = '') and PasswordExists then
      Exit;

    if Length(PasswordPage.Values[0]) < 8 then
    begin
      MsgBox('The administrator password must be at least 8 characters.', mbError, MB_OK);
      Result := False;
      Exit;
    end;

    if PasswordPage.Values[0] <> PasswordPage.Values[1] then
    begin
      MsgBox('The passwords do not match.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';
  if ServiceExists then
    Exec(ExpandConstant('{sys}\net.exe'), 'stop {#ServiceName} /y', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure WriteRuntimeSettings;
var
  Settings: String;
  PasswordPathJson: String;
begin
  ForceDirectories(DataDirectory(''));
  PasswordPathJson := PasswordFilePath;
  StringChangeEx(PasswordPathJson, '\', '\\', True);

  Settings := '{' + #13#10 +
    '  "Router": {' + #13#10 +
    '    "WebPort": ' + GetWebPort('') + ',' + #13#10 +
    '    "OpenBrowserOnStart": false,' + #13#10 +
    '    "InitialListenOnAllInterfaces": ';

  if WizardIsTaskSelected('lanaccess') then
    Settings := Settings +
      'true,' + #13#10 +
      '    "AdminPasswordFile": "' + PasswordPathJson + '"' + #13#10
  else
    Settings := Settings + 'false' + #13#10;

  Settings := Settings + '  }' + #13#10 + '}' + #13#10;

  SaveStringToFile(ProductionSettingsPath, Settings, False);
end;

procedure ConfigurePasswordFile;
var
  ResultCode: Integer;
  Params: String;
begin
  if not WizardIsTaskSelected('lanaccess') then
    Exit;

  if PasswordPage.Values[0] <> '' then
    SaveStringToFile(PasswordFilePath, PasswordPage.Values[0], False);

  if FileExists(PasswordFilePath) then
  begin
    Params := '"' + PasswordFilePath + '" /inheritance:r /grant:r *S-1-5-18:F *S-1-5-32-544:F';
    Exec(ExpandConstant('{sys}\icacls.exe'), Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;

procedure ConfigureFirewall;
var
  ResultCode: Integer;
  Params: String;
begin
  Exec(
    ExpandConstant('{sys}\netsh.exe'),
    'advfirewall firewall delete rule name="{#FirewallRuleName}"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  if not WizardIsTaskSelected('lanaccess') then
    Exit;

  Params := 'advfirewall firewall add rule name="{#FirewallRuleName}" ' +
    'dir=in action=allow program="' + ExpandConstant('{app}\VisionaryRouter.exe') + '" ' +
    'protocol=TCP profile=private remoteip=localsubnet enable=yes';
  // Scoped to the program rather than a port: the Web UI port can later be changed from
  // Diagnostics, or moved automatically when the chosen one is busy.
  Exec(ExpandConstant('{sys}\netsh.exe'), Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure StartRouterService;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\sc.exe'), 'start {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    WriteRuntimeSettings;
    ConfigurePasswordFile;
    ConfigureFirewall;
    StartRouterService;
  end;
end;

function InitializeUninstall: Boolean;
begin
  RemoveDataOnUninstall :=
    MsgBox(
      'Remove saved devices, names, groups, presets and network settings too?' + #13#10 + #13#10 +
      'Choose No to keep them for a future reinstall.',
      mbConfirmation, MB_YESNO) = IDYES;
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and RemoveDataOnUninstall then
    DelTree(DataDirectory(''), True, True, True);
end;
