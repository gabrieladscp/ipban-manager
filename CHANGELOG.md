# Changelog

Formato baseado em [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/) e versionamento
[SemVer 2.0](https://semver.org/lang/pt-BR/). Versões Beta usam o sufixo `-beta.N`.

## [Unreleased]

### Adicionado
- **Instalação completa do IPBan**: o aplicativo traz o IPBan oficial 4.1.0 embutido (SHA-256 conferido na compilação
  e antes de instalar) e o instala/configura sozinho: arquivos em `Program Files\IPBan`, auditoria de logon do Windows,
  serviço com início automático e reinício em falha, whitelist com o IP da sessão de Área de Trabalho Remota e
  checagem do Firewall do Windows. Ver `docs/INSTALACAO.md`.
- Assistente "Instalar e configurar o IPBan", aberto automaticamente quando o servidor não tem IPBan, e botão
  "Instalar IPBan..." na faixa de aviso.
- Painel mostra a versão do IPBan e oferece atualizá-lo quando o embutido for mais novo (preserva configuração, banco e
  logs; guarda os binários antigos e os restaura se o serviço novo não iniciar).
- Linha de comando `IPBanManager.exe --install-ipban [--whitelist=...]` para implantação em massa.
- Instalador offline (Inno) instala/atualiza o IPBan (opção marcada por padrão; `/WHITELIST=` no modo silencioso).

### Corrigido
- O aviso de "IPBan não encontrado" do instalador Inno nunca aparecia (nome do evento errado no script).

## [1.0.3] - 2026-11-22

### Alterado
- Assinatura de código no pipeline agora usa secrets (`SIGN_PFX_BASE64`/`SIGN_PFX_PASSWORD`) no lugar da
  variável `VPK_SIGN_PARAMS`, que exporia a senha do certificado nos logs; o certificado é validado antes e a
  assinatura é conferida depois do empacotamento.
- Ferramentas de teste atualizadas (Microsoft.NET.Test.Sdk 18.12.0, xunit 2.9.3, xunit.runner.visualstudio 4.0.1).

## [1.0.2] - 2026-11-22

### Corrigido
- `SHA256SUMS.txt` das Releases listava arquivos que não fazem parte da versão e o `releases.<canal>.json`
  (reescrito na publicação), de modo que a verificação de hash falhava. Agora cobre somente os arquivos
  imutáveis da versão, e o atestado de proveniência também.

## [1.0.1] - 2026-11-22

### Adicionado
- Atualização automática (Velopack) com canais Stable e Beta, verificação de integridade dos pacotes
  (tamanho, SHA-1 e SHA-256) e reversão automática/manual para a última versão boa.
- Aba "Atualizações" com versão instalada, canal, verificação, instalação e reversão.
- Pipeline no GitHub Actions: CI (build + testes) e Release (empacota, gera `SHA256SUMS.txt`,
  atestado de proveniência e publica no GitHub Releases).

### Corrigido
- Gravação de configurações/estado agora é durável (flush em disco antes da troca atômica): uma queda de
  energia podia deixar o arquivo zerado.
- O título da janela mostra a versão completa, inclusive o sufixo `-beta.N`.

## [1.0.1-beta.1] - 2026-11-22

Pré-lançamento de 1.0.1 (canal Beta), usado para validar a atualização Stable → Beta → Stable.

## [1.0.0]

- Primeira versão: administração do IPBan por interface gráfica (painel, configurações, listas, regras,
  IPs bloqueados, XML avançado e logs).
