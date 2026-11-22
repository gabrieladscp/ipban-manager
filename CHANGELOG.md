# Changelog

Formato baseado em [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/) e versionamento
[SemVer 2.0](https://semver.org/lang/pt-BR/). Versões Beta usam o sufixo `-beta.N`.

## [Unreleased]

## [1.0.1] - 2026-11-22

## [1.0.1-beta.1] - 2026-11-22

### Adicionado
- Atualização automática (Velopack) com canais Stable e Beta, verificação de integridade dos pacotes
  e reversão automática/manual para a última versão boa.
- Aba "Atualizações" com versão instalada, canal, verificação, instalação e reversão.
- Pipeline no GitHub Actions: CI (build + testes) e Release (empacota, gera `SHA256SUMS.txt`,
  atestado de proveniência e publica no GitHub Releases).

### Corrigido
- Gravação de configurações/estado agora é durável (flush em disco antes da troca atômica): uma queda de
  energia podia deixar o arquivo zerado.

## [1.0.0]

- Primeira versão: administração do IPBan por interface gráfica (painel, configurações, listas, regras,
  IPs bloqueados, XML avançado e logs).
