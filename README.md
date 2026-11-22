# IPBan Manager

[![CI](https://github.com/gabrieladscp/ipban-manager/actions/workflows/ci.yml/badge.svg)](https://github.com/gabrieladscp/ipban-manager/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/gabrieladscp/ipban-manager?include_prereleases&label=release)](https://github.com/gabrieladscp/ipban-manager/releases)

Interface gráfica (WPF, .NET Framework 4.8) para **instalar, configurar e administrar** o
[IPBan](https://github.com/DigitalRuby/IPBan) em servidores Windows, sem editar arquivos nem usar comandos.

O IPBan continua sendo o motor (serviço Windows que bloqueia no firewall os IPs com tentativas de login abusivas).
O IPBan Manager **não reimplementa** o motor: ele instala o IPBan oficial, edita o `ipban.config` (que o serviço
recarrega sozinho), controla o serviço, lê o Windows Firewall e envia desbloqueios pelo `unban.txt`.

## Instalação

Baixe na página de [**Releases**](https://github.com/gabrieladscp/ipban-manager/releases):

| Arquivo | Atualiza sozinho | Quando usar |
|---|---|---|
| `IPBanManager-stable-Setup.exe` | **Sim** | Padrão |
| `IPBanManager-<versão>-Offline-Setup.exe` | Não | Servidores sem acesso ao GitHub, AppLocker, implantação silenciosa em massa |

Requisitos: Windows Server 2016+ ou Windows 10+ (x64) e .NET Framework 4.8 (já incluso no Server 2022). O app pede
elevação de administrador ao abrir.

**O IPBan vem junto.** Em um servidor sem IPBan, o assistente **"Instalar e configurar o IPBan"** abre sozinho e:
instala o IPBan oficial (4.1.0, SHA-256 conferido) em `Program Files\IPBan`, ativa a auditoria de logon do Windows,
cria o serviço (início automático e reinício em falha), inclui na whitelist o IP da sua conexão de Área de Trabalho
Remota e avisa se o Firewall do Windows estiver desativado. O instalador offline faz o mesmo, inclusive em modo
silencioso:

```powershell
IPBanManager-1.2.0-Offline-Setup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /WHITELIST=203.0.113.10
```

Guia completo de implantação: [docs/INSTALACAO.md](docs/INSTALACAO.md).

**Verificar um download:** os dois instaladores constam no `SHA256SUMS.txt` da Release e têm atestado de
proveniência (`gh attestation verify <arquivo> --repo gabrieladscp/ipban-manager`).

## Funcionalidades

| Aba | O que faz |
|---|---|
| Painel | Status do serviço (iniciar/parar/reiniciar, com confirmação ao parar), nº de IPs bloqueados, versão do IPBan e botão para atualizá-lo quando houver versão mais nova embutida |
| Configurações | Tentativas (campo numérico com limites), tempo de bloqueio fixo ou escalonado (lista ordenada automaticamente), durações em d/h/min/s, comportamento e usuários |
| Listas | Whitelist, blacklist e whitelist de usuários com campo validado e botões **+** / **−**; regex opcionais |
| Regras e ações | Regras de firewall, listas externas de bloqueio (URL) e programas executados em ban/unban/login, montados por formulários |
| IPs bloqueados | Lista com filtro (automáticos e blacklist), bloqueio manual e desbloqueio |
| Avançado (XML) | Edição do XML completo (modo especialista), validada, com detecção de alteração externa |
| Logs | Últimas linhas do `logfile.txt`, com filtro e atualização automática opcional |
| Atualizações | Versão instalada, canal Stable/Beta, verificação, instalação e reversão |

### Campos à prova de erro
- Cada campo só aceita os caracteres que fazem sentido (dígitos, IP, nome de regra, portas...), inclusive ao colar.
- IPs são validados ao adicionar: 4 números de 0 a 255, faixa CIDR, IPv6 ou domínio; zeros à esquerda, loopback e
  faixas amplas demais são recusados com mensagem explicativa; duplicatas são detectadas.
- Durações são normalizadas (90 min → 1 h 30 min); números ficam sempre dentro dos limites.
- Regras de firewall, listas externas e processos são montados por formulário e gravados no formato exato do IPBan.

### Atualização automática
Velopack + GitHub Releases: o app verifica novas versões em segundo plano, baixa o pacote, confere a integridade
(tamanho, SHA-1 e SHA-256) e pergunta antes de reiniciar. Canais **Stable** (padrão) e **Beta**. Se uma versão nova não
iniciar, o app **reverte sozinho** para a anterior. Configurações e dados ficam fora da pasta do programa.
Detalhes em [docs/AUTO-UPDATE.md](docs/AUTO-UPDATE.md).

### Segurança e robustez
- Valida antes de gravar; preserva comentários e seções que a GUI não edita; só altera valores que mudaram.
- Gravação durável e atômica (flush em disco + `File.Replace`) com backup `ipban.config.bak-*` (10 últimos) e
  repetição se o arquivo estiver em uso.
- Atualização do IPBan preserva configuração, banco e logs, guarda os binários antigos e os restaura se o serviço
  novo não iniciar.
- Alterações não salvas são sinalizadas (●), `Ctrl+S` salva, e a janela avisa ao sair.
- Erros do Windows viram mensagens em português; detalhes técnicos em `C:\ProgramData\IPBanManager\logs`.
- Uma instância por vez; exceções não tratadas são registradas e não derrubam o aplicativo.
- Timers só rodam com a aba visível; objetos COM do firewall são liberados (~68 MB estável, ~0% CPU).

## Arquitetura (MVP – View passiva, SOLID)

```
src/IPBanManager.Core            modelos, interfaces (DIP), validação, erros, Presenters, interfaces de View,
                                 Updates/ (SemVer, canais, rollback) e Provisioning/ (instalação do IPBan)
src/IPBanManager.Infrastructure  config XML, serviço Windows, firewall (COM), unban.txt, logs,
                                 Updates/ (Velopack) e Provisioning/ (pacote, serviço, auditoria, firewall)
src/IPBanManager.Wpf             Views (XAML sem lógica), controles de entrada validada e Composition Root
tests/IPBanManager.Tests         xunit: Core/ Infrastructure/ Presenters/ Support/ (fakes)
installer/IPBanManager.iss       instalador offline (Inno Setup)
.github/workflows/               ci.yml (build + testes) e release.yml (empacota, assina, atesta e publica)
assets/                          ícones e imagens do instalador
```

- Views só exibem dados e disparam eventos; a lógica fica nos Presenters e nas regras de validação do Core,
  testáveis sem WPF.
- Presenters dependem apenas de abstrações; um único `ConfigSectionPresenter<TView>` atende 3 telas (OCP/DRY).

## Desenvolvimento

```powershell
dotnet build IPBanManager.sln                 # baixa e confere o pacote do IPBan na primeira compilação
dotnet test tests/IPBanManager.Tests          # 410 testes; filtro: --filter Category=Unit | Integration
.\build-installer.ps1                         # instalador offline local (o oficial sai pelo GitHub Actions)
.\scripts\new-release.ps1 -Version 1.3.0 -Push  # publica uma versão (ver docs/RELEASING.md)
```

Testes: nomes `Método_Cenário_Resultado`, Arrange/Act/Assert, fakes em `Support/`, pastas temporárias isoladas,
sem `Task.Delay`/sleeps (esperas por sinais com timeout).

## Documentação

- [docs/INSTALACAO.md](docs/INSTALACAO.md): implantação nos clientes, instalação silenciosa, desinstalação
- [docs/AUTO-UPDATE.md](docs/AUTO-UPDATE.md): atualização automática, canais e reversão
- [docs/RELEASING.md](docs/RELEASING.md): como publicar versões, assinatura de código, verificação de downloads
- [CHANGELOG.md](CHANGELOG.md): histórico de versões

## Observações
- Blacklist manual vira a regra `IPBan_GlobalBlacklist_N`; bloqueios automáticos ficam em `IPBan_Block_N`.
- Desinstalar o IPBan Manager **não remove o IPBan** (o servidor não fica sem proteção).
- Por padrão o IPBan envia os IPs bloqueados ao serviço de reputação do projeto; desative em
  Configurações > "Usar o compartilhamento padrão de IPs bloqueados" se a política do cliente exigir.
- Assinatura de código: automática no pipeline quando os secrets `SIGN_PFX_BASE64` e `SIGN_PFX_PASSWORD` estão
  definidos. Sem certificado, o Windows SmartScreen pode exibir um aviso no primeiro uso.
- Fora do escopo: ler o `ipban.sqlite` (data/contagem de falhas por IP).
