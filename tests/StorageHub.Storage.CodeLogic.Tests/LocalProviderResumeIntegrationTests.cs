using CL.Storage.Configuration;
using StorageHub.Domain.Capabilities;
using StorageHub.Domain.Identifiers;
using StorageHub.Domain.Storage;
using StorageHub.Storage.Abstractions;
using StorageHub.Storage.Models;

namespace StorageHub.Storage.CodeLogic.Tests;

/// <summary>
/// A keyed write through real CodeLogic storage keeps its bytes when suspended and continues them
/// on the next open, without the destination ever holding a partial file.
/// </summary>
[Collection(ProviderIntegrationFixtureGroup.Name)]
public sealed class LocalProviderResumeIntegrationTests
{
    [Fact]
    public async Task ASuspendedKeyedWriteContinuesFromWhatTheProviderAlreadyHas()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), $"storagehub-cl-resume-{Guid.NewGuid():N}");
        var providerRoot = Path.Combine(testRoot, "provider");
        Directory.CreateDirectory(providerRoot);
        try
        {
            var library = await CodeLogicTestFramework.EnsureStartedAsync();
            var profileId = ConnectionProfileId.New();
            const string rootIdentity = "resume-root-v1";
            var registration = await new CodeLogicStorageSessionFactory(library).RegisterLocalAsync(
                profileId,
                rootIdentity,
                new LocalConnectionConfig { RootPath = providerRoot, Enabled = true });
            Assert.True(registration.IsSuccess);
            await using var connection = registration.Value;
            Assert.True(connection.Session.Capabilities.Supports(StorageFeature.ResumeUpload));

            var destination = StorageAddress.Create(profileId, rootIdentity, "folder/resumed.bin").Value;
            var payload = Enumerable.Range(0, 200_000).Select(index => (byte)(index % 239)).ToArray();
            var request = new StorageWriteRequest(
                destination,
                StorageWriteMode.CreateNew,
                payload.LongLength,
                resumeKey: "0123abcd");

            var firstOpen = await connection.Session.OpenWriteAsync(request);
            Assert.True(firstOpen.IsSuccess, firstOpen.IsFailure ? $"{firstOpen.Error.Code}: {firstOpen.Error.Message}" : null);
            var first = Assert.IsAssignableFrom<IResumableStorageWriteHandle>(firstOpen.Value);
            Assert.Equal(0, first.AcceptedOffset);
            await first.Content.WriteAsync(payload.AsMemory(0, 70_000));
            var suspended = await first.SuspendAsync();
            Assert.True(suspended.IsSuccess, suspended.IsFailure ? suspended.Error.Message : null);
            await first.DisposeAsync();
            Assert.False(File.Exists(Path.Combine(providerRoot, "folder", "resumed.bin")));

            var second = Assert.IsAssignableFrom<IResumableStorageWriteHandle>(
                (await connection.Session.OpenWriteAsync(request)).Value);
            Assert.Equal(70_000, second.AcceptedOffset);
            var staged = await second.OpenAcceptedContentAsync();
            Assert.True(staged.IsSuccess, staged.IsFailure ? staged.Error.Message : null);
            await using (var stream = staged.Value)
            {
                using var prefix = new MemoryStream();
                await stream.CopyToAsync(prefix);
                Assert.Equal(payload.AsSpan(0, 70_000).ToArray(), prefix.ToArray());
            }

            await second.Content.WriteAsync(payload.AsMemory(70_000));
            var committed = await second.CommitAsync();
            await second.DisposeAsync();

            Assert.True(committed.IsSuccess, committed.IsFailure ? committed.Error.Message : null);
            Assert.Equal(payload.LongLength, committed.Value.Size);
            Assert.Equal(payload, await File.ReadAllBytesAsync(Path.Combine(providerRoot, "folder", "resumed.bin")));
            Assert.Empty(Directory.EnumerateFiles(providerRoot, "*.partial", SearchOption.AllDirectories));

            var other = StorageAddress.Create(profileId, rootIdentity, "folder/aborted.bin").Value;
            var aborted = (await connection.Session.OpenWriteAsync(
                new StorageWriteRequest(other, StorageWriteMode.CreateNew, resumeKey: "0123abcd"))).Value;
            await aborted.Content.WriteAsync(payload.AsMemory(0, 10));
            Assert.True((await aborted.AbortAsync()).IsSuccess);
            await aborted.DisposeAsync();
            Assert.Empty(Directory.EnumerateFiles(providerRoot, "*.partial", SearchOption.AllDirectories));
            Assert.False(File.Exists(Path.Combine(providerRoot, "folder", "aborted.bin")));
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }
}
