# CL.Storage acceptance checks

One check per defect found while reviewing CL.Storage that can be shown on the library's own test servers
(`tests/Storage.Integration.Tests/docker-compose.yml` in CodeLogic.Libs: MinIO, SFTP, Swift). Each prints
PASS or FAIL with what it saw; the exit code is the number of failures (0 = accepted).

```powershell
docker compose -f <CodeLogic.Libs>/tests/Storage.Integration.Tests/docker-compose.yml up -d
# The servers start empty: make the MinIO bucket and the Swift container the checks use.
docker exec storageintegrationtests-minio-1 sh -c "mc alias set l http://127.0.0.1:9000 cltest cltest-pw-minio && mc mb --ignore-existing l/cl-test"
$auth = Invoke-WebRequest http://127.0.0.1:8082/auth/v1.0 -Headers @{ "X-Auth-User" = "test:tester"; "X-Auth-Key" = "testing" }
Invoke-WebRequest -Method Put "$($auth.Headers["X-Storage-Url"])/cl-test" -Headers @{ "X-Auth-Token" = $auth.Headers["X-Auth-Token"] }

# The published version StorageHub uses (the default):
$env:CL_STORAGE_FEED = New-Item -ItemType Directory -Force "$env:TEMP\clfeed-empty"
dotnet run --project eng/cl-storage-acceptance

# Or a locally packed build:
dotnet pack <CodeLogic.Libs>/CL.Storage/CL.Storage.csproj -c Release -p:Version=4.8.96-local.1 -o $env:TEMP\clfeed
$env:CL_STORAGE_FEED = "$env:TEMP\clfeed"
dotnet run --project eng/cl-storage-acceptance -p:StorageVersion=4.8.96-local.1
```

It is not part of `StorageHub.slnx` and is not run by CI.
