#requires -Version 5.1
<#
.SYNOPSIS
  Executa os testes, compila em Release, monta a pasta de distribuição e gera o instalador (Inno Setup 6).
.EXAMPLE
  .\build-installer.ps1               # usa a versão do csproj
  .\build-installer.ps1 -SkipTests
#>
param(
    [switch]$SkipTests,
    [string]$Iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$wpfProj = Join-Path $root 'src\IPBanManager.Wpf\IPBanManager.Wpf.csproj'
$staging = Join-Path $root 'artifacts\app'

function Invoke-Step($title, [scriptblock]$body) {
    Write-Host "==> $title" -ForegroundColor Cyan
    & $body
    if ($LASTEXITCODE -ne 0) { throw "Falha em: $title (código $LASTEXITCODE)" }
}

if (-not (Test-Path $Iscc)) { throw "Inno Setup 6 não encontrado em '$Iscc'. Instale-o em https://jrsoftware.org/isinfo.php" }

# Fonte única da versão: Directory.Build.props (o GitHub Actions a sobrescreve com a tag).
[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
$version = ($props.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
if (-not $version) { throw 'Version não definido em Directory.Build.props.' }
Write-Host "Versão: $version"

if (-not $SkipTests) {
    Invoke-Step 'Testes' { dotnet test (Join-Path $root 'tests\IPBanManager.Tests') -c Release --nologo -v q }
}

Invoke-Step 'Build Release' { dotnet build $wpfProj -c Release --nologo -v q }

Write-Host '==> Montando pasta de distribuição' -ForegroundColor Cyan
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Force $staging | Out-Null
$out = Join-Path $root 'src\IPBanManager.Wpf\bin\Release\net48'
Get-ChildItem $out -File | Where-Object { $_.Extension -in '.exe', '.dll', '.config' } |
    Copy-Item -Destination $staging
Get-ChildItem $staging | Select-Object Name, Length | Format-Table -AutoSize

Invoke-Step 'Instalador (Inno Setup)' { & $Iscc "/DAppVersion=$version" (Join-Path $root 'installer\IPBanManager.iss') }

$setup = Join-Path $root "installer\Output\IPBanManager-Setup-$version.exe"
Write-Host "`nInstalador gerado: $setup" -ForegroundColor Green
Get-FileHash $setup -Algorithm SHA256 | ForEach-Object { "SHA256: $($_.Hash)" }
