using Microsoft.Data.SqlClient;
using SqlBackupTool.Core;

namespace SqlBackupTool.Tests;

/// <summary>
/// Runs against a real SQL Server. Set SQLBACKUP_TEST_CS to enable, e.g.
/// "Server=localhost;Integrated Security=True;TrustServerCertificate=True;". Skipped (passes trivially) when unset.
/// Creates and drops its own temporary databases; never touches existing ones.
/// </summary>
public class BackupServiceIntegrationTests
{
    private static readonly string? Cs = Environment.GetEnvironmentVariable("SQLBACKUP_TEST_CS");

    [Fact]
    public async Task EndToEnd_BackupRetentionAndRestore()
    {
        if (string.IsNullOrEmpty(Cs)) return;

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var dbName = "SqlBackupTool_Test_" + suffix;
        var copyName = dbName + "_Copy";
        var master = new SqlConnectionStringBuilder(Cs) { InitialCatalog = "master" }.ConnectionString;
        await Exec(master, $"CREATE DATABASE [{dbName}]");
        await Exec(master, $"CREATE TABLE [{dbName}].dbo.T (Id int); INSERT [{dbName}].dbo.T VALUES (42);");
        var files = new List<string>();
        try
        {
            var svc = new BackupService(Cs);
            var server = await svc.GetServerInfoAsync();
            var folder = server.DefaultBackupPath ?? throw new InvalidOperationException("No default backup path");

            var dbs = await svc.GetDatabasesAsync(includeSystem: false);
            Assert.Contains(dbs, d => d.Name == dbName && d.IsOnline);

            // --- three backups through BackupJob (unique timestamps)
            var settings = new BackupSettings { TimestampFormat = "yyyyMMdd_HHmmss_fff" };
            var opts = new BackupOptions(folder, CopyOnly: true, Compression: !server.IsExpress, Checksum: true, Verify: true);
            var percents = new List<int>();
            var job = new BackupJob(svc, server, settings);
            for (var i = 0; i < 3; i++)
            {
                var r = (await job.RunAsync([dbName], opts, new ActionProgress<JobProgress>(p => percents.Add(p.Percent)))).Single();
                Assert.True(r.Success, r.Error);
                files.Add(r.FilePath!);
                await Task.Delay(1100); // msdb finish dates have 1s resolution
            }
            Assert.Contains(100, percents);

            var history = await svc.GetBackupHistoryAsync(dbName);
            Assert.Contains(history, h => h.Device == files[0] && h.IsCopyOnly && h.Type == "Full" && h.Name == BackupScript.BackupSetName);

            // --- retention: keep newest 1 → two oldest files removed (server is local in this test setup)
            if (server.IsLocal)
            {
                var deleted = await svc.ApplyRetentionAsync(dbName, new RetentionSettings { KeepLast = 1, OlderThanDays = 0 }, server);
                Assert.Equal(2, deleted);
                Assert.False(File.Exists(files[0]));
                Assert.False(File.Exists(files[1]));
                Assert.True(File.Exists(files[2]));
            }

            // --- restore newest backup as a copy with relocated files
            var headers = await svc.GetBackupHeadersAsync(files[2]);
            Assert.Equal(dbName, headers.Single().DatabaseName);
            var bakFiles = await svc.GetBackupFilesAsync(files[2], 1);
            var moves = BackupScript.PlanMoves(bakFiles, copyName,
                server.DefaultDataPath ?? BackupScript.GetServerDirectory(bakFiles[0].PhysicalName),
                server.DefaultLogPath ?? BackupScript.GetServerDirectory(bakFiles[0].PhysicalName));
            var restore = await svc.RestoreAsync(new RestoreRequest(files[2], 1, copyName, Replace: false, CloseConnections: true, moves));
            Assert.True(restore.Success, restore.Error);
            Assert.Equal(42, await Scalar(master, $"SELECT Id FROM [{copyName}].dbo.T"));

            // --- restore over an existing DB without overwrite is refused; with overwrite succeeds
            var refused = await svc.RestoreAsync(new RestoreRequest(files[2], 1, copyName, Replace: false, CloseConnections: true, moves));
            Assert.False(refused.Success);
            var replaced = await svc.RestoreAsync(new RestoreRequest(files[2], 1, copyName, Replace: true, CloseConnections: true, moves));
            Assert.True(replaced.Success, replaced.Error);

            // --- system databases are blocked
            Assert.False((await svc.RestoreAsync(new RestoreRequest(files[2], 1, "master", true, true, moves))).Success);

            // --- bad folder => failed result, not exception
            var bad = await svc.BackupAsync(dbName, @"Z:\does\not\exist\x.bak", opts with { Verify = false });
            Assert.False(bad.Success);
        }
        finally
        {
            foreach (var db in new[] { copyName, dbName })
                await Exec(master, $"IF DB_ID(N'{db}') IS NOT NULL BEGIN ALTER DATABASE [{db}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{db}]; END");
            foreach (var f in files) if (File.Exists(f)) File.Delete(f);
        }
    }

    private static async Task Exec(string cs, string sql)
    {
        await using var cn = new SqlConnection(cs);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<int> Scalar(string cs, string sql)
    {
        await using var cn = new SqlConnection(cs);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }
}
