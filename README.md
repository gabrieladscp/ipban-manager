# IPBan Manager

[![CI](https://github.com/gabrieladscp/ipban-manager/actions/workflows/ci.yml/badge.svg)](https://github.com/gabrieladscp/ipban-manager/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/gabrieladscp/ipban-manager?include_prereleases&label=release)](https://github.com/gabrieladscp/ipban-manager/releases)

Interface gráfica (WPF, .NET Framework 4.8, Windows Server 2022) para administrar o serviço
[IPBan](https://github.com/DigitalRuby/IPBan) sem editar arquivos nem usar comandos.

O IPBan continua sendo o motor (serviço Windows). A GUI **não reimplementa** o motor: ela edita o
`ipban.config` (que o serviço recarrega sozinho), controla o serviço, lê o Windows Firewall e envia
desbloqueios pelo `unban.txt`.

## Instalação (produção)

Baixe `IPBanManager-stable-Setup.exe` na página de **Releases** do repositório e execute-o
(o app pede elevação de administrador ao abrir). Requisitos: Windows 10 / Server 2016+ (x64) e
.NET Framework 4.8 (já incluso no Server 2022).

**O IPBan vem junto**: se o servidor ainda não tem o IPBan, o app abre o assistente que instala o IPBan oficial
(4.1.0), ativa a auditoria de logon, cria o serviço e coloca o seu IP de acesso remoto na whitelist. Guia completo de
implantação (inclusive silenciosa) em [docs/INSTALACAO.md](docs/INSTALACAO.md).

**Atualização automática** (Velopack + GitHub Releases): o app verifica novas versões em segundo plano, baixa o
pacote, confere a integridade (tamanho, SHA-1 e SHA-256) e pergunta antes de reiniciar. Há dois canais, escolhidos na
aba **Atualizações**: **Stable** (padrão) e **Beta**. Se uma versão nova não iniciar, o app **reverte sozinho** para a
anterior. Configurações e dados ficam fora da pasta do programa e são preservados. Detalhes em
[docs/AUTO-UPDATE.md](docs/AUTO-UPDATE.md); como publicar versões em [docs/RELEASING.md](docs/RELEASING.md).

> **Instalador offline**: cada Release traz também `IPBanManager-<versão>-Offline-Setup.exe` (Inno Setup), para
> servidores sem acesso ao GitHub ou implantação silenciosa em massa. Instalações feitas por ele **não** se
> atualizam sozinhas. Veja [docs/INSTALACAO.md](docs/INSTALACAO.md).

## Funcionalidades

| Aba | O que faz |
|---|---|
| Painel | Status do serviço (iniciar/parar/reiniciar, com confirmação ao parar), nº de IPs bloqueados |
| Configurações | Tentativas, tempos (campos numéricos d/h/min/s), comportamento, usuários |
| Listas | Whitelist, Blacklist, whitelist de usuários e regex |
| Regras e ações | Regras de firewall, listas externas (URL), processos executados em ban/unban/login |
| IPs bloqueados | Lista com filtro (automáticos e blacklist), bloqueio manual, desbloqueio |
| Avançado (XML) | Edição do XML completo, validada, com detecção de alteração externa |
| Logs | Últimas linhas do `logfile.txt`, com filtro e atualização automática opcional |

### Segurança e robustez
- Valida antes de gravar; preserva comentários e seções que a GUI não edita; só altera valores que mudaram.
- Gravação atômica (`File.Replace`) com backup `ipban.config.bak-*` (10 últimos) e repetição se o arquivo estiver em uso.
- Bloqueio manual recusa loopback, pede confirmação para faixas muito amplas (ex.: /8) e para IPs da whitelist.
- Alterações não salvas são sinalizadas (●), `Ctrl+S` salva, e a janela avisa ao sair.
- Erros do Windows (acesso negado, timeout do serviço, arquivo em uso) viram mensagens em português;
  detalhes técnicos vão para `C:\ProgramData\IPBanManager\logs\manager.log` (rotação automática).
- Uma instância por vez; exceções não tratadas são registradas e não derrubam o aplicativo.

### Desempenho
- Timers só rodam com a aba visível; verificação periódica consulta apenas o serviço (a leitura do firewall é rara).
- Objetos COM do firewall são liberados (sem vazamento); medido: ~68 MB estável, ~0% CPU, ~10 ms por consulta.

## Arquitetura (MVP – View passiva)

```
src/IPBanManager.Core            modelos, interfaces (DIP), validação, erros, Presenters, interfaces de View
src/IPBanManager.Infrastructure  config XML, serviço Windows, firewall (COM), unban.txt, logs
src/IPBanManager.Wpf             Views (XAML sem lógica) + Composition Root (App.xaml.cs)
tests/IPBanManager.Tests         xunit: Core/ Infrastructure/ Presenters/ Support/ (fakes)
installer/IPBanManager.iss       Inno Setup
assets/                          ícones e imagens do instalador
```

- Views só exibem dados e disparam eventos; toda a lógica está nos Presenters (testáveis sem WPF).
- Presenters dependem apenas de abstrações; um único `ConfigSectionPresenter<TView>` atende 3 telas (OCP/DRY).

## Desenvolvimento

```
dotnet build IPBanManager.sln
dotnet test tests/IPBanManager.Tests          # 303 testes; filtro: --filter Category=Unit | Integration
.\build-installer.ps1                         # instalador Inno Setup offline (legado, sem auto-atualização)
```

O instalador oficial (com atualização automática) é o `IPBanManager-stable-Setup.exe` gerado pelo GitHub Actions
nas Releases — veja [docs/RELEASING.md](docs/RELEASING.md). Os dois instaladores instalam e configuram o IPBan
quando o servidor ainda não o tem — veja [docs/INSTALACAO.md](docs/INSTALACAO.md).

Testes: nomes `Método_Cenário_Resultado`, Arrange/Act/Assert, fakes em `Support/`, pastas temporárias isoladas,
sem `Task.Delay`/sleeps (esperas por sinais com timeout).

## Observações
- Blacklist manual vira a regra `IPBan_GlobalBlacklist_N`; bloqueios automáticos ficam em `IPBan_Block_N`.
- Assinatura de código: o pipeline assina instalador e executáveis automaticamente quando os secrets
  `SIGN_PFX_BASE64` e `SIGN_PFX_PASSWORD` estão definidos (veja [docs/RELEASING.md](docs/RELEASING.md)).
  Sem certificado, os binários saem sem assinatura e o Windows SmartScreen pode exibir um aviso no primeiro uso.
- Fora do escopo: desinstalar o serviço IPBan (veja `docs/INSTALACAO.md`) e ler o `ipban.sqlite`
  (data/contagem de falhas por IP).
