#ifndef OutputDir
#define OutputDir "..\artifacts\installer"
#endif

#ifndef MyAppVersion
#define MyAppVersion "1.0.0"
#endif

#ifndef IconPath
#define IconPath "..\src\KetoanMini\assets\logo_cuong_phat.ico"
#endif

#define KetoanMiniUninstallKey "Software\Microsoft\Windows\CurrentVersion\Uninstall\{7F0F1B6E-3A84-4A41-B387-A64C988733F7}_is1"

[Setup]
AppId={{D29BC189-80D5-4C1E-BB39-1E49CBAFB0F5}
AppName=KetoanMini Uninstaller
AppVersion={#MyAppVersion}
AppPublisher=Cong ty TNHH Inox Cuong Phat
CreateAppDir=no
Uninstallable=no
SetupIconFile={#IconPath}
OutputDir={#OutputDir}
OutputBaseFilename=KetoanMiniUninstall-{#MyAppVersion}-win-x64
VersionInfoVersion={#MyAppVersion}
VersionInfoProductName=KetoanMini Uninstaller
VersionInfoProductVersion={#MyAppVersion}
VersionInfoDescription=KetoanMini uninstall launcher
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
SetupMutex=KetoanMiniUninstallerMutex
DisableWelcomePage=yes
DisableFinishedPage=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Code]
function TryGetUninstallCommand(var Command: String): Boolean;
begin
  Result :=
    RegQueryStringValue(HKLM, '{#KetoanMiniUninstallKey}', 'UninstallString', Command) or
    RegQueryStringValue(HKCU, '{#KetoanMiniUninstallKey}', 'UninstallString', Command);
end;

procedure SplitCommandLine(Command: String; var FileName: String; var Params: String);
var
  I: Integer;
begin
  Command := Trim(Command);
  FileName := Command;
  Params := '';

  if (Length(Command) > 0) and (Copy(Command, 1, 1) = '"') then
  begin
    I := 2;
    while (I <= Length(Command)) and (Copy(Command, I, 1) <> '"') do
    begin
      I := I + 1;
    end;

    if I <= Length(Command) then
    begin
      FileName := Copy(Command, 2, I - 2);
      Params := Trim(Copy(Command, I + 1, Length(Command)));
    end;
  end;
end;

function InitializeSetup(): Boolean;
var
  Command: String;
  FileName: String;
  Params: String;
  ResultCode: Integer;
begin
  Result := False;

  if not TryGetUninstallCommand(Command) then
  begin
    if not WizardSilent then
    begin
      MsgBox('Khong tim thay KetoanMini tren may nay.', mbInformation, MB_OK);
    end;
    exit;
  end;

  if not WizardSilent then
  begin
    if MsgBox('Ban co muon go KetoanMini khoi may nay khong?', mbConfirmation, MB_YESNO) <> IDYES then
    begin
      exit;
    end;
  end;

  SplitCommandLine(Command, FileName, Params);
  if WizardSilent then
  begin
    Params := Trim(Params + ' /VERYSILENT /SUPPRESSMSGBOXES /NORESTART');
  end;

  if not Exec(FileName, Params, '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
  begin
    if not WizardSilent then
    begin
      MsgBox('Khong chay duoc bo go KetoanMini: ' + FileName, mbError, MB_OK);
    end;
    exit;
  end;

  if (ResultCode <> 0) and not WizardSilent then
  begin
    MsgBox('Bo go KetoanMini ket thuc voi ma loi: ' + IntToStr(ResultCode), mbError, MB_OK);
  end;
end;
