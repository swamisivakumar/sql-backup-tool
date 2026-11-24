using SqlBackupTool.Core;

namespace SqlBackupTool.App;

/// <summary>Edits servers and backup defaults; saves to appsettings.json on OK.</summary>
public sealed class SettingsForm : Form
{
    private readonly AppSettings _work;
    private readonly string _configPath;

    private readonly ListBox _lstServers = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly TextBox _txtFolder = new() { PlaceholderText = "empty = SQL Server's default backup folder" };
    private readonly TextBox _txtPattern = new();
    private readonly TextBox _txtTimestamp = new();
    private readonly CheckBox _chkCopyOnly = new() { Text = "Copy-only", AutoSize = true };
    private readonly CheckBox _chkCompression = new() { Text = "Compression", AutoSize = true };
    private readonly CheckBox _chkChecksum = new() { Text = "Checksum", AutoSize = true };
    private readonly CheckBox _chkVerify = new() { Text = "Verify after backup", AutoSize = true };
    private readonly CheckBox _chkSystem = new() { Text = "Show system databases (master, model, msdb)", AutoSize = true };
    private readonly CheckBox _chkRetention = new() { Text = "Apply retention after each successful backup (default)", AutoSize = true };
    private readonly NumericUpDown _numKeep = new() { Minimum = 0, Maximum = 1000, Width = 80 };
    private readonly NumericUpDown _numDays = new() { Minimum = 0, Maximum = 3650, Width = 80 };
    private readonly Label _lblRetention = new() { AutoSize = true, MaximumSize = new Size(700, 0), ForeColor = SystemColors.GrayText };

    public AppSettings Result => _work;

    public SettingsForm(AppSettings current, string configPath)
    {
        _work = current.Clone();
        _configPath = configPath;
        Text = "Settings";
        Ui.ApplyDialogDefaults(this, new Size(800, 600));

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildServersTab());
        tabs.TabPages.Add(BuildBackupTab());

        var ok = new Button { Text = "Save", AutoSize = true };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        ok.Click += (_, _) => Save();
        CancelButton = cancel;
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(8) };
        buttons.Controls.AddRange([cancel, ok, new Label { Text = configPath, AutoSize = true, ForeColor = SystemColors.GrayText, Padding = new Padding(0, 8, 16, 0) }]);

        Controls.Add(tabs);
        Controls.Add(buttons);
        RefreshServers();
        LoadBackup();
    }

    // ---------------------------------------------------------------- servers

    private TabPage BuildServersTab()
    {
        var add = new Button { Text = "&Add...", AutoSize = true };
        var edit = new Button { Text = "&Edit...", AutoSize = true };
        var remove = new Button { Text = "&Remove", AutoSize = true };
        var makeDefault = new Button { Text = "Set as &default", AutoSize = true };
        add.Click += (_, _) => EditServer(null);
        edit.Click += (_, _) => { if (SelectedServer() is { } s) EditServer(s); };
        _lstServers.DoubleClick += (_, _) => { if (SelectedServer() is { } s) EditServer(s); };
        remove.Click += (_, _) =>
        {
            if (SelectedServer() is not { } s) return;
            if (MessageBox.Show(this, $"Remove server '{s.Name}'?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _work.Servers.Remove(s);
            if (string.Equals(_work.DefaultServer, s.Name, StringComparison.OrdinalIgnoreCase)) _work.DefaultServer = _work.Servers.FirstOrDefault()?.Name;
            RefreshServers();
        };
        makeDefault.Click += (_, _) => { if (SelectedServer() is { } s) { _work.DefaultServer = s.Name; RefreshServers(s); } };

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, FlowDirection = FlowDirection.TopDown, AutoSize = true, Padding = new Padding(8, 0, 0, 0) };
        buttons.Controls.AddRange([add, edit, remove, makeDefault]);
        foreach (Control b in buttons.Controls) b.MinimumSize = new Size(120, 0);

        var hint = new Label
        {
            Dock = DockStyle.Bottom, AutoSize = true, ForeColor = SystemColors.GrayText, Padding = new Padding(0, 8, 0, 0),
            Text = "🔒 = connection string is encrypted with Windows DPAPI and can only be read on this PC (by this user, unless machine scope was chosen).",
            MaximumSize = new Size(620, 0),
        };
        var page = new TabPage("Servers") { Padding = new Padding(10) };
        page.Controls.Add(_lstServers);
        page.Controls.Add(buttons);
        page.Controls.Add(hint);
        return page;
    }

    private ServerProfile? SelectedServer() =>
        _lstServers.SelectedIndex >= 0 && _lstServers.SelectedIndex < _work.Servers.Count ? _work.Servers[_lstServers.SelectedIndex] : null;

    private void RefreshServers(ServerProfile? select = null)
    {
        var index = select is null ? Math.Max(0, _lstServers.SelectedIndex) : _work.Servers.IndexOf(select);
        _lstServers.Items.Clear();
        foreach (var s in _work.Servers)
        {
            var isDefault = string.Equals(s.Name, _work.DefaultServer, StringComparison.OrdinalIgnoreCase);
            var scope = s.IsProtected ? (SecretProtector.IsMachineScope(s.ConnectionString) ? "🔒 machine" : "🔒 user") : "plain text";
            _lstServers.Items.Add($"{s.Name}{(isDefault ? "  (default)" : "")}    [{scope}]" +
                                  (string.IsNullOrWhiteSpace(s.BackupFolder) ? "" : $"    folder: {s.BackupFolder}"));
        }
        if (_lstServers.Items.Count > 0) _lstServers.SelectedIndex = Math.Min(index, _lstServers.Items.Count - 1);
    }

    private void EditServer(ServerProfile? existing)
    {
        var others = _work.Servers.Where(s => s != existing).Select(s => s.Name);
        using var dlg = new ServerEditForm(existing, others);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        if (existing is null)
        {
            _work.Servers.Add(dlg.Result);
            _work.DefaultServer ??= dlg.Result.Name;
        }
        else
        {
            if (string.Equals(_work.DefaultServer, existing.Name, StringComparison.OrdinalIgnoreCase)) _work.DefaultServer = dlg.Result.Name;
            _work.Servers[_work.Servers.IndexOf(existing)] = dlg.Result;
        }
        RefreshServers(dlg.Result);
    }

    // ---------------------------------------------------------------- backup defaults

    private TabPage BuildBackupTab()
    {
        var retentionGrid = Ui.Form(
            ("Always keep newest:", Ui.Flow(_numKeep, Ui.Label("backups per database (0 = no count rule)"))),
            ("Delete only when older than:", Ui.Flow(_numDays, Ui.Label("days (0 = no age rule)"))));
        var retention = new GroupBox { Text = "Retention (old backup clean-up)", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8) };
        var retentionStack = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        retentionStack.Controls.Add(_chkRetention);
        retentionStack.Controls.Add(retentionGrid);
        retentionStack.Controls.Add(_lblRetention);
        retention.Controls.Add(retentionStack);
        _numKeep.ValueChanged += (_, _) => DescribeRetention();
        _numDays.ValueChanged += (_, _) => DescribeRetention();

        var grid = Ui.Form(
            ("Default backup folder:", _txtFolder),
            ("File name pattern:", _txtPattern),
            ("Timestamp format:", _txtTimestamp),
            ("Default options:", Ui.Flow(_chkCopyOnly, _chkCompression, _chkChecksum, _chkVerify)),
            ("", _chkSystem));
        grid.Dock = DockStyle.Top;

        var page = new TabPage("Backup defaults") { Padding = new Padding(10), AutoScroll = true };
        page.Controls.Add(retention);
        page.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 10 });
        page.Controls.Add(grid);
        return page;
    }

    private void LoadBackup()
    {
        var b = _work.Backup;
        _txtFolder.Text = b.DefaultFolder;
        _txtPattern.Text = b.FileNamePattern;
        _txtTimestamp.Text = b.TimestampFormat;
        _chkCopyOnly.Checked = b.CopyOnly;
        _chkCompression.Checked = b.Compression;
        _chkChecksum.Checked = b.Checksum;
        _chkVerify.Checked = b.Verify;
        _chkSystem.Checked = b.IncludeSystemDatabases;
        _chkRetention.Checked = b.Retention.Enabled;
        _numKeep.Value = Math.Clamp(b.Retention.KeepLast, 0, 1000);
        _numDays.Value = Math.Clamp(b.Retention.OlderThanDays, 0, 3650);
        DescribeRetention();
    }

    private void DescribeRetention()
    {
        var r = new RetentionSettings { KeepLast = (int)_numKeep.Value, OlderThanDays = (int)_numDays.Value };
        _lblRetention.Text = "Rule: " + r.Describe() + ". Only backups created by this tool are deleted; the newest backup is always kept. " +
                             "Remote servers need sysadmin rights (xp_delete_file).";
    }

    private void Save()
    {
        var pattern = _txtPattern.Text.Trim();
        if (!pattern.Contains("{database}", StringComparison.OrdinalIgnoreCase) || !pattern.Contains("{timestamp}", StringComparison.OrdinalIgnoreCase))
        {
            Ui.Info(this, "The file name pattern must contain {database} and {timestamp}, otherwise backups would overwrite each other.");
            return;
        }
        try
        {
            var sample = DateTime.Now.ToString(_txtTimestamp.Text);
            if (sample.Length == 0 || BackupScript.SanitizeFileName(sample) != sample) throw new FormatException();
        }
        catch (FormatException)
        {
            Ui.Info(this, "The timestamp format is invalid or produces characters not allowed in file names (e.g. ':' or '/'). Example: yyyyMMdd_HHmmss");
            return;
        }
        if (_work.Servers.Count == 0) { Ui.Info(this, "Add at least one server."); return; }

        var b = _work.Backup;
        b.DefaultFolder = _txtFolder.Text.Trim();
        b.FileNamePattern = pattern;
        b.TimestampFormat = _txtTimestamp.Text;
        b.CopyOnly = _chkCopyOnly.Checked;
        b.Compression = _chkCompression.Checked;
        b.Checksum = _chkChecksum.Checked;
        b.Verify = _chkVerify.Checked;
        b.IncludeSystemDatabases = _chkSystem.Checked;
        b.Retention.Enabled = _chkRetention.Checked;
        b.Retention.KeepLast = (int)_numKeep.Value;
        b.Retention.OlderThanDays = (int)_numDays.Value;
        if (_work.FindServer(_work.DefaultServer) is null) _work.DefaultServer = _work.Servers[0].Name;

        try
        {
            _work.Save(_configPath);
            DialogResult = DialogResult.OK;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Ui.Error(this, $"Could not save {_configPath}:\n\n{ex.Message}\n\nIf the app is installed under Program Files, run it from a writable folder.");
        }
    }
}
