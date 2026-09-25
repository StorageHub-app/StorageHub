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

The terminal fixture also opens a shell on 2222 while holding both a key and a password: SSH.NET
offers the key, the server declines the method, and it falls back on its own.

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

### FTP and FTPS

Four endpoints, because the fixture walks a connection through each TLS posture in turn and vsftpd
holds exactly one policy per process.

| Service | Port | TLS |
|---|---|---|
| `ftp-plain` | 2121 | none |
| `ftp-explicit` | 2122 | `AUTH TLS` on the control channel |
| `ftp-implicit` | 2123 | TLS from the first byte |
| `ftp-mtls` | 2124 | explicit, **and a client certificate is required and validated** |

The lab mints its own certificate authority, which signs both the server certificate and the client
one. The mutual-TLS endpoint checks the client against that CA — without both `require_cert` and
`validate_cert` it would be the explicit endpoint under a longer name, and the fixture's
client-certificate assertions would pass against a server that never looked.

Each endpoint gets its own published passive port range. A data connection is a second socket on a
port the server nominates, so the range has to be published as well as configured, and two servers
sharing one would hand out each other's ports.

### S3

MinIO, on 9000, with a console on 9001 and the bucket created before anything else starts. MinIO no
longer publishes public images (neither `quay.io` nor Docker Hub), so the lab uses `pgsty/minio`, a
community build pinned to one release, for the server and for the bucket job (it carries `mc`).

### Proxy

gost, pinned, as an HTTP proxy on 13128 and a SOCKS5 proxy on 11080 that wants the user name and
password in `.env`. The proxy tests reach MinIO through it as `http://minio:9000`: that name only
resolves inside the lab's network, so a test cannot pass by going around the proxy.

### Paths

The storage fixtures connect with `Root = "mounted"`, which CodeLogic resolves against the server's
filesystem root — so the servers create `/mounted`, not `~/mounted`. That was established by running
a conformance pass and looking at where the files landed, which is worth knowing before changing it.

## Things that were learned by running it

Worth knowing before changing any of it, because each one presents as something else:

- **vsftpd exits 2 silently on an unrecognised setting.** `ssl_tlsv1_2` does not exist in the 3.0.3
  that Debian ships, and a container that crash-loops with no output at all is the only symptom.
- **`vsftpd_log_file=/dev/stdout` does not work.** vsftpd opens its log once per session, after
  dropping privileges, so every login is answered `500 OOPS: failed to open vsftpd log file` while
  the container stays up and says nothing. The lab logs to `/var/log/vsftpd.log`; read it with
  `docker compose exec ftp-plain tail -f /var/log/vsftpd.log`.
- **`utf8_filesystem=YES` is required.** The conformance suite round trips a filename with non-ASCII
  characters. Without it vsftpd never advertises UTF-8, the client stores the file under one
  encoding and looks for it under another, and the upload succeeds while the read back fails — which
  reads like a provider bug and is a server setting.
- **A host key fingerprint has to be decoded from `ssh-keygen`'s own output.** Hashing the key file,
  or the base64 text inside it, each gives a different and entirely plausible-looking wrong answer.

## Nothing here is a secret

The credentials are fixed and written in plain text into `.fixtures/env.ps1` and `.env`, both of
which are git-ignored. Every port binds to `127.0.0.1` explicitly, so the servers are not reachable
from the network. The whole directory is meant to be deleted and remade.
