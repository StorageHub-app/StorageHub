# CL.Storage acceptance checks

One check per finding in `needs-review.md` that can be shown on the library's own test servers
(`tests/Storage.Integration.Tests/docker-compose.yml` in CodeLogic.Libs: MinIO, SFTP, Swift). Each prints
PASS or FAIL with what it saw; the exit code is the number of failures (0 = accepted).

```powershell
docker compose -f <CodeLogic.Libs>/tests/Storage.Integration.Tests/docker-compose.yml up -d
dotnet pack <CodeLogic.Libs>/CL.Storage/CL.Storage.csproj -c Release -p:Version=4.8.94-local.3 -o $env:TEMP\clfeed
$env:CL_STORAGE_FEED = "$env:TEMP\clfeed"
dotnet run --project eng/cl-storage-acceptance -p:StorageVersion=4.8.94-local.3
```

It is not part of `StorageHub.slnx` and is not run by CI.
