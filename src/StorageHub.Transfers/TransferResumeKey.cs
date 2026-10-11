using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using StorageHub.Domain.Storage;

namespace StorageHub.Transfers;

/// <summary>
/// Names the bytes a transfer stages, so an interrupted one can be continued and nothing else can.
/// </summary>
/// <remarks>
/// <para>
/// The key is a digest of where the bytes come from, as the source looked when it was read --
/// its path, version, entity tag, size and modification time -- and of where they go. A retry of
/// the same transfer, or the same file queued again to the same place, arrives at the same key
/// and continues what was staged; a source that changed in any of those respects arrives at
/// another key and starts again, which is the rule CL.Storage's own resume keeps.
/// </para>
/// <para>
/// A source that says nothing about its content but its size gets no key at all: an edited file
/// of the same length would otherwise continue the old one's bytes.
/// </para>
/// </remarks>
public static class TransferResumeKey
{
    public static string? Create(StorageEntry source, StorageAddress destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        var entityTag = source.Address.EntityTag ?? source.ETag;
        if (source.Size is not long size ||
            source.Address.VersionId is null && entityTag is null && source.LastModifiedUtc is null)
        {
            return null;
        }

        var transcript = string.Join(
            '\n',
            "storagehub.resume.v1",
            source.Address.ProfileId.ToString(),
            source.Address.RootIdentity,
            source.Address.CanonicalRelativePath,
            source.Address.NativeItemId ?? string.Empty,
            source.Address.VersionId ?? string.Empty,
            entityTag ?? string.Empty,
            size.ToString(CultureInfo.InvariantCulture),
            source.LastModifiedUtc?.UtcTicks.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            destination.ProfileId.ToString(),
            destination.RootIdentity,
            destination.CanonicalRelativePath);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(transcript)));
    }
}
