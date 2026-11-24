namespace SqlBackupTool.Core;

public sealed record ServerInfo(
    string ServerName, string MachineName, string Version, string Edition, bool IsExpress,
    string? DefaultBackupPath, string? DefaultDataPath, string? DefaultLogPath)
{
    /// <summary>True when SQL Server runs on this PC, so server paths are also local paths.</summary>
    public bool IsLocal => MachineName.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase);
}

public sealed record DatabaseInfo(string Name, string State, string RecoveryModel, decimal SizeMb, DateTime? LastFullBackup)
{
    public bool IsOnline => State.Equals("ONLINE", StringComparison.OrdinalIgnoreCase);
}

public sealed record BackupHistoryItem(
    DateTime Started, DateTime Finished, string Type, decimal SizeMb, decimal? CompressedSizeMb,
    bool IsCopyOnly, string Device, int Position, string? Name);

public sealed record BackupOptions(string Folder, bool CopyOnly, bool Compression, bool Checksum, bool Verify, bool ApplyRetention = false);

public sealed record BackupResult(string Database, bool Success, string? FilePath, TimeSpan Duration, string? Error);

public sealed record JobProgress(int Index, int Count, string Database, int Percent);

public sealed record RetentionCandidate(DateTime Finished, string Path);

public sealed record BackupHeader(string DatabaseName, DateTime FinishDate, string Type, int Position);

/// <summary>A file inside a backup (RESTORE FILELISTONLY). Type: D=data, L=log, F=full-text, S=filestream.</summary>
public sealed record RestoreFile(string LogicalName, string PhysicalName, string Type);

public sealed record FileMove(string LogicalName, string Type, string TargetPath);

public sealed record RestoreRequest(string BackupPath, int Position, string TargetDatabase, bool Replace, bool CloseConnections, IReadOnlyList<FileMove> Moves);

/// <summary>IProgress that invokes synchronously (no SynchronizationContext capture).</summary>
public sealed class ActionProgress<T>(Action<T> action) : IProgress<T>
{
    public void Report(T value) => action(value);
}
