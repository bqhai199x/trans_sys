$ErrorActionPreference = 'Stop'
$env:CODEX_HOME = Join-Path $env:LOCALAPPDATA 'TransSysGateway\codex-home'
New-Item -ItemType Directory -Path $env:CODEX_HOME -Force | Out-Null
& (Join-Path $PSScriptRoot 'node\node.exe') (Join-Path $PSScriptRoot 'node_modules\@openai\codex\bin\codex.js') login
if ($LASTEXITCODE -ne 0) { throw 'Codex login did not complete.' }
