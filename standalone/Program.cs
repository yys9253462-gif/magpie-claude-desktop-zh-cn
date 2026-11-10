using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.IO.Compression;

namespace ClaudeZhStandalone;

public sealed record InstallResult(int ExitCode, string Output, string LogPath, bool Elevated);

public static class Installer
{
    public static string PayloadRoot => EnsurePayload();

    private static string EnsurePayload()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClaudeDesktop-ZH-CN", "payload-v1");
        var marker = Path.Combine(root, ".complete");
        if (File.Exists(marker)) return root;
        Directory.CreateDirectory(root);
        using var resource = typeof(Installer).Assembly.GetManifestResourceStream("ClaudeZhStandalone.payload.zip")
            ?? throw new FileNotFoundException("EXE 内嵌语言包缺失");
        using var archive = new System.IO.Compression.ZipArchive(resource, System.IO.Compression.ZipArchiveMode.Read);
        archive.ExtractToDirectory(root, true);
        File.WriteAllText(marker, "ok");
        return root;
    }

    public static string DetectVersion()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Claude");
            return key?.GetValue("DisplayVersion")?.ToString() ?? "已安装（版本由安装器检测）";
        }
        catch { return "已安装（版本由安装器检测）"; }
    }

    public static async Task<InstallResult> RunAsync(
        string action,
        string language,
        bool allowTakeOwnership,
        string workRoot,
        string scriptsPath,
        string resourcesPath,
        Action<string> log,
        CancellationToken cancellationToken = default)
    {
        if (action is not ("install" or "uninstall")) throw new ArgumentException("不支持的操作", nameof(action));
        if (language is not ("zh-CN" or "zh-TW" or "zh-HK")) throw new ArgumentException("不支持的语言", nameof(language));
        if (action == "uninstall" && allowTakeOwnership) throw new ArgumentException("恢复不接管目录所有权");

        var taskDir = Path.Combine(workRoot, Guid.NewGuid().ToString("N"));
        var payloadDir = Path.Combine(taskDir, "payload");
        Directory.CreateDirectory(payloadDir);
        CopyDirectory(scriptsPath, Path.Combine(payloadDir, "scripts"));
        CopyDirectory(resourcesPath, Path.Combine(payloadDir, "resources"));
        var installer = Path.Combine(payloadDir, "scripts", "install_windows.ps1");
        var logPath = Path.Combine(taskDir, "install.log");
        var arguments = new List<string>
        {
            "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Quote(installer),
            action, language,
            "-OriginalUserProfile", Quote(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
            "-OriginalAppData", Quote(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)),
            "-OriginalLocalAppData", Quote(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)),
        };
        if (allowTakeOwnership) arguments.Add("-AllowTakeOwnership");

        bool elevated = allowTakeOwnership;
        var result = await StartPowerShellAsync(arguments, logPath, elevated, log, cancellationToken);
        if (result.ExitCode != 0 && !elevated && action == "install" && NeedsElevation(result.Output))
        {
            log("普通权限无法写入 Store 目录，请求 Windows 管理员权限…");
            result = await StartPowerShellAsync(arguments, logPath, true, log, cancellationToken);
        }
        return result;
    }

    private static bool NeedsElevation(string output) =>
        output.Contains("拒绝访问", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("写入权限", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("Access is denied", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("AllowTakeOwnership", StringComparison.OrdinalIgnoreCase);

    private static async Task<InstallResult> StartPowerShellAsync(
        List<string> arguments, string logPath, bool elevated, Action<string> log, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = elevated,
            CreateNoWindow = !elevated,
            WindowStyle = elevated ? ProcessWindowStyle.Normal : ProcessWindowStyle.Hidden,
        };
        if (elevated) psi.Verb = "runas";
        psi.Environment["CLAUDE_ZH_SKIP_UPDATE_CHECK"] = "1";
        psi.Environment["CLAUDE_ZH_STAGE_LOG"] = logPath;
        psi.Environment["PYTHONUTF8"] = "1";
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        foreach (var argument in arguments) psi.ArgumentList.Add(argument);

        var output = new StringBuilder();
        try
        {
            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            if (!elevated)
            {
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.StandardOutputEncoding = Encoding.UTF8;
                psi.StandardErrorEncoding = Encoding.UTF8;
                process.OutputDataReceived += (_, e) => { if (e.Data is not null) { output.AppendLine(e.Data); log(e.Data); } };
                process.ErrorDataReceived += (_, e) => { if (e.Data is not null) { output.AppendLine(e.Data); log(e.Data); } };
            }
            if (!process.Start()) throw new InvalidOperationException("无法启动 PowerShell 安装器");
            if (!elevated) { process.BeginOutputReadLine(); process.BeginErrorReadLine(); }
            await process.WaitForExitAsync(cancellationToken);
            if (File.Exists(logPath))
            {
                var fileLog = await ReadSharedTextAsync(logPath);
                output.Clear().Append(fileLog);
                if (elevated) log(fileLog);
            }
            return new InstallResult(process.ExitCode, output.ToString(), logPath, elevated);
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return new InstallResult(1223, "已取消 Windows UAC 授权，未继续提权安装。", logPath, elevated);
        }
    }

    private static async Task<string> ReadSharedTextAsync(string path)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream, Encoding.UTF8, true);
                return await reader.ReadToEndAsync();
            }
            catch (IOException) { await Task.Delay(200); }
        }
        return "安装器已结束，但日志暂时无法读取。";
    }

    private static string Quote(string value) => value;
    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }
}
