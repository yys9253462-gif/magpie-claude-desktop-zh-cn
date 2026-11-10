using System.Diagnostics;

namespace ClaudeZhStandalone;

public sealed class MainForm : Form
{
    private readonly Label status = new() { Dock = DockStyle.Fill, Text = "选择界面语言后，点击“开始汉化”。", Font = new Font("Microsoft YaHei UI", 10F) };
    private readonly TextBox log = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9F), BackColor = Color.FromArgb(35, 32, 34), ForeColor = Color.FromArgb(239, 235, 226), BorderStyle = BorderStyle.None, Padding = new Padding(12) };
    private readonly Button restore = new() { Text = "恢复官方文件", AutoSize = true, Padding = new Padding(15, 9, 15, 9), FlatStyle = FlatStyle.Flat, BackColor = Color.White };
    private readonly Button close = new() { Text = "退出", AutoSize = true, Padding = new Padding(15, 9, 15, 9), FlatStyle = FlatStyle.Flat, BackColor = Color.White };
    private readonly Button install = new() { Text = "开始汉化  →", AutoSize = true, Padding = new Padding(22, 10, 22, 10), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(181, 86, 63), ForeColor = Color.White, Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold) };
    private readonly CheckBox ownership = new() { Text = "允许在 Store 目录无法写入时接管 WindowsApps 所有权（恢复不会自动还原 ACL）", AutoSize = true, ForeColor = Color.FromArgb(106, 74, 45) };
    private readonly ProgressBar progress = new() { Dock = DockStyle.Top, Style = ProgressBarStyle.Marquee, Height = 4, Visible = false };
    private readonly ComboBox language = new() { DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Microsoft YaHei UI", 11F), Width = 310 };
    private bool busy;

    public MainForm()
    {
        Text = "Claude Desktop 中文助手";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(900, 720);
        MinimumSize = new Size(760, 600);
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = Color.FromArgb(249, 247, 244);
        Icon = AppIcon.Create();
        ShowIcon = true;

        var brand = new PictureBox { Image = AppIcon.Create(96).ToBitmap(), Size = new Size(62, 62), SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(0, 0, 14, 0) };
        var title = new Label { Text = "Claude Desktop 中文助手", Font = new Font("Microsoft YaHei UI", 19F, FontStyle.Bold), AutoSize = true, ForeColor = Color.FromArgb(51, 42, 42) };
        var intro = new Label { Text = "选择语言，再开始汉化 · 独立 Windows 版", AutoSize = true, ForeColor = Color.FromArgb(127, 104, 99), Padding = new Padding(0, 7, 0, 0) };
        var titleGroup = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) };
        titleGroup.Controls.AddRange([title, intro]);
        var brandGroup = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(26, 24, 26, 16), WrapContents = false };
        brandGroup.Controls.AddRange([brand, titleGroup]);

        var languageTitle = new Label { Text = "01  选择 Claude 的界面语言", Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold), AutoSize = true, ForeColor = Color.FromArgb(72, 57, 54) };
        language.Items.AddRange(["简体中文（推荐）  ·  zh-CN", "繁體中文（台灣）  ·  zh-TW", "繁體中文（香港）  ·  zh-HK"]);
        language.SelectedIndex = 0;
        var languageHint = new Label { Text = "点击“开始汉化”后才会关闭并修改 Claude；默认不自动执行。", AutoSize = true, ForeColor = Color.FromArgb(143, 86, 42), Padding = new Padding(0, 10, 0, 0) };
        var languageCard = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(28, 12, 28, 18), BackColor = Color.White };
        languageCard.Controls.AddRange([languageTitle, language, languageHint]);
        language.Margin = new Padding(0, 12, 0, 0);

        var actionGroup = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(28, 4, 28, 14) };
        actionGroup.Controls.Add(install);
        install.FlatAppearance.BorderSize = 0;
        install.Click += async (_, _) => await RunAsync("install");

        var logTitle = new Label { Text = "02  执行进度与日志", Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold), AutoSize = true, ForeColor = Color.FromArgb(72, 57, 54), Padding = new Padding(0, 0, 0, 10) };
        var top = new Panel { Dock = DockStyle.Top, Height = 48, Padding = new Padding(2, 8, 2, 8) };
        top.Controls.Add(status);
        var logGroup = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(28, 4, 28, 10) };
        logGroup.Controls.Add(logTitle);
        logGroup.Controls.Add(top);
        logGroup.Controls.Add(log);
        log.Dock = DockStyle.Fill;
        logGroup.Resize += (_, _) => { log.Height = Math.Max(130, logGroup.ClientSize.Height - logTitle.Height - top.Height - 16); };

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(24, 14, 24, 14), FlowDirection = FlowDirection.RightToLeft, BackColor = Color.FromArgb(249, 247, 244) };
        bottom.Controls.Add(close);
        bottom.Controls.Add(restore);
        close.Click += (_, _) => Close();
        restore.Click += async (_, _) =>
        {
            if (MessageBox.Show("将关闭 Claude，并使用备份恢复官方文件。继续吗？", "恢复官方文件", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                await RunAsync("uninstall");
        };
        ownership.Dock = DockStyle.Bottom;
        ownership.Padding = new Padding(28, 5, 28, 8);

        Controls.Add(logGroup);
        Controls.Add(actionGroup);
        Controls.Add(languageCard);
        Controls.Add(progress);
        Controls.Add(ownership);
        Controls.Add(bottom);
        Controls.Add(brandGroup);
        FormClosing += (_, e) => { if (busy) { e.Cancel = true; MessageBox.Show("补丁正在执行，请等待结束后再退出。", "请稍候", MessageBoxButtons.OK, MessageBoxIcon.Information); } };
    }

    private string LanguageCode => language.SelectedIndex switch { 1 => "zh-TW", 2 => "zh-HK", _ => "zh-CN" };
    private string LanguageName => language.SelectedIndex switch { 1 => "繁體中文（台灣）", 2 => "繁體中文（香港）", _ => "简体中文" };

    private async Task RunAsync(string action)
    {
        if (busy) return;
        busy = true;
        install.Enabled = restore.Enabled = language.Enabled = ownership.Enabled = false;
        progress.Visible = true;
        status.Text = action == "install" ? $"正在安装{LanguageName}，请稍候…" : "正在恢复官方文件…";
        log.Clear();
        AppendLog(action == "install" ? $"开始：检测 Claude 并安装{LanguageName}（{LanguageCode}）。" : "开始：从备份恢复官方文件。");
        try
        {
            var result = await Installer.RunAsync(
                action, LanguageCode, ownership.Checked,
                Path.Combine(Path.GetTempPath(), "ClaudeDesktop-ZH-CN"),
                Path.Combine(Installer.PayloadRoot, "scripts"),
                Path.Combine(Installer.PayloadRoot, "resources"),
                AppendLog);
            var success = result.ExitCode == 0;
            status.Text = success
                ? action == "install" ? $"{LanguageName}安装完成，Claude 已重新打开。" : "已恢复官方文件，请重新打开 Claude。"
                : "安装未完成，请查看日志。可先恢复官方文件后重试。";
            AppendLog(success ? $"执行器返回成功。若界面未切换，请在 Claude 的 Language 中选择{LanguageName}。" : $"执行器退出码：{result.ExitCode}");
            AppendLog($"日志：{result.LogPath}");
        }
        catch (Exception ex)
        {
            status.Text = "执行失败，请查看日志。";
            AppendLog(ex.ToString());
        }
        finally
        {
            busy = false;
            progress.Visible = false;
            install.Enabled = restore.Enabled = language.Enabled = ownership.Enabled = true;
        }
    }

    private void AppendLog(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (InvokeRequired) { BeginInvoke(() => AppendLog(text)); return; }
        log.AppendText(text + Environment.NewLine);
    }
}
