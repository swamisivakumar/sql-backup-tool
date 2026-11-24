using System.Runtime.InteropServices;
using SqlBackupTool.Core;

namespace SqlBackupTool.App;

/// <summary>Unattended mode (Task Scheduler, scripts). Writes to the parent console (if any) and the log file.</summary>
internal static class CliRunner
{
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(int processId);

    public static int Run(CliOptions o, string configPath)
    {
        AttachConsole(-1); // ATTACH_PARENT_PROCESS: lets a WinExe print to the console it was started from
        Console.WriteLine();
        try { return RunAsync(o, configPath).GetAwaiter().GetResult(); }
        finally { Console.Out.Flush(); }
    }

    private static async Task<int> RunAsync(CliOptions o, string configPath)
    {
        if (o.Command == CliCommand.Help || o.Error is not null)
        {
            if (o.Error is not null) Console.Error.WriteLine("Error: " + o.Error + Environment.NewLine);
            Console.WriteLine(CliOptions.Usage);
            return o.Error is null ? 0 : 2;
        }

        var logger = new FileLogger(Path.Combine(AppContext.BaseDirectory, "logs"));
        void Log(string m) { Console.WriteLine(m); logger.Write(m); }

        AppSettings settings;
        try { settings = AppSettings.Load(configPath); }
        catch (Exception ex) { Log("ERROR: " + ex.Message); return 2; }

        if (o.Command == CliCommand.ProtectConfig)
        {
            var n = 0;
            foreach (var s in settings.Servers.Where(s => !s.IsProtected))
            {
                s.ConnectionString = SecretProtector.Protect(s.ConnectionString, o.MachineScope);
                n++;
            }
            settings.Save(configPath);
            Log($"Encrypted {n} connection string(s) ({(o.MachineScope ? "machine" : "current user")} scope) in {configPath}.");
            return 0;
        }

        ServerProfile profile;
        BackupService svc;
        ServerInfo server;
        IReadOnlyList<DatabaseInfo> dbs;
        try
        {
            profile = settings.GetServer(o.Server);
            svc = new BackupService(profile.GetConnectionString());
        }
        catch (Exception ex) { Log("ERROR: " + ex.Message); return 2; }

        try
        {
            server = await svc.GetServerInfoAsync();
            dbs = await svc.GetDatabasesAsync(settings.Backup.IncludeSystemDatabases);
        }
        catch (Exception ex) { Log($"ERROR: Cannot connect to '{profile.Name}' ({svc.DataSource}): {ex.Message}"); return 3; }

        if (o.Command == CliCommand.List)
        {
            Console.WriteLine($"{server.ServerName} — SQL Server {server.Version} ({server.Edition})");
            Console.WriteLine($"{"Database",-40} {"State",-10} {"Recovery",-10} {"Size MB",12}  Last full backup");
            foreach (var d in dbs)
                Console.WriteLine($"{d.Name,-40} {d.State,-10} {d.RecoveryModel,-10} {d.SizeMb,12:N2}  {d.LastFullBackup?.ToString("yyyy-MM-dd HH:mm") ?? "never"}");
            return 0;
        }

        // ---- backup
        var byName = dbs.ToDictionary(d => d.Name, StringComparer.OrdinalIgnoreCase);
        var problems = new List<string>();
        var targets = new List<string>();
        foreach (var name in o.All ? dbs.Where(d => d.IsOnline).Select(d => d.Name) : o.Databases)
        {
            if (!byName.TryGetValue(name, out var d)) problems.Add($"{name}: database not found on {server.ServerName}");
            else if (!d.IsOnline) problems.Add($"{name}: database is {d.State}");
            else if (!targets.Contains(d.Name, StringComparer.OrdinalIgnoreCase)) targets.Add(d.Name);
        }

        var folder = o.Folder ?? settings.ResolveFolder(profile, server);
        if (string.IsNullOrWhiteSpace(folder)) { Log("ERROR: No backup folder configured. Use --folder."); return 2; }

        var b = settings.Backup;
        var options = new BackupOptions(folder,
            o.CopyOnly ?? b.CopyOnly,
            (o.Compression ?? b.Compression) && !server.IsExpress,
            o.Checksum ?? b.Checksum,
            o.Verify ?? b.Verify,
            o.Retention ?? b.Retention.Enabled);

        Log($"Command-line backup on {server.ServerName}: {targets.Count} database(s) to {folder}");
        foreach (var p in problems) Log("SKIPPED " + p);

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); Log("Cancelling..."); };

        var results = await new BackupJob(svc, server, b).RunAsync(targets, options, null, new ActionProgress<string>(Log), cts.Token);

        var failed = results.Count(r => !r.Success) + problems.Count + (targets.Count - results.Count);
        Log($"Finished: {results.Count(r => r.Success)} succeeded, {failed} failed/skipped.");
        return failed == 0 ? 0 : 1;
    }
}
