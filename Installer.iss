; =====================================================================
;  Claude Council Manager - Inno Setup script
;  Produces a Windows installer (Setup .exe) for the published app.
;
;  Build the app first, then compile this script:
;     dotnet publish -c Release -p:PublishProfile=FolderProfile
;     "C:\Program Files (x86)\Inno Setup 5\ISCC.exe" Installer.iss
;  (Inno Setup 6 works too: "C:\Program Files (x86)\Inno Setup 6\ISCC.exe")
;
;  Output: dist\ClaudeCouncilManager-Setup-<version>.exe
;  Note: Inno Setup emits a setup .exe (not a .msi). Use WiX if you need .msi.
; =====================================================================

#define MyAppName        "Claude Council Manager"
#define MyAppPublisher   "Matteo Freddi"
#define MyAppExeName     "ClaudeCouncilManager.exe"

; Folder that contains the published files (relative to this .iss in the repo root).
#define PublishDir       "bin\Release\net10.0-windows\publish\win-x64"

; Read the version straight from the published executable (falls back to 1.0.0.0).
#ifexist PublishDir + "\" + MyAppExeName
  #define MyAppVersion   GetFileVersion(PublishDir + "\" + MyAppExeName)
#endif
#ifndef MyAppVersion
  #define MyAppVersion   "1.0.0.0"
#endif

[Setup]
AppId={{311DDA0C-E4F4-4CC3-BF00-B959ED2E1B12}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
; Per-user install (no admin prompt), compatible with Inno Setup 5.
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
PrivilegesRequired=none
Compression=lzma2
SolidCompression=yes
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
OutputDir=dist
OutputBaseFilename=ClaudeCouncilManager-Setup-{#MyAppVersion}
SetupIconFile=Assets\council.ico

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Ship everything produced by the publish step (exe + native SQLite + any future files).
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}";           Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{userdesktop}\{#MyAppName}";     Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; Offer to launch after install.
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
{ ---- Close a running instance so files aren't locked. Claude sessions it started keep running. ---- }
procedure KillRunning();
var
  RC: Integer;
begin
  Exec(ExpandConstant('{cmd}'), '/C taskkill /f /im ' + '{#MyAppExeName}' + ' >nul 2>&1',
       '', SW_HIDE, ewWaitUntilTerminated, RC);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
    KillRunning();
end;

{ ---- Uninstall cleanup: stop the app, optionally remove its data ---- }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
  LegacyDir: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    KillRunning();

    { The database holds the experts and councils configuration: never delete it silently,
      and default to "No" so /SUPPRESSMSGBOXES can't wipe it either. }
    DataDir := ExpandConstant('{localappdata}\ClaudeCouncilManager');
    LegacyDir := ExpandConstant('{localappdata}\ClaudeCouncil');
    if (not UninstallSilent()) and DirExists(DataDir) then
      if SuppressibleMsgBox('Also delete the app''s data (experts, councils, settings, generated protocols) in:' + #13#10 +
                            DataDir + ' ?' + #13#10#13#10 +
                            'The expert folders and Claude Code''s conversations are not touched.',
                            mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES then
      begin
        DelTree(DataDir, True, True, True);
        { Copy left behind by the rename from ClaudeCouncil. }
        if DirExists(LegacyDir) then
          DelTree(LegacyDir, True, True, True);
      end;
  end;
end;
