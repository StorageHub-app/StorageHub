namespace StorageHub.Agent.Host;

/// <summary>
/// Reads a SHA-256 host-key fingerprint in either spelling the product accepts.
/// </summary>
/// <remarks>
/// <para>
/// A fingerprint arrives as 64 hexadecimal digits, with or without colons, or as the
/// <c>SHA256:</c> prefix and unpadded base64 that <c>ssh-keygen</c> prints. The trust store keeps
/// each in its own canonical spelling rather than converting one to the other, so anything that
/// compares a stored fingerprint with a received one has to compare the bytes.
/// </para>
/// <para>
/// The terminal service did not: it compared the received key's base64 form as a string against
/// the stored records, so a host pinned in hexadecimal -- which is what the editor's fetch and the
/// lab both produce -- could open an SFTP listing and never a shell.
/// </para>
/// </remarks>
internal static class SshFingerprints
{
    /// <summary>The 32 bytes a fingerprint names, or nothing when it is not one.</summary>
    internal static bool TryDecode(string? value, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(value)) return false;

        var normalized = value.Trim();
        var hexadecimal = normalized.Replace(":", string.Empty, StringComparison.Ordinal);
        try
        {
            if (hexadecimal.Length == 64 && hexadecimal.All(Uri.IsHexDigit))
            {
                bytes = Convert.FromHexString(hexadecimal);
            }
            else if (normalized.StartsWith("SHA256:", StringComparison.OrdinalIgnoreCase))
            {
                var payload = normalized[7..];
                bytes = Convert.FromBase64String(payload.PadRight((payload.Length + 3) / 4 * 4, '='));
            }

            return bytes.Length == 32;
        }
        catch (FormatException)
        {
            bytes = [];
            return false;
        }
    }

    /// <summary>Whether two fingerprints name the same key, whichever way each is written.</summary>
    internal static bool Equivalent(string? left, string? right) =>
        TryDecode(left, out var leftBytes) &&
        TryDecode(right, out var rightBytes) &&
        leftBytes.AsSpan().SequenceEqual(rightBytes);

    /// <summary>Whether a received key's hash is one of the trusted fingerprints.</summary>
    internal static bool IsTrusted(ReadOnlySpan<byte> hash, IEnumerable<string> trusted)
    {
        foreach (var fingerprint in trusted)
        {
            if (TryDecode(fingerprint, out var bytes) && bytes.AsSpan().SequenceEqual(hash)) return true;
        }

        return false;
    }
}
