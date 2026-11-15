#Requires -Version 7
<#
.SYNOPSIS
    Checks that a published tacit folder runs: the version, the service, the management API, the MCP tools and the web
    interface.

.DESCRIPTION
    Uses a throwaway TACIT_HOME and a free port, so it never touches the user's memory or a running service. It runs
    init, starts serve in the background, waits for it, checks what it answers, then stops it and removes the throwaway
    folder whatever happened. No auxiliary model is configured. Exits non-zero on the first failed check, after printing
    the service's output and log.

.PARAMETER PublishDir
    The folder holding tacit.exe.

.PARAMETER ExpectedVersion
    The version tacit must report. Left out, any non-empty version passes.
#>
param(
    [Parameter(Mandatory)] [string] $PublishDir,
    [string] $ExpectedVersion
)

$ErrorActionPreference = 'Stop'

$tacit = Join-Path (Resolve-Path $PublishDir) 'tacit.exe'
$work = Join-Path ([System.IO.Path]::GetTempPath()) "tacit-smoke-$([guid]::NewGuid().ToString('n'))"
$tacitHome = Join-Path $work 'home'
$service = $null
$failed = $false

function Fail([string] $message) { throw "Smoke check failed: $message" }

function FreePort {
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    $listener.Start()
    try { return $listener.LocalEndpoint.Port } finally { $listener.Stop() }
}

try {
    New-Item -ItemType Directory -Force $work | Out-Null
    $env:TACIT_HOME = $tacitHome

    $version = "$(& $tacit --version)".Trim()
    if ($LASTEXITCODE -ne 0) { Fail "tacit --version exited with $LASTEXITCODE" }
    if (-not $version) { Fail 'tacit --version printed nothing' }
    if ($ExpectedVersion -and $version -ne $ExpectedVersion) { Fail "tacit --version printed '$version', expected '$ExpectedVersion'" }
    Write-Host "version: $version"

    & $tacit init | Out-Null
    if ($LASTEXITCODE -ne 0) { Fail "tacit init exited with $LASTEXITCODE" }
    $port = FreePort
    Add-Content (Join-Path $tacitHome 'config.yaml') "server:`n  port: $port`n"

    $service = Start-Process $tacit -ArgumentList 'serve' -PassThru -NoNewWindow `
        -RedirectStandardOutput (Join-Path $work 'serve.out') -RedirectStandardError (Join-Path $work 'serve.err')
    # Taking the handle now keeps the exit code readable once the process has exited.
    $null = $service.Handle
    $base = "http://127.0.0.1:$port"
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    $status = $null
    while (-not $status) {
        if ($service.HasExited) { Fail "tacit serve exited with $($service.ExitCode) before it answered" }
        if ([DateTime]::UtcNow -gt $deadline) { Fail 'tacit serve did not answer within 30 seconds' }
        try { $status = Invoke-RestMethod "$base/api/status" -TimeoutSec 2 } catch { Start-Sleep -Milliseconds 250 }
    }

    if (-not $status.service.running) { Fail '/api/status does not report the service as running' }
    if ($status.service.version -ne $version) { Fail "/api/status reports version '$($status.service.version)', the executable '$version'" }
    Write-Host 'management API: ok'

    $listing = Invoke-WebRequest "$base/mcp" -Method Post -ContentType 'application/json' -Headers @{
        Accept                 = 'application/json, text/event-stream'
        'MCP-Protocol-Version' = '2025-11-25'
    } -Body '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'
    # The answer is one server-sent event; its data line is the JSON-RPC response.
    $data = ($listing.Content -split "`n" | Where-Object { $_.StartsWith('data:') } | Select-Object -First 1)
    if (-not $data) { Fail 'tools/list returned no event' }
    $tools = (($data.Substring(5) | ConvertFrom-Json).result.tools.name | Sort-Object) -join ','
    if ($tools -ne 'recall,write_page,write_persona,write_recent') { Fail "tools/list returned '$tools'" }
    Write-Host "MCP tools: $tools"

    $page = Invoke-WebRequest "$base/ui/"
    if ($page.StatusCode -ne 200 -or $page.Content -notmatch '<html') { Fail '/ui/ did not return the web interface' }
    Write-Host 'web interface: ok'

    Write-Host 'Smoke checks passed.'
}
catch {
    Write-Host $_.Exception.Message -ForegroundColor Red
    foreach ($file in @((Join-Path $work 'serve.out'), (Join-Path $work 'serve.err')) + @(Get-ChildItem (Join-Path $tacitHome 'logs') -Filter '*.log' -ErrorAction SilentlyContinue | ForEach-Object FullName)) {
        if ($file -and (Test-Path $file)) { Write-Host "---- $file"; Get-Content $file | Write-Host }
    }
    $failed = $true
}
finally {
    if ($service -and -not $service.HasExited) { Stop-Process -Id $service.Id -Force; $service.WaitForExit() }
    Remove-Item Env:TACIT_HOME -ErrorAction SilentlyContinue
    if (Test-Path $work) { Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue }
}
if ($failed) { exit 1 }
