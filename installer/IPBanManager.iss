; Instalador do IPBan Manager (Inno Setup 6)
; Compilar via build-installer.ps1 (recomendado) ou: ISCC IPBanManager.iss /DAppVersion=1.0.0

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppName      "IPBan Manager"
#define AppExe       "IPBanManager.exe"
#define AppPublisher "Skytins"
#define StagingDir   "..\artifacts\app"

[Setup]
; AppId fixo: garante que novas versões atualizem a instalação existente.
AppId={{6F1D3C2A-8B54-4E0B-9A77-3D2C5B1E9F40}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoDescription=Instalador do {#AppName}
VersionInfoCompany={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=IPBanManager-Setup-{#AppVersion}
SetupIconFile=..\assets\installer.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardImageFile=..\assets\wizard-large.bmp
WizardSmallImageFile=..\assets\wizard-small.bmp
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
; O aplicativo exige administrador (serviço, firewall e Program Files).
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
; Impede instalar/atualizar com o aplicativo aberto (mutex criado pelo App).
AppMutex=Global\IPBanManager.SingleInstance
CloseApplications=yes
RestartApplications=no
ShowLanguageDialog=no

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#StagingDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Desinstalar {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent runascurrentuser

[UninstallDelete]
; Logs e preferências do aplicativo (não toca no IPBan nem no ipban.config).
Type: filesandordirs; Name: "{commonappdata}\IPBanManager"

[Code]
const
  // .NET Framework 4.8 = Release 528040 ou superior (Windows Server 2022 já inclui 4.8).
  MinNetRelease = 528040;

function NetFrameworkOk: Boolean;
var
  Release: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release)
            and (Release >= MinNetRelease);
end;

function IPBanServiceInstalled: Boolean;
begin
  Result := RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\IPBan');
end;

function InitializeSetup: Boolean;
begin
  Result := True;
  if not NetFrameworkOk then
  begin
    MsgBox('O {#AppName} requer o .NET Framework 4.8 ou superior.' + #13#10 +
           'Instale-o (já incluso no Windows Server 2022 atualizado) e execute este instalador novamente.',
           mbCriticalError, MB_OK);
    Result := False;
  end;
end;

procedure CurrentStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and (not IPBanServiceInstalled) then
    MsgBox('O serviço IPBan não foi encontrado neste servidor.' + #13#10#13#10 +
           'O {#AppName} administra um IPBan já instalado. Instale o IPBan (https://github.com/DigitalRuby/IPBan) ' +
           'e depois abra o {#AppName}; se o IPBan estiver em outra pasta, use "Alterar pasta..." no topo da janela.',
           mbInformation, MB_OK);
end;
