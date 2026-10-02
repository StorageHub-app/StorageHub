using System.Globalization;
using Microsoft.Data.Sqlite;
using StorageHub.Application.Connections;

namespace StorageHub.Persistence.Connections;

/// <summary>
/// The connections panel's groups, and which connection is filed in which.
/// </summary>
/// <remarks>
/// Every change to a connection's group moves that connection to a new version, so an editor
/// holding it at the old one is told about a conflict rather than writing the old group back.
/// </remarks>
public sealed class SqliteConnectionGroupRepository : IConnectionGroupRepository
{
    private readonly SingleWriterSqliteDatabase _database;
    private readonly ConnectionProfileSchemaInitializer _initializer;
    private readonly TimeProvider _timeProvider;

    public SqliteConnectionGroupRepository(SqliteDatabaseOptions options, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _database = new SingleWriterSqliteDatabase(options);
        _initializer = new ConnectionProfileSchemaInitializer(options);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async ValueTask<IReadOnlyList<ConnectionGroup>> ListAsync(CancellationToken cancellationToken = default)
    {
        await _initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await _database.OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await ReadAllAsync(connection, null, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<ConnectionGroupWriteResult> CreateAsync(
        string name,
        string? iconKey = null,
        string? colorKey = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = ConnectionGroup.NormalizeName(name);
        var icon = ConnectionGroup.NormalizeIconKey(iconKey);
        var color = ConnectionGroup.NormalizeColor(colorKey);
        await _initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var lease = await _database.AcquireWriterAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = lease.Connection.BeginTransaction(deferred: false);

        var existing = await ReadAllAsync(lease.Connection, transaction, cancellationToken).ConfigureAwait(false);
        if (existing.Any(group => Same(group.Name, normalizedName)))
        {
            return new ConnectionGroupWriteResult(ConnectionGroupWriteStatus.NameConflict);
        }

        var now = _timeProvider.GetUtcNow();
        var created = new ConnectionGroup(
            Guid.NewGuid(),
            normalizedName,
            existing.Count == 0 ? 0 : existing.Max(static group => group.SortOrder) + 1,
            icon,
            color,
            1,
            now,
            now);
        await InsertAsync(lease.Connection, transaction, created, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new ConnectionGroupWriteResult(ConnectionGroupWriteStatus.Succeeded, created);
    }

    public async ValueTask<ConnectionGroupWriteResult> UpdateAsync(
        Guid groupId,
        string name,
        string? iconKey,
        string? colorKey,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = ConnectionGroup.NormalizeName(name);
        var icon = ConnectionGroup.NormalizeIconKey(iconKey);
        var color = ConnectionGroup.NormalizeColor(colorKey);
        await _initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var lease = await _database.AcquireWriterAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = lease.Connection.BeginTransaction(deferred: false);

        var existing = await ReadAllAsync(lease.Connection, transaction, cancellationToken).ConfigureAwait(false);
        if (existing.FirstOrDefault(group => group.Id == groupId) is not { } current)
        {
            return new ConnectionGroupWriteResult(ConnectionGroupWriteStatus.NotFound);
        }

        // Refused rather than merged: merging would move connections somebody did not ask to move.
        if (existing.Any(group => group.Id != groupId && Same(group.Name, normalizedName)))
        {
            return new ConnectionGroupWriteResult(ConnectionGroupWriteStatus.NameConflict);
        }

        var updated = new ConnectionGroup(
            current.Id,
            normalizedName,
            current.SortOrder,
            icon,
            color,
            current.Version + 1,
            current.CreatedUtc,
            _timeProvider.GetUtcNow());
        await using (var command = lease.Connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE connection_groups
                SET display_name = $name, icon_key = $icon, color_key = $color,
                    version = $version, updated_utc = $updated
                WHERE group_id = $id;
                """;
            command.Parameters.AddWithValue("$id", Format(updated.Id));
            command.Parameters.AddWithValue("$name", updated.Name);
            command.Parameters.AddWithValue("$icon", (object?)updated.IconKey ?? DBNull.Value);
            command.Parameters.AddWithValue("$color", (object?)updated.ColorKey ?? DBNull.Value);
            command.Parameters.AddWithValue("$version", updated.Version);
            command.Parameters.AddWithValue("$updated", FormatTimestamp(updated.UpdatedUtc));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new ConnectionGroupWriteResult(ConnectionGroupWriteStatus.Succeeded, updated);
    }

    public async ValueTask<ConnectionGroupWriteResult> MoveAsync(
        Guid groupId,
        int index,
        CancellationToken cancellationToken = default)
    {
        await _initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var lease = await _database.AcquireWriterAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = lease.Connection.BeginTransaction(deferred: false);

        var ordered = (await ReadAllAsync(lease.Connection, transaction, cancellationToken).ConfigureAwait(false)).ToList();
        var from = ordered.FindIndex(group => group.Id == groupId);
        if (from < 0) return new ConnectionGroupWriteResult(ConnectionGroupWriteStatus.NotFound);

        var moved = ordered[from];
        ordered.RemoveAt(from);
        ordered.Insert(Math.Clamp(index, 0, ordered.Count), moved);

        // Renumbered from nought every time, so the order never depends on gaps left by deletes.
        var now = FormatTimestamp(_timeProvider.GetUtcNow());
        for (var position = 0; position < ordered.Count; position++)
        {
            if (ordered[position].SortOrder == position) continue;
            await using var command = lease.Connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE connection_groups
                SET sort_order = $order, version = version + 1, updated_utc = $updated
                WHERE group_id = $id;
                """;
            command.Parameters.AddWithValue("$id", Format(ordered[position].Id));
            command.Parameters.AddWithValue("$order", position);
            command.Parameters.AddWithValue("$updated", now);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        var after = await ReadAllAsync(lease.Connection, null, cancellationToken).ConfigureAwait(false);
        return new ConnectionGroupWriteResult(
            ConnectionGroupWriteStatus.Succeeded,
            after.First(group => group.Id == groupId));
    }

    public async ValueTask<ConnectionGroupWriteResult> DeleteAsync(
        Guid groupId,
        CancellationToken cancellationToken = default)
    {
        await _initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var lease = await _database.AcquireWriterAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = lease.Connection.BeginTransaction(deferred: false);

        var existing = await ReadAllAsync(lease.Connection, transaction, cancellationToken).ConfigureAwait(false);
        if (existing.FirstOrDefault(group => group.Id == groupId) is not { } removed)
        {
            return new ConnectionGroupWriteResult(ConnectionGroupWriteStatus.NotFound);
        }

        // Its connections first, each to a new version, so nothing is lost and an editor open on
        // one of them finds out that it changed.
        await using (var release = lease.Connection.CreateCommand())
        {
            release.Transaction = transaction;
            release.CommandText = """
                UPDATE connection_profiles
                SET group_id = NULL, version = version + 1, updated_utc = $updated
                WHERE group_id = $id;
                """;
            release.Parameters.AddWithValue("$id", Format(groupId));
            release.Parameters.AddWithValue("$updated", FormatTimestamp(_timeProvider.GetUtcNow()));
            await release.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var delete = lease.Connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM connection_groups WHERE group_id = $id;";
            delete.Parameters.AddWithValue("$id", Format(groupId));
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new ConnectionGroupWriteResult(ConnectionGroupWriteStatus.Succeeded, removed);
    }

    public async ValueTask<long?> AssignAsync(
        Guid connectionId,
        Guid? groupId,
        CancellationToken cancellationToken = default)
    {
        await _initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var lease = await _database.AcquireWriterAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = lease.Connection.BeginTransaction(deferred: false);

        if (groupId is { } wanted && !await GroupExistsAsync(lease.Connection, transaction, wanted, cancellationToken)
                .ConfigureAwait(false))
        {
            return null;
        }

        long? version;
        await using (var command = lease.Connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE connection_profiles
                SET group_id = $group, version = version + 1, updated_utc = $updated
                WHERE profile_id = $id AND deleted_utc IS NULL
                RETURNING version;
                """;
            command.Parameters.AddWithValue("$id", Format(connectionId));
            command.Parameters.AddWithValue("$group", groupId is { } id ? Format(id) : DBNull.Value);
            command.Parameters.AddWithValue("$updated", FormatTimestamp(_timeProvider.GetUtcNow()));
            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            version = result is null or DBNull ? null : Convert.ToInt64(result, CultureInfo.InvariantCulture);
        }

        if (version is null) return null;
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return version;
    }

    public async ValueTask<bool> ImportOnceAsync(
        IReadOnlyList<ConnectionGroupImport> groups,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(groups);
        await _initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var lease = await _database.AcquireWriterAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = lease.Connection.BeginTransaction(deferred: false);
        var connection = lease.Connection;

        await using (var check = connection.CreateCommand())
        {
            check.Transaction = transaction;
            check.CommandText = "SELECT COUNT(*) FROM application_settings WHERE setting_key = $key;";
            check.Parameters.AddWithValue("$key", ConnectionGroupsSchemaMigration.ImportedSettingKey);
            var seen = Convert.ToInt64(
                await check.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                CultureInfo.InvariantCulture);
            if (seen > 0) return false;
        }

        var now = _timeProvider.GetUtcNow();
        var existing = (await ReadAllAsync(connection, transaction, cancellationToken).ConfigureAwait(false)).ToList();
        var next = existing.Count == 0 ? 0 : existing.Max(static group => group.SortOrder) + 1;
        var filed = 0;
        foreach (var imported in groups)
        {
            string name;
            try
            {
                name = ConnectionGroup.NormalizeName(imported.Name);
            }
            catch (ArgumentException)
            {
                // A name the store cannot keep leaves its connections Ungrouped rather than
                // failing the whole arrangement.
                continue;
            }

            var group = existing.FirstOrDefault(candidate => Same(candidate.Name, name));
            if (group is null)
            {
                group = new ConnectionGroup(
                    Guid.NewGuid(),
                    name,
                    next++,
                    SafeIcon(imported.IconKey),
                    SafeColor(imported.ColorKey),
                    1,
                    now,
                    now);
                await InsertAsync(connection, transaction, group, cancellationToken).ConfigureAwait(false);
                existing.Add(group);
            }

            foreach (var member in imported.Members.Distinct())
            {
                await using var file = connection.CreateCommand();
                file.Transaction = transaction;
                file.CommandText = """
                    UPDATE connection_profiles
                    SET group_id = $group, version = version + 1, updated_utc = $updated
                    WHERE profile_id = $id AND group_id IS NULL AND deleted_utc IS NULL;
                    """;
                file.Parameters.AddWithValue("$id", Format(member));
                file.Parameters.AddWithValue("$group", Format(group.Id));
                file.Parameters.AddWithValue("$updated", FormatTimestamp(now));
                filed += await file.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        await using (var mark = connection.CreateCommand())
        {
            mark.Transaction = transaction;
            mark.CommandText = """
                INSERT INTO application_settings(setting_key, non_secret_value_json, updated_utc)
                VALUES ($key, json_object('groups', $groups, 'filed', $filed), $updated);
                """;
            mark.Parameters.AddWithValue("$key", ConnectionGroupsSchemaMigration.ImportedSettingKey);
            mark.Parameters.AddWithValue("$groups", groups.Count);
            mark.Parameters.AddWithValue("$filed", filed);
            mark.Parameters.AddWithValue("$updated", FormatTimestamp(now));
            await mark.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static string? SafeIcon(string? icon)
    {
        try
        {
            return ConnectionGroup.NormalizeIconKey(icon);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static string? SafeColor(string? color)
    {
        try
        {
            return ConnectionGroup.NormalizeColor(color);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static async ValueTask<bool> GroupExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid groupId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM connection_groups WHERE group_id = $id;";
        command.Parameters.AddWithValue("$id", Format(groupId));
        return Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture) > 0;
    }

    private static async ValueTask InsertAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ConnectionGroup group,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO connection_groups
            (group_id, display_name, sort_order, icon_key, color_key, version, created_utc, updated_utc)
            VALUES ($id, $name, $order, $icon, $color, $version, $created, $updated);
            """;
        command.Parameters.AddWithValue("$id", Format(group.Id));
        command.Parameters.AddWithValue("$name", group.Name);
        command.Parameters.AddWithValue("$order", group.SortOrder);
        command.Parameters.AddWithValue("$icon", (object?)group.IconKey ?? DBNull.Value);
        command.Parameters.AddWithValue("$color", (object?)group.ColorKey ?? DBNull.Value);
        command.Parameters.AddWithValue("$version", group.Version);
        command.Parameters.AddWithValue("$created", FormatTimestamp(group.CreatedUtc));
        command.Parameters.AddWithValue("$updated", FormatTimestamp(group.UpdatedUtc));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<IReadOnlyList<ConnectionGroup>> ReadAllAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT group_id, display_name, sort_order, icon_key, color_key, version, created_utc, updated_utc
            FROM connection_groups
            ORDER BY sort_order, display_name COLLATE NOCASE, group_id;
            """;
        var groups = new List<ConnectionGroup>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            groups.Add(new ConnectionGroup(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetInt64(5),
                ParseTimestamp(reader.GetString(6)),
                ParseTimestamp(reader.GetString(7))));
        }

        return groups;
    }

    private static bool Same(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static string Format(Guid id) => id.ToString("D", CultureInfo.InvariantCulture);

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.ParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            .ToUniversalTime();
}
