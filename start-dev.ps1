param(
    [switch]$SkipInstall,
    [string]$DataDirectory = (Join-Path $PSScriptRoot 'data')
)
$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$runDirectory = Join-Path $taskRoot '.run'
$stateFile = Join-Path $runDirectory 'services.json'
New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null

function Invoke-Checked([string]$Command, [string[]]$Arguments, [string]$Directory) {
    Push-Location -LiteralPath $Directory
    try {
        & $Command @Arguments
        if ($LASTEXITCODE -ne 0) { throw "$Command failed with exit code $LASTEXITCODE" }
    } finally { Pop-Location }
}
function Get-PortListener([int]$Port) {
    @(Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue)
}

if (Test-Path -LiteralPath $stateFile) {
    $previous = Get-Content -LiteralPath $stateFile -Raw | ConvertFrom-Json
    foreach ($service in $previous.services) {
        $process = Get-Process -Id $service.id -ErrorAction SilentlyContinue
        if ($process -and $process.StartTime.ToUniversalTime().Ticks -eq ([datetimeoffset]$service.startedAt).UtcDateTime.Ticks) {
            throw "AI.Assistant already has a managed process. Run .\stop-dev.ps1 before restarting."
        }
    }
}
foreach ($port in @(5088, 5188)) {
    if (Get-PortListener $port) { throw "Port $port is occupied. Its current process was left running." }
}
$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
$node = (Get-Command node -ErrorAction Stop).Source
$pnpm = (Get-Command pnpm -ErrorAction Stop).Source
if (-not $SkipInstall) {
    Invoke-Checked $pnpm @('install', '--frozen-lockfile') (Join-Path $taskRoot 'frontend')
    Invoke-Checked $dotnet @('restore', 'backend/AI.Assistant.Api/AI.Assistant.Api.csproj', '--nologo') $taskRoot
}
Invoke-Checked $dotnet @('build', 'backend/AI.Assistant.Api/AI.Assistant.Api.csproj', '--no-restore', '--nologo', '-v:q') $taskRoot

$services = @()
$oldDataDirectory = [Environment]::GetEnvironmentVariable('Assistant__DataDirectory', 'Process')
try {
    $env:Assistant__DataDirectory = [IO.Path]::GetFullPath($DataDirectory)
    $backend = Start-Process -FilePath $dotnet -ArgumentList @('bin/Debug/net10.0/AI.Assistant.Api.dll') -WorkingDirectory (Join-Path $taskRoot 'backend/AI.Assistant.Api') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $runDirectory 'backend.log') -RedirectStandardError (Join-Path $runDirectory 'backend.error.log')
    $services += @{ name = 'backend'; id = $backend.Id; startedAt = $backend.StartTime.ToUniversalTime().ToString('o') }
    $frontend = Start-Process -FilePath $node -ArgumentList @('node_modules/vite/bin/vite.js', '--host', '127.0.0.1', '--port', '5188', '--strictPort') -WorkingDirectory (Join-Path $taskRoot 'frontend') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $runDirectory 'frontend.log') -RedirectStandardError (Join-Path $runDirectory 'frontend.error.log')
    $services += @{ name = 'frontend'; id = $frontend.Id; startedAt = $frontend.StartTime.ToUniversalTime().ToString('o') }
    @{ root = $taskRoot; dataDirectory = $env:Assistant__DataDirectory; services = $services } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $stateFile -Encoding UTF8
    $ready = $false
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        if ($backend.HasExited -or $frontend.HasExited) { throw 'AI.Assistant process exited; inspect .run/*.log.' }
        try {
            $health = Invoke-RestMethod 'http://127.0.0.1:5088/api/health' -TimeoutSec 2
            $page = Invoke-WebRequest 'http://127.0.0.1:5188' -TimeoutSec 2 -UseBasicParsing
            if ($health.application -eq 'AI.Assistant' -and $page.StatusCode -eq 200) { $ready = $true; break }
        } catch { Start-Sleep -Milliseconds 500 }
    }
    if (-not $ready) { throw 'AI.Assistant startup timed out; inspect .run/*.log.' }
    Write-Host 'AI.Assistant is ready: http://127.0.0.1:5188'
    Write-Host 'Backend: http://127.0.0.1:5088/api/health'
    Write-Host 'Open the workspace directly; no login is required.'
    Write-Host 'Stop: .\stop-dev.ps1'
} catch {
    foreach ($service in $services) { Stop-Process -Id $service.id -ErrorAction SilentlyContinue }
    if (Test-Path -LiteralPath $stateFile) { Remove-Item -LiteralPath $stateFile }
    throw
} finally {
    [Environment]::SetEnvironmentVariable('Assistant__DataDirectory', $oldDataDirectory, 'Process')
}
