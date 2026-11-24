namespace SqlBackupTool.Core;

/// <summary>Backs up a list of databases one after another (+ optional retention). Shared by the UI and the command line.</summary>
public sealed class BackupJob(BackupService service, ServerInfo server, BackupSettings settings)
{
    public async Task<IReadOnlyList<BackupResult>> RunAsync(
        IReadOnlyList<string> databases, BackupOptions options,
        IProgress<JobProgress>? progress = null, IProgress<string>? log = null, CancellationToken ct = default)
    {
        var results = new List<BackupResult>();
        for (var i = 0; i < databases.Count && !ct.IsCancellationRequested; i++)
        {
            var db = databases[i];
            var index = i;
            var fileName = BackupScript.BuildFileName(settings.FileNamePattern, db, DateTime.Now, settings.TimestampFormat);
            var path = BackupScript.CombineServerPath(options.Folder, fileName);

            log?.Report($"==== [{i + 1}/{databases.Count}] {db} started");
            progress?.Report(new JobProgress(i, databases.Count, db, 0));
            var pct = new ActionProgress<int>(p => progress?.Report(new JobProgress(index, databases.Count, db, p)));

            var r = await service.BackupAsync(db, path, options, pct, log, ct);
            results.Add(r);
            log?.Report(r.Success
                ? $"==== {db}: SUCCESS in {r.Duration:hh\\:mm\\:ss} -> {r.FilePath}"
                : $"==== {db}: FAILED after {r.Duration:hh\\:mm\\:ss} — {r.Error}");

            if (r.Success && options.ApplyRetention && settings.Retention.IsActive && !ct.IsCancellationRequested)
            {
                try { await service.ApplyRetentionAsync(db, settings.Retention, server, log, ct); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    log?.Report($"Retention failed for [{db}]: {ex.Message}");
                }
            }
        }
        return results;
    }
}
