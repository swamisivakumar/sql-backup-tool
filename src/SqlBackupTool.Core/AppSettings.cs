using System.Text.Json;
using System.Text.Json.Serialization;

namespace SqlBackupTool.Core;

public sealed class AppSettings
{
    public List<ServerProfile> Servers { get; set; } = new();
    public string? DefaultServer { get; set; }
    public BackupSettings Backup { get; set; } = new();

    /// <summary>Legacy v1 format (single connection string). Migrated into <see cref="Servers"/> on load.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? ConnectionStrings { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
    };

    public static AppSettings Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Configuration file not found: {path}", path);
        var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions)
                ?? throw new InvalidOperationException("Configuration file is empty.");
        s.Normalize();
        return s;
    }

    public static AppSettings Parse(string json)
    {
        var s = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        s.Normalize();
        return s;
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>Writes atomically (temp file + replace) so a crash never leaves a half-written config.</summary>
    public void Save(string path)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, ToJson());
        File.Move(tmp, path, overwrite: true);
    }

    public AppSettings Clone() => Parse(ToJson());

    private void Normalize()
    {
        if (Servers.Count == 0 && ConnectionStrings is not null &&
            ConnectionStrings.TryGetValue("SqlServer", out var cs) && !string.IsNullOrWhiteSpace(cs))
        {
            Servers.Add(new ServerProfile { Name = "Default", ConnectionString = cs });
            DefaultServer ??= "Default";
        }
        ConnectionStrings = null;
        Backup ??= new BackupSettings();
        Backup.Retention ??= new RetentionSettings();
    }

    public ServerProfile? FindServer(string? name) =>
        name is null ? null : Servers.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Named server, or the default server, or the first one.</summary>
    public ServerProfile GetServer(string? name = null)
    {
        if (name is not null)
            return FindServer(name) ?? throw new InvalidOperationException($"Server '{name}' is not defined in appsettings.json.");
        return FindServer(DefaultServer) ?? Servers.FirstOrDefault()
               ?? throw new InvalidOperationException("No servers are defined in appsettings.json.");
    }

    /// <summary>Folder precedence: server override → global default → SQL Server's default backup path.</summary>
    public string? ResolveFolder(ServerProfile profile, ServerInfo? server) =>
        !string.IsNullOrWhiteSpace(profile.BackupFolder) ? profile.BackupFolder :
        !string.IsNullOrWhiteSpace(Backup.DefaultFolder) ? Backup.DefaultFolder :
        server?.DefaultBackupPath;
}

public sealed class ServerProfile
{
    public string Name { get; set; } = "";

    /// <summary>Plain connection string, or DPAPI-protected value ("dpapi:user:..." / "dpapi:machine:...").</summary>
    public string ConnectionString { get; set; } = "";

    /// <summary>Optional backup folder for this server (overrides Backup.DefaultFolder).</summary>
    public string? BackupFolder { get; set; }

    [JsonIgnore] public bool IsProtected => SecretProtector.IsProtected(ConnectionString);

    public string GetConnectionString() => SecretProtector.Unprotect(ConnectionString);

    public override string ToString() => Name;
}

public sealed class BackupSettings
{
    /// <summary>Folder on the SQL Server machine. Empty = server's default backup folder.</summary>
    public string DefaultFolder { get; set; } = "";
    public string FileNamePattern { get; set; } = "{database}_{timestamp}.bak";
    public string TimestampFormat { get; set; } = "yyyyMMdd_HHmmss";
    public bool CopyOnly { get; set; } = true;
    public bool Compression { get; set; } = true;
    public bool Checksum { get; set; } = true;
    public bool Verify { get; set; } = true;
    public bool IncludeSystemDatabases { get; set; }
    public RetentionSettings Retention { get; set; } = new();
}

public sealed class RetentionSettings
{
    /// <summary>Default state of the "apply retention" option.</summary>
    public bool Enabled { get; set; }

    /// <summary>Always keep at least this many of the newest backups per database (0 = no count rule; newest is always kept).</summary>
    public int KeepLast { get; set; } = 7;

    /// <summary>Only delete backups older than this many days (0 = no age rule).</summary>
    public int OlderThanDays { get; set; } = 30;

    [JsonIgnore] public bool IsActive => KeepLast > 0 || OlderThanDays > 0;

    public string Describe() => (KeepLast > 0, OlderThanDays > 0) switch
    {
        (true, true) => $"keep newest {KeepLast}, delete the rest once older than {OlderThanDays} days",
        (true, false) => $"keep newest {KeepLast} per database",
        (false, true) => $"delete backups older than {OlderThanDays} days (newest always kept)",
        _ => "no rule configured",
    };
}
