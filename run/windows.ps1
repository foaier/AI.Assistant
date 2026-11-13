<#
    为 run 目录中的批处理入口提供前后端启动、停止、构建和测试能力。
    所有路径均以项目目录为基准，兼容 Windows PowerShell 5.1。
#>
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Start', 'Stop', 'Build', 'Test')]
    [string]$Action,
    [switch]$SkipInstall,
    [string]$DataDirectory
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$frontendDirectory = Join-Path $projectRoot 'frontend'
$backendDirectory = Join-Path $projectRoot 'backend/AI.Assistant.Api'
$runtimeDirectory = Join-Path $projectRoot '.run'
$stateFile = Join-Path $runtimeDirectory 'services.json'
$backendProject = 'backend/AI.Assistant.Api/AI.Assistant.Api.csproj'

function Invoke-Checked {
    <#
        在指定目录执行命令；命令失败时立即终止当前操作。
    #>
    param([string]$Command, [string[]]$Arguments, [string]$Directory)
    Push-Location -LiteralPath $Directory
    try {
        & $Command @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "$Command 执行失败，退出码：$LASTEXITCODE"
        }
    } finally {
        Pop-Location
    }
}

function Read-ServiceState {
    <#
        读取本项目的进程记录，拒绝操作其他项目留下的状态文件。
    #>
    if (Test-Path -LiteralPath $stateFile) {
        $state = Get-Content -LiteralPath $stateFile -Raw | ConvertFrom-Json
        if ([IO.Path]::GetFullPath($state.root) -ne $projectRoot) {
            throw '进程记录属于其他项目，请检查 .run/services.json。'
        }
        return $state
    }
}

function Get-ManagedProcess {
    <#
        同时核对进程编号和启动时间，避免结束重复使用相同编号的其他进程。
    #>
    param($Service)
    $process = Get-Process -Id $Service.id -ErrorAction SilentlyContinue
    if ($process -and
        $process.StartTime.ToUniversalTime().Ticks -eq ([datetimeoffset]$Service.startedAt).UtcDateTime.Ticks) {
        return $process
    }
}

function Stop-ManagedServices {
    <#
        只停止本项目记录且身份匹配的进程，数据目录和日志均予以保留。
    #>
    param($Services)
    foreach ($service in $Services) {
        $process = Get-ManagedProcess $service
        if ($process) {
            Stop-Process -InputObject $process -ErrorAction Stop
            Write-Host "已停止 AI.Assistant $($service.name)。"
        }
    }
}

function Start-Services {
    <#
        检查端口和依赖，启动后端与 Vite，健康检查成功后输出访问地址。
        启动失败时清理本次创建的进程，避免遗留只运行一半的服务。
    #>
    $previous = Read-ServiceState
    foreach ($service in $previous.services) {
        if (Get-ManagedProcess $service) {
            throw '项目已启动，请先执行 run\stop.bat。'
        }
    }
    foreach ($port in @(5088, 5188)) {
        if ([Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners().Port -contains $port) {
            throw "端口 $port 已被占用，请先停止占用端口的服务。"
        }
    }
    $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
    $node = (Get-Command node -ErrorAction Stop).Source
    if (-not $SkipInstall) {
        $pnpm = (Get-Command pnpm.cmd -ErrorAction Stop).Source
        Invoke-Checked $pnpm @('install', '--frozen-lockfile') $frontendDirectory
        Invoke-Checked $dotnet @('restore', $backendProject, '--nologo') $projectRoot
    }
    Invoke-Checked $dotnet @('build', $backendProject, '--no-restore', '--nologo', '-v:q') $projectRoot

    if ([string]::IsNullOrWhiteSpace($DataDirectory)) {
        $resolvedDataDirectory = Join-Path $projectRoot 'data'
    } elseif ([IO.Path]::IsPathRooted($DataDirectory)) {
        $resolvedDataDirectory = [IO.Path]::GetFullPath($DataDirectory)
    } else {
        $resolvedDataDirectory = [IO.Path]::GetFullPath((Join-Path $projectRoot $DataDirectory))
    }
    New-Item -ItemType Directory -Path $runtimeDirectory -Force | Out-Null
    $services = @()
    $oldDataDirectory = [Environment]::GetEnvironmentVariable('Assistant__DataDirectory', 'Process')
    try {
        $env:Assistant__DataDirectory = $resolvedDataDirectory
        $backend = Start-Process -FilePath $dotnet `
            -ArgumentList @('bin/Debug/net10.0/AI.Assistant.Api.dll', '--urls', 'http://127.0.0.1:5088') `
            -WorkingDirectory $backendDirectory -WindowStyle Hidden -PassThru `
            -RedirectStandardOutput (Join-Path $runtimeDirectory 'backend.log') `
            -RedirectStandardError (Join-Path $runtimeDirectory 'backend.error.log')
        $services += @{ name = 'backend'; id = $backend.Id; startedAt = $backend.StartTime.ToString('o') }
        $frontend = Start-Process -FilePath $node `
            -ArgumentList @('node_modules/vite/bin/vite.js', '--host', '127.0.0.1', '--port', '5188', '--strictPort') `
            -WorkingDirectory $frontendDirectory -WindowStyle Hidden -PassThru `
            -RedirectStandardOutput (Join-Path $runtimeDirectory 'frontend.log') `
            -RedirectStandardError (Join-Path $runtimeDirectory 'frontend.error.log')
        $services += @{ name = 'frontend'; id = $frontend.Id; startedAt = $frontend.StartTime.ToString('o') }
        @{ root = $projectRoot; dataDirectory = $resolvedDataDirectory; services = $services } |
            ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $stateFile -Encoding UTF8

        $ready = $false
        for ($attempt = 0; $attempt -lt 60; $attempt++) {
            $backend.Refresh()
            $frontend.Refresh()
            if ($backend.HasExited -or $frontend.HasExited) {
                throw '前后端进程提前退出，请查看 .run 目录中的日志。'
            }
            try {
                $health = Invoke-RestMethod 'http://127.0.0.1:5088/api/health' -TimeoutSec 2
                $page = Invoke-WebRequest 'http://127.0.0.1:5188' -TimeoutSec 2 -UseBasicParsing
                if ($health.application -eq 'AI.Assistant' -and $page.StatusCode -eq 200) {
                    $ready = $true
                    break
                }
            } catch {
                Start-Sleep -Milliseconds 500
            }
        }
        if (-not $ready) { throw '启动超时，请查看 .run 目录中的日志。' }
        Write-Host 'AI.Assistant 前后端已启动：http://127.0.0.1:5188'
        Write-Host '后端健康检查：http://127.0.0.1:5088/api/health'
        Write-Host '停止服务：run\stop.bat'
    } catch {
        Stop-ManagedServices $services
        if (Test-Path -LiteralPath $stateFile) { Remove-Item -LiteralPath $stateFile }
        throw
    } finally {
        [Environment]::SetEnvironmentVariable('Assistant__DataDirectory', $oldDataDirectory, 'Process')
    }
}

try {
    switch ($Action) {
        'Start' { Start-Services }
        'Stop' {
            $state = Read-ServiceState
            if ($state) {
                Stop-ManagedServices $state.services
                Remove-Item -LiteralPath $stateFile
            } else {
                Write-Host '没有本项目启动的前后端进程。'
            }
        }
        'Build' {
            $pnpm = (Get-Command pnpm.cmd -ErrorAction Stop).Source
            $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
            if (-not $SkipInstall) {
                Invoke-Checked $pnpm @('install', '--frozen-lockfile') $frontendDirectory
            }
            Invoke-Checked $pnpm @('build') $frontendDirectory
            Invoke-Checked $dotnet @('publish', $backendProject, '-c', 'Release', '-o', '.run/publish', '--nologo') $projectRoot
            Write-Host '构建完成：frontend/dist 和 .run/publish。'
        }
        'Test' {
            $pnpm = (Get-Command pnpm.cmd -ErrorAction Stop).Source
            $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
            if (-not $SkipInstall) {
                Invoke-Checked $pnpm @('install', '--frozen-lockfile') $frontendDirectory
            }
            Invoke-Checked $pnpm @('test') $frontendDirectory
            Invoke-Checked $dotnet @('test', 'backend/AI.Assistant.Tests/AI.Assistant.Tests.csproj', '--nologo') $projectRoot
        }
    }
    exit 0
} catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
