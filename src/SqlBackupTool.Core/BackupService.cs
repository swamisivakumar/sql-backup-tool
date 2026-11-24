using System.Diagnostics;
using Microsoft.Data.SqlClient;

namespace SqlBackupTool.Core;

public sealed class BackupService(string connectionString)
{
    private readonly string _connectionString = ForceMaster(connectionString);

    private static string ForceMaster(string cs)
    {
        var b = new SqlConnectionStringBuilder(cs) { InitialCatalog = "master", ApplicationName = "SqlBackupTool" };
        return b.ConnectionString;
    }

    public string DataSource => new SqlConnectionStringBuilder(_connectionString).DataSource;

    private async Task<SqlConnection> OpenAsync(CancellationToken ct)
    {
        var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync(ct);
        return cn;
    }

    private static void AttachMessages(SqlConnection cn, IProgress<int>? percent, IProgress<string>? log) =>
        cn.InfoMessage += (_, e) =>
        {
            foreach (SqlError err in e.Errors)
            {
                if (BackupScript.ParsePercent(err.Message) is { } p) percent?.Report(p);
                log?.Report(err.Message);
            }
        };

    // ------------------------------------------------------------------ queries

    public async Task<ServerInfo> GetServerInfoAsync(CancellationToken ct = default)
    {
        await using var cn = await OpenAsync(ct);
        const string sql = """
            SELECT CAST(SERVERPROPERTY('ServerName') AS nvarchar(256)),
                   CAST(SERVERPROPERTY('MachineName') AS nvarchar(256)),
                   CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)),
                   CAST(SERVERPROPERTY('Edition') AS nvarchar(128)),
                   CAST(SERVERPROPERTY('EngineEdition') AS int),
                   CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(512)),
                   CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(512)),
                   CAST(SERVERPROPERTY('InstanceDefaultLogPath') AS nvarchar(512));
            """;
        await using var cmd = new SqlCommand(sql, cn);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        await r.ReadAsync(ct);
        string? Str(int i) => r.IsDBNull(i) ? null : r.GetString(i);
        return new ServerInfo(
            Str(0) ?? DataSource, Str(1) ?? "", Str(2) ?? "", Str(3) ?? "",
            !r.IsDBNull(4) && r.GetInt32(4) == 4, // 4 = Express
            Str(5), Str(6), Str(7));
    }

    public async Task<IReadOnlyList<DatabaseInfo>> GetDatabasesAsync(bool includeSystem, CancellationToken ct = default)
    {
        await using var cn = await OpenAsync(ct);
        const string sql = """
            SELECT d.name, d.state_desc, d.recovery_model_desc,
                   CAST(SUM(CAST(mf.size AS bigint)) * 8 / 1024.0 AS decimal(18,2)) AS size_mb,
                   (SELECT MAX(b.backup_finish_date) FROM msdb.dbo.backupset b
                     WHERE b.database_name = d.name AND b.type = 'D') AS last_full
            FROM sys.databases d
            JOIN sys.master_files mf ON mf.database_id = d.database_id
            WHERE d.name <> 'tempdb' AND (@includeSystem = 1 OR d.database_id > 4)
            GROUP BY d.name, d.state_desc, d.recovery_model_desc
            ORDER BY d.name;
            """;
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@includeSystem", includeSystem);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var list = new List<DatabaseInfo>();
        while (await r.ReadAsync(ct))
            list.Add(new DatabaseInfo(r.GetString(0), r.GetString(1), r.GetString(2), r.GetDecimal(3),
                r.IsDBNull(4) ? null : r.GetDateTime(4)));
        return list;
    }

    public async Task<bool> DatabaseExistsAsync(string database, CancellationToken ct = default)
    {
        await using var cn = await OpenAsync(ct);
        await using var cmd = new SqlCommand("SELECT DB_ID(@db);", cn);
        cmd.Parameters.AddWithValue("@db", database);
        return await cmd.ExecuteScalarAsync(ct) is not (null or DBNull);
    }

    public async Task<IReadOnlyList<BackupHistoryItem>> GetBackupHistoryAsync(string database, CancellationToken ct = default)
    {
        await using var cn = await OpenAsync(ct);
        const string sql = """
            SELECT TOP (50) b.backup_start_date, b.backup_finish_date,
                   CASE b.type WHEN 'D' THEN 'Full' WHEN 'I' THEN 'Differential' WHEN 'L' THEN 'Log'
                               WHEN 'F' THEN 'File' ELSE b.type END,
                   CAST(b.backup_size / 1048576.0 AS decimal(18,2)),
                   CAST(b.compressed_backup_size / 1048576.0 AS decimal(18,2)),
                   b.is_copy_only, mf.physical_device_name, b.position, b.name
            FROM msdb.dbo.backupset b
            JOIN msdb.dbo.backupmediafamily mf ON mf.media_set_id = b.media_set_id
            WHERE b.database_name = @db
            ORDER BY b.backup_finish_date DESC;
            """;
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@db", database);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var list = new List<BackupHistoryItem>();
        while (await r.ReadAsync(ct))
            list.Add(new BackupHistoryItem(r.GetDateTime(0), r.GetDateTime(1), r.GetString(2), r.GetDecimal(3),
                r.IsDBNull(4) ? null : r.GetDecimal(4), r.GetBoolean(5), r.GetString(6), r.GetInt32(7),
                r.IsDBNull(8) ? null : r.GetString(8)));
        return list;
    }

    // ------------------------------------------------------------------ backup

    /// <summary>Runs a full backup (and optional verify). Never throws for SQL errors; returns a failed result instead.</summary>
    public async Task<BackupResult> BackupAsync(
        string database, string filePath, BackupOptions options,
        IProgress<int>? percent = null, IProgress<string>? log = null, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await using var cn = await OpenAsync(ct);
            AttachMessages(cn, percent, log);

            log?.Report($"BACKUP DATABASE [{database}] TO DISK = '{filePath}'");
            await using (var cmd = new SqlCommand(BackupScript.BuildBackupSql(options), cn) { CommandTimeout = 0 })
            {
                cmd.Parameters.AddWithValue("@db", database);
                cmd.Parameters.AddWithValue("@path", filePath);
                cmd.Parameters.AddWithValue("@name", BackupScript.BackupSetName);
                await cmd.ExecuteNonQueryAsync(ct);
            }
            percent?.Report(100);

            if (options.Verify)
            {
                log?.Report("Verifying backup file...");
                await using var cmd = new SqlCommand(BackupScript.BuildVerifySql(options), cn) { CommandTimeout = 0 };
                cmd.Parameters.AddWithValue("@path", filePath);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            return new BackupResult(database, true, filePath, sw.Elapsed, null);
        }
        catch (Exception ex) when (ex is OperationCanceledException || (ex is SqlException && ct.IsCancellationRequested))
        {
            return new BackupResult(database, false, filePath, sw.Elapsed, "Cancelled by user.");
        }
        catch (SqlException ex)
        {
            return new BackupResult(database, false, filePath, sw.Elapsed, ex.Message);
        }
    }

    // ------------------------------------------------------------------ retention

    /// <summary>Disk backups of <paramref name="database"/> that were made by this tool (per msdb).</summary>
    public async Task<IReadOnlyList<RetentionCandidate>> GetToolBackupsAsync(string database, CancellationToken ct = default)
    {
        await using var cn = await OpenAsync(ct);
        const string sql = """
            SELECT b.backup_finish_date, mf.physical_device_name
            FROM msdb.dbo.backupset b
            JOIN msdb.dbo.backupmediafamily mf ON mf.media_set_id = b.media_set_id
            WHERE b.database_name = @db AND b.type = 'D' AND mf.device_type = 2
              AND (b.name = @setName OR b.name LIKE N'%(SqlBackupTool)');
            """;
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@db", database);
        cmd.Parameters.AddWithValue("@setName", BackupScript.BackupSetName);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var list = new List<RetentionCandidate>();
        while (await r.ReadAsync(ct)) list.Add(new RetentionCandidate(r.GetDateTime(0), r.GetString(1)));
        return list;
    }

    /// <summary>
    /// Deletes old backup files made by this tool. Local server: File.Delete. Remote server: xp_delete_file
    /// (runs on the server, requires sysadmin). Returns the number of files deleted.
    /// </summary>
    public async Task<int> ApplyRetentionAsync(string database, RetentionSettings rules, ServerInfo server,
        IProgress<string>? log = null, CancellationToken ct = default)
    {
        var toDelete = RetentionPolicy.SelectForDeletion(await GetToolBackupsAsync(database, ct), rules, DateTime.Now);
        if (toDelete.Count == 0)
        {
            log?.Report($"Retention: nothing to delete for [{database}] ({rules.Describe()}).");
            return 0;
        }

        var deleted = 0;
        foreach (var c in toDelete)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (server.IsLocal)
                {
                    if (!File.Exists(c.Path)) { log?.Report($"Retention: already gone {c.Path}"); continue; }
                    File.Delete(c.Path);
                }
                else
                {
                    await using var cn = await OpenAsync(ct);
                    await using var cmd = new SqlCommand("EXEC master.sys.xp_delete_file 0, @path;", cn);
                    cmd.Parameters.AddWithValue("@path", c.Path);
                    await cmd.ExecuteNonQueryAsync(ct);
                }
                deleted++;
                log?.Report($"Retention: deleted {c.Path} (from {c.Finished:yyyy-MM-dd HH:mm})");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqlException)
            {
                log?.Report($"Retention: could not delete {c.Path} — {ex.Message}");
            }
        }
        return deleted;
    }

    // ------------------------------------------------------------------ restore

    public async Task<IReadOnlyList<BackupHeader>> GetBackupHeadersAsync(string backupPath, CancellationToken ct = default)
    {
        await using var cn = await OpenAsync(ct);
        await using var cmd = new SqlCommand("RESTORE HEADERONLY FROM DISK = @path;", cn) { CommandTimeout = 0 };
        cmd.Parameters.AddWithValue("@path", backupPath);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var list = new List<BackupHeader>();
        while (await r.ReadAsync(ct))
        {
            var type = Convert.ToInt32(r["BackupType"]) switch { 1 => "Full", 2 => "Log", 5 => "Differential", var t => t.ToString() };
            list.Add(new BackupHeader((string)r["DatabaseName"], (DateTime)r["BackupFinishDate"], type, Convert.ToInt32(r["Position"])));
        }
        return list;
    }

    public async Task<IReadOnlyList<RestoreFile>> GetBackupFilesAsync(string backupPath, int position, CancellationToken ct = default)
    {
        await using var cn = await OpenAsync(ct);
        await using var cmd = new SqlCommand("RESTORE FILELISTONLY FROM DISK = @path WITH FILE = @pos;", cn) { CommandTimeout = 0 };
        cmd.Parameters.AddWithValue("@path", backupPath);
        cmd.Parameters.AddWithValue("@pos", position);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var list = new List<RestoreFile>();
        while (await r.ReadAsync(ct))
            list.Add(new RestoreFile((string)r["LogicalName"], (string)r["PhysicalName"], ((string)r["Type"]).Trim()));
        return list;
    }

    /// <summary>Restores a full backup. Never throws for SQL errors; returns a failed result instead.</summary>
    public async Task<BackupResult> RestoreAsync(RestoreRequest req, IProgress<int>? percent = null,
        IProgress<string>? log = null, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        BackupResult Fail(string error) => new(req.TargetDatabase, false, req.BackupPath, sw.Elapsed, error);

        if (string.IsNullOrWhiteSpace(req.TargetDatabase)) return Fail("Target database name is empty.");
        if (BackupScript.SystemDatabases.Contains(req.TargetDatabase))
            return Fail("Restoring system databases is not supported by this tool.");

        var singleUserSet = false;
        try
        {
            await using var cn = await OpenAsync(ct);
            AttachMessages(cn, percent, log);

            var exists = await DatabaseExistsAsync(req.TargetDatabase, ct);
            if (exists && !req.Replace)
                return Fail($"Database [{req.TargetDatabase}] already exists. Enable overwrite to replace it.");

            if (exists && req.CloseConnections)
            {
                log?.Report($"Closing connections to [{req.TargetDatabase}] (SINGLE_USER WITH ROLLBACK IMMEDIATE)...");
                await ExecAlterAsync(cn, req.TargetDatabase, "SINGLE_USER WITH ROLLBACK IMMEDIATE", ct);
                singleUserSet = true;
            }

            log?.Report($"RESTORE DATABASE [{req.TargetDatabase}] FROM DISK = '{req.BackupPath}' (FILE = {req.Position})");
            foreach (var m in req.Moves) log?.Report($"  MOVE '{m.LogicalName}' TO '{m.TargetPath}'");

            await using (var cmd = new SqlCommand(BackupScript.BuildRestoreSql(req.Moves.Count, req.Replace), cn) { CommandTimeout = 0 })
            {
                cmd.Parameters.AddWithValue("@db", req.TargetDatabase);
                cmd.Parameters.AddWithValue("@path", req.BackupPath);
                cmd.Parameters.AddWithValue("@pos", req.Position);
                for (var i = 0; i < req.Moves.Count; i++)
                {
                    cmd.Parameters.AddWithValue($"@l{i}", req.Moves[i].LogicalName);
                    cmd.Parameters.AddWithValue($"@p{i}", req.Moves[i].TargetPath);
                }
                await cmd.ExecuteNonQueryAsync(ct);
            }
            percent?.Report(100);
            return new BackupResult(req.TargetDatabase, true, req.BackupPath, sw.Elapsed, null);
        }
        catch (Exception ex) when (ex is OperationCanceledException || (ex is SqlException && ct.IsCancellationRequested))
        {
            return Fail("Cancelled by user. The database may be left in RESTORING state.");
        }
        catch (SqlException ex)
        {
            return Fail(ex.Message);
        }
        finally
        {
            if (singleUserSet)
            {
                try
                {
                    await using var cn = await OpenAsync(CancellationToken.None);
                    await ExecAlterAsync(cn, req.TargetDatabase, "MULTI_USER", CancellationToken.None);
                }
                catch (SqlException) { /* database may be restoring/absent; nothing more we can do */ }
            }
        }
    }

    private static async Task ExecAlterAsync(SqlConnection cn, string database, string option, CancellationToken ct)
    {
        // Identifier via QUOTENAME; option is one of our own constants.
        var sql = $"IF DB_ID(@db) IS NOT NULL BEGIN DECLARE @s nvarchar(max) = N'ALTER DATABASE ' + QUOTENAME(@db) + N' SET {option}'; EXEC (@s); END";
        await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 0 };
        cmd.Parameters.AddWithValue("@db", database);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
