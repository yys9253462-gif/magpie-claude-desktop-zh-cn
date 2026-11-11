using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace ClaudeZhStandalone;

public sealed class MainForm : Form
{
    private static readonly Color Background = Color.FromArgb(247, 245, 241);
    private static readonly Color Ink = Color.FromArgb(47, 40, 42);
    private static readonly Color Muted = Color.FromArgb(124, 111, 107);
    private static readonly Color Accent = Color.FromArgb(184, 86, 65);
    private readonly Label status = new() { Dock = DockStyle.Fill, Text = "准备就绪 · 选择语言后开始", Font = new Font("Microsoft YaHei UI", 10F), ForeColor = Ink, TextAlign = ContentAlignment.MiddleLeft };
    private readonly TextBox log = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9F), BackColor = Color.FromArgb(35, 31, 36), ForeColor = Color.FromArgb(232, 224, 219), BorderStyle = BorderStyle.None };
    private readonly Button restore = new() { Text = "恢复官方文件", Dock = DockStyle.Fill, Height = 40, FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = Ink };
    private readonly Button close = new() { Text = "退出", Dock = DockStyle.Fill, Height = 40, FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = Ink };
    private readonly Button install = new() { Text = "开始汉化   →", Dock = DockStyle.Fill, Height = 48, FlatStyle = FlatStyle.Flat, BackColor = Accent, ForeColor = Color.White, Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold) };
    private readonly CheckBox ownership = new() { Text = "需要时接管 WindowsApps 目录所有权（恢复不会自动还原权限）", Dock = DockStyle.Fill, Height = 38, ForeColor = Muted };
    private readonly ProgressBar progress = new() { Dock = DockStyle.Bottom, Style = ProgressBarStyle.Marquee, Height = 4, Visible = false };
    private readonly ComboBox language = new() { DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Microsoft YaHei UI", 11F), Dock = DockStyle.Fill, Height = 36 };
    private bool busy;

    public MainForm()
    {
        Text = "Claude Desktop 中文助手";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1040, 760);
        MinimumSize = new Size(900, 660);
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = Background;
        Icon = AppIcon.Create();
        ShowIcon = true;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(0) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 114));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 190));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        Controls.Add(root);
        root.Controls.Add(Header(), 0, 0);
        root.Controls.Add(LanguageCard(), 0, 1);
        root.Controls.Add(LogCard(), 0, 2);
        root.Controls.Add(Footer(), 0, 3);

        FormClosing += (_, e) =>
        {
            if (!busy) return;
            e.Cancel = true;
            MessageBox.Show("补丁正在执行，请等待结束后再退出。", "请稍候", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
    }

    private Control Header()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(30, 24, 30, 10), BackColor = Background };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var icon = new PictureBox { Image = AppIcon.Create(96).ToBitmap(), Size = new Size(62, 62), SizeMode = PictureBoxSizeMode.Zoom, Anchor = AnchorStyles.Left };
        var titles = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(0, 3, 0, 0) };
        titles.Controls.Add(new Label { Text = "Claude Desktop 中文助手", Font = new Font("Microsoft YaHei UI", 20F, FontStyle.Bold), AutoSize = true, ForeColor = Ink });
        titles.Controls.Add(new Label { Text = "选择语言，再开始汉化  /  独立 Windows 版", Font = new Font("Microsoft YaHei UI", 9F), AutoSize = true, ForeColor = Muted, Padding = new Padding(0, 7, 0, 0) });
        layout.Controls.Add(icon, 0, 0);
        layout.Controls.Add(titles, 1, 0);
        return layout;
    }

    private Control LanguageCard()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(26, 18, 26, 16), BackColor = Color.White, Margin = new Padding(26, 0, 26, 0) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 63));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 37));
        var left = new Panel { Dock = DockStyle.Fill, Padding = new Padding(2, 0, 20, 0) };
        left.Controls.Add(new Label { Text = "01   选择界面语言", Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold), AutoSize = true, ForeColor = Ink, Location = new Point(2, 0) });
        language.Items.AddRange(["简体中文（推荐）  ·  zh-CN", "繁體中文（台灣）  ·  zh-TW", "繁體中文（香港）  ·  zh-HK"]);
        language.SelectedIndex = 0;
        language.Location = new Point(2, 48);
        language.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
        left.Controls.Add(language);
        left.Controls.Add(new Label { Text = "点击开始后才会关闭并修改 Claude。", AutoSize = true, ForeColor = Muted, Location = new Point(2, 106) });
        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 2, 2, 0) };
        install.FlatAppearance.BorderSize = 0;
        install.Click += async (_, _) => await RunAsync("install");
        right.Controls.Add(install);
        install.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        install.Location = new Point(16, 4);
        ownership.Location = new Point(16, 62);
        ownership.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        ownership.Height = 60;
        right.Controls.Add(ownership);
        layout.Controls.Add(left, 0, 0);
        layout.Controls.Add(right, 1, 0);
        return layout;
    }

    private Control LogCard()
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(26, 18, 26, 14), BackColor = Background };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, BackColor = Color.White, Padding = new Padding(18, 14, 18, 16) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label { Text = "02   执行进度与日志", Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold), ForeColor = Ink, Dock = DockStyle.Fill }, 0, 0);
        layout.Controls.Add(status, 0, 1);
        layout.Controls.Add(log, 0, 2);
        panel.Controls.Add(layout);
        panel.Controls.Add(progress);
        progress.BringToFront();
        return panel;
    }

    private Control Footer()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(26, 14, 26, 20), BackColor = Background };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        layout.Controls.Add(new Label { Text = "本地补丁 · 保留备份 · 无需 Magpie", ForeColor = Muted, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        layout.Controls.Add(restore, 1, 0);
        layout.Controls.Add(close, 2, 0);
        restore.Margin = close.Margin = new Padding(8, 0, 0, 0);
        close.Click += (_, _) => Close();
        restore.Click += async (_, _) =>
        {
            if (MessageBox.Show("将关闭 Claude，并使用备份恢复官方文件。继续吗？", "恢复官方文件", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                await RunAsync("uninstall");
        };
        return layout;
    }

    private string LanguageCode => language.SelectedIndex switch { 1 => "zh-TW", 2 => "zh-HK", _ => "zh-CN" };
    private string LanguageName => language.SelectedIndex switch { 1 => "繁體中文（台灣）", 2 => "繁體中文（香港）", _ => "简体中文" };

    private async Task RunAsync(string action)
    {
        if (busy) return;

        // 安装/恢复都要改写 Claude.exe，运行中的实例会锁住文件。
        // 先提醒用户关掉，避免到脚本里才报「无法获得写权限」这种误导提示。
        if (Installer.IsClaudeRunning())
        {
            var answer = MessageBox.Show(
                "检测到 Claude Desktop 正在运行。\n\n" +
                "安装补丁需要改写 Claude.exe，运行中的实例会锁住文件导致失败。\n\n" +
                "是否现在关闭 Claude Desktop 并继续？",
                "请先关闭 Claude", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes) return;
        }

        busy = true;
        install.Enabled = restore.Enabled = language.Enabled = ownership.Enabled = false;
        progress.Visible = true;
        status.Text = action == "install" ? $"正在安装{LanguageName}，请稍候…" : "正在恢复官方文件…";
        log.Clear();
        AppendLog(action == "install" ? $"开始：检测 Claude 并安装{LanguageName}（{LanguageCode}）。" : "开始：从备份恢复官方文件。");
        try
        {
            var result = await Installer.RunAsync(action, LanguageCode, ownership.Checked,
                Path.Combine(Path.GetTempPath(), "ClaudeDesktop-ZH-CN"),
                Path.Combine(Installer.PayloadRoot, "scripts"), Path.Combine(Installer.PayloadRoot, "resources"), AppendLog);
            var success = result.ExitCode == 0;
            status.Text = success ? action == "install" ? $"{LanguageName}安装完成，Claude 已重新打开。" : "已恢复官方文件，请重新打开 Claude。" : "安装未完成，请查看日志。可先恢复官方文件后重试。";
            AppendLog(success ? $"执行器返回成功。若界面未切换，请在 Claude 的 Language 中选择{LanguageName}。" : $"执行器退出码：{result.ExitCode}");
            if (!success && result.Output.Contains("被进程占用", StringComparison.Ordinal))
            {
                AppendLog("提示：失败原因是文件仍被进程占用，不是权限问题。请完全退出 Claude Desktop（含系统托盘图标）后重试。");
            }
            AppendLog($"日志：{result.LogPath}");
        }
        catch (Exception ex) { status.Text = "执行失败，请查看日志。"; AppendLog(ex.ToString()); }
        finally { busy = false; progress.Visible = false; install.Enabled = restore.Enabled = language.Enabled = ownership.Enabled = true; }
    }

    private void AppendLog(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (InvokeRequired) { BeginInvoke(() => AppendLog(text)); return; }
        log.AppendText(text + Environment.NewLine);
    }
}
