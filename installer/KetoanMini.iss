#ifndef SourceDir
#define SourceDir "..\publish\win-x64"
#endif

#ifndef OutputDir
#define OutputDir "..\artifacts\installer"
#endif

#ifndef MyAppVersion
#define MyAppVersion "1.0.0"
#endif

#ifndef IconPath
#define IconPath "..\src\KetoanMini\assets\logo_cuong_phat.ico"
#endif

[Setup]
AppId={{7F0F1B6E-3A84-4A41-B387-A64C988733F7}
AppName=KetoanMini
AppVersion={#MyAppVersion}
AppVerName=KetoanMini {#MyAppVersion}
AppPublisher=Cong ty TNHH Inox Cuong Phat
DefaultDirName={autopf}\KetoanMini
DefaultGroupName=KetoanMini
UninstallDisplayIcon={app}\KetoanMini.exe
SetupIconFile={#IconPath}
OutputDir={#OutputDir}
OutputBaseFilename=KetoanMiniSetup-{#MyAppVersion}-win-x64
VersionInfoVersion={#MyAppVersion}
VersionInfoProductName=KetoanMini
VersionInfoProductVersion={#MyAppVersion}
VersionInfoDescription=KetoanMini installer and updater
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
UsePreviousAppDir=yes
DirExistsWarning=no
CloseApplications=yes
RestartApplications=no
SetupMutex=KetoanMiniInstallerMutex
DisableProgramGroupPage=yes
DisableWelcomePage=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\KetoanMini"; Filename: "{app}\KetoanMini.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\KetoanMini"; Filename: "{app}\KetoanMini.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\KetoanMini.exe"; Description: "{cm:LaunchProgram,KetoanMini}"; Flags: nowait postinstall skipifsilent
Filename: "{app}\KetoanMini.exe"; Flags: nowait skipifnotsilent
