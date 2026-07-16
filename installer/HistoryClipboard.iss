#ifndef AppVersion
  #define AppVersion "0.1.0-preview.2"
#endif

#ifndef PublishDir
  #define PublishDir "..\artifacts\portable\HistoryClipboard-0.1.0-preview.2-unsigned-win-x64"
#endif

#ifndef InstallerOutputDir
  #define InstallerOutputDir "..\artifacts"
#endif

#ifndef InstallerBaseName
  #define InstallerBaseName "HistoryClipboard-0.1.0-preview.2-unsigned-win-x64-setup"
#endif

#define AppExecutable "HistoryClipboard.App.exe"
#define RunKeyPath "Software\Microsoft\Windows\CurrentVersion\Run"
#define RunValueName "HistoryClipboard"

[Setup]
AppId={{92B90231-45C7-4382-BD5A-9216D18FC0D1}
AppName=HistoryClipboard (Preview, Unsigned)
AppVerName=HistoryClipboard {#AppVersion} (Preview, Unsigned)
AppVersion={#AppVersion}
AppPublisher=HistoryClipboard
AppComments=Local-only unsigned preview build
DefaultDirName={localappdata}\Programs\HistoryClipboard
DefaultGroupName=HistoryClipboard
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#InstallerOutputDir}
OutputBaseFilename={#InstallerBaseName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
Uninstallable=yes
UninstallDisplayName=HistoryClipboard {#AppVersion} (Preview, Unsigned)
UninstallDisplayIcon={app}\{#AppExecutable}
CloseApplications=yes
CloseApplicationsFilter={#AppExecutable}
RestartApplications=no
AppMutex=Local\HistoryClipboard.SingleInstance
SignedUninstaller=no
VersionInfoCompany=HistoryClipboard
VersionInfoDescription=HistoryClipboard unsigned preview installer
VersionInfoProductName=HistoryClipboard (Preview, Unsigned)
VersionInfoVersion=0.1.0.2
VersionInfoTextVersion={#AppVersion}+unsigned
VersionInfoProductVersion=0.1.0.2
VersionInfoProductTextVersion={#AppVersion}+unsigned

; PrivilegesRequiredOverridesAllowed is intentionally omitted. Its empty default
; prevents /ALLUSERS from turning this package into an administrative install.

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; \
    Excludes: "*.pdb,*.user,*.suo,*.tmp,*.temp,*.db,*.log,settings.json,history.db,history.db-*,Images\*,TestResults\*,.git\*,bin\*,obj\*"; \
    Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\HistoryClipboard"; Filename: "{app}\{#AppExecutable}"; \
    WorkingDir: "{app}"; Comment: "HistoryClipboard unsigned preview"
Name: "{group}\Uninstall HistoryClipboard"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\{#AppExecutable}"; \
    Description: "Launch HistoryClipboard (unsigned preview)"; \
    Flags: nowait postinstall skipifsilent

[Code]
var
  DeleteUserDataAfterUninstall: Boolean;

function IsSilentUninstall: Boolean;
var
  Index: Integer;
begin
  Result := False;
  for Index := 1 to ParamCount do
  begin
    if SameText(ParamStr(Index), '/SILENT') or
       SameText(ParamStr(Index), '/VERYSILENT') then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

function InstalledRunCommand: String;
begin
  Result := '"' + ExpandConstant('{app}\{#AppExecutable}') + '"';
end;

procedure RemoveOwnedRunValue;
var
  CurrentCommand: String;
begin
  if RegQueryStringValue(
       HKEY_CURRENT_USER,
       '{#RunKeyPath}',
       '{#RunValueName}',
       CurrentCommand) and
     SameText(Trim(CurrentCommand), InstalledRunCommand) then
  begin
    RegDeleteValue(
      HKEY_CURRENT_USER,
      '{#RunKeyPath}',
      '{#RunValueName}');
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  UserDataDirectory: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    RemoveOwnedRunValue;
    DeleteUserDataAfterUninstall := False;
    UserDataDirectory := ExpandConstant('{localappdata}\HistoryClipboard');

    if (not IsSilentUninstall) and DirExists(UserDataDirectory) then
    begin
      DeleteUserDataAfterUninstall :=
        SuppressibleMsgBox(
          'Also delete local clipboard history, settings, and cached images?' + #13#10 + #13#10 +
          UserDataDirectory + #13#10 + #13#10 +
          'Choose No to keep this data for a later reinstall.',
          mbConfirmation,
          MB_YESNO,
          IDNO) = IDYES;
    end;
  end;

  if (CurUninstallStep = usPostUninstall) and
     DeleteUserDataAfterUninstall then
  begin
    UserDataDirectory := ExpandConstant('{localappdata}\HistoryClipboard');
    if not DelTree(UserDataDirectory, True, True, True) then
    begin
      SuppressibleMsgBox(
        'HistoryClipboard was removed, but some local data could not be deleted:' + #13#10 + #13#10 +
        UserDataDirectory,
        mbError,
        MB_OK,
        IDOK);
    end;
  end;
end;
