#Requires -Version 5.1

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $Version,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $OutputRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:Utf8NoBom = [System.Text.UTF8Encoding]::new($false)
$script:Rid = 'win-x64'
$script:PackId = 'StorageHub.Desktop'
$script:MsiUpgradeCode = '{B3EFE8FA-EACC-421F-8782-EF2FFA41E905}'

function Resolve-AbsolutePath {
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [string] $BasePath
    )

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $BasePath $Path))
}

function Get-RelativeChildPath {
    param(
        [Parameter(Mandatory)]
        [string] $ParentPath,

        [Parameter(Mandatory)]
        [string] $ChildPath
    )

    $parent = [System.IO.Path]::GetFullPath($ParentPath).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    $child = [System.IO.Path]::GetFullPath($ChildPath)
    if (-not $child.StartsWith($parent, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "'$child' is not beneath '$parent'."
    }

    return $child.Substring($parent.Length)
}

function Assert-GeneratedChildPath {
    param(
        [Parameter(Mandatory)]
        [string] $ParentPath,

        [Parameter(Mandatory)]
        [string] $ChildPath
    )

    $parent = [System.IO.Path]::GetFullPath($ParentPath).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    $child = [System.IO.Path]::GetFullPath($ChildPath)

    if (-not $child.StartsWith($parent, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to treat '$child' as generated content outside '$parent'."
    }
}

function Remove-GeneratedDirectory {
    param(
        [Parameter(Mandatory)]
        [string] $ParentPath,

        [Parameter(Mandatory)]
        [string] $DirectoryPath
    )

    Assert-GeneratedChildPath -ParentPath $ParentPath -ChildPath $DirectoryPath
    if (-not (Test-Path -LiteralPath $DirectoryPath)) {
        return
    }

    $item = Get-Item -LiteralPath $DirectoryPath -Force
    if (-not $item.PSIsContainer) {
        throw "Refusing to replace '$DirectoryPath' because it is not a directory."
    }

    if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Refusing to recursively remove reparse-point directory '$DirectoryPath'."
    }

    Remove-Item -LiteralPath $DirectoryPath -Recurse -Force
}

function Invoke-NativeCommand {
    param(
        [Parameter(Mandatory)]
        [string] $FilePath,

        [Parameter(Mandatory)]
        [string[]] $ArgumentList,

        [Parameter(Mandatory)]
        [string] $Description
    )

    Write-Host "==> $Description"
    & $FilePath @ArgumentList
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

function Copy-PublishPayload {
    param(
        [Parameter(Mandatory)]
        [string] $SourceDirectory,

        [Parameter(Mandatory)]
        [string] $DestinationDirectory
    )

    New-Item -ItemType Directory -Path $DestinationDirectory -Force | Out-Null
    foreach ($file in Get-ChildItem -LiteralPath $SourceDirectory -File -Recurse) {
        if ($file.Extension.Equals('.pdb', [System.StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        $relativePath = Get-RelativeChildPath `
            -ParentPath $SourceDirectory `
            -ChildPath $file.FullName
        $destinationPath = Join-Path $DestinationDirectory $relativePath
        $destinationParent = Split-Path -Parent $destinationPath
        New-Item -ItemType Directory -Path $destinationParent -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destinationPath -Force
    }
}

function Copy-Symbols {
    param(
        [Parameter(Mandatory)]
        [string] $SourceDirectory,

        [Parameter(Mandatory)]
        [string] $DestinationDirectory
    )

    foreach ($file in Get-ChildItem -LiteralPath $SourceDirectory -Filter '*.pdb' -File -Recurse) {
        $relativePath = Get-RelativeChildPath `
            -ParentPath $SourceDirectory `
            -ChildPath $file.FullName
        $destinationPath = Join-Path $DestinationDirectory $relativePath
        $destinationParent = Split-Path -Parent $destinationPath
        New-Item -ItemType Directory -Path $destinationParent -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destinationPath -Force
    }
}

function New-DeterministicZip {
    param(
        [Parameter(Mandatory)]
        [string] $SourceDirectory,

        [Parameter(Mandatory)]
        [string] $DestinationPath
    )

    Add-Type -AssemblyName System.IO.Compression
    if (Test-Path -LiteralPath $DestinationPath) {
        Remove-Item -LiteralPath $DestinationPath -Force
    }

    [string[]] $relativePaths = @(
        Get-ChildItem -LiteralPath $SourceDirectory -File -Recurse |
            ForEach-Object {
                Get-RelativeChildPath -ParentPath $SourceDirectory -ChildPath $_.FullName
            }
    )
    [System.Array]::Sort($relativePaths, [System.StringComparer]::Ordinal)

    $destinationParent = Split-Path -Parent $DestinationPath
    New-Item -ItemType Directory -Path $destinationParent -Force | Out-Null
    $archiveStream = [System.IO.File]::Open(
        $DestinationPath,
        [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::Write,
        [System.IO.FileShare]::None)
    try {
        $archive = [System.IO.Compression.ZipArchive]::new(
            $archiveStream,
            [System.IO.Compression.ZipArchiveMode]::Create,
            $false,
            [System.Text.Encoding]::UTF8)
        try {
            $fixedTimestamp = [System.DateTimeOffset]::new(
                1980,
                1,
                1,
                0,
                0,
                0,
                [System.TimeSpan]::Zero)
            foreach ($relativePath in $relativePaths) {
                $entryPath = $relativePath.Replace(
                    [System.IO.Path]::DirectorySeparatorChar,
                    [char] '/')
                $entry = $archive.CreateEntry(
                    $entryPath,
                    [System.IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = $fixedTimestamp
                $entryStream = $entry.Open()
                try {
                    $sourceStream = [System.IO.File]::OpenRead(
                        (Join-Path $SourceDirectory $relativePath))
                    try {
                        $sourceStream.CopyTo($entryStream)
                    }
                    finally {
                        $sourceStream.Dispose()
                    }
                }
                finally {
                    $entryStream.Dispose()
                }
            }
        }
        finally {
            $archive.Dispose()
        }
    }
    finally {
        $archiveStream.Dispose()
    }
}

function Get-MsiTableRows {
    param(
        [Parameter(Mandatory)]
        [string] $MsiPath,

        [Parameter(Mandatory)]
        [string] $Query,

        [Parameter(Mandatory)]
        [int] $ColumnCount
    )

    # Windows Installer's own automation interface, read-only: what the package will do is in its
    # tables, and reading them needs neither an install nor the WiX tools.
    $installer = New-Object -ComObject WindowsInstaller.Installer
    $database = $installer.GetType().InvokeMember(
        'OpenDatabase', 'InvokeMethod', $null, $installer, @($MsiPath, 0))
    $view = $database.GetType().InvokeMember('OpenView', 'InvokeMethod', $null, $database, @($Query))
    try {
        [void] $view.GetType().InvokeMember('Execute', 'InvokeMethod', $null, $view, $null)
        $rows = [System.Collections.Generic.List[string[]]]::new()
        while ($true) {
            $record = $view.GetType().InvokeMember('Fetch', 'InvokeMethod', $null, $view, $null)
            if ($null -eq $record) {
                break
            }
            $row = [string[]]::new($ColumnCount)
            for ($column = 1; $column -le $ColumnCount; $column++) {
                $row[$column - 1] = $record.GetType().InvokeMember(
                    'StringData', 'GetProperty', $null, $record, @($column))
            }
            $rows.Add($row)
        }
        return , $rows.ToArray()
    }
    finally {
        [void] $view.GetType().InvokeMember('Close', 'InvokeMethod', $null, $view, $null)
        [void] [System.Runtime.InteropServices.Marshal]::ReleaseComObject($view)
        [void] [System.Runtime.InteropServices.Marshal]::ReleaseComObject($database)
        [void] [System.Runtime.InteropServices.Marshal]::ReleaseComObject($installer)
    }
}

function Assert-MsiPackage {
    param(
        [Parameter(Mandatory)]
        [string] $MsiPath,

        [Parameter(Mandatory)]
        [string] $ExpectedVersion
    )

    $properties = @{}
    foreach ($row in (Get-MsiTableRows -MsiPath $MsiPath -Query 'SELECT Property, Value FROM Property' -ColumnCount 2)) {
        $properties[$row[0]] = $row[1]
    }
    if ($properties['UpgradeCode'] -cne $script:MsiUpgradeCode) {
        throw "The MSI does not carry StorageHub's upgrade code $($script:MsiUpgradeCode)."
    }
    if ($properties['ProductVersion'] -cne $ExpectedVersion) {
        throw "The MSI is version '$($properties['ProductVersion'])', not '$ExpectedVersion'."
    }
    if ($properties.ContainsKey('ALLUSERS')) {
        throw 'The MSI is not a per-user package.'
    }

    $actions = @{}
    foreach ($row in (Get-MsiTableRows -MsiPath $MsiPath -Query 'SELECT Action, Target FROM CustomAction' -ColumnCount 2)) {
        $actions[$row[0]] = $row[1]
    }
    $sequenced = @{}
    foreach ($row in (Get-MsiTableRows -MsiPath $MsiPath -Query 'SELECT Action, Condition FROM InstallExecuteSequence' -ColumnCount 2)) {
        $sequenced[$row[0]] = $row[1]
    }
    foreach ($expected in @(
            @{ Action = 'StopAgentBeforeUpdate'; Hook = '--package-hook before-update' },
            @{ Action = 'ReleaseStorageHubBeforeUninstall'; Hook = '--package-hook before-uninstall' },
            @{ Action = 'RegisterAutostartAfterInstall'; Hook = '--package-hook after-install' },
            @{ Action = 'RefreshAutostartAfterUpdate'; Hook = '--package-hook after-update' },
            @{ Action = 'ReopenStorageHub'; Hook = 'StorageHub.Desktop.exe"' })) {
        if (-not $actions.ContainsKey($expected.Action) -or
            -not $actions[$expected.Action].EndsWith($expected.Hook, [System.StringComparison]::Ordinal)) {
            throw "The MSI does not define custom action '$($expected.Action)' running '$($expected.Hook)'."
        }
        if (-not $sequenced.ContainsKey($expected.Action)) {
            throw "The MSI does not schedule custom action '$($expected.Action)'."
        }
    }
    if ($sequenced['ReleaseStorageHubBeforeUninstall'] -notmatch 'NOT UPGRADINGPRODUCTCODE') {
        throw 'The uninstall hook would also run while an upgrade removes the older package.'
    }

    # FileName is "SHORT~1.EXE|Long name.exe" wherever the long name is not a valid short one.
    $files = @(
        foreach ($row in (Get-MsiTableRows -MsiPath $MsiPath -Query 'SELECT FileName FROM File' -ColumnCount 1)) {
            ($row[0] -split '\|')[-1]
        }
    )
    foreach ($requiredFile in @(
            'StorageHub.Desktop.exe',
            'StorageHub.Agent.Host.exe',
            'StorageHub.ShellExtension.Native.dll',
            'BUILDINFO.json')) {
        if ($files -cnotcontains $requiredFile) {
            throw "The MSI does not install '$requiredFile'."
        }
    }
    if (@($files | Where-Object { $_.EndsWith('.pdb', [System.StringComparison]::OrdinalIgnoreCase) }).Count -ne 0) {
        throw 'The MSI installs program database symbols.'
    }
}

function Get-PeSubsystem {
    param(
        [Parameter(Mandatory)]
        [string] $ExecutablePath
    )

    $stream = [System.IO.File]::OpenRead($ExecutablePath)
    try {
        $reader = [System.IO.BinaryReader]::new($stream)
        try {
            if ($reader.ReadUInt16() -ne 0x5A4D) {
                throw "'$ExecutablePath' is not a PE executable."
            }

            $stream.Position = 0x3C
            $peOffset = $reader.ReadInt32()
            if ($peOffset -lt 0 -or $peOffset -gt ($stream.Length - 94)) {
                throw "'$ExecutablePath' has an invalid PE header offset."
            }

            $stream.Position = $peOffset
            if ($reader.ReadUInt32() -ne 0x00004550) {
                throw "'$ExecutablePath' has no PE signature."
            }

            $stream.Position = $peOffset + 24 + 68
            return $reader.ReadUInt16()
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Get-LockFileSnapshot {
    param(
        [Parameter(Mandatory)]
        [string] $SourceRoot
    )

    $snapshot = @{}
    foreach ($lockFile in Get-ChildItem -LiteralPath $SourceRoot -Filter 'packages.lock.json' -File -Recurse) {
        $snapshot[$lockFile.FullName] = (Get-FileHash -LiteralPath $lockFile.FullName -Algorithm SHA256).Hash
    }

    return $snapshot
}

function Assert-LockFilesUnchanged {
    param(
        [Parameter(Mandatory)]
        [hashtable] $Before,

        [Parameter(Mandatory)]
        [hashtable] $After
    )

    if ($Before.Count -ne $After.Count) {
        throw 'Packaging changed the set of source packages.lock.json files.'
    }

    foreach ($path in $Before.Keys) {
        if (-not $After.ContainsKey($path) -or $After[$path] -ne $Before[$path]) {
            throw "Packaging modified source lock file '$path'."
        }
    }
}

function Assert-ReplaceableReleaseBundle {
    param(
        [Parameter(Mandatory)]
        [string] $BundlePath,

        [Parameter(Mandatory)]
        [string] $ExpectedPackId,

        [Parameter(Mandatory)]
        [string] $ExpectedRid,

        [Parameter(Mandatory)]
        [string] $ExpectedClStoragePackageVersion
    )

    if (-not (Test-Path -LiteralPath $BundlePath)) {
        return
    }

    $bundleItem = Get-Item -LiteralPath $BundlePath -Force
    if (-not $bundleItem.PSIsContainer -or
        ($bundleItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Refusing to replace non-directory or reparse-point OutputRoot '$BundlePath'."
    }

    if (@(Get-ChildItem -LiteralPath $BundlePath -Force).Count -eq 0) {
        return
    }

    $buildInfoPath = Join-Path $BundlePath 'BUILDINFO.json'
    if (-not (Test-Path -LiteralPath $buildInfoPath -PathType Leaf)) {
        throw "Refusing to replace non-empty OutputRoot '$BundlePath' without BUILDINFO.json ownership metadata."
    }

    try {
        $buildInfo = Get-Content -LiteralPath $buildInfoPath -Raw | ConvertFrom-Json
    }
    catch {
        throw "Refusing to replace OutputRoot '$BundlePath' because BUILDINFO.json is invalid."
    }

    if ($buildInfo.packId -cne $ExpectedPackId -or
        $buildInfo.rid -cne $ExpectedRid -or
        $buildInfo.clStoragePackageVersion -cne $ExpectedClStoragePackageVersion -or
        $buildInfo.unsigned -ne $true) {
        throw "Refusing to replace OutputRoot '$BundlePath' because its ownership metadata does not match this packager."
    }
}

$semVerPattern = '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-((?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*))*))?(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$'
$semVerMatch = [System.Text.RegularExpressions.Regex]::Match(
    $Version,
    $semVerPattern,
    [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)
if (-not $semVerMatch.Success -or $Version.Length -gt 128) {
    throw "Version '$Version' is not a supported SemVer 2.0 version."
}

# Windows Installer reads three numbers below 256.256.65536 and nothing else, so a release
# candidate's MSI carries its release's version and upgrades a candidate before it as a
# same-version upgrade (eng\installer\StorageHub.wxs).
$msiVersion = '{0}.{1}.{2}' -f $semVerMatch.Groups[1].Value, $semVerMatch.Groups[2].Value, $semVerMatch.Groups[3].Value
if ([int] $semVerMatch.Groups[1].Value -gt 255 -or
    [int] $semVerMatch.Groups[2].Value -gt 255 -or
    [int] $semVerMatch.Groups[3].Value -gt 65535) {
    throw "Version '$Version' cannot be expressed as a Windows Installer product version."
}

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$outputRootPath = Resolve-AbsolutePath -Path $OutputRoot -BasePath $repoRoot
if (Test-Path -LiteralPath $outputRootPath -PathType Leaf) {
    throw "OutputRoot '$outputRootPath' is a file."
}

$dotnetCommand = @(Get-Command dotnet -CommandType Application -ErrorAction Stop)[0]
$desktopProject = Join-Path $repoRoot 'src\StorageHub.Desktop\StorageHub.Desktop.csproj'
$agentProject = Join-Path $repoRoot 'src\StorageHub.Agent.Host\StorageHub.Agent.Host.csproj'
$licensePath = Join-Path $repoRoot 'LICENSE'
$readmePath = Join-Path $repoRoot 'README.md'
$iconPath = Join-Path $repoRoot 'assets\branding\storagehub.ico'
$installerProject = Join-Path $repoRoot 'eng\installer\StorageHub.Installer.wixproj'
$installerSource = Join-Path $repoRoot 'eng\installer\StorageHub.wxs'
$directoryPackagesPropsPath = Join-Path $repoRoot 'Directory.Packages.props'

foreach ($requiredFile in @(
        $desktopProject,
        $agentProject,
        $licensePath,
        $readmePath,
        $iconPath,
        $installerProject,
        $installerSource,
        $directoryPackagesPropsPath)) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        throw "Required packaging input '$requiredFile' does not exist."
    }
}

$propsText = Get-Content -LiteralPath $directoryPackagesPropsPath -Raw
$versionMatch = [System.Text.RegularExpressions.Regex]::Match(
    $propsText,
    '<PackageVersion\s+Include="CodeLogic\.Storage"\s+Version="([^"]+)"\s*/>')
if (-not $versionMatch.Success) {
    throw 'Directory.Packages.props does not contain a pinned CodeLogic.Storage package version.'
}
$clStoragePackageVersion = $versionMatch.Groups[1].Value

$sourceLocksBefore = Get-LockFileSnapshot -SourceRoot (Join-Path $repoRoot 'src')

$dotnetVersionOutput = & $dotnetCommand.Source --version
if ($LASTEXITCODE -ne 0) {
    throw "dotnet --version failed with exit code $LASTEXITCODE."
}
$dotnetSdkVersion = ([string] ($dotnetVersionOutput | Select-Object -First 1)).Trim()

$storageHubCommit = $null
$gitCommand = @(Get-Command git -CommandType Application -ErrorAction SilentlyContinue)[0]
if ($null -ne $gitCommand) {
    $commitOutput = & $gitCommand.Source `
        -c "safe.directory=$repoRoot" `
        -C $repoRoot `
        rev-parse HEAD 2>$null
    if ($LASTEXITCODE -eq 0) {
        $commitCandidate = ([string] ($commitOutput | Select-Object -First 1)).Trim()
        if ($commitCandidate -match '^[0-9a-fA-F]{40}$') {
            $storageHubCommit = $commitCandidate.ToLowerInvariant()
        }
    }
}

$sourceDateEpoch = [System.Environment]::GetEnvironmentVariable('SOURCE_DATE_EPOCH')
if ([string]::IsNullOrWhiteSpace($sourceDateEpoch)) {
    $buildTimestamp = [System.DateTimeOffset]::UtcNow
}
else {
    [long] $epochSeconds = 0
    if ($sourceDateEpoch -notmatch '^\d+$' -or
        -not [long]::TryParse($sourceDateEpoch, [ref] $epochSeconds)) {
        throw "SOURCE_DATE_EPOCH '$sourceDateEpoch' is not a valid non-negative Unix timestamp."
    }
    $buildTimestamp = [System.DateTimeOffset]::FromUnixTimeSeconds($epochSeconds)
}

$releaseName = "StorageHub-$Version-$($script:Rid)"
$releaseRoot = $outputRootPath
$outputParentPath = Split-Path -Parent $outputRootPath
$outputLeafName = Split-Path -Leaf $outputRootPath
if ([string]::IsNullOrWhiteSpace($outputParentPath) -or
    [string]::IsNullOrWhiteSpace($outputLeafName)) {
    throw "OutputRoot '$outputRootPath' must not be a filesystem root."
}
$workBase = Join-Path $outputParentPath ".$outputLeafName.storagehub-package-work"
$workRoot = Join-Path $workBase 'active'
Assert-GeneratedChildPath -ParentPath $outputParentPath -ChildPath $workBase
Assert-GeneratedChildPath -ParentPath $workBase -ChildPath $workRoot
Assert-GeneratedChildPath -ParentPath $outputParentPath -ChildPath $releaseRoot

$mutexHashAlgorithm = [System.Security.Cryptography.SHA256]::Create()
try {
    $mutexBytes = [System.Text.Encoding]::UTF8.GetBytes($releaseRoot.ToUpperInvariant())
    $mutexHash = [System.BitConverter]::ToString(
        $mutexHashAlgorithm.ComputeHash($mutexBytes)).Replace('-', '')
}
finally {
    $mutexHashAlgorithm.Dispose()
}
$packageMutex = [System.Threading.Mutex]::new(
    $false,
    "Local\StorageHub.Package.$mutexHash")
$packageMutexAcquired = $false
try {
    try {
        $packageMutexAcquired = $packageMutex.WaitOne(0)
    }
    catch [System.Threading.AbandonedMutexException] {
        $packageMutexAcquired = $true
    }
    if (-not $packageMutexAcquired) {
        throw "Another packaging process is already building '$releaseRoot'."
    }

New-Item -ItemType Directory -Path $outputParentPath -Force | Out-Null
New-Item -ItemType Directory -Path $workBase -Force | Out-Null
if (((Get-Item -LiteralPath $workBase -Force).Attributes -band
        [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw "Refusing to use reparse-point work directory '$workBase'."
}
if (Test-Path -LiteralPath $workRoot) {
    $existingWorkMarker = Join-Path $workRoot '.storagehub-package-owner'
    if (-not (Test-Path -LiteralPath $existingWorkMarker -PathType Leaf) -or
        (Get-Content -LiteralPath $existingWorkMarker -Raw).Trim() -cne
            'StorageHub deterministic Windows packaging work v1') {
        throw "Refusing to remove unrecognized work directory '$workRoot'."
    }
}
Remove-GeneratedDirectory -ParentPath $workBase -DirectoryPath $workRoot
New-Item -ItemType Directory -Path $workRoot | Out-Null
[System.IO.File]::WriteAllText(
    (Join-Path $workRoot '.storagehub-package-owner'),
    'StorageHub deterministic Windows packaging work v1' + [System.Environment]::NewLine,
    $script:Utf8NoBom)

$buildArtifacts = Join-Path $workRoot 'build'
$desktopPublish = Join-Path $workRoot 'publish\Desktop'
$agentPublish = Join-Path $workRoot 'publish\Agent'
$stageRoot = Join-Path $workRoot 'stage'
$stageAgent = Join-Path $stageRoot 'Agent'
$symbolRoot = Join-Path $workRoot 'symbols'
$msiOutput = Join-Path $workRoot 'msi'
$msiIntermediate = Join-Path $workRoot 'msi-obj'
$candidateRelease = Join-Path $workRoot 'release'

foreach ($directory in @(
        $buildArtifacts,
        $desktopPublish,
        $agentPublish,
        $stageRoot,
        $symbolRoot,
        $msiOutput,
        $candidateRelease)) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

$pathMap = "$repoRoot=/_/StorageHub"

# Restored for the RuntimeIdentifiers the projects declare (src\Directory.Build.props), which is
# what the committed lock files cover: naming the runtime here as well replaced that pair with
# win-x64 alone, and locked mode refused every project. Publishing names it.
$restoreArguments = @(
    '--artifacts-path', $buildArtifacts,
    '--nologo'
)
$commonDependencyArguments = @(
    '--runtime', $script:Rid
) + $restoreArguments
$commonPublishArguments = @(
    '--configuration', 'Release',
    '--self-contained', 'true',
    '--no-restore',
    "-p:Version=$Version",
    '-p:ContinuousIntegrationBuild=true',
    "-p:PathMap=$pathMap",
    '-p:UseAppHost=true',
    '-p:SelfContained=true',
    '-p:PublishTrimmed=false',
    '-p:PublishAot=false',
    '-p:PublishSingleFile=false',
    '-p:PublishReadyToRun=false',
    '-p:DebugSymbols=true',
    '-p:DebugType=portable',
    '-p:ErrorOnDuplicatePublishOutputFiles=true'
) + $commonDependencyArguments

$completed = $false
Push-Location $repoRoot
try {
    $desktopRestoreArguments = @(
        'restore',
        $desktopProject,
        '--locked-mode'
    ) + $restoreArguments
    Invoke-NativeCommand `
        -FilePath $dotnetCommand.Source `
        -ArgumentList $desktopRestoreArguments `
        -Description 'Restore locked win-x64 StorageHub Desktop dependencies'

    $agentRestoreArguments = @(
        'restore',
        $agentProject,
        '--locked-mode'
    ) + $restoreArguments
    Invoke-NativeCommand `
        -FilePath $dotnetCommand.Source `
        -ArgumentList $agentRestoreArguments `
        -Description 'Restore locked win-x64 StorageHub Agent dependencies'

    # The Explorer drop broker is opt-in so that a clone without the Visual Studio C++ build tools
    # can still build and test the solution. A release must always carry it, and the project's
    # publish target fails the build if the DLL does not reach the publish directory.
    $desktopArguments = @(
        'publish',
        $desktopProject,
        '--output',
        $desktopPublish
    ) + $commonPublishArguments + @(
        '-p:StorageHubBuildShellExtension=true'
    )
    Invoke-NativeCommand `
        -FilePath $dotnetCommand.Source `
        -ArgumentList $desktopArguments `
        -Description 'Publish self-contained StorageHub Desktop'

    $agentArguments = @(
        'publish',
        $agentProject,
        '--output',
        $agentPublish
    ) + $commonPublishArguments + @(
        '-p:StorageHubPackagedAgent=true'
    )
    Invoke-NativeCommand `
        -FilePath $dotnetCommand.Source `
        -ArgumentList $agentArguments `
        -Description 'Publish self-contained StorageHub Agent as WinExe'

    $sourceLocksAfter = Get-LockFileSnapshot -SourceRoot (Join-Path $repoRoot 'src')
    Assert-LockFilesUnchanged -Before $sourceLocksBefore -After $sourceLocksAfter

    Copy-PublishPayload -SourceDirectory $desktopPublish -DestinationDirectory $stageRoot
    Copy-PublishPayload -SourceDirectory $agentPublish -DestinationDirectory $stageAgent
    Copy-Symbols `
        -SourceDirectory $desktopPublish `
        -DestinationDirectory (Join-Path $symbolRoot 'Desktop')
    Copy-Symbols `
        -SourceDirectory $agentPublish `
        -DestinationDirectory (Join-Path $symbolRoot 'Agent')

    Copy-Item -LiteralPath $licensePath -Destination (Join-Path $stageRoot 'LICENSE') -Force
    Copy-Item -LiteralPath $readmePath -Destination (Join-Path $stageRoot 'README.md') -Force

    $buildInfo = [ordered] @{
        version = $Version
        packId = $script:PackId
        storageHubCommit = $storageHubCommit
        clStoragePackageVersion = $clStoragePackageVersion
        rid = $script:Rid
        dotnetSdkVersion = $dotnetSdkVersion
        unsigned = $true
        buildTimestampUtc = $buildTimestamp.ToUniversalTime().ToString(
            'O',
            [System.Globalization.CultureInfo]::InvariantCulture)
    }
    $buildInfoJson = ($buildInfo | ConvertTo-Json -Depth 3) + [System.Environment]::NewLine
    [System.IO.File]::WriteAllText(
        (Join-Path $stageRoot 'BUILDINFO.json'),
        $buildInfoJson,
        $script:Utf8NoBom)
    [System.IO.File]::WriteAllText(
        (Join-Path $stageRoot 'release-version.txt'),
        $Version + [System.Environment]::NewLine,
        $script:Utf8NoBom)

    $desktopExe = Join-Path $stageRoot 'StorageHub.Desktop.exe'
    $agentExe = Join-Path $stageAgent 'StorageHub.Agent.Host.exe'
    foreach ($requiredPayload in @(
            $desktopExe,
            (Join-Path $stageRoot 'StorageHub.Desktop.dll'),
            (Join-Path $stageRoot 'StorageHub.Desktop.deps.json'),
            (Join-Path $stageRoot 'StorageHub.Desktop.runtimeconfig.json'),
            (Join-Path $stageRoot 'StorageHub.ShellExtension.Native.dll'),
            (Join-Path $stageRoot 'coreclr.dll'),
            $agentExe,
            (Join-Path $stageAgent 'StorageHub.Agent.Host.dll'),
            (Join-Path $stageAgent 'StorageHub.Agent.Host.deps.json'),
            (Join-Path $stageAgent 'StorageHub.Agent.Host.runtimeconfig.json'),
            (Join-Path $stageAgent 'coreclr.dll'))) {
        if (-not (Test-Path -LiteralPath $requiredPayload -PathType Leaf)) {
            throw "Published payload is missing '$requiredPayload'."
        }
    }

    if ((Get-PeSubsystem -ExecutablePath $desktopExe) -ne 2) {
        throw 'StorageHub.Desktop.exe is not a Windows GUI subsystem executable.'
    }
    if ((Get-PeSubsystem -ExecutablePath $agentExe) -ne 2) {
        throw 'StorageHub.Agent.Host.exe is not a Windows GUI subsystem executable.'
    }
    if (@(Get-ChildItem -LiteralPath $stageRoot -Filter '*.pdb' -File -Recurse).Count -ne 0) {
        throw 'The installer staging directory contains program database symbols.'
    }

    # The MSI, from the staged folder. The WiX SDK restores from NuGet like any other, so this
    # needs nothing installed beyond the .NET SDK; its own obj and bin stay in the work folder.
    $installerArguments = @(
        'build',
        $installerProject,
        '--configuration', 'Release',
        '--nologo',
        "-p:StageDir=$stageRoot\",
        "-p:ProductVersion=$msiVersion",
        "-p:IconPath=$([System.IO.Path]::GetFullPath($iconPath))",
        "-p:OutputPath=$msiOutput\",
        "-p:IntermediateOutputPath=$msiIntermediate\",
        "-p:BaseIntermediateOutputPath=$msiIntermediate\"
    )
    Invoke-NativeCommand `
        -FilePath $dotnetCommand.Source `
        -ArgumentList $installerArguments `
        -Description 'Build the unsigned per-user StorageHub MSI'

    $msiArtifacts = @(Get-ChildItem -LiteralPath $msiOutput -Filter '*.msi' -File -Recurse)
    if ($msiArtifacts.Count -ne 1) {
        throw "The installer build produced $($msiArtifacts.Count) MSI packages; expected exactly one."
    }
    $msiName = "$releaseName.msi"
    $msiPath = Join-Path $candidateRelease $msiName
    Copy-Item -LiteralPath $msiArtifacts[0].FullName -Destination $msiPath
    Assert-MsiPackage -MsiPath $msiPath -ExpectedVersion $msiVersion

    if (@(Get-ChildItem -LiteralPath $symbolRoot -Filter '*.pdb' -File -Recurse).Count -gt 0) {
        New-DeterministicZip `
            -SourceDirectory $symbolRoot `
            -DestinationPath (Join-Path $candidateRelease "$releaseName-symbols.zip")
    }

    foreach ($metadataName in @('BUILDINFO.json', 'release-version.txt', 'LICENSE', 'README.md')) {
        Copy-Item `
            -LiteralPath (Join-Path $stageRoot $metadataName) `
            -Destination (Join-Path $candidateRelease $metadataName) `
            -Force
    }

    $nestedReleaseFiles = @(
        Get-ChildItem -LiteralPath $candidateRelease -File -Recurse |
            Where-Object { $_.DirectoryName -cne $candidateRelease }
    )
    if ($nestedReleaseFiles.Count -ne 0) {
        throw 'The generated GitHub release bundle is not flat.'
    }

    $checksumLines = [System.Collections.Generic.List[string]]::new()
    $releaseFiles = @(
        Get-ChildItem -LiteralPath $candidateRelease -File -Recurse |
            Where-Object { $_.Name -ne 'SHA256SUMS' }
    )
    [string[]] $relativeReleasePaths = @(
        $releaseFiles |
            ForEach-Object {
                Get-RelativeChildPath -ParentPath $candidateRelease -ChildPath $_.FullName
            }
    )
    [System.Array]::Sort($relativeReleasePaths, [System.StringComparer]::Ordinal)
    foreach ($relativePath in $relativeReleasePaths) {
        $hash = (Get-FileHash `
                -LiteralPath (Join-Path $candidateRelease $relativePath) `
                -Algorithm SHA256).Hash.ToLowerInvariant()
        $checksumPath = $relativePath.Replace(
            [System.IO.Path]::DirectorySeparatorChar,
            [char] '/')
        $checksumLines.Add("$hash  $checksumPath")
    }
    [System.IO.File]::WriteAllText(
        (Join-Path $candidateRelease 'SHA256SUMS'),
        [string]::Join("`n", $checksumLines) + "`n",
        $script:Utf8NoBom)

    Assert-ReplaceableReleaseBundle `
        -BundlePath $releaseRoot `
        -ExpectedPackId $script:PackId `
        -ExpectedRid $script:Rid `
        -ExpectedClStoragePackageVersion $clStoragePackageVersion
    $previousRelease = Join-Path $workRoot 'previous-release'
    if (Test-Path -LiteralPath $releaseRoot) {
        Move-Item -LiteralPath $releaseRoot -Destination $previousRelease
    }
    try {
        Move-Item -LiteralPath $candidateRelease -Destination $releaseRoot
    }
    catch {
        if ((Test-Path -LiteralPath $previousRelease) -and
            -not (Test-Path -LiteralPath $releaseRoot)) {
            Move-Item -LiteralPath $previousRelease -Destination $releaseRoot
        }
        throw
    }
    if (Test-Path -LiteralPath $previousRelease) {
        Remove-GeneratedDirectory `
            -ParentPath $workRoot `
            -DirectoryPath $previousRelease
    }
    $completed = $true

    Write-Host "Windows release created at '$releaseRoot'."
    Get-ChildItem -LiteralPath $releaseRoot -File |
        Sort-Object Name |
        Select-Object Name, Length
}
finally {
    Pop-Location
    if ($completed) {
        Remove-GeneratedDirectory -ParentPath $workBase -DirectoryPath $workRoot
        if (@(Get-ChildItem -LiteralPath $workBase -Force).Count -eq 0) {
            Remove-Item -LiteralPath $workBase -Force
        }
    }
    elseif (Test-Path -LiteralPath $workRoot) {
        Write-Warning "Packaging failed; isolated diagnostics remain at '$workRoot'."
    }
}
}
finally {
    if ($packageMutexAcquired) {
        $packageMutex.ReleaseMutex()
    }
    $packageMutex.Dispose()
}
