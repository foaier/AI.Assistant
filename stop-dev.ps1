$ErrorActionPreference = 'Stop'
$stateFile = Join-Path $PSScriptRoot '.run/services.json'
if (-not (Test-Path -LiteralPath $stateFile)) { Write-Host 'No managed SmartAssistant services.'; exit 0 }
$state = Get-Content -LiteralPath $stateFile -Raw | ConvertFrom-Json
if ([IO.Path]::GetFullPath($state.root) -ne [IO.Path]::GetFullPath($PSScriptRoot)) {
    throw 'Service state belongs to a different project directory.'
}
foreach ($service in $state.services) {
    $process = Get-Process -Id $service.id -ErrorAction SilentlyContinue
    if ($process -and $process.StartTime.ToUniversalTime().Ticks -eq ([datetimeoffset]$service.startedAt).UtcDateTime.Ticks) {
        Stop-Process -Id $service.id
        Write-Host "Stopped SmartAssistant $($service.name)."
    }
}
Remove-Item -LiteralPath $stateFile
