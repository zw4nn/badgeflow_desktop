#define MyAppName "BadgeFlow"
#define MyAppVersion "1.4.0"
#define MyAppPublisher "BadgeFlow"
#define MyAppExeName "BadgeFlow.exe"

[Setup]
AppId={{D23D2E9A-1A2C-4C3A-9EB7-DAE2C5CFD711}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} Desktop {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\BadgeFlow
DefaultGroupName=BadgeFlow
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=installer-output
OutputBaseFilename=BadgeFlow-Setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=BadgeFlow Desktop Installer
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
SetupLogging=yes
SetupIconFile=Assets\BadgeFlow.ico

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "Créer un raccourci sur le Bureau"; GroupDescription: "Raccourcis :"; Flags: unchecked

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\BadgeFlow"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\BadgeFlow"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Lancer BadgeFlow"; Flags: nowait postinstall skipifsilent

[Code]
var
  TitleEgg: TNewStaticText;
  VersionEgg: TNewStaticText;
  TitleClicks: Integer;
  VersionClicks: Integer;

procedure TitleEggClick(Sender: TObject);
begin
  TitleClicks := TitleClicks + 1;
  if TitleClicks = 5 then
  begin
    MsgBox('Mode terrain déverrouillé.' + #13#10 + #13#10 +
      'Règle n°1 : si le FDI hésite, BadgeFlow garde son calme.' + #13#10 +
      'Règle n°2 : le café reste hors garantie.', mbInformation, MB_OK);
    TitleClicks := 0;
  end;
end;

procedure VersionEggClick(Sender: TObject);
begin
  VersionClicks := VersionClicks + 1;
  if VersionClicks = 3 then
  begin
    MsgBox('Easter egg #2 :' + #13#10 + #13#10 +
      '7020656E n''est toujours pas un badge.', mbInformation, MB_OK);
    VersionClicks := 0;
  end;
end;

procedure InitializeWizard;
begin
  TitleClicks := 0;
  VersionClicks := 0;

  TitleEgg := TNewStaticText.Create(WizardForm);
  TitleEgg.Parent := WizardForm.WelcomePage;
  TitleEgg.Caption := 'BadgeFlow';
  TitleEgg.Font.Style := [fsBold];
  TitleEgg.Font.Size := 18;
  TitleEgg.Cursor := crHand;
  TitleEgg.Left := ScaleX(180);
  TitleEgg.Top := ScaleY(34);
  TitleEgg.AutoSize := True;
  TitleEgg.OnClick := @TitleEggClick;

  VersionEgg := TNewStaticText.Create(WizardForm);
  VersionEgg.Parent := WizardForm.FinishedPage;
  VersionEgg.Caption := 'Desktop 1.4.0';
  VersionEgg.Cursor := crHand;
  VersionEgg.Font.Color := clGray;
  VersionEgg.Left := ScaleX(392);
  VersionEgg.Top := ScaleY(270);
  VersionEgg.AutoSize := True;
  VersionEgg.OnClick := @VersionEggClick;
end;
