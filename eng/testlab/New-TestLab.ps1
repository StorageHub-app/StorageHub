<#
.SYNOPSIS
    Mints the test lab's keys, starts its servers, and prints how to point the fixtures at them.

.DESCRIPTION
    The integration fixtures pin fingerprints, so the keys and the environment variables cannot be
    produced independently -- the fixture's idea of the host key has to be a hash of the file the
    server is actually presenting. This makes both in one pass.

    Everything it generates lives in eng/testlab/.fixtures, which is ignored by git. Nothing here
    is a secret worth keeping: the credentials are throwaway, the servers listen on loopback only,
    and the whole directory is meant to be deleted and remade.

.PARAMETER Force
    Mint fresh keys even when .fixtures already holds a set. Rotating the host keys invalidates the
    fingerprints in any shell that has already been configured, so a new set means new exports.

.PARAMETER SkipStart
    Generate the fixtures and write the environment file without starting the containers.

.EXAMPLE
    ./eng/testlab/New-TestLab.ps1
    Then, in the shell that will run the tests:  . ./eng/testlab/.fixtures/env.ps1
#>
[CmdletBinding()]
param(
    [switch] $Force,
    [switch] $SkipStart
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$labRoot = $PSScriptRoot
$fixtures = Join-Path $labRoot '.fixtures'
$sshFixtures = Join-Path $fixtures 'ssh'

# ---------------------------------------------------------------- preconditions

foreach ($tool in @('docker', 'ssh-keygen')) {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        throw "The test lab needs '$tool' on PATH. ssh-keygen ships with Git for Windows."
    }
}

# A clear message now beats a wall of compose output. Docker Desktop takes a while to come up, and
# "the daemon is not running yet" is the single most likely reason this script fails.
& docker version --format '{{.Server.Version}}' *> $null
if ($LASTEXITCODE -ne 0) {
    throw 'The Docker engine is not responding. Start Docker Desktop and wait for it to report running.'
}

if ($Force -and (Test-Path $fixtures)) {
    Remove-Item $fixtures -Recurse -Force
}

$null = New-Item -ItemType Directory -Force -Path $sshFixtures

# ---------------------------------------------------------------- keys

function New-SshKey {
    param([string] $Path, [string] $Passphrase, [string] $Comment)

    if (Test-Path $Path) { return }

    # No passphrase on a host key: sshd cannot be asked for one at startup. Client keys get one,
    # because an unencrypted client key would not exercise the passphrase path the provider takes.
    #
    # The empty passphrase is written '""' rather than ''. Windows PowerShell drops an empty string
    # when it builds a native command line, so -N '' does not pass an empty argument -- it passes
    # no argument at all, and ssh-keygen then reads the comment as the passphrase and the rest as
    # too many arguments.
    $arguments = @(
        '-t', 'ed25519',
        '-f', $Path,
        '-N', $(if ([string]::IsNullOrEmpty($Passphrase)) { '""' } else { $Passphrase }),
        '-C', $Comment,
        '-q')
    & ssh-keygen @arguments
    if ($LASTEXITCODE -ne 0) { throw "ssh-keygen failed for $Path." }
}

<#
    The fingerprint a fixture pins: SHA-256 over the host key blob, as hex.

    ssh-keygen prints the same hash base64-encoded after "SHA256:", which is what SSH.NET reports
    and what the agent's trust store stores. The fixtures want it as hex because they convert it
    back to base64 themselves. Decoding ssh-keygen's own output is what keeps the two definitions
    from drifting -- hashing the file, or the base64 text, would each give a different and
    plausible-looking wrong answer.
#>
function Get-HostKeyFingerprint {
    param([string] $PublicKeyPath)

    $line = & ssh-keygen -l -E sha256 -f $PublicKeyPath
    if ($LASTEXITCODE -ne 0) { throw "ssh-keygen could not read $PublicKeyPath." }

    $base64 = ($line -split '\s+')[1] -replace '^SHA256:', ''
    # ssh-keygen strips the padding that Convert.FromBase64String requires.
    switch ($base64.Length % 4) {
        2 { $base64 += '==' }
        3 { $base64 += '=' }
    }

    return [System.BitConverter]::ToString([Convert]::FromBase64String($base64)).Replace('-', '')
}

$hostKey = Join-Path $sshFixtures 'hostkey'
$rotatedHostKey = Join-Path $sshFixtures 'hostkey-rotated'
New-SshKey -Path $hostKey -Passphrase '' -Comment 'storagehub-testlab-host'
New-SshKey -Path $rotatedHostKey -Passphrase '' -Comment 'storagehub-testlab-host-rotated'

# The client keys carry a .key extension because the fixtures insist on one, and a passphrase
# because the provider's passphrase handling is part of what they check.
$clientKeyPassphrase = 'storagehub-client-passphrase'
$alternateKeyPassphrase = 'storagehub-alternate-passphrase'
$clientKey = Join-Path $sshFixtures 'client.key'
$alternateKey = Join-Path $sshFixtures 'alternate.key'
New-SshKey -Path $clientKey -Passphrase $clientKeyPassphrase -Comment 'storagehub-testlab-client'
New-SshKey -Path $alternateKey -Passphrase $alternateKeyPassphrase -Comment 'storagehub-testlab-alternate'

# Only the primary key is published to the servers. The alternate exists precisely so that there is
# a well-formed, correctly-passphrased key that is still not allowed in.
Copy-Item (Join-Path $sshFixtures 'client.key.pub') (Join-Path $sshFixtures 'client.pub') -Force

$hostFingerprint = Get-HostKeyFingerprint (Join-Path $sshFixtures 'hostkey.pub')
$rotatedFingerprint = Get-HostKeyFingerprint (Join-Path $sshFixtures 'hostkey-rotated.pub')

if ($hostFingerprint -eq $rotatedFingerprint) {
    throw 'The two host keys hashed to the same fingerprint, which cannot happen; check ssh-keygen.'
}

# ---------------------------------------------------------------- certificates

<#
    The FTPS material is minted inside a container.

    openssl ships with Git for Windows but is not on PATH from PowerShell, and reaching into Git's
    own installation directory to find it works right up until somebody installs Git somewhere
    else. Doing it in a container means the lab needs nothing on the host but Docker, which is also
    what it needs to run at all.
#>
$tlsFixtures = Join-Path $fixtures 'tls'
$null = New-Item -ItemType Directory -Force -Path $tlsFixtures

Write-Host 'Minting the lab certificate authority and certificates...' -ForegroundColor Cyan
# No 2>&1 on either call. Windows PowerShell wraps a native command's stderr in error records and
# then, under ErrorActionPreference Stop, treats the first line docker writes there as a terminating
# failure -- and docker writes its ordinary build progress to stderr. $LASTEXITCODE is the thing
# that actually knows whether the command worked.
& docker build --quiet --tag storagehub-testlab-certs (Join-Path $labRoot 'certs') | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not build the certificate generator image.' }

& docker run --rm --volume "${tlsFixtures}:/out" storagehub-testlab-certs | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'The certificate generator failed.' }

$fingerprintFile = Join-Path $tlsFixtures 'fingerprints.env'
if (-not (Test-Path $fingerprintFile)) {
    throw 'The certificate generator did not write fingerprints.env.'
}

$tls = @{}
foreach ($line in Get-Content $fingerprintFile) {
    if ($line -match '^([A-Z0-9_]+)=(.*)$') { $tls[$Matches[1]] = $Matches[2] }
}
foreach ($name in @('STORAGEHUB_FTP_SERVER_SHA256', 'STORAGEHUB_FTP_CLIENT_PFX_PASSWORD')) {
    if (-not $tls.ContainsKey($name)) { throw "The certificate generator did not report $name." }
}

# ---------------------------------------------------------------- settings

$settings = [ordered] @{
    STORAGEHUB_REQUIRE_SFTP              = '1'
    STORAGEHUB_SFTP_USERNAME             = 'storagehub'
    STORAGEHUB_SFTP_PASSWORD             = 'storagehub-testlab-password'
    STORAGEHUB_SFTP_PASSWORD_PORT        = '2222'
    STORAGEHUB_SFTP_PRIVATE_KEY_PORT     = '2223'
    STORAGEHUB_SFTP_ROTATED_PORT         = '2224'
    STORAGEHUB_SFTP_HOST_SHA256          = $hostFingerprint
    STORAGEHUB_SFTP_ROTATED_HOST_SHA256  = $rotatedFingerprint
    STORAGEHUB_SFTP_CLIENT_KEY_PATH      = (Resolve-Path $clientKey).Path
    STORAGEHUB_SFTP_CLIENT_KEY_PASSPHRASE = $clientKeyPassphrase
    STORAGEHUB_SFTP_ALTERNATE_KEY_PATH   = (Resolve-Path $alternateKey).Path
    STORAGEHUB_SFTP_ALTERNATE_KEY_PASSPHRASE = $alternateKeyPassphrase

    STORAGEHUB_REQUIRE_FTP               = '1'
    STORAGEHUB_FTP_USERNAME              = 'storagehub'
    STORAGEHUB_FTP_PASSWORD              = 'storagehub-testlab-password'
    STORAGEHUB_FTP_PLAIN_PORT            = '2121'
    STORAGEHUB_FTP_EXPLICIT_PORT         = '2122'
    STORAGEHUB_FTP_IMPLICIT_PORT         = '2123'
    STORAGEHUB_FTP_MTLS_PORT             = '2124'
    STORAGEHUB_FTP_SERVER_SHA256         = $tls['STORAGEHUB_FTP_SERVER_SHA256']
    STORAGEHUB_FTP_CLIENT_PFX_PATH       = (Resolve-Path (Join-Path $tlsFixtures 'client.pfx')).Path
    STORAGEHUB_FTP_CLIENT_PFX_PASSWORD   = $tls['STORAGEHUB_FTP_CLIENT_PFX_PASSWORD']

    STORAGEHUB_REQUIRE_MINIO             = '1'
    STORAGEHUB_MINIO_ENDPOINT            = 'http://127.0.0.1:9000/'
    STORAGEHUB_MINIO_ACCESS_KEY          = 'storagehub-testlab'
    STORAGEHUB_MINIO_SECRET_KEY          = 'storagehub-testlab-secret'
    STORAGEHUB_MINIO_BUCKET              = 'storagehub-testlab'

    # The sync engine, driven between the SFTP server and MinIO. Its own prefix rather than the
    # STORAGEHUB_SFTP_* names above, because SftpProviderIntegrationTests treats any one of those
    # being set as "configured" and then throws over the rest -- so sharing them would make
    # configuring this lab break that suite. It points at the key-only server, since the connection
    # editor accepts an encrypted private key and not a password for SFTP.
    STORAGEHUB_REQUIRE_SYNC_LAB          = '1'
    STORAGEHUB_SYNCLAB_SFTP_PORT         = '2223'
    STORAGEHUB_SYNCLAB_SFTP_ROOT         = 'mounted'
    STORAGEHUB_SYNCLAB_SFTP_USERNAME     = 'storagehub'
    STORAGEHUB_SYNCLAB_SFTP_KEY_PATH     = (Resolve-Path $clientKey).Path
    STORAGEHUB_SYNCLAB_SFTP_KEY_PASSPHRASE = $clientKeyPassphrase
    STORAGEHUB_SYNCLAB_SFTP_HOST_SHA256  = $hostFingerprint
}

# compose reads .env from its own directory; the tests read env.ps1. Both are generated from the
# one table above so they cannot disagree about a port.
# Written without a byte-order mark. Windows PowerShell's -Encoding utf8 emits one, and compose
# reads the first line literally -- the BOM becomes part of the first variable's name, so the
# first setting in the file silently stops existing.
$envFile = Join-Path $labRoot '.env'
[System.IO.File]::WriteAllLines(
    $envFile,
    [string[]] @($settings.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }))

$exports = $settings.GetEnumerator() | ForEach-Object {
    '$env:{0} = ''{1}''' -f $_.Key, ($_.Value -replace "'", "''")
}
[System.IO.File]::WriteAllLines(
    (Join-Path $fixtures 'env.ps1'),
    [string[]] (@(
        '# Generated by New-TestLab.ps1. Dot-source this in the shell that runs the tests:'
        '#   . ./eng/testlab/.fixtures/env.ps1'
        ''
    ) + $exports))

# ---------------------------------------------------------------- start

if (-not $SkipStart) {
    Write-Host 'Building and starting the test lab...' -ForegroundColor Cyan
    & docker compose --project-directory $labRoot up --detach --build
    if ($LASTEXITCODE -ne 0) { throw 'docker compose failed to start the test lab.' }
}

Write-Host ''
Write-Host 'Test lab ready.' -ForegroundColor Green
# Parenthesised: without them -f binds to Write-Host's own -ForegroundColor parameter rather than
# to the format string, and the port number is offered as a console colour.
Write-Host ('  SFTP password   127.0.0.1:{0}' -f $settings.STORAGEHUB_SFTP_PASSWORD_PORT)
Write-Host ('  SFTP key only   127.0.0.1:{0}' -f $settings.STORAGEHUB_SFTP_PRIVATE_KEY_PORT)
Write-Host ('  SFTP rotated    127.0.0.1:{0}' -f $settings.STORAGEHUB_SFTP_ROTATED_PORT)
Write-Host ('  FTP plain       127.0.0.1:{0}' -f $settings.STORAGEHUB_FTP_PLAIN_PORT)
Write-Host ('  FTPS explicit   127.0.0.1:{0}' -f $settings.STORAGEHUB_FTP_EXPLICIT_PORT)
Write-Host ('  FTPS implicit   127.0.0.1:{0}' -f $settings.STORAGEHUB_FTP_IMPLICIT_PORT)
Write-Host ('  FTPS mutual     127.0.0.1:{0}' -f $settings.STORAGEHUB_FTP_MTLS_PORT)
Write-Host  '  MinIO (S3)      127.0.0.1:9000, console on 9001'
Write-Host ''
Write-Host 'Point a shell at it with:' -ForegroundColor Cyan
Write-Host '  . ./eng/testlab/.fixtures/env.ps1'
