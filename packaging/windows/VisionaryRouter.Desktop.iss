#ifndef SourceDir
#define SourceDir "..\..\artifacts\desktop"
#endif
#ifndef OutputDir
#define OutputDir "..\..\artifacts"
#endif
#ifndef AppVersion
#define AppVersion "1.1.0"
#endif
[Setup]
AppId={{48BD5D52-8B29-4735-8BB1-D7F1243AE63F}
AppName=Visionary Router
AppVersion={#AppVersion}
AppPublisher=Visionary Router contributors
DefaultDirName={localappdata}\Programs\Visionary Router
DefaultGroupName=Visionary Router
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=VisionaryRouter-Setup-win-x64-unsigned
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\VisionaryRouter.Desktop.exe
AppMutex=Local\VisionaryRouterDesktop
CloseApplications=yes
RestartApplications=no
UsePreviousAppDir=yes
SetupLogging=yes
[Tasks]
Name: "desktopicon"; Description: "Create a Desktop shortcut"; Flags: checkedonce
[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{group}\Visionary Router"; Filename: "{app}\VisionaryRouter.Desktop.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\Visionary Router"; Filename: "{app}\VisionaryRouter.Desktop.exe"; WorkingDir: "{app}"; Tasks: desktopicon
[Run]
Filename: "{app}\VisionaryRouter.Desktop.exe"; Description: "Launch Visionary Router"; Flags: nowait postinstall skipifsilent
; User data is outside the application folder and intentionally preserved on uninstall.
