# Publicando versões

O app se atualiza sozinho a partir das **GitHub Releases** deste repositório. Quem publica é o GitHub Actions;
você só cria uma tag.

## Configuração única do repositório

1. **Actions > General > Workflow permissions**: *Read and write permissions* (o `release.yml` já declara o
   mínimo necessário por `permissions:`, mas o repositório precisa permitir).
2. **Branch protection na `main`** (recomendado): exigir pull request e o check **CI / Build e testes**.
   O `release.yml` só publica versões *Stable* de commits que estão na `main`.
3. Repositório **público** (ou use um token com acesso a Releases privadas — veja "Repositório privado" abaixo).
4. (Opcional) Assinatura de código — veja a seção "Assinatura de código" abaixo.

Nenhum outro segredo é necessário: o workflow usa o `GITHUB_TOKEN` automático e injeta a URL do repositório no
executável (`-p:UpdateRepoUrl=https://github.com/gabrieladscp/ipban-manager`).

## Versionamento (SemVer 2.0)

| Tag | Canal | Release no GitHub |
|---|---|---|
| `v1.2.0` | **Stable** | normal |
| `v1.3.0-beta.1` | **Beta** | marcada como *pre-release* |

Somente `-beta.N` é aceito como pré-release (o workflow recusa qualquer outra tag). Ordem de precedência:
`1.2.0-beta.1 < 1.2.0-beta.2 < 1.2.0` — quem está no Beta recebe o Stable quando ele sai.

## Passo a passo

```powershell
.\scripts\new-release.ps1 -Version 1.2.0          # valida, atualiza Directory.Build.props + CHANGELOG, commit e tag
git push origin main v1.2.0                       # ou use -Push no próprio script
```

O que o workflow `Release` faz, nesta ordem (qualquer falha interrompe a publicação):

1. Valida a tag (SemVer estrito) e define o canal; Stable só a partir da `main`.
2. Roda **todos os testes**.
3. `dotnet publish` com `-p:Version` da tag e confere que o `.exe` ficou com essa versão.
4. Baixa a Release anterior do canal (mantém o feed contínuo) e empacota com **Velopack** (`vpk pack`,
   pacote completo, sem deltas).
5. Gera `SHA256SUMS.txt` e o **atestado de proveniência** (Sigstore) dos binários.
6. Publica a Release (`vpk upload github`) e anexa `SHA256SUMS.txt`.

Arquivos da Release: `IPBanManager-<canal>-Setup.exe` (instalador com atualização automática),
`IPBanManager-<versão>-Offline-Setup.exe` (instalador offline Inno Setup, sem atualização automática),
`IPBanManager-<canal>-Portable.zip`, `*-full.nupkg` + `releases.<canal>.json` (consumidos pelo app) e
`SHA256SUMS.txt`.

`SHA256SUMS.txt` e o atestado cobrem os arquivos **imutáveis desta versão** (Setup, Offline-Setup, Portable e o
`.nupkg` da versão). O `releases.<canal>.json` fica de fora de propósito: o `vpk upload --merge` o reescreve ao publicar
(une com o feed anterior). Ele não precisa de hash próprio porque contém o SHA-1/SHA-256/tamanho de cada pacote,
e o app recusa qualquer pacote que não confira com eles.

**Ensaio sem publicar:** *Actions > Release > Run workflow*, informe a versão e deixe `publish` desmarcado. Os
artefatos ficam disponíveis por 30 dias na execução.

## Verificando um download (usuários/auditoria)

```powershell
# 1) hash
(Get-FileHash .\IPBanManager-stable-Setup.exe -Algorithm SHA256).Hash   # compare com SHA256SUMS.txt
# 2) proveniência: prova que o arquivo saiu deste repositório/workflow
gh attestation verify .\IPBanManager-stable-Setup.exe --repo gabrieladscp/ipban-manager
```

## Assinatura de código

Sem certificado tudo funciona (a integridade é garantida por SHA-256 + atestado), mas o Windows SmartScreen pode
avisar "editor desconhecido" ao executar o instalador. Para assinar, obtenha um certificado de **assinatura de
código** (OV/EV, de uma autoridade como DigiCert, Sectigo, Certum) em formato `.pfx` e cadastre dois *secrets* em
**Settings > Secrets and variables > Actions > Secrets**:

| Secret | Valor |
|---|---|
| `SIGN_PFX_BASE64` | o `.pfx` em base64: `[Convert]::ToBase64String([IO.File]::ReadAllBytes('cert.pfx')) \| Set-Clipboard` |
| `SIGN_PFX_PASSWORD` | a senha do `.pfx` |

Pela linha de comando: `gh secret set SIGN_PFX_PASSWORD` e
`[Convert]::ToBase64String([IO.File]::ReadAllBytes('cert.pfx')) | gh secret set SIGN_PFX_BASE64`.

A partir da próxima Release, o workflow abre o certificado (falha com mensagem clara se a senha estiver errada, se
faltar a chave privada ou se estiver expirado), assina instalador e executáveis com carimbo de tempo, confere a
assinatura e apaga o `.pfx` do runner. Secrets nunca aparecem nos logs. Servidor de carimbo de tempo diferente:
variável de repositório `SIGN_TIMESTAMP_URL` (padrão `http://timestamp.digicert.com`).

> Não use *variables* para a senha: num repositório público elas ficam visíveis nos logs.

## Retirar uma versão ruim

- Marque a Release como *draft* ou apague-a: o feed deixa de oferecê-la.
- Quem já a instalou e ela não inicia volta sozinho para a versão anterior (veja `AUTO-UPDATE.md`).
- Publique a correção com um número **maior** (nunca reutilize uma tag).

## Repositório privado

O `GithubSource` do app usa a API pública. Para um repositório privado seria preciso embutir um token no
aplicativo, o que o expõe a qualquer usuário — **não recomendado**. Prefira repositório público (o código-fonte do
IPBan e deste gerenciador não contém segredos) ou hospede o feed em um servidor próprio (`SimpleWebSource`).
