#requires -Version 5.1
<#
.SYNOPSIS
  Prepara uma versão: valida o SemVer, atualiza Directory.Build.props e o CHANGELOG, faz o commit e cria a tag.
  Quem publica é o GitHub Actions, ao receber a tag (.github/workflows/release.yml).

.EXAMPLE
  .\scripts\new-release.ps1 -Version 1.2.0              # Stable (a partir da main)
  .\scripts\new-release.ps1 -Version 1.3.0-beta.1       # Beta (qualquer branch)
  .\scripts\new-release.ps1 -Version 1.2.0 -Push        # além disso, envia o commit e a tag ao GitHub
#>
param(
    [Parameter(Mandatory)][string]$Version,
    [switch]$Push
)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)

# SemVer 2.0 estrito; o único pré-release aceito é -beta.N (mesma regra do workflow).
$pattern = '^(?<v>(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)(?:-beta\.(?:0|[1-9]\d*))?)$'
if ($Version.TrimStart('v') -notmatch $pattern) {
    throw "Versão '$Version' inválida. Use MAJOR.MINOR.PATCH (Stable) ou MAJOR.MINOR.PATCH-beta.N (Beta). Exemplos: 1.2.0, 1.3.0-beta.1"
}
$Version = $Matches.v
$tag = "v$Version"
$isBeta = $Version.Contains('-')

# ---- pré-condições ----
git rev-parse --is-inside-work-tree 2>$null | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Esta pasta não é um repositório git.' }
if (git status --porcelain) { throw 'Há alterações não commitadas. Faça commit ou stash antes de criar a versão.' }
if (git tag --list $tag) { throw "A tag $tag já existe." }
$branch = (git rev-parse --abbrev-ref HEAD).Trim()
if (-not $isBeta -and $branch -ne 'main') { throw "Versões Stable só saem da branch main (você está em '$branch')." }

# a nova versão precisa ser maior que a anterior
$props = Join-Path (Get-Location) 'Directory.Build.props'
$content = Get-Content $props -Raw -Encoding UTF8
if ($content -notmatch '<Version>(?<old>[^<]+)</Version>') { throw 'Tag <Version> não encontrada em Directory.Build.props.' }
$old = $Matches.old
function Compare-SemVer([string]$a, [string]$b) {
    $pa = $a -split '[-.]'; $pb = $b -split '[-.]'
    for ($i = 0; $i -lt 3; $i++) { if ([int]$pa[$i] -ne [int]$pb[$i]) { return [int]$pa[$i] - [int]$pb[$i] } }
    $preA = $a.Contains('-'); $preB = $b.Contains('-')
    if ($preA -and -not $preB) { return -1 }
    if (-not $preA -and $preB) { return 1 }
    if ($preA) { return [int]$pa[4] - [int]$pb[4] }
    return 0
}
if ((Compare-SemVer $Version $old) -le 0) { throw "A versão $Version precisa ser maior que a atual ($old)." }

# ---- atualizar arquivos (com reversão se algo falhar no meio) ----
# Avisos do git (ex.: conversão LF/CRLF) saem em stderr; no Windows PowerShell 5.1 com ErrorActionPreference=Stop
# isso viraria erro fatal. Por isso o git é chamado por este invólucro, que só confere o código de saída.
function Invoke-Git {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & git @args 2>&1
        if ($LASTEXITCODE -ne 0) { throw "git $($args -join ' ') falhou: $($output -join ' ')" }
    }
    finally { $ErrorActionPreference = $previous }
}

$changelog = Join-Path (Get-Location) 'CHANGELOG.md'
try {
    $content = [regex]::Replace($content, '<Version>[^<]+</Version>', "<Version>$Version</Version>")
    Set-Content -Path $props -Value $content -Encoding UTF8 -NoNewline

    if (Test-Path $changelog) {
        $text = Get-Content $changelog -Raw -Encoding UTF8
        $heading = "## [$Version] - $(Get-Date -Format 'yyyy-MM-dd')"
        if ($text -match '## \[Unreleased\]') {
            $text = $text -replace '## \[Unreleased\]', "## [Unreleased]`r`n`r`n$heading"
            Set-Content -Path $changelog -Value $text -Encoding UTF8 -NoNewline
        }
    }

    Invoke-Git add Directory.Build.props CHANGELOG.md
    Invoke-Git commit -m "chore(release): $tag"
    Invoke-Git tag -a $tag -m "IPBan Manager $Version"
}
catch {
    # Deixa a árvore como estava: nada de arquivo alterado sem commit/tag.
    $ErrorActionPreference = 'Continue'
    & git reset -q HEAD -- Directory.Build.props CHANGELOG.md 2>&1 | Out-Null
    & git checkout -q -- Directory.Build.props CHANGELOG.md 2>&1 | Out-Null
    throw "Não foi possível preparar a versão (alterações revertidas): $($_.Exception.Message)"
}
Write-Host "Versão $old -> $Version preparada (canal $(if ($isBeta) { 'Beta' } else { 'Stable' })). Tag $tag criada." -ForegroundColor Green

if ($Push) {
    Invoke-Git push origin $branch $tag
    Write-Host "Enviado. Acompanhe em: Actions > Release." -ForegroundColor Green
} else {
    Write-Host "Para publicar:  git push origin $branch $tag"
}
