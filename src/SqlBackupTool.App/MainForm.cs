using SqlBackupTool.Core;

namespace SqlBackupTool.App;

public sealed class MainForm : Form
{
    private AppSettings _settings;
    private readonly string _configPath;
    private readonly FileLogger _logger = new(Path.Combine(AppContext.BaseDirectory, "logs"));
    private BackupService? _service;
    private ServerProfile? _profile;
    private ServerInfo? _server;
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _historyCts;
    private bool _populatingServers;

    private readonly ComboBox _cboServer = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    private readonly Button _btnRefresh = new() { Text = "&Refresh", AutoSize = true };
    private readonly Button _btnSettings = new() { Text = "&Settings...", AutoSize = true };
    private readonly Label _lblServer = Ui.Label("", bold: true);
    private readonly ListView _lvDatabases = Ui.ListView(checkBoxes: true);
    private readonly ListView _lvHistory = Ui.ListView(checkBoxes: false);
    private readonly Label _lblHistory = Ui.Label("Backup history", bold: true);
    private readonly Button _btnRestore = new() { Text = "Res&tore selected backup...", AutoSize = true, Enabled = false };
    private readonly Button _btnRestoreFile = new() { Text = "Restore from &file...", AutoSize = true };
    private readonly TextBox _txtFolder = new() { Dock = DockStyle.Fill };
    private readonly Button _btnBrowse = new() { Text = "...", Width = 32 };
    private readonly CheckBox _chkCopyOnly = new() { Text = "Copy-only", AutoSize = true };
    private readonly CheckBox _chkCompression = new() { Text = "Compression", AutoSize = true };
    private readonly CheckBox _chkChecksum = new() { Text = "Checksum", AutoSize = true };
    private readonly CheckBox _chkVerify = new() { Text = "Verify after backup", AutoSize = true };
    private readonly CheckBox _chkRetention = new() { AutoSize = true };
    private readonly Button _btnBackup = new() { Text = "&Back up selected", AutoSize = true };
    private readonly Button _btnCancel = new() { Text = "&Cancel", AutoSize = true, Enabled = false };
    private readonly Button _btnCopyCmd = new() { Text = "Copy command line", AutoSize = true };
    private readonly Button _btnSchedule = new() { Text = "Sc&hedule...", AutoSize = true };
    private readonly Button _btnOpenLog = new() { Text = "Open log", AutoSize = true };
    private readonly ProgressBar _progress = new() { Dock = DockStyle.Fill, Minimum = 0, Maximum = 100 };
    private readonly Label _lblStatus = Ui.Label("Ready");
    private readonly TextBox _txtLog = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = new Font("Consolas", 9f) };
    private readonly ToolTip _tips = new();

    public MainForm(AppSettings settings, string configPath, string? initialServer)
    {
        _settings = settings;
        _configPath = configPath;

        Text = "SQL Server Backup Tool";
        Icon = Ui.AppIcon;
        Font = SystemFonts.MessageBoxFont ?? Font;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1180, 800);
        MinimumSize = new Size(900, 600);

        BuildLayout();
        ApplyBackupDefaults();
        PopulateServers(initialServer);

        _cboServer.SelectedIndexChanged += async (_, _) => { if (!_populatingServers) await ConnectAsync(resetFolder: true); };
        _btnRefresh.Click += async (_, _) => await ConnectAsync(resetFolder: false);
        _btnSettings.Click += async (_, _) => await OpenSettingsAsync();
        _btnBrowse.Click += (_, _) => BrowseFolder();
        _btnBackup.Click += async (_, _) => await RunBackupsAsync();
        _btnCancel.Click += (_, _) => { _cts?.Cancel(); _btnCancel.Enabled = false; SetStatus("Cancelling..."); };
        _btnCopyCmd.Click += (_, _) => CopyCommandLine();
        _btnSchedule.Click += (_, _) => OpenSchedule();
        _btnOpenLog.Click += (_, _) => OpenLog();
        _btnRestore.Click += async (_, _) => await OpenRestoreAsync(fromHistory: true);
        _btnRestoreFile.Click += async (_, _) => await OpenRestoreAsync(fromHistory: false);
        _lvDatabases.SelectedIndexChanged += async (_, _) => await LoadHistoryAsync();
        _lvHistory.SelectedIndexChanged += (_, _) => _btnRestore.Enabled = SelectedHistory() is { Type: "Full" } && _cts is null;
        _lvHistory.DoubleClick += async (_, _) => { if (_btnRestore.Enabled) await OpenRestoreAsync(fromHistory: true); };
        _lvDatabases.ItemCheck += (_, e) =>
        {
            if (e.NewValue == CheckState.Checked && _lvDatabases.Items[e.Index].Tag is DatabaseInfo { IsOnline: false })
                e.NewValue = CheckState.Unchecked; // offline/restoring databases cannot be backed up
        };
        Shown += async (_, _) => await ConnectAsync(resetFolder: true);
        FormClosing += (_, e) =>
        {
            if (_cts is not null && MessageBox.Show(this, "A backup is running. Cancel it and exit?", Text,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.No)
                e.Cancel = true;
            else _cts?.Cancel();
        };
    }

    // ================================================================ layout

    private void BuildLayout()
    {
        _lvDatabases.Columns.Add("Database", 210);
        _lvDatabases.Columns.Add("State", 75);
        _lvDatabases.Columns.Add("Recovery", 75);
        _lvDatabases.Columns.Add("Size (MB)", 85, HorizontalAlignment.Right);
        _lvDatabases.Columns.Add("Last full backup", 125);

        _lvHistory.Columns.Add("Finished", 130);
        _lvHistory.Columns.Add("Type", 75);
        _lvHistory.Columns.Add("Size (MB)", 80, HorizontalAlignment.Right);
        _lvHistory.Columns.Add("Compressed", 85, HorizontalAlignment.Right);
        _lvHistory.Columns.Add("Copy-only", 70);
        _lvHistory.Columns.Add("File", 420);

        var top = Ui.Flow(Ui.Label("Server:"), _cboServer, _btnRefresh, _btnSettings, _lblServer);

        var btnAll = new Button { Text = "Select all", AutoSize = true };
        var btnNone = new Button { Text = "Select none", AutoSize = true };
        btnAll.Click += (_, _) => SetAllChecked(true);
        btnNone.Click += (_, _) => SetAllChecked(false);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical };
        split.Panel1.Controls.Add(Stack(Ui.Flow(Ui.Label("Databases", bold: true), btnAll, btnNone), _lvDatabases));
        split.Panel2.Controls.Add(Stack(Ui.Flow(_lblHistory), _lvHistory, Ui.Flow(_btnRestore, _btnRestoreFile)));
        Load += (_, _) => split.SplitterDistance = (int)(split.Width * 0.47);

        var folderRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, AutoSize = true };
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        folderRow.Controls.Add(Ui.Label("Backup folder (on server):"), 0, 0);
        folderRow.Controls.Add(_txtFolder, 1, 0);
        folderRow.Controls.Add(_btnBrowse, 2, 0);

        var options = new GroupBox { Text = "Backup options", Dock = DockStyle.Fill, AutoSize = true };
        var optionsLayout = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, RowCount = 3 };
        optionsLayout.Controls.Add(folderRow, 0, 0);
        optionsLayout.Controls.Add(Ui.Flow(_chkCopyOnly, _chkCompression, _chkChecksum, _chkVerify), 0, 1);
        optionsLayout.Controls.Add(Ui.Flow(_chkRetention), 0, 2);
        options.Controls.Add(optionsLayout);

        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 7, AutoSize = true };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 4; i++) actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _btnBackup.Font = Ui.Bold;
        actions.Controls.Add(_btnBackup, 0, 0);
        actions.Controls.Add(_btnCancel, 1, 0);
        actions.Controls.Add(_progress, 2, 0);
        actions.Controls.Add(_lblStatus, 3, 0);
        actions.Controls.Add(_btnCopyCmd, 4, 0);
        actions.Controls.Add(_btnSchedule, 5, 0);
        actions.Controls.Add(_btnOpenLog, 6, 0);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, Padding = new Padding(8) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 62));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
        root.Controls.Add(top, 0, 0);
        root.Controls.Add(split, 0, 1);
        root.Controls.Add(options, 0, 2);
        root.Controls.Add(actions, 0, 3);
        root.Controls.Add(_txtLog, 0, 4);
        Controls.Add(root);

        _tips.SetToolTip(_txtFolder, "Path is resolved by the SQL Server service on the server machine, not on this PC.");
        _tips.SetToolTip(_chkCopyOnly, "Does not affect the existing differential / log backup chain (recommended for ad-hoc backups).");
        _tips.SetToolTip(_chkRetention, "Only backups made by this tool are ever deleted. Configure the rule in Settings.");
        _tips.SetToolTip(_btnCopyCmd, "Copies a command that repeats this backup unattended (for scripts or Task Scheduler).");
    }

    /// <summary>Vertical stack: auto-sized rows, except the first ListView which fills.</summary>
    private static TableLayoutPanel Stack(params Control[] rows)
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = rows.Length };
        foreach (var c in rows)
        {
            t.RowStyles.Add(c is ListView ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.AutoSize));
            t.Controls.Add(c);
        }
        return t;
    }

    private void ApplyBackupDefaults()
    {
        var b = _settings.Backup;
        _chkCopyOnly.Checked = b.CopyOnly;
        _chkCompression.Checked = b.Compression;
        _chkChecksum.Checked = b.Checksum;
        _chkVerify.Checked = b.Verify;
        _chkRetention.Checked = b.Retention.Enabled && b.Retention.IsActive;
        _chkRetention.Enabled = b.Retention.IsActive;
        _chkRetention.Text = "Delete old backups made by this tool: " + b.Retention.Describe();
    }

    private void PopulateServers(string? select)
    {
        _populatingServers = true;
        _cboServer.Items.Clear();
        foreach (var s in _settings.Servers) _cboServer.Items.Add(s);
        var target = _settings.FindServer(select) ?? _settings.FindServer(_profile?.Name) ?? _settings.GetServer();
        _cboServer.SelectedItem = target;
        _populatingServers = false;
    }

    // ================================================================ data

    private async Task ConnectAsync(bool resetFolder)
    {
        if (_cboServer.SelectedItem is not ServerProfile profile) return;
        _profile = profile;
        _server = null;
        _lvDatabases.Items.Clear();
        _lvHistory.Items.Clear();
        _lblHistory.Text = "Backup history";

        try { _service = new BackupService(profile.GetConnectionString()); }
        catch (Exception ex)
        {
            _service = null;
            _lblServer.Text = "Not connected";
            Ui.Error(this, ex.Message);
            return;
        }

        SetBusy(true);
        SetStatus($"Connecting to {_service.DataSource}...");
        try
        {
            _server = await _service.GetServerInfoAsync();
            _lblServer.Text = $"{_server.ServerName}  —  SQL Server {_server.Version}  ({_server.Edition})";

            _chkCompression.Enabled = !_server.IsExpress;
            _chkCompression.Text = _server.IsExpress ? "Compression (not supported on Express)" : "Compression";
            _chkCompression.Checked = !_server.IsExpress && _settings.Backup.Compression;

            if (resetFolder || string.IsNullOrWhiteSpace(_txtFolder.Text))
                _txtFolder.Text = _settings.ResolveFolder(profile, _server) ?? "";

            Log($"Connected to {_server.ServerName} ({_server.Edition}) as profile '{profile.Name}'.");
            await LoadDatabasesAsync();
        }
        catch (Exception ex)
        {
            _lblServer.Text = $"Not connected ({_service.DataSource})";
            SetStatus("Connection failed");
            Log("ERROR connecting: " + ex.Message);
            Ui.Error(this, $"Could not connect to '{profile.Name}':\n\n{ex.Message}\n\nCheck the connection in Settings.");
        }
        finally { SetBusy(false); }
    }

    private async Task LoadDatabasesAsync()
    {
        if (_service is null) return;
        var checkedNames = _lvDatabases.CheckedItems.Cast<ListViewItem>().Select(i => i.Text).ToHashSet();
        var selectedName = _lvDatabases.SelectedItems.Count > 0 ? _lvDatabases.SelectedItems[0].Text : null;

        var dbs = await _service.GetDatabasesAsync(_settings.Backup.IncludeSystemDatabases);
        _lvDatabases.BeginUpdate();
        _lvDatabases.Items.Clear();
        foreach (var db in dbs)
        {
            var item = new ListViewItem([db.Name, db.State, db.RecoveryModel, db.SizeMb.ToString("N2"),
                db.LastFullBackup?.ToString("yyyy-MM-dd HH:mm") ?? "never"]) { Tag = db };
            if (!db.IsOnline) item.ForeColor = SystemColors.GrayText;
            _lvDatabases.Items.Add(item);
            item.Checked = db.IsOnline && checkedNames.Contains(db.Name);
            item.Selected = db.Name == selectedName;
        }
        _lvDatabases.EndUpdate();
        SetStatus($"{dbs.Count} database(s)");
    }

    private async Task LoadHistoryAsync()
    {
        _historyCts?.Cancel();
        _btnRestore.Enabled = false;
        if (_service is null || _lvDatabases.SelectedItems.Count == 0 || _lvDatabases.SelectedItems[0].Tag is not DatabaseInfo db)
        {
            _lvHistory.Items.Clear();
            _lblHistory.Text = "Backup history";
            return;
        }

        var cts = _historyCts = new CancellationTokenSource();
        _lblHistory.Text = $"Backup history — {db.Name} (loading...)";
        try
        {
            var items = await _service.GetBackupHistoryAsync(db.Name, cts.Token);
            if (cts.IsCancellationRequested) return;
            _lvHistory.BeginUpdate();
            _lvHistory.Items.Clear();
            foreach (var h in items)
                _lvHistory.Items.Add(new ListViewItem([h.Finished.ToString("yyyy-MM-dd HH:mm:ss"), h.Type,
                    h.SizeMb.ToString("N2"), h.CompressedSizeMb?.ToString("N2") ?? "", h.IsCopyOnly ? "Yes" : "", h.Device]) { Tag = h });
            _lvHistory.EndUpdate();
            _lblHistory.Text = $"Backup history — {db.Name} ({items.Count} shown, latest first)";
        }
        catch (Exception ex) when (!cts.IsCancellationRequested)
        {
            _lblHistory.Text = $"Backup history — {db.Name} (error: {ex.Message})";
        }
        catch (OperationCanceledException) { }
    }

    private BackupHistoryItem? SelectedHistory() =>
        _lvHistory.SelectedItems.Count > 0 ? _lvHistory.SelectedItems[0].Tag as BackupHistoryItem : null;

    // ================================================================ backup

    private bool TryGetSelection(out List<string> databases, out BackupOptions options)
    {
        databases = _lvDatabases.CheckedItems.Cast<ListViewItem>().Select(i => ((DatabaseInfo)i.Tag!).Name).ToList();
        options = null!;
        if (_service is null || _server is null || _profile is null) { Ui.Info(this, "Not connected to a server."); return false; }
        if (databases.Count == 0) { Ui.Info(this, "Tick at least one database."); return false; }
        var folder = _txtFolder.Text.Trim();
        if (folder.Length == 0)
        {
            Ui.Info(this, "Enter a backup folder (a path on the SQL Server machine).");
            _txtFolder.Focus();
            return false;
        }
        options = new BackupOptions(folder, _chkCopyOnly.Checked, _chkCompression.Enabled && _chkCompression.Checked,
            _chkChecksum.Checked, _chkVerify.Checked, _chkRetention.Enabled && _chkRetention.Checked);
        return true;
    }

    private async Task RunBackupsAsync()
    {
        if (!TryGetSelection(out var databases, out var options)) return;

        _cts = new CancellationTokenSource();
        SetBusy(true);
        _btnCancel.Enabled = true;
        IReadOnlyList<BackupResult> results;
        try
        {
            var progress = new Progress<JobProgress>(p =>
            {
                _progress.Value = Math.Clamp(p.Percent, 0, 100);
                SetStatus($"[{p.Index + 1}/{p.Count}] {p.Database} — {p.Percent}%");
            });
            results = await new BackupJob(_service!, _server!, _settings.Backup)
                .RunAsync(databases, options, progress, new Progress<string>(Log), _cts.Token);
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            _btnCancel.Enabled = false;
            SetBusy(false);
        }

        var ok = results.Count(r => r.Success);
        var failed = results.Where(r => !r.Success).ToList();
        var skipped = databases.Count - results.Count;
        SetStatus($"Done: {ok} succeeded, {failed.Count} failed" + (skipped > 0 ? $", {skipped} skipped" : ""));

        var summary = $"{ok} of {databases.Count} backup(s) succeeded." +
                      (failed.Count > 0 ? "\n\nFailed:\n" + string.Join("\n", failed.Select(f => $"• {f.Database}: {f.Error}")) : "") +
                      (skipped > 0 ? $"\n\n{skipped} not started (cancelled)." : "");
        MessageBox.Show(this, summary, Text, MessageBoxButtons.OK, failed.Count > 0 || skipped > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);

        try { await LoadDatabasesAsync(); await LoadHistoryAsync(); } catch (Exception ex) { Log("Refresh failed: " + ex.Message); }
    }

    private void CopyCommandLine()
    {
        if (!TryGetSelection(out var databases, out var options)) return;
        var cmd = CliOptions.Quote(Environment.ProcessPath!) + " " + CliOptions.BuildBackupArguments(_profile!.Name, databases, options);
        Clipboard.SetText(cmd);
        Log("Command line copied to clipboard: " + cmd);
        SetStatus("Command line copied");
    }

    private void OpenSchedule()
    {
        if (!TryGetSelection(out var databases, out var options)) return;
        using var dlg = new ScheduleForm(_profile!.Name, CliOptions.BuildBackupArguments(_profile.Name, databases, options),
            _profile.IsProtected && !SecretProtector.IsMachineScope(_profile.ConnectionString));
        if (dlg.ShowDialog(this) == DialogResult.OK) Log(dlg.ResultMessage);
    }

    // ================================================================ restore / settings

    private async Task OpenRestoreAsync(bool fromHistory)
    {
        if (_service is null || _server is null) { Ui.Info(this, "Not connected to a server."); return; }
        string? path = null;
        var position = 1;
        string? sourceDb = null;
        if (fromHistory && SelectedHistory() is { } h)
        {
            path = h.Device;
            position = h.Position;
            sourceDb = _lvDatabases.SelectedItems.Count > 0 ? _lvDatabases.SelectedItems[0].Text : null;
        }

        using var dlg = new RestoreForm(_service, _server, path, position, sourceDb, _txtFolder.Text, Log);
        dlg.ShowDialog(this);
        if (dlg.Restored)
        {
            try { await LoadDatabasesAsync(); } catch (Exception ex) { Log("Refresh failed: " + ex.Message); }
        }
    }

    private async Task OpenSettingsAsync()
    {
        using var dlg = new SettingsForm(_settings, _configPath);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _settings = dlg.Result;
        ApplyBackupDefaults();
        var current = _profile?.Name;
        PopulateServers(current);
        Log("Settings saved.");
        await ConnectAsync(resetFolder: true);
    }

    // ================================================================ helpers

    private void BrowseFolder()
    {
        if (_server is { IsLocal: false })
        {
            Ui.Info(this, $"SQL Server runs on another machine ({_server.MachineName}). Type the folder path as seen from that server.");
            return;
        }
        using var dlg = new FolderBrowserDialog { InitialDirectory = _txtFolder.Text, UseDescriptionForTitle = true, Description = "Backup folder" };
        if (dlg.ShowDialog(this) == DialogResult.OK) _txtFolder.Text = dlg.SelectedPath;
    }

    private void OpenLog()
    {
        if (File.Exists(_logger.CurrentFile))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_logger.CurrentFile) { UseShellExecute = true });
        else
            Ui.Info(this, "No log file for today yet.");
    }

    private void SetAllChecked(bool value)
    {
        foreach (ListViewItem item in _lvDatabases.Items) item.Checked = value;
    }

    private void SetBusy(bool busy)
    {
        foreach (var c in new Control[] { _cboServer, _btnRefresh, _btnSettings, _btnBackup, _lvDatabases, _txtFolder,
                     _btnBrowse, _btnRestoreFile, _btnCopyCmd, _btnSchedule })
            c.Enabled = !busy;
        _btnRestore.Enabled = !busy && SelectedHistory() is { Type: "Full" };
        UseWaitCursor = busy && _cts is null;
    }

    private void SetStatus(string text) => _lblStatus.Text = text;

    private void Log(string message)
    {
        _logger.Write(message);
        _txtLog.AppendText($"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}");
    }
}
