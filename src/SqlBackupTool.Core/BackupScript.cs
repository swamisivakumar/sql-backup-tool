using System.Text.RegularExpressions;

namespace SqlBackupTool.Core;

/// <summary>Pure helpers for file names and T-SQL text (unit-testable, no DB access).</summary>
public static partial class BackupScript
{
    /// <summary>Backup-set name written to msdb; retention only ever touches backups carrying this name.</summary>
    public const string BackupSetName = "SqlBackupTool full backup";

    private static readonly HashSet<char> InvalidChars =
        Path.GetInvalidFileNameChars().Concat(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }).ToHashSet();

    public static readonly IReadOnlySet<string> SystemDatabases =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "master", "model", "msdb", "tempdb" };

    public static string SanitizeFileName(string name)
    {
        var safe = new string(name.Select(c => InvalidChars.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim();
        return safe.Length == 0 ? "database" : safe;
    }

    public static string BuildFileName(string pattern, string database, DateTime timestamp, string timestampFormat)
    {
        var name = pattern
            .Replace("{database}", SanitizeFileName(database), StringComparison.OrdinalIgnoreCase)
            .Replace("{timestamp}", timestamp.ToString(timestampFormat), StringComparison.OrdinalIgnoreCase);
        return name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) ? name : name + ".bak";
    }

    /// <summary>Joins folder and file using the folder's own separator (SQL Server on Linux uses '/').</summary>
    public static string CombineServerPath(string folder, string fileName)
    {
        folder = folder.Trim();
        if (folder.Length == 0) throw new ArgumentException("Folder is empty.", nameof(folder));
        var sep = folder.Contains('/') && !folder.Contains('\\') ? '/' : '\\';
        return folder.TrimEnd('/', '\\') + sep + fileName;
    }

    /// <summary>Directory part of a server-side path (works for both '\' and '/').</summary>
    public static string GetServerDirectory(string path)
    {
        var i = path.LastIndexOfAny(['\\', '/']);
        return i <= 0 ? path : path[..i];
    }

    /// <summary>BACKUP statement; database name and path are passed as @db / @path parameters.</summary>
    public static string BuildBackupSql(BackupOptions o)
    {
        var with = new List<string> { "INIT", "NAME = @name", "STATS = 5" };
        if (o.CopyOnly) with.Add("COPY_ONLY");
        if (o.Compression) with.Add("COMPRESSION");
        if (o.Checksum) with.Add("CHECKSUM");
        return "BACKUP DATABASE @db TO DISK = @path WITH " + string.Join(", ", with) + ";";
    }

    public static string BuildVerifySql(BackupOptions o) =>
        "RESTORE VERIFYONLY FROM DISK = @path" + (o.Checksum ? " WITH CHECKSUM" : "") + ";";

    /// <summary>RESTORE statement with parameters @db, @path, @pos and @l{i}/@p{i} per moved file.</summary>
    public static string BuildRestoreSql(int moveCount, bool replace)
    {
        var with = new List<string> { "FILE = @pos" };
        for (var i = 0; i < moveCount; i++) with.Add($"MOVE @l{i} TO @p{i}");
        with.Add("STATS = 5");
        with.Add("RECOVERY");
        if (replace) with.Add("REPLACE");
        return "RESTORE DATABASE @db FROM DISK = @path WITH " + string.Join(", ", with) + ";";
    }

    /// <summary>
    /// New physical file names for restoring under <paramref name="targetDb"/>, so a copy never collides
    /// with the original database's files: Target.mdf, Target_1.ndf, Target_log.ldf, Target_&lt;logical&gt; …
    /// </summary>
    public static IReadOnlyList<FileMove> PlanMoves(IReadOnlyList<RestoreFile> files, string targetDb, string dataFolder, string logFolder)
    {
        var safe = SanitizeFileName(targetDb);
        int data = 0, log = 0;
        var moves = new List<FileMove>();
        foreach (var f in files)
        {
            var (folder, name) = f.Type.ToUpperInvariant() switch
            {
                "D" => (dataFolder, data++ == 0 ? $"{safe}.mdf" : $"{safe}_{data - 1}.ndf"),
                "L" => (logFolder, log++ == 0 ? $"{safe}_log.ldf" : $"{safe}_log{log - 1}.ldf"),
                _ => (dataFolder, $"{safe}_{SanitizeFileName(f.LogicalName)}"),
            };
            moves.Add(new FileMove(f.LogicalName, f.Type, CombineServerPath(folder, name)));
        }
        return moves;
    }

    /// <summary>Extracts N from SQL Server's "N percent processed." progress message.</summary>
    public static int? ParsePercent(string message)
    {
        var m = PercentRegex().Match(message);
        return m.Success ? int.Parse(m.Groups[1].Value) : null;
    }

    [GeneratedRegex(@"^\s*(\d{1,3})\s+percent processed", RegexOptions.IgnoreCase)]
    private static partial Regex PercentRegex();
}
