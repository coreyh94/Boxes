; Per-user installer for Boxes: no admin prompt, Start menu entry, starts with Windows,
; and an uninstaller in Settings › Apps. Build with build-release.ps1 (passes AppVersion).

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId={{6F1C9B52-3E7A-4C1D-9B8E-2A5F7D4C3B10}
AppName=Boxes
AppVersion={#AppVersion}
AppVerName=Boxes {#AppVersion}
AppPublisher=coreyh94
AppPublisherURL=https://github.com/coreyh94/Boxes
AppSupportURL=https://github.com/coreyh94/Boxes/issues
PrivilegesRequired=lowest
DefaultDirName={autopf}\Boxes
DisableDirPage=yes
DisableProgramGroupPage=yes
UninstallDisplayName=Boxes
UninstallDisplayIcon={app}\Boxes.exe
SetupIconFile=..\DesktopBoxes\Boxes.ico
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=..\release
OutputBaseFilename=Boxes-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
; Boxes is closed by [Code] below; Restart Manager can't close a tray app whose windows hide on close.
CloseApplications=no

[Files]
Source: "..\release\win-x64\DesktopBoxes.exe"; DestDir: "{app}"; DestName: "Boxes.exe"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Boxes"; Filename: "{app}\Boxes.exe"; Comment: "Organise your desktop into boxes"

[Registry]
; Same entry the app's "Start Boxes with Windows" setting manages; removed on uninstall.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "DesktopBoxes"; ValueData: """{app}\Boxes.exe"""; Flags: uninsdeletevalue

[Run]
Filename: "{app}\Boxes.exe"; Description: "Start Boxes now"; Flags: postinstall nowait skipifsilent

[Code]
procedure StopBoxes();
var Code: Integer;
begin
  // Layouts are saved as they change, so ending the process loses nothing.
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM Boxes.exe', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM DesktopBoxes.exe', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Sleep(500);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopBoxes();
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  StopBoxes();
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var Data: String;
begin
  if CurUninstallStep <> usPostUninstall then Exit;
  RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run', 'DesktopBoxes');
  // Saved items may be the only copy of files whose desktop originals were deleted, so keep them unless asked.
  Data := ExpandConstant('{localappdata}\DesktopBoxes');
  if DirExists(Data) and not UninstallSilent() then
    if MsgBox('Also delete your boxes and the items saved in them?' #13#10#13#10 +
              'Choose No to keep them (in ' + Data + ') in case you reinstall Boxes.',
              mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DelTree(Data, True, True, True);
end;
