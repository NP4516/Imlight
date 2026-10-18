; Imlight server installer (Inno Setup 6).
; Build it with build-installer.ps1, which publishes the launcher into .\publish\launcher
; and the server into .\publish\server first. The release workflow does the same.

#define AppName "Imlight Server"
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId={{6E1F4C2B-7D0A-4C8E-9B57-2F3A9D1C4E80}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Revive101
AppPublisherURL=https://github.com/Revive101/Imlight
; Installed per user so the server can write its logs, cache and database without admin rights.
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Imlight
DefaultGroupName=Imlight
DisableProgramGroupPage=yes
OutputDir=output
OutputBaseFilename=ImlightSetup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
LicenseFile=..\LICENSE

[Files]
Source: "publish\launcher\ImlightLauncher.exe"; DestDir: "{app}"; Flags: ignoreversion
; The launcher keeps itself and the server up to date from GitHub; keep the owner's launcher settings.
Source: "publish\launcher\launcher.json"; DestDir: "{app}"; Flags: onlyifdoesntexist uninsneveruninstall
Source: "publish\server\*"; DestDir: "{app}\Server"; Excludes: "Config\Imlight.ini"; Flags: recursesubdirs ignoreversion
; Never replace a configuration the owner has already edited.
Source: "publish\server\Config\Imlight.ini"; DestDir: "{app}\Server\Config"; Flags: onlyifdoesntexist uninsneveruninstall
Source: "README.md"; DestDir: "{app}"; DestName: "Getting Started.md"; Flags: ignoreversion

[INI]
Filename: "{app}\Server\Config\Imlight.ini"; Section: "Database"; Key: "AdminAccountUsername"; String: "{code:GetAdminUser}"
Filename: "{app}\Server\Config\Imlight.ini"; Section: "Database"; Key: "AdminAccountPassword"; String: "{code:GetAdminPassword}"
Filename: "{app}\Server\Config\Imlight.ini"; Section: "Patch Server"; Key: "PatchServerInternalUrl"; String: "{code:GetPatchUrl}"
Filename: "{app}\Server\Config\Imlight.ini"; Section: "Randomizer"; Key: "Enabled"; String: "{code:GetRandomizerEnabled}"

[Dirs]
Name: "{app}\Server\logs"
Name: "{app}\Server\id-lookup"

[Icons]
Name: "{group}\Start Imlight Server"; Filename: "{app}\ImlightLauncher.exe"; WorkingDir: "{app}"
Name: "{group}\ID Lookup (items, spells, mobs)"; Filename: "{app}\Server\id-lookup\index.html"
Name: "{group}\Server Settings"; Filename: "notepad.exe"; Parameters: """{app}\Server\Config\Imlight.ini"""
Name: "{group}\Server Logs"; Filename: "{app}\Server\logs"
Name: "{group}\Getting Started"; Filename: "notepad.exe"; Parameters: """{app}\Getting Started.md"""
Name: "{group}\Uninstall Imlight Server"; Filename: "{uninstallexe}"
Name: "{userdesktop}\Start Imlight Server"; Filename: "{app}\ImlightLauncher.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"

[Run]
Filename: "{app}\ImlightLauncher.exe"; WorkingDir: "{app}"; Description: "Start the Imlight server now"; Flags: postinstall nowait skipifsilent unchecked

[Code]
var
  AccountPage: TInputQueryWizardPage;
  PatchPage: TInputQueryWizardPage;
  ModePage: TInputOptionWizardPage;

function ExistingSetting(const Section, Key, Default: String): String;
begin
  Result := GetIniString(Section, Key, Default, ExpandConstant('{app}\Server\Config\Imlight.ini'));
end;

procedure InitializeWizard;
begin
  AccountPage := CreateInputQueryPage(wpSelectDir,
    'Admin account', 'This account can use every in-game command.',
    'Pick a username and password for your admin account. Change the password from the default.');
  AccountPage.Add('Username:', False);
  AccountPage.Add('Password:', True);
  AccountPage.Values[0] := 'admin';

  PatchPage := CreateInputQueryPage(AccountPage.ID,
    'Patch server', 'Where the server downloads game data from.',
    'Imlight needs a patch server such as Aurorium. Leave this as it is if Aurorium runs on this computer with its default port.');
  PatchPage.Add('Patch server address:', False);
  PatchPage.Values[0] := 'http://127.0.0.1:12369';

  ModePage := CreateInputOptionPage(PatchPage.ID,
    'Server mode', 'How the game plays on this server.',
    'Randomizer mode shuffles schools, spells and drops while keeping them balanced. You can change this later in Server Settings.',
    True, False);
  ModePage.Add('Normal');
  ModePage.Add('Randomizer');
  ModePage.SelectedValueIndex := 0;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  // On an upgrade, start from what the owner already set.
  if CurPageID = AccountPage.ID then begin
    AccountPage.Values[0] := ExistingSetting('Database', 'AdminAccountUsername', AccountPage.Values[0]);
    AccountPage.Values[1] := ExistingSetting('Database', 'AdminAccountPassword', AccountPage.Values[1]);
  end
  else if CurPageID = PatchPage.ID then
    PatchPage.Values[0] := ExistingSetting('Patch Server', 'PatchServerInternalUrl', PatchPage.Values[0])
  else if CurPageID = ModePage.ID then begin
    if CompareText(ExistingSetting('Randomizer', 'Enabled', 'false'), 'true') = 0 then
      ModePage.SelectedValueIndex := 1;
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = AccountPage.ID) and ((Trim(AccountPage.Values[0]) = '') or (Trim(AccountPage.Values[1]) = '')) then begin
    MsgBox('Please enter both a username and a password.', mbError, MB_OK);
    Result := False;
  end;
end;

function GetAdminUser(Param: String): String;
begin
  Result := Trim(AccountPage.Values[0]);
end;

function GetAdminPassword(Param: String): String;
begin
  Result := AccountPage.Values[1];
end;

function GetPatchUrl(Param: String): String;
begin
  Result := Trim(PatchPage.Values[0]);
end;

function GetRandomizerEnabled(Param: String): String;
begin
  if ModePage.SelectedValueIndex = 1 then
    Result := 'true'
  else
    Result := 'false';
end;
