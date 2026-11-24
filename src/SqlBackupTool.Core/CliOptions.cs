using System.Text;

namespace SqlBackupTool.Core;

public enum CliCommand { Gui, Backup, List, ProtectConfig, Help }

/// <summary>Command-line arguments. No arguments = start the GUI.</summary>
public sealed class CliOptions
{
    public CliCommand Command { get; private set; } = CliCommand.Gui;
    public string? Server { get; private set; }
    public List<string> Databases { get; } = new();
    public bool All { get; private set; }
    public string? Folder { get; private set; }
    public bool? CopyOnly { get; private set; }
    public bool? Compression { get; private set; }
    public bool? Checksum { get; private set; }
    public bool? Verify { get; private set; }
    public bool? Retention { get; private set; }
    public bool MachineScope { get; private set; }
    public string? Error { get; private set; }

    public const string Usage = """
        SQL Server Backup Tool

        Usage:
          SqlBackupTool.exe                                   Start the Windows application
          SqlBackupTool.exe --backup [options]                Back up databases without UI
          SqlBackupTool.exe --list [--server NAME]            List databases
          SqlBackupTool.exe --protect-config [--machine]      Encrypt all plain connection strings in appsettings.json
          SqlBackupTool.exe --help

        Backup options:
          --server NAME            Server profile from appsettings.json (default: DefaultServer)
          --db NAME                Database to back up (repeatable)
          --dbs A,B,C              Comma-separated list of databases
          --all                    All online user databases
          --folder PATH            Backup folder on the SQL Server machine
          --[no-]copy-only  --[no-]compression  --[no-]checksum  --[no-]verify  --[no-]retention
                                   Override the defaults from appsettings.json
          --silent                 Accepted for compatibility (the command line never shows dialogs)

        Exit codes: 0 = all succeeded, 1 = one or more backups failed, 2 = invalid arguments/config, 3 = connection failed
        """;

    public static CliOptions Parse(IReadOnlyList<string> args)
    {
        var o = new CliOptions();
        if (args.Count == 0) return o;

        string? Next(ref int i, string flag)
        {
            if (i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal)) return args[++i];
            o.Error ??= $"Missing value for {flag}.";
            return null;
        }

        for (var i = 0; i < args.Count; i++)
        {
            var a = args[i].Trim();
            var lower = a.ToLowerInvariant();
            switch (lower)
            {
                case "--backup": o.Command = CliCommand.Backup; break;
                case "--list": o.Command = CliCommand.List; break;
                case "--protect-config": o.Command = CliCommand.ProtectConfig; break;
                case "--help" or "-h" or "-?" or "/?": o.Command = CliCommand.Help; break;
                case "--server": o.Server = Next(ref i, a); break;
                case "--db": if (Next(ref i, a) is { } db) o.Databases.Add(db); break;
                case "--dbs":
                    if (Next(ref i, a) is { } list)
                        o.Databases.AddRange(list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                    break;
                case "--all": o.All = true; break;
                case "--folder": o.Folder = Next(ref i, a); break;
                case "--machine": o.MachineScope = true; break;
                case "--silent": break;
                case "--copy-only": o.CopyOnly = true; break;
                case "--no-copy-only": o.CopyOnly = false; break;
                case "--compression": o.Compression = true; break;
                case "--no-compression": o.Compression = false; break;
                case "--checksum": o.Checksum = true; break;
                case "--no-checksum": o.Checksum = false; break;
                case "--verify": o.Verify = true; break;
                case "--no-verify": o.Verify = false; break;
                case "--retention": o.Retention = true; break;
                case "--no-retention": o.Retention = false; break;
                default: o.Error ??= $"Unknown argument: {a}"; break;
            }
        }

        // "--db X" alone implies --backup
        if (o.Command == CliCommand.Gui && (o.Databases.Count > 0 || o.All)) o.Command = CliCommand.Backup;
        // "--server X" alone opens the GUI on that server; anything invalid shows help.
        if (o.Command == CliCommand.Gui && o.Error is not null) o.Command = CliCommand.Help;
        if (o.Command == CliCommand.Backup && o.Databases.Count == 0 && !o.All)
            o.Error ??= "Specify --db NAME, --dbs A,B or --all.";
        return o;
    }

    /// <summary>Builds the arguments for an unattended backup with every option spelled out explicitly.</summary>
    public static string BuildBackupArguments(string server, IReadOnlyList<string> databases, BackupOptions o)
    {
        var sb = new StringBuilder("--backup --server ").Append(Quote(server));
        if (databases.Any(d => d.Contains(',')))
            foreach (var d in databases) sb.Append(" --db ").Append(Quote(d));
        else
            sb.Append(" --dbs ").Append(Quote(string.Join(",", databases)));
        sb.Append(" --folder ").Append(Quote(o.Folder));
        sb.Append(o.CopyOnly ? " --copy-only" : " --no-copy-only");
        sb.Append(o.Compression ? " --compression" : " --no-compression");
        sb.Append(o.Checksum ? " --checksum" : " --no-checksum");
        sb.Append(o.Verify ? " --verify" : " --no-verify");
        sb.Append(o.ApplyRetention ? " --retention" : " --no-retention");
        return sb.ToString();
    }

    /// <summary>Quotes one argument using the Windows (CommandLineToArgvW) rules.</summary>
    public static string Quote(string arg)
    {
        if (arg.Length > 0 && !arg.Any(c => char.IsWhiteSpace(c) || c == '"')) return arg;
        var sb = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var c in arg)
        {
            if (c == '\\') { backslashes++; continue; }
            if (c == '"') { sb.Append('\\', backslashes * 2 + 1).Append('"'); backslashes = 0; continue; }
            sb.Append('\\', backslashes).Append(c);
            backslashes = 0;
        }
        sb.Append('\\', backslashes * 2).Append('"');
        return sb.ToString();
    }
}
