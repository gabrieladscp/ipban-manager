# Implantação nos servidores dos clientes

O IPBan Manager traz o **IPBan oficial 4.1.0** embutido (SHA-256 conferido na compilação e de novo antes de instalar).
Em um servidor sem IPBan, basta instalar o IPBan Manager: ele instala e configura o IPBan.

## Qual instalador usar

| Instalador | Onde baixar | Atualiza sozinho | Quando usar |
|---|---|---|---|
| `IPBanManager-stable-Setup.exe` | GitHub Releases | **Sim** | Padrão |
| `IPBanManager-<versão>-Offline-Setup.exe` (Inno Setup) | GitHub Releases | Não | Servidores sem acesso ao GitHub, AppLocker que bloqueia `%LocalAppData%`, implantação silenciosa em massa |

Os dois são gerados pelo GitHub Actions a partir do mesmo build, constam no `SHA256SUMS.txt` e têm atestado de
proveniência (`gh attestation verify <arquivo> --repo gabrieladscp/ipban-manager`). O offline instala em
`C:\Program Files\IPBan Manager` para todos os usuários; para atualizá-lo, rode o offline da versão nova por cima.

## Instalação padrão (com atualização automática)

1. Execute `IPBanManager-stable-Setup.exe` com um usuário administrador.
2. O IPBan Manager abre ao final (confirme o UAC). Se o servidor não tiver IPBan, o assistente
   **"Instalar e configurar o IPBan"** abre sozinho. Se o app não abrir, inicie-o pelo menu Iniciar.
3. Confira a whitelist (vem preenchida com o IP da sua conexão de Área de Trabalho Remota), inclua outros IPs fixos
   de administração e clique em **Instalar**.

## Instalação offline / silenciosa (Inno Setup)

Interativa: execute o instalador e deixe marcada a opção **"Instalar/atualizar e configurar o serviço IPBan"**.

Silenciosa (implantação em massa):

```powershell
IPBanManager-1.2.0-Offline-Setup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /WHITELIST=203.0.113.10,198.51.100.0/24
```

Sem `/WHITELIST`, entram na whitelist os IPs das sessões de Área de Trabalho Remota ativas no momento.

Também é possível só (re)instalar o IPBan a partir de um IPBan Manager já instalado:

```powershell
& "$env:LocalAppData\IPBanManager\current\IPBanManager.exe" --install-ipban --whitelist=203.0.113.10
# código de saída: 0 = ok, 1 = falha, 2 = whitelist inválida (detalhes em C:\ProgramData\IPBanManager\logs)
```

## O que é feito no servidor

Mesma sequência do script oficial do IPBan (`install_latest.ps1`), mais a whitelist e a checagem do firewall:

1. Confere o SHA-256 do pacote embutido (recusa se não conferir).
2. Copia o IPBan para `C:\Program Files\IPBan` (ou para a pasta do serviço já existente).
3. Inclui os IPs informados na whitelist do `ipban.config` (os já existentes são mantidos).
4. Ativa a auditoria de logon do Windows (categorias Logon/Logoff e Account Logon), sem a qual as falhas de login
   não aparecem no log de Segurança.
5. Cria o serviço `IPBan` (início automático atrasado; reinicia após 1 minuto em caso de falha) e o inicia.
6. Avisa se o Firewall do Windows estiver desativado em algum perfil (o IPBan bloqueia pelo firewall).

## Atualização do IPBan

Quando uma nova versão do IPBan Manager traz um IPBan mais novo, o **Painel** mostra
"Versão do IPBan: X — Atualizar para Y...". A atualização:

- para o serviço por alguns segundos;
- preserva `ipban.config`, `ipban.override.config`, `nlog.config`, o banco `ipban.sqlite` e os logs;
- guarda os binários antigos em `C:\ProgramData\IPBanManager\ipban-backup\` (3 últimos);
- se o serviço novo não iniciar, devolve os binários antigos e religa o serviço anterior.

O instalador Inno também atualiza um IPBan mais antigo quando reinstalado.

## Desinstalação

Desinstalar o IPBan Manager **não remove o IPBan**, para o servidor não ficar sem proteção. Para remover o IPBan:

```powershell
Stop-Service IPBan; sc.exe delete IPBan; Remove-Item 'C:\Program Files\IPBan' -Recurse -Force
Get-NetFirewallRule -DisplayName 'IPBan_*' | Remove-NetFirewallRule   # regras de bloqueio criadas por ele
```

## Observações

- Requisitos: Windows Server 2016 ou mais novo, 64 bits, .NET Framework 4.8 (incluído no Server 2022).
- Por padrão o IPBan (`UseDefaultBannedIPAddressHandler=true`) envia os IPs bloqueados ao serviço de reputação do
  projeto (ipthreat.net). Se a política do cliente não permitir, desative em **Avançado (XML)**.
- Para atualizar o IPBan embutido numa versão futura do IPBan Manager: altere `IPBanBundleVersion`, `IPBanBundleUrl` e
  `IPBanBundleSha256` em `src/IPBanManager.Wpf/IPBanManager.Wpf.csproj` (o SHA-256 é o *digest* do arquivo na Release
  do DigitalRuby/IPBan).
