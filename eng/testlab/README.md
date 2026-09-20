# The StorageHub test lab

Real servers, on loopback, for the integration fixtures to talk to.

## Why this exists

The fixtures were already written. `SftpProviderIntegrationTests`, `SshTerminalIntegrationTests`,
`SshHostKeyDiscoveryIntegrationTests` and the FTP and MinIO suites all read their endpoints from
`STORAGEHUB_*` environment variables and return early when those are unset — so on any machine that
had not been set up by hand, they silently did nothing. What was missing was never the tests. It was
anything for them to point at.

That matters more than it sounds. These are the suites that cover the things unit tests cannot
reach: that a pinned host key actually refuses a server whose key has changed, that a well-formed
private key which is simply not in `authorized_keys` is rejected as *unauthorized* rather than as
something vaguer, and that no failure message anywhere leaks a password, a passphrase or a key path.

## Using it

```powershell
./eng/testlab/New-TestLab.ps1          # mint keys, build images, start servers
. ./eng/testlab/.fixtures/env.ps1      # point this shell at them
dotnet test StorageHub.slnx
```

To stop it, and to take its data with it:

```powershell
docker compose --project-directory ./eng/testlab down --volumes
```

`New-TestLab.ps1 -Force` mints a fresh set of keys. Because the fixtures pin fingerprints, that
invalidates any shell already configured from an older set — dot-source `env.ps1` again afterwards.

Run it from PowerShell rather than from Git Bash. The generated values include Windows paths, and
`source`-ing the `.env` in bash eats the backslashes.

## What it runs

### SSH and SFTP

Three servers, because what is under test is a server's *policy* — which authentication it accepts
and which host key it presents — and one server cannot hold three policies at once.

| Service | Port | Accepts | Host key |
|---|---|---|---|
| `sftp-password` | 2222 | password only | primary |
| `sftp-key` | 2223 | public key only | primary |
| `sftp-rotated` | 2224 | password only | **rotated** |

The pairing is deliberate, and each half is asserted:

- A password offered to `sftp-key` must be refused, and a private key offered to `sftp-password`
  must be refused too. A server that quietly accepted both would turn both assertions into tests of
  nothing.
- `sftp-rotated` takes the same username and password as `sftp-password` and differs *only* in its
  host key. That is what makes pinning testable: connecting to it with the primary fingerprint has
  to fail, and fail because the key changed rather than because the credentials did.
- Two client keys are minted, both well-formed and both passphrase-protected. Only one is ever
  published to `authorized_keys`. The other exists so that "rejected" can be distinguished from
  "malformed".

The image runs an ordinary `sshd` with a real shell rather than a chrooted `internal-sftp`, because
StorageHub's SSH client panes open an interactive shell and the terminal fixture drives one.

### Paths

The storage fixtures connect with `Root = "mounted"`, which CodeLogic resolves against the server's
filesystem root — so the servers create `/mounted`, not `~/mounted`. That was established by running
a conformance pass and looking at where the files landed, which is worth knowing before changing it.

## Nothing here is a secret

The credentials are fixed and written in plain text into `.fixtures/env.ps1` and `.env`, both of
which are git-ignored. Every port binds to `127.0.0.1` explicitly, so the servers are not reachable
from the network. The whole directory is meant to be deleted and remade.
