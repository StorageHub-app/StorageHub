#Requires -Version 5.1

<#
.SYNOPSIS
Installs the release MSI for the current user, checks what it installed and registered, and
uninstalls it again, keeping the data.

.DESCRIPTION
Changes this user's installed programs, sign-in entry and Explorer registration, so it refuses to
run outside CI unless -AllowOutsideCi and -ConfirmDisposableRunner are both given, and refuses on
any account where StorageHub is already installed or registered at sign-in. The package is
per-user and needs no elevation. With -PreviousBundleRoot the earlier MSI is installed first, so
the upgrade, its hooks and its kept sign-in entry are what is tested.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $BundleRoot,

    # A bundle from an earlier version, installed first so that this one is tested as the upgrade.
    [string] $PreviousBundleRoot,

    [switch] $AllowOutsideCi,

    [switch] $ConfirmDisposableRunner,

    [ValidateRange(30, 600)]
    [int] $ProcessTimeoutSeconds = 180
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Test-TrueEnvironmentValue {
    param(
        [Parameter(Mandatory)]
        [string] $Name
    )

    $value = [System.Environment]::GetEnvironmentVariable($Name)
    return $value -in @('1', 'true', 'yes')
}

function ConvertTo-WindowsCommandLineArgument {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [string] $Value
    )

    if ($Value.Length -gt 0 -and $Value -notmatch '[\s"]') {
        return $Value
    }

    $builder = [System.Text.StringBuilder]::new()
    [void] $builder.Append([char] 34)
    $backslashCount = 0
    foreach ($character in $Value.ToCharArray()) {
        if ($character -eq [char] 92) {
            $backslashCount++
            continue
        }

        if ($character -eq [char] 34) {
            [void] $builder.Append([char] 92, ($backslashCount * 2) + 1)
            [void] $builder.Append([char] 34)
            $backslashCount = 0
            continue
        }

        if ($backslashCount -gt 0) {
            [void] $builder.Append([char] 92, $backslashCount)
            $backslashCount = 0
        }
        [void] $builder.Append($character)
    }

    if ($backslashCount -gt 0) {
        [void] $builder.Append([char] 92, $backslashCount * 2)
    }
    [void] $builder.Append([char] 34)
    return $builder.ToString()
}

function Invoke-CheckedProcess {
    param(
        [Parameter(Mandatory)]
        [string] $FilePath,

        [Parameter(Mandatory)]
        [string[]] $ArgumentList,

        [Parameter(Mandatory)]
        [hashtable] $EnvironmentVariables,

        [Parameter(Mandatory)]
        [string] $Description,

        [Parameter(Mandatory)]
        [int] $TimeoutSeconds
    )

    Write-Host "==> $Description"
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $quotedArguments = @(
        $ArgumentList | ForEach-Object { ConvertTo-WindowsCommandLineArgument -Value $_ }
    )
    $startInfo.Arguments = [string]::Join(' ', $quotedArguments)
    foreach ($name in $EnvironmentVariables.Keys) {
        $startInfo.EnvironmentVariables[$name] = [string] $EnvironmentVariables[$name]
    }

    $process = [System.Diagnostics.Process]::Start($startInfo)
    if ($null -eq $process) {
        throw "Could not start $Description."
    }

    try {
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            try {
                $process.Kill()
            }
            catch {
                Write-Warning "Could not terminate timed-out process $($process.Id): $($_.Exception.Message)"
            }
            throw "$Description did not exit within $TimeoutSeconds seconds."
        }

        if ($process.ExitCode -ne 0) {
            throw "$Description failed with exit code $($process.ExitCode)."
        }
    }
    finally {
        $process.Dispose()
    }
}

function Wait-ForCondition {
    param(
        [Parameter(Mandatory)]
        [scriptblock] $Condition,

        [Parameter(Mandatory)]
        [int] $TimeoutSeconds,

        [Parameter(Mandatory)]
        [string] $FailureMessage
    )

    $deadline = [System.DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        if (& $Condition) {
            return
        }
        Start-Sleep -Milliseconds 250
    } while ([System.DateTimeOffset]::UtcNow -lt $deadline)

    throw $FailureMessage
}

function Assert-SeparateDirectoryTrees {
    param(
        [Parameter(Mandatory)]
        [string] $FirstPath,

        [Parameter(Mandatory)]
        [string] $SecondPath,

        [Parameter(Mandatory)]
        [string] $Description
    )

    $first = [System.IO.Path]::GetFullPath($FirstPath).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    $second = [System.IO.Path]::GetFullPath($SecondPath).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    $firstPrefix = $first + [System.IO.Path]::DirectorySeparatorChar
    $secondPrefix = $second + [System.IO.Path]::DirectorySeparatorChar
    if ($first.Equals($second, [System.StringComparison]::OrdinalIgnoreCase) -or
        $first.StartsWith($secondPrefix, [System.StringComparison]::OrdinalIgnoreCase) -or
        $second.StartsWith($firstPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "$Description must be distinct, non-nested directory trees."
    }
}

function Get-StorageHubAutoStartEntries {
    $entries = [System.Collections.Generic.List[string]]::new()
    foreach ($registryPath in @(
            'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run',
            'HKCU:\Software\Microsoft\Windows\CurrentVersion\RunOnce')) {
        if (-not (Test-Path -LiteralPath $registryPath)) {
            continue
        }

        $properties = Get-ItemProperty -LiteralPath $registryPath
        if ($null -eq $properties) {
            continue
        }

        foreach ($property in $properties.PSObject.Properties) {
            if ($property.Name.StartsWith('PS', [System.StringComparison]::Ordinal)) {
                continue
            }

            $entry = "$registryPath::$($property.Name)=$($property.Value)"
            if ($entry.IndexOf('StorageHub', [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
                $entries.Add($entry)
            }
        }
    }

    return @($entries | Sort-Object)
}

function Get-ProcessIdsByExecutablePath {
    param(
        [Parameter(Mandatory)]
        [string] $ExecutablePath
    )

    $expectedPath = [System.IO.Path]::GetFullPath($ExecutablePath)
    $processName = [System.IO.Path]::GetFileNameWithoutExtension($expectedPath)
    $processIds = [System.Collections.Generic.List[int]]::new()
    foreach ($process in Get-Process -Name $processName -ErrorAction SilentlyContinue) {
        try {
            if (-not $process.HasExited -and
                [string]::Equals(
                    [System.IO.Path]::GetFullPath($process.MainModule.FileName),
                    $expectedPath,
                    [System.StringComparison]::OrdinalIgnoreCase)) {
                $processIds.Add($process.Id)
            }
        }
        catch {
            # An inaccessible or exiting process is not the packaged Agent.
        }
        finally {
            $process.Dispose()
        }
    }

    return @($processIds)
}

function Test-ProcessHasExited {
    param(
        [Parameter(Mandatory)]
        [int] $ProcessId
    )

    try {
        $process = [System.Diagnostics.Process]::GetProcessById($ProcessId)
    }
    catch [System.ArgumentException] {
        return $true
    }

    try {
        return $process.HasExited
    }
    finally {
        $process.Dispose()
    }
}

function Get-ReleaseMsi {
    param(
        [Parameter(Mandatory)]
        [string] $Root
    )

    $bundleFullPath = if ([System.IO.Path]::IsPathRooted($Root)) {
        [System.IO.Path]::GetFullPath($Root)
    }
    else {
        [System.IO.Path]::GetFullPath((Join-Path (Get-Location).Path $Root))
    }
    if (-not (Test-Path -LiteralPath $bundleFullPath -PathType Container)) {
        throw "Release bundle '$bundleFullPath' does not exist."
    }
    $nestedBundleFiles = @(
        Get-ChildItem -LiteralPath $bundleFullPath -File -Recurse |
            Where-Object { $_.DirectoryName -cne $bundleFullPath }
    )
    if ($nestedBundleFiles.Count -ne 0) {
        throw "Release bundle '$bundleFullPath' is not flat."
    }

    # The MSI is the one Windows installer: a bundle that still carries Velopack's Setup.exe or
    # a portable ZIP was not built by this repository's packaging.
    foreach ($retired in @('*-Setup.exe', '*-portable.zip', '*.nupkg', 'RELEASES*', 'assets.*.json')) {
        if (@(Get-ChildItem -LiteralPath $bundleFullPath -Filter $retired -File).Count -ne 0) {
            throw "Release bundle '$bundleFullPath' carries a retired installer artifact matching '$retired'."
        }
    }
    $installerCandidates = @(Get-ChildItem -LiteralPath $bundleFullPath -Filter '*.msi' -File)
    if ($installerCandidates.Count -ne 1) {
        throw "Release bundle must contain exactly one MSI; found $($installerCandidates.Count)."
    }
    $installer = $installerCandidates[0]

    $checksumsPath = Join-Path $bundleFullPath 'SHA256SUMS'
    if (-not (Test-Path -LiteralPath $checksumsPath -PathType Leaf)) {
        throw 'Release bundle does not contain SHA256SUMS.'
    }
    $installerHashes = @(
        foreach ($line in Get-Content -LiteralPath $checksumsPath) {
            if ($line -match '^(?<hash>[0-9A-Fa-f]{64})\s+(?:\*)?(?<name>.+)$' -and
                $Matches.name.Trim() -ceq $installer.Name) {
                $Matches.hash
            }
        }
    )
    if ($installerHashes.Count -ne 1) {
        throw 'SHA256SUMS must contain exactly one entry for the MSI.'
    }
    $actualInstallerHash = (Get-FileHash -LiteralPath $installer.FullName -Algorithm SHA256).Hash
    if ($actualInstallerHash -cne $installerHashes[0].ToUpperInvariant()) {
        throw 'The MSI does not match SHA256SUMS.'
    }

    $buildInfoPath = Join-Path $bundleFullPath 'BUILDINFO.json'
    if (-not (Test-Path -LiteralPath $buildInfoPath -PathType Leaf)) {
        throw 'Release bundle does not contain BUILDINFO.json.'
    }
    try {
        $buildInfo = Get-Content -LiteralPath $buildInfoPath -Raw | ConvertFrom-Json
    }
    catch {
        throw 'Release bundle BUILDINFO.json is invalid.'
    }
    if ($buildInfo.packId -cne 'StorageHub.Desktop' -or $buildInfo.rid -cnotin @('win-x64', 'win-arm64')) {
        throw 'Release bundle does not use the immutable StorageHub.Desktop package identity.'
    }

    # Each architecture is exercised on its own kind of machine. ARM64 Windows would install the
    # x64 package too, under emulation, and passing that way would say nothing about the ARM64 one.
    $machine = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
    if ($buildInfo.rid -cne "win-$machine") {
        throw "The bundle is for $($buildInfo.rid), but this machine is win-$machine."
    }
    if ($installer.Name -cne "StorageHub-$($buildInfo.version)-$($buildInfo.rid).msi") {
        throw "The MSI is named '$($installer.Name)', which is not the name the updater looks for."
    }

    return $installer.FullName
}

function Invoke-Msiexec {
    param(
        [Parameter(Mandatory)]
        [string[]] $ArgumentList,

        [Parameter(Mandatory)]
        [string] $LogPath,

        [Parameter(Mandatory)]
        [string] $Description
    )

    # Quiet, and never a reboot. The package is per-user, so none of this asks for elevation.
    Invoke-CheckedProcess `
        -FilePath (Join-Path $env:SystemRoot 'System32\msiexec.exe') `
        -ArgumentList ($ArgumentList + @('/qn', '/norestart', '/l*v', $LogPath)) `
        -EnvironmentVariables $childEnvironment `
        -Description $Description `
        -TimeoutSeconds $ProcessTimeoutSeconds
}

function Get-RunEntry {
    $runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    if (-not (Test-Path -LiteralPath $runKey)) {
        return $null
    }

    # Read through the key rather than as a property: under strict mode a value that is not there
    # throws "property cannot be found" instead of giving null, which a clean runner always hits.
    return (Get-Item -LiteralPath $runKey).GetValue('StorageHub.Agent')
}

function Test-StorageHubInstalledHere {
    $key = 'HKCU:\Software\StorageHub\Installer'
    if (-not (Test-Path -LiteralPath $key)) {
        return $false
    }

    $folder = (Get-Item -LiteralPath $key).GetValue('InstallFolder')
    return $null -ne $folder -and
        [string]::Equals(
            ([string] $folder).TrimEnd('\'),
            $installDirectory.TrimEnd('\'),
            [System.StringComparison]::OrdinalIgnoreCase)
}

if ([System.Environment]::OSVersion.Platform -ne [System.PlatformID]::Win32NT) {
    throw 'The Windows installer smoke test can only run on Windows.'
}

$isCi = (Test-TrueEnvironmentValue -Name 'CI') -or
    (Test-TrueEnvironmentValue -Name 'GITHUB_ACTIONS')
if (-not $isCi -and -not $AllowOutsideCi) {
    throw 'Installer execution is refused outside CI. Pass -AllowOutsideCi to acknowledge local system changes explicitly.'
}

$runnerEnvironment = [System.Environment]::GetEnvironmentVariable('RUNNER_ENVIRONMENT')
$isGithubHostedRunner = (Test-TrueEnvironmentValue -Name 'GITHUB_ACTIONS') -and
    [string]::Equals(
        $runnerEnvironment,
        'github-hosted',
        [System.StringComparison]::OrdinalIgnoreCase)
$isConfirmedDisposableRunner = $ConfirmDisposableRunner -or
    (Test-TrueEnvironmentValue -Name 'STORAGEHUB_DISPOSABLE_RUNNER') -or
    $isGithubHostedRunner
if (-not $isConfirmedDisposableRunner) {
    throw 'Installer execution is refused on a non-disposable worker. Use a GitHub-hosted runner or explicitly confirm an isolated test machine.'
}

# An installed StorageHub, or one already registered at sign-in, is somebody's real installation:
# installing over it would upgrade it and uninstalling would remove it.
if ((Test-Path -LiteralPath 'HKCU:\Software\StorageHub\Installer') -or $null -ne (Get-RunEntry)) {
    throw 'StorageHub is already installed or registered at sign-in for this user; refusing to touch it.'
}

$installerFullPath = Get-ReleaseMsi -Root $BundleRoot
$previousInstallerFullPath = if ([string]::IsNullOrWhiteSpace($PreviousBundleRoot)) {
    $null
}
else {
    Get-ReleaseMsi -Root $PreviousBundleRoot
}

$smokeRoot = Join-Path `
    ([System.IO.Path]::GetTempPath()) `
    ("StorageHub-installer-smoke-" + [System.Guid]::NewGuid().ToString('N'))
$installDirectory = Join-Path $smokeRoot 'Install'
$dataRoot = Join-Path $smokeRoot 'Data'
$logRoot = Join-Path $smokeRoot 'Logs'
$sentinelPath = Join-Path $dataRoot 'uninstall-preservation.sentinel'
$sentinelContent = [System.Guid]::NewGuid().ToString('D')

Assert-SeparateDirectoryTrees `
    -FirstPath $installDirectory `
    -SecondPath $dataRoot `
    -Description 'Installer smoke-test program and configured data directories'
$defaultDataRoot = Join-Path `
    ([System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::LocalApplicationData)) `
    'StorageHub'
Assert-SeparateDirectoryTrees `
    -FirstPath $installDirectory `
    -SecondPath $defaultDataRoot `
    -Description 'Installer program and default durable data directories'

$autoStartBefore = @(Get-StorageHubAutoStartEntries)

New-Item -ItemType Directory -Path $dataRoot -Force | Out-Null
New-Item -ItemType Directory -Path $logRoot -Force | Out-Null
[System.IO.File]::WriteAllText(
    $sentinelPath,
    $sentinelContent,
    [System.Text.UTF8Encoding]::new($false))

# The data root keeps the installed agent away from this user's real data. The sign-in entry is
# not switched off: registering it on install, and taking it away on uninstall, is part of what is
# under test.
$childEnvironment = @{
    STORAGEHUB_DATA_ROOT = $dataRoot
}
$installAttempted = $false
$uninstallAttempted = $false
$completed = $false
$liveAgentProcessId = $null
$brokerRegistered = $false
$brokerClassId = '{D7AE012A-EC7C-4CC3-AD34-7EE7155518CE}'
$brokerClassKey = "HKCU:\Software\Classes\CLSID\$brokerClassId"
$brokerHandlerKey = 'HKCU:\Software\Classes\Directory\shellex\CopyHookHandlers\StorageHub'
$desktopExe = Join-Path $installDirectory 'StorageHub.Desktop.exe'
$agentExe = Join-Path $installDirectory 'Agent\StorageHub.Agent.Host.exe'
$startMenuShortcut = Join-Path `
    ([System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::Programs)) `
    'StorageHub.lnk'
$expectedRunEntry = '"{0}" --agent-only' -f $desktopExe

try {
    $installAttempted = $true
    if ($null -ne $previousInstallerFullPath) {
        Invoke-Msiexec `
            -ArgumentList @('/i', $previousInstallerFullPath, "INSTALLFOLDER=$installDirectory\") `
            -LogPath (Join-Path $logRoot 'install-previous.log') `
            -Description 'Silently install the previous StorageHub'
        if (-not (Test-Path -LiteralPath $desktopExe -PathType Leaf)) {
            throw 'The previous StorageHub did not install its desktop.'
        }
    }

    Invoke-Msiexec `
        -ArgumentList @('/i', $installerFullPath, "INSTALLFOLDER=$installDirectory\") `
        -LogPath (Join-Path $logRoot 'install.log') `
        -Description $(if ($null -ne $previousInstallerFullPath) { 'Silently upgrade StorageHub' } else { 'Silently install StorageHub' })

    Wait-ForCondition `
        -Condition {
            (Test-Path -LiteralPath $desktopExe -PathType Leaf) -and
            (Test-Path -LiteralPath $agentExe -PathType Leaf)
        } `
        -TimeoutSeconds 30 `
        -FailureMessage 'The installed Desktop or Agent executable was not found.'

    foreach ($requiredPayload in @(
            (Join-Path $installDirectory 'coreclr.dll'),
            (Join-Path $installDirectory 'StorageHub.ShellExtension.Native.dll'),
            (Join-Path $installDirectory 'Agent\coreclr.dll'),
            (Join-Path $installDirectory 'BUILDINFO.json'),
            (Join-Path $installDirectory 'release-version.txt'),
            (Join-Path $installDirectory 'LICENSE'),
            (Join-Path $installDirectory 'README.md'))) {
        if (-not (Test-Path -LiteralPath $requiredPayload -PathType Leaf)) {
            throw "Installed payload is missing '$requiredPayload'."
        }
    }
    if (@(Get-ChildItem -LiteralPath $installDirectory -Filter '*.pdb' -File -Recurse).Count -ne 0) {
        throw 'The installed payload contains program database symbols.'
    }
    if (-not (Test-StorageHubInstalledHere)) {
        throw 'The MSI did not record its install folder, so the installed desktop could not update itself.'
    }
    if (-not (Test-Path -LiteralPath $startMenuShortcut -PathType Leaf)) {
        throw 'The MSI did not add the Start menu shortcut.'
    }

    # The after-install hook registers the sign-in entry, as 1.4's install did; an upgrade keeps it.
    if ((Get-RunEntry) -cne $expectedRunEntry) {
        throw "The sign-in entry is '$(Get-RunEntry)', not '$expectedRunEntry'."
    }

    $brokerDll = Join-Path $installDirectory 'StorageHub.ShellExtension.Native.dll'
    $regsvr32 = Join-Path $env:SystemRoot 'System32\regsvr32.exe'
    Invoke-CheckedProcess `
        -FilePath $regsvr32 `
        -ArgumentList @('/s', $brokerDll) `
        -EnvironmentVariables $childEnvironment `
        -Description 'Register the installed Explorer drop broker' `
        -TimeoutSeconds $ProcessTimeoutSeconds
    $brokerRegistered = $true
    if (-not (Test-Path -LiteralPath $brokerClassKey) -or
        -not (Test-Path -LiteralPath $brokerHandlerKey)) {
        throw 'The installed Explorer drop broker did not create its current-user registration.'
    }

    Invoke-CheckedProcess `
        -FilePath $agentExe `
        -ArgumentList @('--run-once') `
        -EnvironmentVariables $childEnvironment `
        -Description 'Run the installed Agent once' `
        -TimeoutSeconds $ProcessTimeoutSeconds
    Invoke-CheckedProcess `
        -FilePath $agentExe `
        -ArgumentList @('--health') `
        -EnvironmentVariables $childEnvironment `
        -Description 'Run the installed Agent health check' `
        -TimeoutSeconds $ProcessTimeoutSeconds

    Invoke-CheckedProcess `
        -FilePath $desktopExe `
        -ArgumentList @('--agent-only') `
        -EnvironmentVariables $childEnvironment `
        -Description 'Start the packaged Agent as the sign-in entry does' `
        -TimeoutSeconds $ProcessTimeoutSeconds
    Wait-ForCondition `
        -Condition {
            @(Get-ProcessIdsByExecutablePath -ExecutablePath $agentExe).Count -eq 1
        } `
        -TimeoutSeconds 20 `
        -FailureMessage 'The sign-in launch did not leave one packaged Agent running.'
    $liveAgentProcessIds = @(Get-ProcessIdsByExecutablePath -ExecutablePath $agentExe)
    if ($liveAgentProcessIds.Count -ne 1) {
        throw "Expected one live packaged Agent; found $($liveAgentProcessIds.Count)."
    }
    $liveAgentProcessId = $liveAgentProcessIds[0]

    # The before-uninstall hook stops that agent, takes the sign-in entry and the drop broker's
    # registration away, and leaves the data alone.
    $uninstallAttempted = $true
    Invoke-Msiexec `
        -ArgumentList @('/x', $installerFullPath) `
        -LogPath (Join-Path $logRoot 'uninstall.log') `
        -Description 'Silently uninstall StorageHub'

    Wait-ForCondition `
        -Condition {
            -not (Test-Path -LiteralPath $desktopExe) -and
            -not (Test-Path -LiteralPath $agentExe) -and
            (Test-ProcessHasExited -ProcessId $liveAgentProcessId)
        } `
        -TimeoutSeconds 45 `
        -FailureMessage 'StorageHub binaries or the live packaged Agent remain after silent uninstall.'

    if (-not (Test-Path -LiteralPath $sentinelPath -PathType Leaf)) {
        throw 'The uninstall removed the isolated StorageHub data directory.'
    }
    if ((Get-Content -LiteralPath $sentinelPath -Raw) -ne $sentinelContent) {
        throw 'The uninstall changed the isolated StorageHub data sentinel.'
    }

    $autoStartAfterUninstall = @(Get-StorageHubAutoStartEntries)
    if ([string]::Join("`n", $autoStartAfterUninstall) -ne
        [string]::Join("`n", $autoStartBefore)) {
        throw 'StorageHub autostart registry state was not restored after uninstall.'
    }
    if ((Test-Path -LiteralPath $brokerClassKey) -or
        (Test-Path -LiteralPath $brokerHandlerKey)) {
        throw 'StorageHub Explorer integration remained registered after uninstall.'
    }
    $brokerRegistered = $false
    if ((Test-Path -LiteralPath 'HKCU:\Software\StorageHub\Installer') -or
        (Test-Path -LiteralPath $startMenuShortcut)) {
        throw 'The uninstall left the install record or the Start menu shortcut behind.'
    }

    $completed = $true
    Write-Host 'Installer smoke test passed: payload, sign-in entry and hooks verified, live-Agent uninstall completed, and data was preserved.'
}
finally {
    if ($brokerRegistered) {
        Remove-Item -LiteralPath $brokerClassKey -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $brokerHandlerKey -Recurse -Force -ErrorAction SilentlyContinue
    }
    if ($installAttempted -and -not $uninstallAttempted -and (Test-StorageHubInstalledHere)) {
        try {
            Invoke-Msiexec `
                -ArgumentList @('/x', $installerFullPath) `
                -LogPath (Join-Path $logRoot 'cleanup-uninstall.log') `
                -Description 'Best-effort cleanup uninstall'
        }
        catch {
            Write-Warning "Cleanup uninstall failed: $($_.Exception.Message)"
        }
    }

    if ($completed) {
        if (Test-Path -LiteralPath $smokeRoot) {
            $tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd(
                [System.IO.Path]::DirectorySeparatorChar,
                [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
            $validatedSmokeRoot = [System.IO.Path]::GetFullPath($smokeRoot)
            if (-not $validatedSmokeRoot.StartsWith(
                    $tempRoot,
                    [System.StringComparison]::OrdinalIgnoreCase) -or
                -not (Split-Path -Leaf $validatedSmokeRoot).StartsWith(
                    'StorageHub-installer-smoke-',
                    [System.StringComparison]::Ordinal)) {
                throw "Refusing to clean unexpected smoke-test path '$validatedSmokeRoot'."
            }
            Remove-Item -LiteralPath $validatedSmokeRoot -Recurse -Force
        }
    }
    elseif (Test-Path -LiteralPath $smokeRoot) {
        Write-Warning "Installer smoke diagnostics, msiexec logs included, remain at '$smokeRoot'."
    }
}
