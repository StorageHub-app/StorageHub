using System.Text.Json;
using System.Text.Json.Serialization;

namespace StorageHub.Desktop.Updates;

/// <summary>Which installer a package is for.</summary>
internal enum UpdatePackageKind
{
    /// <summary>A Windows Installer package, applied by msiexec.</summary>
    Msi = 1,

    /// <summary>A Debian package, applied by the system package manager.</summary>
    Deb = 2,
}

/// <summary>One downloadable package within a release.</summary>
/// <param name="Kind">Which installer applies it.</param>
/// <param name="Architecture">The machine architecture, as <see cref="System.Runtime.InteropServices.Architecture"/> names it.</param>
/// <param name="Url">Where to fetch it. HTTPS only - see <see cref="UpdateManifest.Validate"/>.</param>
/// <param name="Sha256">The expected digest, lower-case hex.</param>
/// <param name="SizeBytes">The expected length, so a download can be refused before it is finished.</param>
internal sealed record UpdatePackage(
    UpdatePackageKind Kind,
    string Architecture,
    string Url,
    string Sha256,
    long SizeBytes);

/// <summary>
/// A published release, as the update feed describes it.
/// </summary>
/// <remarks>
/// StorageHub is installed by an MSI on Windows and a .deb on Linux, so an update cannot swap files
/// underneath itself: it fetches the platform's own package and hands it to the platform's own
/// installer. What this manifest carries is therefore one release described once, with a package
/// per platform, rather than a patch.
/// </remarks>
internal sealed record UpdateManifest(
    int SchemaVersion,
    string Version,
    DateTimeOffset Published,
    string? Notes,
    IReadOnlyList<UpdatePackage> Packages)
{
    internal const int CurrentSchemaVersion = 1;

    /// <summary>Longest feed this will parse, so a hostile host cannot make the desktop work hard.</summary>
    internal const int MaximumBytes = 256 * 1024;

    /// <summary>Largest package this will accept a claim of, at 2 GiB.</summary>
    internal const long MaximumPackageBytes = 2L * 1024 * 1024 * 1024;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Reads a feed, or says why it will not.
    /// </summary>
    /// <remarks>
    /// Returns a reason rather than throwing: the caller is always reading something fetched over
    /// the network, and a feed that is malformed, truncated or simply not ours is an ordinary thing
    /// to encounter rather than an exceptional one.
    /// </remarks>
    internal static bool TryParse(ReadOnlySpan<byte> payload, out UpdateManifest? manifest, out string? error)
    {
        manifest = null;
        error = null;

        if (payload.Length == 0)
        {
            error = "The update feed was empty.";
            return false;
        }

        if (payload.Length > MaximumBytes)
        {
            error = "The update feed is larger than StorageHub will read.";
            return false;
        }

        UpdateManifest? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<UpdateManifest>(payload, Options);
        }
        catch (JsonException)
        {
            error = "The update feed is not valid JSON.";
            return false;
        }

        if (parsed is null)
        {
            error = "The update feed was empty.";
            return false;
        }

        error = parsed.Validate();
        if (error is not null) return false;

        manifest = parsed;
        return true;
    }

    /// <summary>
    /// Why this manifest cannot be trusted, or null when it can.
    /// </summary>
    /// <remarks>
    /// The URL check is the one that matters most. Everything this describes ends up being run by
    /// an installer with administrative rights, so a package fetched over plain HTTP is a package
    /// anyone on the path can replace. Refusing the feed is the only safe answer; falling back to
    /// HTTP because HTTPS failed would defeat the digest as well.
    /// </remarks>
    internal string? Validate()
    {
        if (SchemaVersion < 1) return "The update feed does not declare a format.";
        if (SchemaVersion > CurrentSchemaVersion)
        {
            return $"The update feed is in a newer format ({SchemaVersion}) than this build reads.";
        }

        if (!SemanticRelease.IsWellFormed(Version)) return "The update feed does not name a version.";
        if (Packages is null or { Count: 0 }) return "The update feed lists no packages.";
        if (Packages.Count > 16) return "The update feed lists more packages than a release has.";

        foreach (var package in Packages)
        {
            if (!Enum.IsDefined(package.Kind)) return "The update feed names an unknown package kind.";

            if (string.IsNullOrWhiteSpace(package.Architecture))
            {
                return "The update feed names a package with no architecture.";
            }

            if (!Uri.TryCreate(package.Url, UriKind.Absolute, out var url) ||
                !string.Equals(url.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
            {
                return "The update feed names a package that is not served over HTTPS.";
            }

            if (package.SizeBytes is <= 0 or > MaximumPackageBytes)
            {
                return "The update feed names a package of an implausible size.";
            }

            if (!IsSha256Hex(package.Sha256))
            {
                return "The update feed names a package without a usable checksum.";
            }
        }

        return null;
    }

    /// <summary>The package this machine can apply, or null when the release has none.</summary>
    internal UpdatePackage? PackageFor(UpdatePackageKind kind, string architecture) =>
        Packages.FirstOrDefault(package =>
            package.Kind == kind &&
            string.Equals(package.Architecture, architecture, StringComparison.OrdinalIgnoreCase));

    private static bool IsSha256Hex(string? value) =>
        value is { Length: 64 } && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
