using MediaBrowser.Common.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.CozyComments.Data;

/// <summary>A stored comment row.</summary>
public sealed class CommentRow
{
    /// <summary>Gets or sets the comment id.</summary>
    public long Id { get; set; }

    /// <summary>Gets or sets the Jellyfin item the comment belongs to.</summary>
    public Guid ItemId { get; set; }

    /// <summary>Gets or sets the Jellyfin user who wrote it.</summary>
    public Guid UserId { get; set; }

    /// <summary>Gets or sets the top-level comment this is a reply to (null = top-level).</summary>
    public long? ParentId { get; set; }

    /// <summary>Gets or sets the comment text.</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>Gets or sets the optional 1-5 star rating.</summary>
    public int? Stars { get; set; }

    /// <summary>Gets or sets a value indicating whether the comment is a spoiler.</summary>
    public bool Spoiler { get; set; }

    /// <summary>Gets or sets the creation time (Unix ms, UTC).</summary>
    public long CreatedMs { get; set; }

    /// <summary>Gets or sets the last edit time (Unix ms, UTC).</summary>
    public long? EditedMs { get; set; }
}

/// <summary>
/// SQLite storage in <c>&lt;config&gt;/plugins/configurations/Jellyfin.Plugin.CozyComments/comments.db</c>.
/// Uses the Microsoft.Data.Sqlite assembly that ships with Jellyfin itself.
/// </summary>
public sealed class CommentStore
{
    private const string Columns = "id, item_id, user_id, parent_id, body, stars, spoiler, created_ms, edited_ms";

    private readonly ILogger<CommentStore> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly object _initLock = new();
    private readonly string _connectionString;
    private int _initialized;

    /// <summary>Initializes a new instance of the <see cref="CommentStore"/> class.</summary>
    /// <param name="paths">Jellyfin application paths.</param>
    /// <param name="logger">Logger.</param>
    public CommentStore(IApplicationPaths paths, ILogger<CommentStore> logger)
    {
        _logger = logger;
        DataDirectory = LegacyMigration.DataDirectory(paths, logger);
        DatabasePath = Path.Combine(DataDirectory, "comments.db");
        BackupDirectory = Path.Combine(DataDirectory, "backups");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            DefaultTimeout = 15,
            Pooling = true,
        }.ToString();
    }

    /// <summary>Gets the plugin's data folder.</summary>
    public string DataDirectory { get; }

    /// <summary>Gets the database file path.</summary>
    public string DatabasePath { get; }

    /// <summary>Gets the backup folder.</summary>
    public string BackupDirectory { get; }

    private SqliteConnection Open()
    {
        EnsureInitialized();
        var c = new SqliteConnection(_connectionString);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=10000;";
        cmd.ExecuteNonQuery();
        return c;
    }

    private void EnsureInitialized()
    {
        if (Volatile.Read(ref _initialized) == 1)
        {
            return;
        }

        lock (_initLock)
        {
            if (_initialized == 1)
            {
                return;
            }

            Directory.CreateDirectory(DataDirectory);
            using var c = new SqliteConnection(_connectionString);
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = """
                PRAGMA journal_mode=WAL;
                CREATE TABLE IF NOT EXISTS comments(
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    item_id TEXT NOT NULL,
                    user_id TEXT NOT NULL,
                    parent_id INTEGER NULL REFERENCES comments(id) ON DELETE CASCADE,
                    body TEXT NOT NULL,
                    stars INTEGER NULL CHECK (stars BETWEEN 1 AND 5),
                    spoiler INTEGER NOT NULL DEFAULT 0,
                    created_ms INTEGER NOT NULL,
                    edited_ms INTEGER NULL
                );
                CREATE INDEX IF NOT EXISTS ix_comments_item ON comments(item_id, created_ms DESC);
                CREATE INDEX IF NOT EXISTS ix_comments_parent ON comments(parent_id);
                CREATE INDEX IF NOT EXISTS ix_comments_created ON comments(created_ms DESC);
                CREATE TABLE IF NOT EXISTS meta(key TEXT PRIMARY KEY, value TEXT);
                INSERT OR IGNORE INTO meta(key, value) VALUES ('schema', '1');
                """;
            cmd.ExecuteNonQuery();
            _logger.LogInformation("[Cozy Comments] Database ready at {Path}", DatabasePath);
            Volatile.Write(ref _initialized, 1);
        }
    }

    private static string G(Guid g) => g.ToString("N");

    private static CommentRow Read(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        ItemId = Guid.Parse(r.GetString(1)),
        UserId = Guid.Parse(r.GetString(2)),
        ParentId = r.IsDBNull(3) ? null : r.GetInt64(3),
        Body = r.GetString(4),
        Stars = r.IsDBNull(5) ? null : r.GetInt32(5),
        Spoiler = r.GetInt64(6) != 0,
        CreatedMs = r.GetInt64(7),
        EditedMs = r.IsDBNull(8) ? null : r.GetInt64(8),
    };

    /// <summary>All comments and replies of one item, newest first.</summary>
    /// <param name="itemId">The item.</param>
    /// <returns>The rows.</returns>
    public List<CommentRow> GetForItem(Guid itemId)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT {Columns} FROM comments WHERE item_id = $i ORDER BY created_ms DESC, id DESC LIMIT 5000";
        cmd.Parameters.AddWithValue("$i", G(itemId));
        var list = new List<CommentRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(Read(r));
        }

        return list;
    }

    /// <summary>One comment by id.</summary>
    /// <param name="id">Comment id.</param>
    /// <returns>The row or null.</returns>
    public CommentRow? Get(long id)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT {Columns} FROM comments WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? Read(r) : null;
    }

    /// <summary>Newest-first (id, item) pairs for the comments page. The caller filters by item access.</summary>
    /// <param name="max">Maximum rows.</param>
    /// <param name="includeReplies">Include replies.</param>
    /// <param name="includeSpoilers">Include spoiler comments.</param>
    /// <returns>The pairs.</returns>
    public List<(long Id, Guid ItemId)> GetRecentIndex(int max, bool includeReplies, bool includeSpoilers)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        var where = new List<string>();
        if (!includeReplies)
        {
            where.Add("parent_id IS NULL");
        }

        if (!includeSpoilers)
        {
            where.Add("spoiler = 0");
        }

        var filter = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : string.Empty;
        cmd.CommandText = $"SELECT id, item_id FROM comments {filter} ORDER BY created_ms DESC, id DESC LIMIT $n";
        cmd.Parameters.AddWithValue("$n", max);
        var list = new List<(long, Guid)>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add((r.GetInt64(0), Guid.Parse(r.GetString(1))));
        }

        return list;
    }

    /// <summary>Several comments by id, newest first.</summary>
    /// <param name="ids">Comment ids.</param>
    /// <returns>The rows.</returns>
    public List<CommentRow> GetMany(IReadOnlyCollection<long> ids)
    {
        var result = new List<CommentRow>();
        if (ids.Count == 0)
        {
            return result;
        }

        using var c = Open();
        using var cmd = c.CreateCommand();
        var names = new List<string>();
        var n = 0;
        foreach (var id in ids)
        {
            var p = "$p" + n++;
            names.Add(p);
            cmd.Parameters.AddWithValue(p, id);
        }

        cmd.CommandText = $"SELECT {Columns} FROM comments WHERE id IN ({string.Join(',', names)}) ORDER BY created_ms DESC, id DESC";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            result.Add(Read(r));
        }

        return result;
    }

    /// <summary>Inserts a comment and sets its id.</summary>
    /// <param name="row">The new row.</param>
    /// <returns>The row with its id.</returns>
    public async Task<CommentRow> InsertAsync(CommentRow row)
    {
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = """
                INSERT INTO comments(item_id, user_id, parent_id, body, stars, spoiler, created_ms)
                VALUES ($i, $u, $p, $b, $s, $sp, $c);
                SELECT last_insert_rowid();
                """;
            cmd.Parameters.AddWithValue("$i", G(row.ItemId));
            cmd.Parameters.AddWithValue("$u", G(row.UserId));
            cmd.Parameters.AddWithValue("$p", (object?)row.ParentId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$b", row.Body);
            cmd.Parameters.AddWithValue("$s", (object?)row.Stars ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$sp", row.Spoiler ? 1 : 0);
            cmd.Parameters.AddWithValue("$c", row.CreatedMs);
            row.Id = (long)(cmd.ExecuteScalar() ?? 0L);
            return row;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>Updates text, stars and spoiler flag of a comment.</summary>
    /// <param name="id">Comment id.</param>
    /// <param name="body">New text.</param>
    /// <param name="stars">New stars.</param>
    /// <param name="spoiler">New spoiler flag.</param>
    /// <param name="editedMs">Edit time.</param>
    /// <returns>A task.</returns>
    public async Task UpdateAsync(long id, string body, int? stars, bool spoiler, long editedMs)
    {
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE comments SET body = $b, stars = $s, spoiler = $sp, edited_ms = $e WHERE id = $id";
            cmd.Parameters.AddWithValue("$b", body);
            cmd.Parameters.AddWithValue("$s", (object?)stars ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$sp", spoiler ? 1 : 0);
            cmd.Parameters.AddWithValue("$e", editedMs);
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>Deletes a comment and (for a top-level comment) its replies.</summary>
    /// <param name="id">Comment id.</param>
    /// <returns>Number of deleted rows.</returns>
    public async Task<int> DeleteAsync(long id)
    {
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            using var c = Open();
            using var tx = c.BeginTransaction();
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM comments WHERE parent_id = $id; DELETE FROM comments WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", id);
            var n = cmd.ExecuteNonQuery();
            tx.Commit();
            return n;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>Writes <c>backups/comments-YYYY-MM-DD.db</c> (online SQLite backup) and keeps the newest <paramref name="keep"/>.</summary>
    /// <param name="keep">How many dated backups to keep (at least 1).</param>
    /// <returns>The backup path.</returns>
    public async Task<string> BackupAsync(int keep)
    {
        Directory.CreateDirectory(BackupDirectory);
        var target = Path.Combine(BackupDirectory, $"comments-{DateTime.Now:yyyy-MM-dd}.db");
        var tmp = target + ".tmp";
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            using var src = Open();
            if (File.Exists(tmp))
            {
                File.Delete(tmp);
            }

            using (var dst = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = tmp, Pooling = false }.ToString()))
            {
                dst.Open();
                src.BackupDatabase(dst);
            }

            File.Move(tmp, target, overwrite: true);
        }
        finally
        {
            _writeLock.Release();
        }

        // Rotation only touches our own dated files in our own backups folder.
        var old = new DirectoryInfo(BackupDirectory)
            .GetFiles("comments-????-??-??.db")
            .OrderByDescending(f => f.Name, StringComparer.Ordinal)
            .Skip(Math.Max(1, keep))
            .ToList();
        foreach (var f in old)
        {
            f.Delete();
            _logger.LogInformation("[Cozy Comments] Rotated old backup {File}", f.Name);
        }

        return target;
    }
}
