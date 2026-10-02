using System.Text.RegularExpressions;

namespace StorageHub.Application.Connections;

/// <summary>
/// A named group in the connections panel, kept by the agent with the connections it holds.
/// </summary>
/// <remarks>
/// <para>
/// Groups used to be an arrangement the desktop kept in its own settings file, made by dragging,
/// with a connection's free-typed folder path deciding where a new one landed. Kept here instead,
/// a connection names its group by id, the editor can offer the groups that exist rather than a
/// box to type one into, and every desktop on the machine sees the same arrangement.
/// </para>
/// <para>
/// A name is unique without regard to case, because two groups differing only in case would be
/// two groups somebody meant as one. Groups are shown in <see cref="SortOrder"/>; the connections in
/// a group are shown by name.
/// </para>
/// </remarks>
public sealed record ConnectionGroup
{
    public const int MaximumNameLength = 128;
    public const int MaximumIconKeyLength = 64;

    private static readonly Regex ColorPattern = new(
        "^#[0-9A-Fa-f]{6}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public ConnectionGroup(
        Guid id,
        string name,
        int sortOrder,
        string? iconKey,
        string? colorKey,
        long version,
        DateTimeOffset createdUtc,
        DateTimeOffset updatedUtc)
    {
        if (id == Guid.Empty) throw new ArgumentException("A group needs an identifier.", nameof(id));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(version);
        Id = id;
        Name = NormalizeName(name);
        SortOrder = sortOrder;
        IconKey = NormalizeIconKey(iconKey);
        ColorKey = NormalizeColor(colorKey);
        Version = version;
        CreatedUtc = createdUtc;
        UpdatedUtc = updatedUtc;
    }

    public Guid Id { get; }

    public string Name { get; }

    /// <summary>Where the group sits in the panel, lowest first.</summary>
    public int SortOrder { get; }

    /// <summary>One of the built-in icon keys, or null for a folder.</summary>
    public string? IconKey { get; }

    /// <summary>The group's colour as #RRGGBB, one of the swatches a connection can wear, or null for none.</summary>
    public string? ColorKey { get; }

    public long Version { get; }

    public DateTimeOffset CreatedUtc { get; }

    public DateTimeOffset UpdatedUtc { get; }

    /// <summary>A name as it is kept: trimmed, and refused when empty, too long or carrying control characters.</summary>
    public static string NormalizeName(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0) throw new ArgumentException("A group needs a name.", nameof(name));
        if (trimmed.Length > MaximumNameLength)
        {
            throw new ArgumentOutOfRangeException(nameof(name), $"A group name cannot exceed {MaximumNameLength} characters.");
        }

        if (trimmed.Any(char.IsControl))
        {
            throw new ArgumentException("A group name cannot contain control characters.", nameof(name));
        }

        return trimmed;
    }

    public static string? NormalizeIconKey(string? iconKey)
    {
        var trimmed = iconKey?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        if (trimmed.Length > MaximumIconKeyLength || trimmed.Any(char.IsControl))
        {
            throw new ArgumentException("The icon key is not one StorageHub can show.", nameof(iconKey));
        }

        return trimmed;
    }

    public static string? NormalizeColor(string? colorKey)
    {
        var trimmed = colorKey?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        if (!ColorPattern.IsMatch(trimmed))
        {
            throw new ArgumentException("A group colour must use #RRGGBB notation.", nameof(colorKey));
        }

        return trimmed.ToUpperInvariant();
    }
}

public enum ConnectionGroupWriteStatus
{
    Succeeded = 1,
    NotFound = 2,
    NameConflict = 3
}

public sealed record ConnectionGroupWriteResult(
    ConnectionGroupWriteStatus Status,
    ConnectionGroup? Group = null);

/// <summary>One group of an arrangement made before groups were the agent's, to be brought in once.</summary>
/// <param name="Members">The connections filed in it, by id. Ids that are not saved connections are ignored.</param>
public sealed record ConnectionGroupImport(
    string Name,
    string? IconKey,
    string? ColorKey,
    IReadOnlyList<Guid> Members);

public interface IConnectionGroupRepository
{
    /// <summary>Every group, in the order the panel shows them.</summary>
    ValueTask<IReadOnlyList<ConnectionGroup>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds a group after the others.</summary>
    ValueTask<ConnectionGroupWriteResult> CreateAsync(
        string name,
        string? iconKey = null,
        string? colorKey = null,
        CancellationToken cancellationToken = default);

    /// <summary>Renames a group and sets its icon and colour, keeping its place and its connections.</summary>
    ValueTask<ConnectionGroupWriteResult> UpdateAsync(
        Guid groupId,
        string name,
        string? iconKey,
        string? colorKey,
        CancellationToken cancellationToken = default);

    /// <summary>Moves a group to a position among the others, the rest keeping their order.</summary>
    ValueTask<ConnectionGroupWriteResult> MoveAsync(
        Guid groupId,
        int index,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a group. Its connections go to Ungrouped, each at a new version; none is deleted.
    /// </summary>
    ValueTask<ConnectionGroupWriteResult> DeleteAsync(
        Guid groupId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Files a connection in a group, or in Ungrouped given null, at a new version of the connection.
    /// </summary>
    /// <returns>The connection's new version, or null when there is no such connection or group.</returns>
    ValueTask<long?> AssignAsync(
        Guid connectionId,
        Guid? groupId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Brings in an arrangement made before groups were kept here, once.
    /// </summary>
    /// <remarks>
    /// Groups are matched by name and made when missing, in the order given, after any already
    /// here; a connection is filed only while it is in no group, so nothing filed since is moved.
    /// The first call that is applied is recorded, and every later one changes nothing.
    /// </remarks>
    /// <returns>Whether this call applied the arrangement, rather than finding one already applied.</returns>
    ValueTask<bool> ImportOnceAsync(
        IReadOnlyList<ConnectionGroupImport> groups,
        CancellationToken cancellationToken = default);
}
