; Instalador do IPBan Manager (Inno Setup 6)
; Compilar via build-installer.ps1 (recomendado) ou: ISCC IPBanManager.iss /DAppVersion=1.0.0

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
; Versão numérica do arquivo (sem o sufixo -beta.N, que o Windows não aceita em VersionInfo).
#ifndef NumericVersion
  #define NumericVersion AppVersion
#endif
#ifndef StagingDir
  #define StagingDir "..\artifacts\app"
#endif
#define AppName      "IPBan Manager"
#define AppExe       "IPBanManager.exe"
#define AppPublisher "Skytins"

[Setup]
; AppId fixo: garante que novas versões atualizem a instalação existente.
AppId={{6F1D3C2A-8B54-4E0B-9A77-3D2C5B1E9F40}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
VersionInfoVersion={#NumericVersion}
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
Name: "installipban"; Description: "Instalar/atualizar e configurar o serviço IPBan 4.1.0 (recomendado)"; GroupDescription: "Proteção:"
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

// Instala/atualiza o IPBan embutido usando o próprio aplicativo (mesma rotina do assistente da interface).
// Implantação silenciosa: IPBanManager-Setup.exe /VERYSILENT /WHITELIST=203.0.113.10,198.51.100.0/24
// Sem /WHITELIST, entram na whitelist os IPs das sessões de Área de Trabalho Remota ativas.
procedure InstallIPBan;
var
  Params, Whitelist: String;
  ResultCode: Integer;
begin
  WizardForm.StatusLabel.Caption := 'Instalando e configurando o serviço IPBan...';
  Params := '--install-ipban';
  Whitelist := ExpandConstant('{param:WHITELIST|}');
  if Whitelist <> '' then
    Params := Params + ' "--whitelist=' + Whitelist + '"';
  if not Exec(ExpandConstant('{app}\{#AppExe}'), Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    ResultCode := -1;
  if ResultCode <> 0 then
    SuppressibleMsgBox('Não foi possível instalar o serviço IPBan (código ' + IntToStr(ResultCode) + ').' + #13#10#13#10 +
           'Abra o {#AppName} e use o botão "Instalar IPBan" para ver os detalhes. ' +
           'Log: ' + ExpandConstant('{commonappdata}') + '\IPBanManager\logs',
           mbError, MB_OK, IDOK);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep <> ssPostInstall then Exit;
  if WizardIsTaskSelected('installipban') then
    InstallIPBan
  else if not IPBanServiceInstalled then
    SuppressibleMsgBox('O serviço IPBan não foi instalado. Abra o {#AppName} e clique em "Instalar IPBan" quando quiser ativar a proteção.',
           mbInformation, MB_OK, IDOK);
end;
