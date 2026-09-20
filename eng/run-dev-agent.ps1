#Requires -Version 5.1
<#
.SYNOPSIS
    Runs the StorageHub agent from this working tree, for the desktop to talk to.

.DESCRIPTION
    The desktop finds the agent by a named pipe derived from the current account's SID, so a
    development agent only has to be running as you for the desktop to reach it. What it also needs
    is somewhere it is allowed to write.

    By default the agent owns %PROGRAMDATA%\StorageHub and hardens it to the account that created
    it. A machine that has ever run the installed service therefore has that directory owned by
    LocalSystem, and an agent started from a working tree cannot re-protect it -- it exits with
    "The StorageHub data directory could not be protected for the current user." So this points the
    agent at a development root under LOCALAPPDATA instead, which leaves the installed service and
    its data untouched.

    The development root is a separate database: connections made here are not the ones the
    installed service has. That is the point -- it is a working tree, not an installation.

.PARAMETER DataRoot
    Where the development agent keeps its database, vault and runtime state.

.PARAMETER Foreground
    Run in this window instead of in the background, which is what you want when it will not start
    and you need to read why.

.EXAMPLE
    ./eng/run-dev-agent.ps1
    Builds and starts the agent in the background, and prints the pipe the desktop will look for.
#>
[CmdletBinding()]
param(
    [string] $DataRoot = (Join-Path $env:LOCALAPPDATA 'StorageHub.Dev'),
    [switch] $Foreground
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repository 'src/StorageHub.Agent.Host/StorageHub.Agent.Host.csproj'

# Stopping first: the agent holds its own executable open, so a build while one is running fails
# with a file-in-use error that reads like a broken project rather than a running process.
$running = Get-Process -Name 'StorageHub.Agent.Host' -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "Stopping $($running.Count) running development agent(s)."
    $running | Stop-Process -Force
    Start-Sleep -Seconds 1
}

Write-Host 'Building the agent host.'
dotnet build $project --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "The agent host did not build (exit $LASTEXITCODE)." }

$exe = Join-Path $repository 'src/StorageHub.Agent.Host/bin/Debug/net10.0/StorageHub.Agent.Host.exe'
if (-not (Test-Path $exe)) { throw "The agent host was not found at $exe." }

$env:STORAGEHUB_DATA_ROOT = $DataRoot
Write-Host "Data root: $DataRoot"

if ($Foreground) {
    & $exe
    exit $LASTEXITCODE
}

$log = Join-Path $DataRoot 'agent-host.log'
New-Item -ItemType Directory -Force -Path $DataRoot | Out-Null
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

# Named after the account's SID, so this is also the check that the desktop will look for the same
# one: if it is missing, the agent is running but not listening where the desktop expects.
$pipes = [System.IO.Directory]::GetFiles('\\.\pipe\') | Where-Object { $_ -like '*StorageHub.Agent.v1.user-*' }
Write-Host "Agent running as process $($agent.Id)." -ForegroundColor Green
if ($pipes) {
    $pipes | ForEach-Object { Write-Host "  listening on $_" }
} else {
    Write-Host '  but no user pipe appeared, which the desktop will not find.' -ForegroundColor Yellow
}

Write-Host "Log: $log"
