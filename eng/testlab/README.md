# The StorageHub test lab

Real servers, on loopback, for the integration fixtures to talk to.

## Why this exists

The fixtures were already written. `SftpProviderIntegrationTests`, `SshTerminalIntegrationTests`,
`SshHostKeyDiscoveryIntegrationTests` and the FTP, WebDAV, MinIO and cloud suites all read their endpoints from
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
- A third client key, `unprotected.key`, has no passphrase and is published too: 2.0 accepts such a
  key where 1.4 refused it, and the SFTP fixture connects with it, handing the library no
  passphrase. A lab made before it existed gains it on the next run of `New-TestLab.ps1`; restart
  `sftp-key` afterwards, since the server copies `authorized_keys` when it starts.

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

### WebDAV

rclone (`rclone serve webdav`, pinned) twice: once in plain HTTP and once over HTTPS with the
lab's server certificate, the same one the FTPS endpoints present.

| Service | Port | TLS |
|---|---|---|
| `webdav` | 18080 | none |
| `webdav-tls` | 18443 | HTTPS, lab certificate |

`WebDavProviderIntegrationTests` reads:

| Variable | Meaning |
|---|---|
| `STORAGEHUB_REQUIRE_WEBDAV` | `1` makes a missing or malformed setting a failure instead of a skip |
| `STORAGEHUB_WEBDAV_URL` | the plain server, an `http://` URL |
| `STORAGEHUB_WEBDAV_TLS_URL` | the HTTPS server, an `https://` URL |
| `STORAGEHUB_WEBDAV_USERNAME`, `STORAGEHUB_WEBDAV_PASSWORD` | the one user both servers accept |
| `STORAGEHUB_WEBDAV_SERVER_SHA256` | the HTTPS certificate's SHA-256, as hex — the FTPS fingerprint |

With none of them set the fixture does nothing. Set, it runs the conformance pass over plain HTTP,
the round trip over pinned HTTPS, a saved profile through the real connector (password in a vault,
pin in a trust store), and the failures: a wrong pin and an unpinned connection to the lab's
certificate are both TLS failures, a wrong password is *unauthorized* and is never repeated back, and
plain HTTP that nobody acknowledged is refused before anything is sent.

Every run writes below its own `mounted/run-<id>` folder, which it makes first: a WebDAV root has
to exist, as a mount point does, and a reused lab still holds what the last run wrote. The two
servers keep separate data, but nothing depends on that.

Any WebDAV server will do. Without Docker, `pip install wsgidav cheroot` and a config with a
`simple_dc` user, `accept_basic: true` and, for the HTTPS one, `ssl_certificate`/`ssl_private_key`
pointing at the lab certificate, is what the fixture was first run against.

### S3

MinIO, on 9000, with a console on 9001 and the bucket created before anything else starts. MinIO no
longer publishes public images (neither `quay.io` nor Docker Hub), so the lab uses `pgsty/minio`, a
community build pinned to one release, for the server and for the bucket job (it carries `mc`).

### Azure Blob Storage

Azurite (`mcr.microsoft.com/azure-storage/azurite`, pinned to 3.37.0, the release the fixture was run against), blob service only, on 10000. It has one
account, Microsoft's published `devstoreaccount1`, and speaks plain HTTP.

| Service | Port | TLS |
|---|---|---|
| `azurite` | 10000 | none |

`AzureBlobProviderIntegrationTests` reads:

| Variable | Meaning |
|---|---|
| `STORAGEHUB_REQUIRE_AZURITE` | `1` makes a missing setting a failure instead of a skip |
| `STORAGEHUB_AZURE_CONNECTION_STRING` | a connection string naming the emulator's blob endpoint |
| `STORAGEHUB_AZURE_CONTAINER` | the container to work in; the fixture creates it |

Set, it runs the conformance pass signed in with the connection string, then a saved profile through
the real connector (the string in a vault, plain HTTP acknowledged on the profile), and the failures:
the same string without the acknowledgement is refused before anything is sent, a wrong account key
is *unauthorized*, and a shared key aimed at an `http://` service URL is refused outright. A sentinel
blob outside the run's prefix has to survive every run.

Without Docker, `npm install -g azurite` and
`azurite-blob --blobHost 127.0.0.1 --blobPort 10000 --skipApiVersionCheck --loose` is what the
fixture was first run against.

### Google Cloud Storage

fake-gcs-server (`fsouza/fake-gcs-server`, pinned to 1.56.1, the release the fixture was run
against) on 4443, in plain HTTP, keeping its buckets in memory. It checks no credentials.

| Service | Port | TLS |
|---|---|---|
| `fake-gcs` | 4443 | none |

`GoogleCloudStorageProviderIntegrationTests` reads:

| Variable | Meaning |
|---|---|
| `STORAGEHUB_REQUIRE_FAKE_GCS` | `1` makes a missing or malformed setting a failure instead of a skip |
| `STORAGEHUB_GCS_SERVICE_URL` | the emulator's address; `/storage/v1/` is added by the client |
| `STORAGEHUB_GCS_BUCKET` | the bucket to work in; the fixture creates it |

Set, it runs the conformance pass signed in anonymously, then a saved profile through the real
connector signed in with a service-account key from a vault. The key is minted by the fixture and
the connection goes through a small forwarder in the fixture that keeps each request's
`Authorization` header, so the pass shows that every request was signed with the vaulted key —
something fake-gcs-server itself never checks. Then the failures: a plain-HTTP service URL nobody
acknowledged cannot be saved and is refused by the library too, and a key that is not a key is
refused without being repeated back.

`-external-url` matters: it is where fake-gcs-server sends a resumable upload's next request, and
without it that is the container's own address, which the host cannot reach.

Without Docker, `go install github.com/fsouza/fake-gcs-server@v1.56.1` and
`fake-gcs-server -scheme http -host 127.0.0.1 -port 4443 -backend memory -public-host 127.0.0.1:4443 -external-url http://127.0.0.1:4443`
is what the fixture was first run against.

### OpenStack Swift

The OpenStack project's all-in-one Swift (`openstackswift/saio:py3`, pinned by digest because the
image has had no release tag since 2020) on 8080, in plain HTTP, signing in with TempAuth and its
built-in `test:tester` account, password `testing`.

| Service | Port | TLS |
|---|---|---|
| `swift` | 8080 | none |

`SwiftProviderIntegrationTests` reads:

| Variable | Meaning |
|---|---|
| `STORAGEHUB_REQUIRE_SWIFT` | `1` makes a missing or malformed setting a failure instead of a skip |
| `STORAGEHUB_SWIFT_AUTH_URL` | TempAuth's address, ending `/auth/v1.0` |
| `STORAGEHUB_SWIFT_USERNAME` | the TempAuth user, as `account:user` |
| `STORAGEHUB_SWIFT_PASSWORD` | its key |
| `STORAGEHUB_SWIFT_CONTAINER` | the container to work in; the fixture creates it |

Set, it runs the conformance pass signed in with TempAuth, then a saved profile through the real
connector with the password in a vault, then a second one that has only a storage URL and a token
from the vault, the way a pre-issued token is used. Then the failures: a wrong password and a
forged token are each refused as unauthorized without either being repeated back, and a plain-HTTP
URL nobody acknowledged cannot be saved and is refused by the library too.

TempAuth answers with a storage URL made from the `Host` the request came in on, which is why the
port is the same on both sides of the mapping: change `STORAGEHUB_SWIFT_PORT` and the URLs follow.

Keystone v3 is not in the lab. No Keystone image is small enough to be worth it here, so that sign-in
is covered by the unit tests, down to the configuration handed to the library, and not against a
server.

Without Docker, `pip install swift` (the fixture was first run against 2.38.2) and a running
`memcached`, then the usual one-node configuration: account, container and object servers on
loopback with one-device rings, and a proxy whose pipeline is
`catch_errors proxy-logging cache tempauth proxy-logging proxy-server` with
`user_test_tester = testing .admin`, `account_autocreate = true` and `user = root` in each server's
`[DEFAULT]` when there is no `swift` user to drop to. Point `STORAGEHUB_SWIFT_AUTH_URL` at that
proxy's `/auth/v1.0`.

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
