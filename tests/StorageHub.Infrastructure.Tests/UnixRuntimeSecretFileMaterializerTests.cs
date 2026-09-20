using StorageHub.Infrastructure.Unix;
using StorageHub.Testing;

namespace StorageHub.Infrastructure.Unix.Tests;

/// <summary>
/// Handing a provider's key material to a library that wants a path.
/// </summary>
/// <remarks>
/// This is what gives SSH.NET an SSH private key. The material is plaintext by necessity, so what
/// is asserted here is how private it is while it exists and that it does not outlive the
/// connection - including the 0600 that ssh itself would insist on.
/// </remarks>
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
public sealed class UnixRuntimeSecretFileMaterializerTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"storagehub-material-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }

    [LinuxOnlyFact]
    public async Task MaterialIsWrittenPrivateAndRemovedWithTheConnection()
    {
        var materializer = new UnixRuntimeSecretFileMaterializer(_root);
        string path;

        await using (var file = await materializer.MaterializeAsync(new byte[] { 1, 2, 3 }, ".pem"))
        {
            path = file.FullPath;

            // ssh refuses a key file that is not 0600, so this is both the safe mode and the only
            // one a shell-out would accept.
            Assert.Equal(UnixFileSystem.PrivateFileMode, File.GetUnixFileMode(path));
            Assert.Equal<byte[]>([1, 2, 3], await File.ReadAllBytesAsync(path));
        }

        Assert.False(File.Exists(path));
    }

    [LinuxOnlyFact]
    public async Task TheDirectoryHoldingMaterialIsPrivate()
    {
        var materializer = new UnixRuntimeSecretFileMaterializer(_root);
        await using var file = await materializer.MaterializeAsync(new byte[] { 1 }, ".pem");

        Assert.Equal(UnixFileSystem.PrivateDirectoryMode, File.GetUnixFileMode(_root));
    }

    [LinuxOnlyFact]
    public async Task OrphansLeftByAProcessThatDiedAreScavenged()
    {
        // tmpfs clears these at the end of a session, but a lingering agent may not see one for
        // weeks, so it sweeps its own.
        var materializer = new UnixRuntimeSecretFileMaterializer(_root);
        await using (var file = await materializer.MaterializeAsync(new byte[] { 1 }, ".pem"))
        {
            File.SetLastWriteTimeUtc(file.FullPath, DateTime.UtcNow.AddHours(-2));
        }

        var orphan = Path.Combine(_root, ".material-orphan.pem");
        await File.WriteAllBytesAsync(orphan, [7]);
        File.SetLastWriteTimeUtc(orphan, DateTime.UtcNow.AddHours(-2));

        Assert.Equal(1, materializer.ScavengeOrphans(TimeSpan.FromHours(1)));
        Assert.False(File.Exists(orphan));
    }

    [LinuxOnlyFact]
    public void AKernelFilesystemIsNotStorage()
    {
        // A path under /proc is a sign of a badly resolved runtime root rather than a directory to
        // create, and writing key material there would be the worst possible way to find out.
        _ = Assert.Throws<ArgumentException>(
            () => new UnixRuntimeSecretFileMaterializer("/proc/self/storagehub"));
    }

    [LinuxOnlyFact]
    public void ARelativeRootIsRefused()
    {
        _ = Assert.Throws<ArgumentException>(
            () => new UnixRuntimeSecretFileMaterializer("runtime-secrets"));
    }
}
