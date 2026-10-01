using Microsoft.Data.Sqlite;

namespace StorageHub.Persistence;

/// <summary>
/// Allows a stored SSH private key to carry no passphrase.
///
/// Schema v14 let a certificate go without a password but kept a check that every SSH key has a
/// passphrase, because the SFTP connector refused an unencrypted key. It no longer does: a key
/// without a passphrase is accepted, kept encrypted in the vault like any other material, and the
/// desktop warns that a passphrase is still recommended. The check goes; nothing else changes.
/// </summary>
public sealed class OptionalSshKeyPassphraseSchemaMigration : IDatabaseMigration
{
    public const int SchemaVersion = 15;
    public int Version => SchemaVersion;
    public string Name => "optional-ssh-key-passphrase";

    public async ValueTask ApplyAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = SchemaSql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private const string SchemaSql = """
        CREATE TABLE credential_references_v15
        (
            credential_id TEXT NOT NULL PRIMARY KEY,
            credential_kind TEXT NOT NULL
                CHECK (credential_kind IN ('pkcs12-certificate', 'ssh-private-key')),
            display_name TEXT NOT NULL COLLATE NOCASE,
            description TEXT NULL,
            tags_json TEXT NOT NULL DEFAULT '[]'
                CHECK (json_valid(tags_json) AND json_type(tags_json) = 'array'),
            material_reference TEXT NOT NULL,
            passphrase_reference TEXT NULL,
            summary_json TEXT NOT NULL
                CHECK (json_valid(summary_json) AND json_type(summary_json) = 'object'),
            version INTEGER NOT NULL CHECK (version > 0),
            created_utc TEXT NOT NULL,
            updated_utc TEXT NOT NULL,
            last_used_utc TEXT NULL
        );

        INSERT INTO credential_references_v15
        SELECT credential_id, credential_kind, display_name, description, tags_json,
               material_reference, passphrase_reference, summary_json, version,
               created_utc, updated_utc, last_used_utc
        FROM credential_references;

        CREATE TABLE profile_credentials_v15
        (
            profile_id TEXT NOT NULL REFERENCES connection_profiles(profile_id) ON DELETE CASCADE,
            credential_slot TEXT NOT NULL,
            credential_id TEXT NOT NULL
                REFERENCES credential_references_v15(credential_id) ON DELETE RESTRICT,
            PRIMARY KEY (profile_id, credential_slot)
        );
        INSERT INTO profile_credentials_v15 SELECT * FROM profile_credentials;

        DROP TABLE profile_credentials;
        DROP TABLE credential_references;
        ALTER TABLE credential_references_v15 RENAME TO credential_references;
        ALTER TABLE profile_credentials_v15 RENAME TO profile_credentials;

        CREATE UNIQUE INDEX ux_credential_references_display_name
            ON credential_references(display_name COLLATE NOCASE);
        CREATE INDEX ix_credential_references_kind
            ON credential_references(credential_kind, display_name COLLATE NOCASE);
        CREATE INDEX ix_profile_credentials_credential
            ON profile_credentials(credential_id);
        """;
}
