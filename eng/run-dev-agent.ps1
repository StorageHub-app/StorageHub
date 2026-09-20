#Requires -Version 5.1
<#
.SYNOPSIS
    Runs the StorageHub agent from this working tree, for the desktop to talk to.

.DESCRIPTION
    The desktop finds the agent by a named pipe derived from the current account's SID, so an agent
    running as you is one the desktop can reach. Nothing has to be configured on either side.

    By default this uses the agent's normal data root, %PROGRAMDATA%\StorageHub, which is what an
    installed StorageHub would use: connections made here are the ones you will have. Pass
    -DataRoot to keep a separate database instead, which is worth doing when a test is about to
    write things you do not want to keep.

.PARAMETER DataRoot
    Somewhere other than %PROGRAMDATA%\StorageHub to keep the database, vault and runtime state.
    A separate root is a separate set of connections.

.PARAMETER Foreground
    Run in this window instead of in the background, which is what you want when it will not start
    and you need to read why.

.NOTES
    If the agent exits with "The StorageHub data directory could not be protected for the current
    user", the data root belongs to another account. That is what a pre-2.0 installation leaves
    behind: StorageHub used to run its agent as a machine-wide service under LocalSystem, which
    hardened that directory to itself. Clear it with eng/remove-legacy-agent-service.ps1.

.EXAMPLE
    ./eng/run-dev-agent.ps1
    Builds and starts the agent in the background, and prints the pipe the desktop will look for.

.EXAMPLE
    ./eng/run-dev-agent.ps1 -DataRoot "$env:LOCALAPPDATA\StorageHub.Scratch"
    The same, on a database of its own.
#>
[CmdletBinding()]
param(
    [string] $DataRoot,
    [switch] $Foreground
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repository 'src/StorageHub.Agent.Host/StorageHub.Agent.Host.csproj'

# Stopping first: the agent holds its own executable open, so a build while one is running fails
# with a file-in-use error that reads like a broken project rather than a running process.
$running = Get-Process -Name 'StorageHub.Agent.Host' -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "Stopping $(@($running).Count) running agent(s) from this tree."
    $running | Stop-Process -Force
    Start-Sleep -Seconds 1
}

Write-Host 'Building the agent host.'
dotnet build $project --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "The agent host did not build (exit $LASTEXITCODE)." }

$exe = Join-Path $repository 'src/StorageHub.Agent.Host/bin/Debug/net10.0/StorageHub.Agent.Host.exe'
if (-not (Test-Path $exe)) { throw "The agent host was not found at $exe." }

if ($DataRoot) {
    $env:STORAGEHUB_DATA_ROOT = $DataRoot
    $logDirectory = $DataRoot
    Write-Host "Data root: $DataRoot (a database of its own)"
} else {
    # Left unset rather than set to the same value: what the agent resolves is the agent's answer,
    # and repeating it here would be a second place for it to be wrong.
    Remove-Item Env:\STORAGEHUB_DATA_ROOT -ErrorAction SilentlyContinue
    $logDirectory = Join-Path $env:TEMP 'StorageHub.AgentHost'
    Write-Host "Data root: the agent's own ($(Join-Path $env:ProgramData 'StorageHub'))"
}

if ($Foreground) {
    & $exe
    exit $LASTEXITCODE
}

New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
$log = Join-Path $logDirectory 'agent-host.log'
$agent = Start-Process -FilePath $exe `
    -RedirectStandardOutput $log `
    -RedirectStandardError "$log.err" `
    -PassThru -WindowStyle Hidden

Start-Sleep -Seconds 3
if (-not (Get-Process -Id $agent.Id -ErrorAction SilentlyContinue)) {
    Write-Host 'The agent exited. Its last words:' -ForegroundColor Red
    if (Test-Path "$log.err") { Get-Content "$log.err" -Tail 20 }
    Write-Host 'Run again with -Foreground to watch it start.'
    exit 1
}

# Named from the account's SID, so this is also the check that the desktop will look for the same
# one: if it is missing, the agent is running but not listening where the desktop expects.
$pipes = [System.IO.Directory]::GetFiles('\\.\pipe\') | Where-Object { $_ -like '*StorageHub.Agent.v1.user-*' }
Write-Host "Agent running as process $($agent.Id)." -ForegroundColor Green
if ($pipes) {
    $pipes | ForEach-Object { Write-Host "  listening on $_" }
} else {
    Write-Host '  but no user pipe appeared, which the desktop will not find.' -ForegroundColor Yellow
}

Write-Host "Log: $log"
