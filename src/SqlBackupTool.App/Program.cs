using SqlBackupTool.Core;

namespace SqlBackupTool.App;

static class Program
{
    public static string ConfigPath { get; } = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

    [STAThread]
    static int Main(string[] args)
    {
        var cli = CliOptions.Parse(args);
        if (cli.Command != CliCommand.Gui)
            return CliRunner.Run(cli, ConfigPath);

        ApplicationConfiguration.Initialize();

        AppSettings settings;
        try
        {
            settings = AppSettings.Load(ConfigPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not load configuration:\n\n{ex.Message}\n\nFile: {ConfigPath}",
                "SQL Backup Tool", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 2;
        }

        if (settings.Servers.Count == 0)
        {
            MessageBox.Show("No SQL Server connection is configured yet. Add one in the next window.",
                "SQL Backup Tool", MessageBoxButtons.OK, MessageBoxIcon.Information);
            using var dlg = new SettingsForm(settings, ConfigPath);
            if (dlg.ShowDialog() != DialogResult.OK || dlg.Result.Servers.Count == 0) return 2;
            settings = dlg.Result;
        }

        Application.Run(new MainForm(settings, ConfigPath, cli.Server));
        return 0;
    }
}
