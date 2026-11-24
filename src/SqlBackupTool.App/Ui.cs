namespace SqlBackupTool.App;

/// <summary>Small layout helpers for code-built forms.</summary>
internal static class Ui
{
    public static Icon? AppIcon { get; } = LoadIcon();

    private static Icon? LoadIcon()
    {
        try { return Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? Application.ExecutablePath); }
        catch { return null; }
    }

    public static Font Bold { get; } = new(SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont, FontStyle.Bold);

    public static Label Label(string text, bool bold = false) =>
        new() { Text = text, AutoSize = true, Padding = new Padding(0, 6, 4, 0), Font = bold ? Bold : null };

    public static ListView ListView(bool checkBoxes) => new()
    {
        View = View.Details, FullRowSelect = true, CheckBoxes = checkBoxes, MultiSelect = false,
        HideSelection = false, GridLines = true, Dock = DockStyle.Fill,
    };

    public static FlowLayoutPanel Flow(params Control[] controls)
    {
        var p = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        p.Controls.AddRange(controls);
        return p;
    }

    /// <summary>Two-column "label: control" grid.</summary>
    public static TableLayoutPanel Form(params (string label, Control control)[] rows)
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var (label, control) in rows)
        {
            if (control is TextBox or ComboBox or NumericUpDown) control.Dock = DockStyle.Fill;
            t.Controls.Add(Label(label));
            t.Controls.Add(control);
        }
        return t;
    }

    public static void ApplyDialogDefaults(Form f, Size size)
    {
        f.Icon = AppIcon;
        f.StartPosition = FormStartPosition.CenterParent;
        f.Size = size;
        f.MinimumSize = new Size(Math.Min(size.Width, 520), Math.Min(size.Height, 360));
        f.ShowInTaskbar = false;
        f.Font = SystemFonts.MessageBoxFont ?? f.Font;
    }

    public static void Error(IWin32Window owner, string message, string title = "SQL Backup Tool") =>
        MessageBox.Show(owner, message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);

    public static void Info(IWin32Window owner, string message, string title = "SQL Backup Tool") =>
        MessageBox.Show(owner, message, title, MessageBoxButtons.OK, MessageBoxIcon.Information);
}
