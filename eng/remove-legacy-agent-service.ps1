#Requires -Version 5.1
<#
.SYNOPSIS
    Removes the pre-2.0 StorageHub agent service and the data root it owns.

.DESCRIPTION
    StorageHub used to run its agent as a machine-wide Windows service, which listened on
    "StorageHub.Agent.v1.machine" and kept its data under a root it hardened to LocalSystem. 2.0 has
    one mode: the agent runs as you, listens on a pipe named from your account's SID, and owns
    %PROGRAMDATA%\StorageHub as your account.

    An installation that still has the old service therefore collides with the new agent twice. The
    desktop no longer looks for the machine pipe, so nothing answers it; and the new agent cannot
    take the data root, because it is owned by LocalSystem and cannot be re-protected by a user.
    Its error says so and says nothing about a service: "The StorageHub data directory could not be
    protected for the current user."

    This removes both halves of that: the service, and the directories it owned.

.PARAMETER KeepData
    Remove the service but leave the data directories. The new agent still will not be able to use
    them, so this is for looking at what was there before removing it by hand.

.NOTES
    THIS DELETES THE OLD AGENT'S DATABASE AND VAULT. Saved connections and their stored credentials
    go with it. There is no migration: the old vault is protected with the machine's DPAPI key and
    the new one with yours, so the entries could not be read across the move even if the files were
    kept.

    Needs elevation, because deleting a service and a LocalSystem-owned directory both do.
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [switch] $KeepData
)

$ErrorActionPreference = 'Stop'

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'This needs to run elevated: deleting a service and a LocalSystem-owned directory both do.'
}

$serviceName = 'StorageHubAgent'
$dataRoots = @(
    (Join-Path $env:ProgramData 'StorageHub'),
    (Join-Path $env:ProgramData 'StorageHubAgent')
)

$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($service) {
    if ($PSCmdlet.ShouldProcess($serviceName, 'Stop and delete the service')) {
        if ($service.Status -ne 'Stopped') {
            Write-Host "Stopping $serviceName."
            Stop-Service -Name $serviceName -Force
            $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
        }

        # sc.exe rather than Remove-Service, which is PowerShell 6 and later; this script has to run
        # on the Windows PowerShell that every Windows machine already has.
        Write-Host "Deleting $serviceName."
        & sc.exe delete $serviceName | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "sc.exe delete returned $LASTEXITCODE." }
    }
} else {
    Write-Host "No $serviceName service is installed."
}

# The old agent's own process, if one is still running outside the service.
Get-Process -Name 'StorageHub.Agent.Windows' -ErrorAction SilentlyContinue | ForEach-Object {
    if ($PSCmdlet.ShouldProcess("process $($_.Id)", 'Stop the old agent')) {
        Write-Host "Stopping the old agent process $($_.Id)."
        $_ | Stop-Process -Force
    }
}

if ($KeepData) {
    Write-Host 'Leaving the data directories in place, as asked.'
    Write-Host 'The new agent will not be able to use them while LocalSystem owns them.'
    return
}

foreach ($root in $dataRoots) {
    if (-not (Test-Path -LiteralPath $root)) {
        Write-Host "Not there: $root"
        continue
    }

    if (-not $PSCmdlet.ShouldProcess($root, 'Delete the old agent data root')) { continue }

    # Ownership first. Administrators can take ownership of anything on the machine but do not
    # necessarily have access to it, and the old agent hardened these to LocalSystem specifically.
    Write-Host "Taking ownership of $root."
    & takeown.exe /F $root /R /D Y 2>&1 | Out-Null
    & icacls.exe $root /grant "*S-1-5-32-544:(OI)(CI)F" /T /C 2>&1 | Out-Null

    Write-Host "Deleting $root."
    Remove-Item -LiteralPath $root -Recurse -Force
}

Write-Host ''
Write-Host 'Done. The new agent will create its own data root as your account the next time it starts.' -ForegroundColor Green
Write-Host 'Start it with: .\eng\run-dev-agent.ps1'
