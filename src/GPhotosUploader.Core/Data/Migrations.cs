namespace GPhotosUploader.Core.Data;

/// <summary>
/// Ordered SQLite migrations. Each script is applied exactly once,
/// within a transaction, and the version is recorded in schema_version.
/// To evolve the schema: add an entry (version + 1, "ALTER TABLE ...").
/// </summary>
public static class Migrations
{
    public static readonly IReadOnlyList<(int Version, string Script)> All = new List<(int, string)>
    {
        (1, """
            CREATE TABLE settings (
                key   TEXT PRIMARY KEY,
                value TEXT
            );

            CREATE TABLE google_account (
                id           INTEGER PRIMARY KEY CHECK (id = 1),
                email        TEXT,
                display_name TEXT,
                connected_at TEXT,
                scopes       TEXT
            );

            CREATE TABLE media_files (
                id                   INTEGER PRIMARY KEY AUTOINCREMENT,
                local_path           TEXT NOT NULL UNIQUE,
                file_name            TEXT NOT NULL,
                extension            TEXT NOT NULL,
                file_size            INTEGER NOT NULL,
                sha256_hash          TEXT,
                created_at           TEXT,
                modified_at          TEXT,
                scan_status          TEXT NOT NULL DEFAULT 'scanned',
                upload_status        TEXT NOT NULL DEFAULT 'discovered',
                google_media_item_id TEXT,
                upload_token         TEXT,
                upload_token_at      TEXT,
                retry_count          INTEGER NOT NULL DEFAULT 0,
                last_error           TEXT,
                first_seen_at        TEXT NOT NULL,
                last_seen_at         TEXT NOT NULL,
                uploaded_at          TEXT
            );
            CREATE INDEX idx_media_files_upload_status ON media_files(upload_status);
            CREATE INDEX idx_media_files_hash ON media_files(sha256_hash);

            CREATE TABLE upload_batches (
                id            INTEGER PRIMARY KEY AUTOINCREMENT,
                created_at    TEXT NOT NULL,
                completed_at  TEXT,
                file_count    INTEGER NOT NULL,
                success_count INTEGER NOT NULL DEFAULT 0,
                failure_count INTEGER NOT NULL DEFAULT 0,
                status        TEXT NOT NULL
            );

            CREATE TABLE upload_attempts (
                id            INTEGER PRIMARY KEY AUTOINCREMENT,
                media_file_id INTEGER NOT NULL REFERENCES media_files(id),
                batch_id      INTEGER REFERENCES upload_batches(id),
                started_at    TEXT NOT NULL,
                finished_at   TEXT,
                outcome       TEXT,
                error         TEXT
            );
            CREATE INDEX idx_upload_attempts_file ON upload_attempts(media_file_id);

            CREATE TABLE app_logs (
                id        INTEGER PRIMARY KEY AUTOINCREMENT,
                timestamp TEXT NOT NULL,
                level     TEXT NOT NULL,
                source    TEXT,
                message   TEXT NOT NULL
            );
            CREATE INDEX idx_app_logs_timestamp ON app_logs(timestamp);
            """)
    };
}
