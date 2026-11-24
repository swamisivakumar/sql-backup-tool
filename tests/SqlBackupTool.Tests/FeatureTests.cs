using SqlBackupTool.Core;

namespace SqlBackupTool.Tests;

public class RetentionPolicyTests
{
    private static readonly DateTime Now = new(2026, 11, 24, 12, 0, 0);

    private static List<RetentionCandidate> Daily(int count) =>
        Enumerable.Range(0, count).Select(i => new RetentionCandidate(Now.AddDays(-i), $@"D:\b\db_{i}.bak")).ToList();

    [Fact]
    public void KeepLastOnly_DeletesEverythingBeyondN()
    {
        var del = RetentionPolicy.SelectForDeletion(Daily(10), new RetentionSettings { KeepLast = 3, OlderThanDays = 0 }, Now);
        Assert.Equal(7, del.Count);
        Assert.DoesNotContain(del, d => d.Path.EndsWith("db_0.bak") || d.Path.EndsWith("db_2.bak"));
    }

    [Fact]
    public void BothRules_MustAgree()
    {
        // keep 3; of the rest only those older than 5 days → indexes 6..9
        var del = RetentionPolicy.SelectForDeletion(Daily(10), new RetentionSettings { KeepLast = 3, OlderThanDays = 5 }, Now);
        Assert.Equal(["db_6.bak", "db_7.bak", "db_8.bak", "db_9.bak"], del.Select(d => Path.GetFileName(d.Path)));
    }

    [Fact]
    public void AgeOnly_AlwaysKeepsNewest()
    {
        var old = new[] { new RetentionCandidate(Now.AddDays(-100), @"D:\a.bak"), new RetentionCandidate(Now.AddDays(-90), @"D:\b.bak") };
        var del = RetentionPolicy.SelectForDeletion(old, new RetentionSettings { KeepLast = 0, OlderThanDays = 30 }, Now);
        Assert.Equal([@"D:\a.bak"], del.Select(d => d.Path));
    }

    [Fact]
    public void NoRules_DeletesNothing() =>
        Assert.Empty(RetentionPolicy.SelectForDeletion(Daily(10), new RetentionSettings { KeepLast = 0, OlderThanDays = 0 }, Now));

    [Fact]
    public void IgnoresNonBakFiles_AndDuplicatePaths()
    {
        var c = new[]
        {
            new RetentionCandidate(Now, @"D:\new.bak"),
            new RetentionCandidate(Now.AddDays(-50), @"D:\data.mdf"),
            new RetentionCandidate(Now.AddDays(-60), @"D:\old.bak"),
            new RetentionCandidate(Now.AddDays(-61), @"D:\OLD.BAK"),
        };
        var del = RetentionPolicy.SelectForDeletion(c, new RetentionSettings { KeepLast = 1, OlderThanDays = 0 }, Now);
        Assert.Single(del);
        Assert.Equal(@"D:\old.bak", del[0].Path, ignoreCase: true);
    }
}

public class RestoreScriptTests
{
    [Fact]
    public void PlanMoves_CreatesNonCollidingNames()
    {
        var files = new[]
        {
            new RestoreFile("Sales", @"C:\Data\Sales.mdf", "D"),
            new RestoreFile("Sales2", @"C:\Data\Sales2.ndf", "D"),
            new RestoreFile("Sales_log", @"C:\Logs\Sales_log.ldf", "L"),
            new RestoreFile("ft", @"C:\Data\ft", "F"),
        };
        var moves = BackupScript.PlanMoves(files, "Sales_Copy", @"E:\Data\", @"F:\Logs");
        Assert.Equal([@"E:\Data\Sales_Copy.mdf", @"E:\Data\Sales_Copy_1.ndf", @"F:\Logs\Sales_Copy_log.ldf", @"E:\Data\Sales_Copy_ft"],
            moves.Select(m => m.TargetPath));
        Assert.Equal(files.Select(f => f.LogicalName), moves.Select(m => m.LogicalName));
    }

    [Fact]
    public void BuildRestoreSql_ParameterisesEveryMove()
    {
        var sql = BackupScript.BuildRestoreSql(2, replace: true);
        Assert.Equal("RESTORE DATABASE @db FROM DISK = @path WITH FILE = @pos, MOVE @l0 TO @p0, MOVE @l1 TO @p1, STATS = 5, RECOVERY, REPLACE;", sql);
        Assert.DoesNotContain("REPLACE", BackupScript.BuildRestoreSql(1, replace: false));
    }

    [Theory]
    [InlineData(@"C:\Data\x.mdf", @"C:\Data")]
    [InlineData("/var/opt/mssql/data/x.mdf", "/var/opt/mssql/data")]
    public void GetServerDirectory(string path, string expected) => Assert.Equal(expected, BackupScript.GetServerDirectory(path));
}

public class CliOptionsTests
{
    [Fact]
    public void NoArgs_IsGui() => Assert.Equal(CliCommand.Gui, CliOptions.Parse([]).Command);

    [Fact]
    public void ServerOnly_IsGuiWithServer()
    {
        var o = CliOptions.Parse(["--server", "Prod"]);
        Assert.Equal(CliCommand.Gui, o.Command);
        Assert.Equal("Prod", o.Server);
    }

    [Fact]
    public void Backup_ParsesEverything()
    {
        var o = CliOptions.Parse(["--backup", "--server", "Local", "--db", "A", "--dbs", "B, C", "--folder", @"D:\B k", "--no-verify", "--retention", "--silent"]);
        Assert.Null(o.Error);
        Assert.Equal(CliCommand.Backup, o.Command);
        Assert.Equal(["A", "B", "C"], o.Databases);
        Assert.Equal(@"D:\B k", o.Folder);
        Assert.False(o.Verify);
        Assert.True(o.Retention);
        Assert.Null(o.CopyOnly);
    }

    [Fact]
    public void DbAlone_ImpliesBackup() => Assert.Equal(CliCommand.Backup, CliOptions.Parse(["--db", "Sales"]).Command);

    [Fact]
    public void BackupWithoutDatabases_IsError() => Assert.NotNull(CliOptions.Parse(["--backup"]).Error);

    [Fact]
    public void UnknownArgument_ShowsHelpWithError()
    {
        var o = CliOptions.Parse(["--bogus"]);
        Assert.Equal(CliCommand.Help, o.Command);
        Assert.NotNull(o.Error);
    }

    [Fact]
    public void MissingValue_IsError() => Assert.NotNull(CliOptions.Parse(["--backup", "--db"]).Error);

    [Theory]
    [InlineData("simple", "simple")]
    [InlineData("has space", "\"has space\"")]
    [InlineData(@"C:\My Backups\", "\"C:\\My Backups\\\\\"")]
    [InlineData("a\"b", "\"a\\\"b\"")]
    [InlineData("", "\"\"")]
    public void Quote_FollowsWindowsRules(string input, string expected) => Assert.Equal(expected, CliOptions.Quote(input));

    [Fact]
    public void BuildBackupArguments_RoundTripsThroughParse()
    {
        var opts = new BackupOptions(@"C:\My Backups\", CopyOnly: true, Compression: false, Checksum: true, Verify: false, ApplyRetention: true);
        var args = CliOptions.BuildBackupArguments("Local Server", ["Sales", "HR Db"], opts);
        var parsed = CliOptions.Parse(SplitWindows(args));
        Assert.Null(parsed.Error);
        Assert.Equal("Local Server", parsed.Server);
        Assert.Equal(["Sales", "HR Db"], parsed.Databases);
        Assert.Equal(@"C:\My Backups\", parsed.Folder);
        Assert.Equal((true, false, true, false, true), (parsed.CopyOnly, parsed.Compression, parsed.Checksum, parsed.Verify, parsed.Retention));
    }

    /// <summary>Minimal CommandLineToArgvW-compatible splitter for the round-trip test.</summary>
    private static string[] SplitWindows(string s)
    {
        var args = new List<string>();
        var cur = new System.Text.StringBuilder();
        bool inQ = false, any = false;
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c == '\\')
            {
                var n = 0;
                while (i < s.Length && s[i] == '\\') { n++; i++; }
                if (i < s.Length && s[i] == '"') { cur.Append('\\', n / 2); if (n % 2 == 1) cur.Append('"'); else { inQ = !inQ; any = true; } }
                else { cur.Append('\\', n); i--; }
            }
            else if (c == '"') { inQ = !inQ; any = true; }
            else if (char.IsWhiteSpace(c) && !inQ) { if (cur.Length > 0 || any) args.Add(cur.ToString()); cur.Clear(); any = false; }
            else cur.Append(c);
        }
        if (cur.Length > 0 || any) args.Add(cur.ToString());
        return args.ToArray();
    }
}

public class SettingsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SecretProtector_RoundTrips(bool machine)
    {
        const string cs = "Server=x;User ID=sa;Password=p@ss;";
        var enc = SecretProtector.Protect(cs, machine);
        Assert.True(SecretProtector.IsProtected(enc));
        Assert.Equal(machine, SecretProtector.IsMachineScope(enc));
        Assert.DoesNotContain("p@ss", enc);
        Assert.Equal(cs, SecretProtector.Unprotect(enc));
    }

    [Fact]
    public void SecretProtector_PlainPassesThrough() => Assert.Equal("Server=x;", SecretProtector.Unprotect("Server=x;"));

    [Fact]
    public void SecretProtector_TamperedValue_GivesFriendlyError() =>
        Assert.Throws<InvalidOperationException>(() => SecretProtector.Unprotect("dpapi:user:AAAA"));

    [Fact]
    public void LegacyConfig_IsMigrated()
    {
        var s = AppSettings.Parse("""{ "ConnectionStrings": { "SqlServer": "Server=old;" }, "Backup": { "Verify": false } }""");
        Assert.Single(s.Servers);
        Assert.Equal("Server=old;", s.GetServer().ConnectionString);
        Assert.False(s.Backup.Verify);
        Assert.NotNull(s.Backup.Retention);
        Assert.DoesNotContain("ConnectionStrings", s.ToJson());
    }

    [Fact]
    public void GetServer_FallsBackToDefaultThenFirst()
    {
        var s = AppSettings.Parse("""{ "Servers": [ { "Name": "A", "ConnectionString": "x" }, { "Name": "B", "ConnectionString": "y" } ], "DefaultServer": "b" }""");
        Assert.Equal("B", s.GetServer().Name);
        Assert.Equal("A", s.GetServer("a").Name);
        Assert.Throws<InvalidOperationException>(() => s.GetServer("zzz"));
    }

    [Fact]
    public void ResolveFolder_Precedence()
    {
        var s = new AppSettings { Backup = { DefaultFolder = @"D:\Global" } };
        var info = new ServerInfo("s", "m", "v", "e", false, @"C:\SqlDefault", null, null);
        Assert.Equal(@"E:\Server", s.ResolveFolder(new ServerProfile { BackupFolder = @"E:\Server" }, info));
        Assert.Equal(@"D:\Global", s.ResolveFolder(new ServerProfile(), info));
        s.Backup.DefaultFolder = "";
        Assert.Equal(@"C:\SqlDefault", s.ResolveFolder(new ServerProfile(), info));
    }

    [Fact]
    public void SaveAndLoad_RoundTrip()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sbt_{Guid.NewGuid():N}.json");
        try
        {
            var s = new AppSettings { DefaultServer = "A", Servers = { new ServerProfile { Name = "A", ConnectionString = "x" } } };
            s.Backup.Retention.KeepLast = 3;
            s.Save(path);
            var loaded = AppSettings.Load(path);
            Assert.Equal("A", loaded.GetServer().Name);
            Assert.Equal(3, loaded.Backup.Retention.KeepLast);
        }
        finally { File.Delete(path); }
    }
}
