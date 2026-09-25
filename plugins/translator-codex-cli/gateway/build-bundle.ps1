param(
    [Parameter(Mandatory = $true)][string]$NodeExecutable,
    [Parameter(Mandatory = $true)][string]$CodexNodeModules,
    [Parameter(Mandatory = $true)][string]$OutputZip
)
$ErrorActionPreference = 'Stop'
$nodeSource = (Resolve-Path -LiteralPath $NodeExecutable).Path
$codexSource = (Resolve-Path -LiteralPath $CodexNodeModules).Path
$codexPackage = Get-Content -LiteralPath (Join-Path $codexSource '@openai\codex\package.json') -Raw | ConvertFrom-Json
if ($codexPackage.version -ne '0.155.0') { throw 'This profile requires Codex CLI 0.155.0.' }
$nodeVersion = & $nodeSource --version
if ($nodeVersion -notmatch '^v24\.') { throw 'Node.js 24 is required.' }
$nodeArch = & $nodeSource -p 'process.arch'
if ($nodeArch -ne 'x64') { throw 'Windows x64 Node.js is required.' }
Push-Location $PSScriptRoot
try {
    & npm.cmd run build
    if ($LASTEXITCODE -ne 0) { throw 'TypeScript build failed.' }
    $stage = Join-Path $PSScriptRoot ('bundle\' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path (Join-Path $stage 'node'), (Join-Path $stage 'node_modules\@openai') -Force | Out-Null
    Copy-Item -LiteralPath $nodeSource -Destination (Join-Path $stage 'node\node.exe')
    Copy-Item -LiteralPath (Join-Path $codexSource '@openai\codex') -Destination (Join-Path $stage 'node_modules\@openai') -Recurse
    $platformPackage = Join-Path $codexSource '@openai\codex-win32-x64'
    if (Test-Path -LiteralPath $platformPackage) {
        Copy-Item -LiteralPath $platformPackage -Destination (Join-Path $stage 'node_modules\@openai') -Recurse
    } elseif (-not (Test-Path -LiteralPath (Join-Path $stage 'node_modules\@openai\codex\node_modules\@openai\codex-win32-x64'))) {
        throw 'Codex Windows x64 native package is missing.'
    }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'dist') -Destination $stage -Recurse
    foreach ($file in @('install.ps1', 'login.ps1', 'package.json')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $stage }
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $OutputZip -Force
} finally { Pop-Location }
