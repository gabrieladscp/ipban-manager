# Atualização automática

Implementada com [Velopack](https://velopack.io) (instalador + atualizador) sobre GitHub Releases.

## Onde cada coisa fica

| Camada | Arquivo | Papel |
|---|---|---|
| Core | `Updates/SemVer.cs` | Versão semântica 2.0 e precedência (`1.2.0-beta.1 < 1.2.0`) |
| Core | `Updates/UpdateHealthGuard.cs` | Decide se uma versão nova é saudável ou deve ser revertida |
| Core | `Presenters/UpdatePresenter.cs` | Fluxos de verificar/baixar/instalar/reverter (sem Velopack, testável) |
| Infrastructure | `Updates/VelopackUpdateService.cs` | Único ponto que conhece o Velopack: canais, integridade, rollback |
| Infrastructure | `Updates/RollbackPackageStore.cs` | Guarda e restaura o pacote da última versão boa (SHA-256) |
| Infrastructure | `Updates/XmlFileStore.cs` | Preferências e estado em `ProgramData`, gravação durável |
| Wpf | `Program.cs` | `Main` próprio: `VelopackApp.Run()` antes de tudo |
| Wpf | `Views/UpdatesView.xaml` | Aba **Atualizações** |

Fora de uma instalação do Velopack (build local, `.exe` direto, instalador antigo do Inno) o app **continua
funcionando normalmente**; só a aba Atualizações avisa que a atualização automática está indisponível.

## Dados do cliente são preservados

O Velopack troca apenas a pasta do programa (`%LocalAppData%\IPBanManager\current`). Tudo do usuário fica fora:

| Dado | Local |
|---|---|
| Configuração do IPBan | `ipban.config` na pasta do IPBan (nunca tocada) |
| Pasta escolhida do IPBan | `C:\ProgramData\IPBanManager\install-dir.txt` |
| Logs do app | `C:\ProgramData\IPBanManager\logs\` |
| Preferências/estado de atualização | `C:\ProgramData\IPBanManager\update\` |

## Canais

- **Stable** (padrão): só versões sem pré-release.
- **Beta**: consulta os dois feeds e escolhe a **maior versão SemVer** — assim um `1.2.0` estável substitui o
  `1.2.0-beta.3`. Trocar de Beta para Stable oferece (com confirmação) a versão estável mesmo que mais antiga.

Downgrades nunca são instalados automaticamente.

## Fluxo normal

```
45 s após abrir, e a cada N horas -> consulta o feed (canal escolhido)
  nova versão? -> guarda o pacote da versão atual (backup) -> baixa o pacote completo
  -> verifica integridade -> pergunta "Reiniciar agora?" (nunca com edições não salvas)
  -> Velopack instala e reabre.  Se o usuário adiar, instala na próxima abertura.
```

## Integridade dos pacotes

1. Somente repositórios `https://github.com/<dono>/<repo>` (neste projeto, `gabrieladscp/ipban-manager`) são aceitos como origem.
2. O pacote só é aceito se o feed informar hash; o Velopack confere o checksum ao baixar.
3. Segunda verificação independente antes de aplicar: **tamanho**, **SHA-256** e **SHA-1** do feed.
   Qualquer divergência apaga o arquivo, cancela e mostra "Nada foi instalado".
4. Sempre o pacote **completo** (nunca reconstruído por deltas), para que os hashes publicados valem para os
   bytes exatos instalados.
5. No GitHub: `SHA256SUMS.txt` e atestado de proveniência (`gh attestation verify`). Assinatura de código é
   opcional (secrets `SIGN_PFX_BASE64` e `SIGN_PFX_PASSWORD`; veja `RELEASING.md`).

## Rollback

Uma versão nova fica "em prova" até comprovar que funciona:

| Evento | Resultado |
|---|---|
| Janela abriu, carga inicial concluída e **30 s** estável | Versão promovida a "última boa" |
| Encerramento normal depois de iniciar com sucesso | Versão promovida |
| Falhou ao iniciar 2 vezes (processo morto/crash antes de ficar saudável) | **3ª abertura reverte sozinha** para a anterior |
| Exceção fatal durante a prova | Próxima abertura reverte |
| Usuário clica em **Reverter** na aba | Reinstala a versão anterior preservada |

Na reversão: o pacote preservado é verificado por SHA-256, a versão ruim entra na **lista de bloqueio** (não é
oferecida de novo, só uma versão *mais nova*) e os pacotes baixados dela são apagados para o auto-apply do
Velopack não reinstalá-la. O pacote da versão anterior é guardado **antes** do download, porque o Velopack
apaga pacotes antigos ao baixar.

## Falhas e o que o usuário vê

| Situação | Comportamento |
|---|---|
| Sem internet / GitHub fora / limite da API | Verificação manual: mensagem clara. Automática: só log; tenta de novo no próximo ciclo (com 2 novas tentativas imediatas) |
| Pacote adulterado/corrompido | Descartado; "Nada foi instalado"; app segue na versão atual |
| Download interrompido | Nada é aplicado; o arquivo parcial é descartado |
| Edições não salvas | Nunca reinicia; instala na próxima abertura |
| Estado/preferências corrompidos (ex.: queda de energia) | Volta aos padrões, aviso no log; o app abre normalmente |
| Sem pacote de rollback | Segue com a versão atual (aviso no log) em vez de ficar sem aplicativo |

## Desenvolvimento e QA local (sem GitHub)

Compile com `-p:AllowLocalUpdateSource=true -p:UpdateRepoUrl=<pasta>` para usar uma pasta como feed
(`vpk pack ... --outputDir <pasta>`). Essa opção **nunca** é usada nas Releases: o executável publicado só
aceita `https://github.com/...`.
