$ErrorActionPreference = 'Stop'
Push-Location -LiteralPath $PSScriptRoot
try {
    Push-Location -LiteralPath (Join-Path $PSScriptRoot 'frontend')
    try {
        & pnpm test
        if ($LASTEXITCODE -ne 0) { throw 'Frontend tests failed.' }
    } finally { Pop-Location }
    & dotnet test backend/AI.Assistant.Tests/AI.Assistant.Tests.csproj --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Backend tests failed.' }
} finally { Pop-Location }
