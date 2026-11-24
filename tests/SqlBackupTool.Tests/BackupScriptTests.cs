using SqlBackupTool.Core;

namespace SqlBackupTool.Tests;

public class BackupScriptTests
{
    private static readonly DateTime Ts = new(2026, 11, 24, 13, 5, 9);

    [Fact]
    public void BuildFileName_ReplacesTokens()
        => Assert.Equal("Sales_20261124_130509.bak", BackupScript.BuildFileName("{database}_{timestamp}.bak", "Sales", Ts, "yyyyMMdd_HHmmss"));

    [Fact]
    public void BuildFileName_SanitizesInvalidCharacters()
        => Assert.Equal("a_b_c_20261124.bak", BackupScript.BuildFileName("{database}_{timestamp}.bak", "a/b:c", Ts, "yyyyMMdd"));

    [Fact]
    public void BuildFileName_AddsBakExtension()
        => Assert.Equal("X.bak", BackupScript.BuildFileName("{database}", "X", Ts, "yyyy"));

    [Theory]
    [InlineData(@"D:\Backups", @"D:\Backups\f.bak")]
    [InlineData(@"D:\Backups\", @"D:\Backups\f.bak")]
    [InlineData(@"\\nas\sql", @"\\nas\sql\f.bak")]
    [InlineData("/var/opt/mssql/backup/", "/var/opt/mssql/backup/f.bak")]
    public void CombineServerPath_UsesFolderSeparator(string folder, string expected)
        => Assert.Equal(expected, BackupScript.CombineServerPath(folder, "f.bak"));

    [Fact]
    public void CombineServerPath_RejectsEmpty()
        => Assert.Throws<ArgumentException>(() => BackupScript.CombineServerPath("  ", "f.bak"));

    [Fact]
    public void BuildBackupSql_IsParameterisedAndHonoursOptions()
    {
        var sql = BackupScript.BuildBackupSql(new BackupOptions("x", CopyOnly: true, Compression: false, Checksum: true, Verify: false));
        Assert.StartsWith("BACKUP DATABASE @db TO DISK = @path WITH", sql);
        Assert.Contains("COPY_ONLY", sql);
        Assert.Contains("CHECKSUM", sql);
        Assert.DoesNotContain("COMPRESSION", sql);
    }

    [Fact]
    public void BuildVerifySql_AddsChecksumOnlyWhenEnabled()
    {
        Assert.EndsWith("WITH CHECKSUM;", BackupScript.BuildVerifySql(new BackupOptions("x", true, true, true, true)));
        Assert.Equal("RESTORE VERIFYONLY FROM DISK = @path;", BackupScript.BuildVerifySql(new BackupOptions("x", true, true, false, true)));
    }

    [Theory]
    [InlineData("10 percent processed.", 10)]
    [InlineData("100 percent processed.", 100)]
    [InlineData("Processed 360 pages for database 'X'", null)]
    public void ParsePercent(string msg, int? expected) => Assert.Equal(expected, BackupScript.ParsePercent(msg));
}
