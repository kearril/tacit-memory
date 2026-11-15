#Requires -Version 7
<#
.SYNOPSIS
    Checks that a published tacit folder runs: the version, the service, the management API, the MCP tools, backup and
    restore, the CLI's own calls to the service and the web interface.

.DESCRIPTION
    Uses a throwaway TACIT_HOME and a free port, so it never touches the user's memory or a running service. It runs
    init, starts serve in the background, waits for it, then reads and writes through every kind of request: the
    management API's reads and saves, a page and an activity written and read back through MCP, a reindex, a backup and
    the preview of restoring it, and tacit status. The release is trimmed, so this is where a part it lost would show.
    It stops the service and removes the throwaway folder whatever happened. No auxiliary model is configured. Exits
    non-zero on the first failed check, after printing the service's output and log.

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

    # The release is trimmed and reads JSON only through generated contracts, so every kind of request and answer is
    # exercised once: a type that went missing fails here rather than for a user.
    function Api([string] $method, [string] $path, $body) {
        $arguments = @{ Uri = "$base/api/$path"; Method = $method }
        if ($null -ne $body) { $arguments.ContentType = 'application/json'; $arguments.Body = $body | ConvertTo-Json -Depth 20 -Compress }
        try { return Invoke-RestMethod @arguments } catch { Fail "$method /api/$path failed: $($_.ErrorDetails.Message ?? $_.Exception.Message)" }
    }

    $null = Api GET 'activity'
    $null = Api GET 'bindings'
    $null = Api GET 'backups'
    $null = Api GET 'openapi.json'
    $logs = Api GET 'logs'
    $null = Api GET "logs/$($logs.today)"
    $config = Api GET 'config'
    $null = Api PUT 'config' @{ hash = $config.hash; config = $config.file }
    $resident = (Api GET 'prompts').documents | Where-Object name -EQ 'resident'
    $null = Api POST 'prompts/resident/check' @{ text = $resident.text }
    $null = Api PUT 'prompts/resident' @{ hash = $resident.hash; text = $resident.text }
    Write-Host 'management API: ok'

    $rpc = 0
    function Mcp([string] $method, $params) {
        $request = @{ jsonrpc = '2.0'; id = ++$script:rpc; method = $method; params = $params } | ConvertTo-Json -Depth 20 -Compress
        $answer = Invoke-WebRequest "$base/mcp" -Method Post -ContentType 'application/json' -Body $request -Headers @{
            Accept                 = 'application/json, text/event-stream'
            'MCP-Protocol-Version' = '2025-11-25'
        }
        # The answer is one server-sent event; its data line is the JSON-RPC response.
        $data = ($answer.Content -split "`n" | Where-Object { $_.StartsWith('data:') } | Select-Object -First 1)
        if (-not $data) { Fail "$method returned no event" }
        $response = $data.Substring(5) | ConvertFrom-Json
        if ($response.error) { Fail "$method failed: $($response.error.message)" }
        return $response.result
    }
    function Tool([string] $name, $arguments) {
        $result = Mcp 'tools/call' @{ name = $name; arguments = $arguments }
        $text = ($result.content | ForEach-Object text) -join "`n"
        if ($result.isError) { Fail "$name refused: $text" }
        return $text
    }

    $tools = ((Mcp 'tools/list' @{}).tools.name | Sort-Object) -join ','
    if ($tools -ne 'recall,write_page,write_persona,write_recent') { Fail "tools/list returned '$tools'" }
    $null = Tool 'write_page' @{
        op       = 'create'; page = 'smoke/check'; title = 'Smoke check'; type = 'work'
        aliases  = @('smoke page', 'release check')
        sections = @(@{ name = 'Output'; body = 'The release package answers every kind of request.' })
    }
    $null = Tool 'write_recent' @{ text = 'The smoke check wrote a page.'; workstream = 'smoke' }
    $read = Tool 'recall' @{ page = 'smoke/check' }
    if ($read -notmatch 'every kind of request') { Fail "recall did not return the page it wrote: $read" }
    $null = Tool 'recall' @{ query = 'release package' }
    Write-Host "MCP tools: $tools"

    $null = Api POST 'reindex' @{}
    $backup = Api POST 'backup' @{ target = 'local' }
    $preview = Api POST 'restore' @{ path = $backup.location; preview = $true }
    if (-not $preview.memoryHead) { Fail 'the restore preview of the new backup names no commit' }
    Write-Host 'index, backup and restore preview: ok'

    & $tacit status | Out-Null
    if ($LASTEXITCODE -ne 0) { Fail "tacit status exited with $LASTEXITCODE" }
    Write-Host 'tacit status: ok'

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
