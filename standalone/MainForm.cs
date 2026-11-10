using System.Diagnostics;

namespace ClaudeZhStandalone;

public sealed class MainForm : Form
{
    private readonly Label status = new() { Dock = DockStyle.Fill, Text = "正在准备简体中文补丁…", Font = new Font("Microsoft YaHei UI", 10F) };
    private readonly TextBox log = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9F), BackColor = Color.FromArgb(35, 33, 30), ForeColor = Color.FromArgb(239, 235, 226) };
    private readonly Button restore = new() { Text = "恢复官方文件", AutoSize = true, Enabled = false, Padding = new Padding(12, 7, 12, 7) };
    private readonly Button close = new() { Text = "关闭", AutoSize = true, Padding = new Padding(12, 7, 12, 7) };
    private readonly CheckBox ownership = new() { Text = "允许在 Store 目录无法写入时接管 WindowsApps 所有权（恢复不会自动还原 ACL）", AutoSize = true, ForeColor = Color.FromArgb(106, 74, 45) };
    private readonly ProgressBar progress = new() { Dock = DockStyle.Top, Style = ProgressBarStyle.Marquee, Height = 5 };
    private bool busy;

    public MainForm()
    {
        Text = "Claude Desktop 一键中文";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(850, 660);
        MinimumSize = new Size(700, 520);
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = Color.FromArgb(249, 247, 243);

        var title = new Label { Text = "Claude Desktop 一键中文", Font = new Font("Microsoft YaHei UI", 19F, FontStyle.Bold), AutoSize = true };
        var intro = new Label { Text = "双击后自动安装简体中文 · 不需要 Magpie、Node.js 或 Python", AutoSize = true, ForeColor = Color.DimGray, Padding = new Padding(0, 8, 0, 14) };
        var warning = new Label { Text = "将关闭并重新打开 Claude。Store 版可能需要 Windows 管理员授权；不会自动接管目录所有权。", AutoSize = true, ForeColor = Color.FromArgb(143, 86, 42), Padding = new Padding(0, 0, 0, 12) };
        var header = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(18, 18, 18, 8) };
        header.Controls.AddRange([title, intro, warning]);

        var top = new Panel { Dock = DockStyle.Top, Height = 62, Padding = new Padding(18, 8, 18, 8) };
        top.Controls.Add(status);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(16), FlowDirection = FlowDirection.RightToLeft };
        buttons.Controls.Add(close);
        buttons.Controls.Add(restore);
        close.Click += (_, _) => Close();
        restore.Click += async (_, _) => await RunAsync("uninstall");
        ownership.Dock = DockStyle.Bottom;
        ownership.Padding = new Padding(20, 8, 20, 12);

        Controls.Add(log);
        Controls.Add(progress);
        Controls.Add(top);
        Controls.Add(ownership);
        Controls.Add(buttons);
        Controls.Add(header);
        log.BringToFront();
        Shown += async (_, _) => await RunAsync("install");
        FormClosing += (_, e) => { if (busy) { e.Cancel = true; MessageBox.Show("补丁正在执行，请等待结束后再关闭。", "请稍候", MessageBoxButtons.OK, MessageBoxIcon.Information); } };
    }

    private async Task RunAsync(string action)
    {
        if (busy) return;
        busy = true;
        restore.Enabled = false;
        ownership.Enabled = false;
        progress.Visible = true;
        status.Text = action == "install" ? "正在自动安装简体中文，请稍候…" : "正在恢复官方文件…";
        log.Clear();
        AppendLog(action == "install" ? "开始：自动检测 Claude 并安装简体中文。" : "开始：从备份恢复官方文件。");
        try
        {
            var result = await Installer.RunAsync(
                action, "zh-CN", ownership.Checked,
                Path.Combine(Path.GetTempPath(), "ClaudeDesktop-ZH-CN"),
                Path.Combine(Installer.PayloadRoot, "scripts"),
                Path.Combine(Installer.PayloadRoot, "resources"),
                AppendLog);
            var success = result.ExitCode == 0;
            status.Text = success
                ? action == "install" ? "中文补丁安装完成，Claude 已重新打开。" : "已恢复官方文件，请重新打开 Claude。"
                : "安装未完成，请查看日志。可先恢复官方文件后重试。";
            AppendLog(success ? "执行器返回成功。若界面未切换，请在 Claude 的 Language 中选择简体中文。" : $"执行器退出码：{result.ExitCode}");
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
            restore.Enabled = true;
            ownership.Enabled = true;
        }
    }

    private void AppendLog(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (InvokeRequired) { BeginInvoke(() => AppendLog(text)); return; }
        log.AppendText(text + Environment.NewLine);
    }
}
