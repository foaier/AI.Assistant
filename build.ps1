$ErrorActionPreference = 'Stop'
Push-Location -LiteralPath $PSScriptRoot
try {
    Push-Location -LiteralPath (Join-Path $PSScriptRoot 'frontend')
    try {
        & pnpm install --frozen-lockfile
        if ($LASTEXITCODE -ne 0) { throw 'Frontend dependency install failed.' }
        & pnpm build
        if ($LASTEXITCODE -ne 0) { throw 'Frontend build failed.' }
    } finally { Pop-Location }
    & dotnet publish backend/SmartAssistant.Api/SmartAssistant.Api.csproj -c Release -o .run/publish --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Backend publish failed.' }
    Write-Host 'Built frontend/dist and .run/publish. See README.md for running the built application.'
} finally { Pop-Location }
