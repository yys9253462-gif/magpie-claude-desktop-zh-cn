using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace ClaudeZhStandalone;

public sealed record InstallResult(int ExitCode, string Output, string LogPath, bool Elevated);

public static class Installer
{
    public static string PayloadRoot => EnsurePayload();

    /// <summary>
    /// 释放内嵌语言包。用包内容哈希做目录名，版本变化时自动失效旧缓存，
    /// 不会因为 %TEMP% 里残留的旧版本而报"参数不存在"。
    /// </summary>
    private static string EnsurePayload()
    {
        using var resource = typeof(Installer).Assembly.GetManifestResourceStream("ClaudeZhStandalone.payload.zip")
            ?? throw new FileNotFoundException("EXE 内嵌语言包缺失");

        using var buffer = new MemoryStream();
        resource.CopyTo(buffer);
        var bytes = buffer.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()[..16];

        var root = Path.Combine(Path.GetTempPath(), "ClaudeDesktop-ZH-CN", "payload-" + hash);
        var marker = Path.Combine(root, ".complete");
        if (File.Exists(marker)) return root;

        Directory.CreateDirectory(root);
        using (var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read))
        {
            archive.ExtractToDirectory(root, true);
        }
        File.WriteAllText(marker, hash);
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

    public static string CurrentUserSid()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.User?.Value ?? "";
        }
        catch { return ""; }
    }

    /// <summary>
    /// 检测正在运行的 Claude Desktop 进程（不含 Claude Code 的 CLI 进程）。
    /// 安装补丁需要改写 Claude.exe，运行中的实例会锁住文件，
    /// 表现是 ERROR_SHARING_VIOLATION，而不是权限问题。
    /// </summary>
    public static bool IsClaudeRunning()
    {
        try
        {
            using var current = Process.GetCurrentProcess();
            foreach (var process in Process.GetProcessesByName("claude"))
            {
                try
                {
                    var path = process.MainModule?.FileName;
                    if (string.IsNullOrEmpty(path)) continue;
                    // 只认 Claude Desktop 的可执行文件，跳过同名的 CLI。
                    if (!path.EndsWith("Claude.exe", StringComparison.OrdinalIgnoreCase) &&
                        !path.EndsWith("claude.exe", StringComparison.OrdinalIgnoreCase)) continue;
                    if (path.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase) ||
                        path.Contains("AnthropicClaude", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                catch
                {
                    // 无权读取 MainModule 时跳过
                }
            }
        }
        catch { }
        return false;
    }

    public static bool IsAntigravityRunning()
    {
        try
        {
            return Process.GetProcessesByName("Antigravity").Length > 0;
        }
        catch { return false; }
    }

    public static (bool Installed, bool Patched, string Version) DetectAntigravity()
    {
        try
        {
            var localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appDir = Path.Combine(localApp, "Programs", "antigravity");
            var exePath = Path.Combine(appDir, "Antigravity.exe");
            var resDir = Path.Combine(appDir, "resources");
            var asarPath = Path.Combine(resDir, "app.asar");
            var disabledPath = asarPath + ".disabled";
            var unpackedDir = Path.Combine(resDir, "app");

            var installed = File.Exists(exePath) || File.Exists(asarPath) || File.Exists(disabledPath);
            var patched = File.Exists(disabledPath) && Directory.Exists(unpackedDir);
            var version = "";
            if (File.Exists(exePath))
            {
                var info = FileVersionInfo.GetVersionInfo(exePath);
                version = info.ProductVersion ?? info.FileVersion ?? "";
            }
            return (installed, patched, version);
        }
        catch { return (false, false, ""); }
    }

    public static async Task<InstallResult> RunAsync(
        string action,
        string language,
        bool allowTakeOwnership,
        string workRoot,
        string scriptsPath,
        string resourcesPath,
        Action<string> log,
        CancellationToken cancellationToken = default,
        string target = "claude")
    {
        if (target is not ("claude" or "antigravity")) throw new ArgumentException("不支持的目标应用", nameof(target));
        if (action is not ("install" or "uninstall")) throw new ArgumentException("不支持的操作", nameof(action));
        if (language is not ("zh-CN" or "zh-TW" or "zh-HK")) throw new ArgumentException("不支持的语言", nameof(language));
        if (target == "antigravity" && language != "zh-CN") throw new ArgumentException("Antigravity 当前仅支持简体中文（zh-CN）", nameof(language));
        if (action == "uninstall" && allowTakeOwnership) throw new ArgumentException("恢复不接管目录所有权");
        if (target == "antigravity" && allowTakeOwnership) throw new ArgumentException("Antigravity 不需要接管目录所有权");

        var taskDir = Path.Combine(workRoot, Guid.NewGuid().ToString("N"));
        var payloadDir = Path.Combine(taskDir, "payload");
        Directory.CreateDirectory(payloadDir);
        CopyDirectory(scriptsPath, Path.Combine(payloadDir, "scripts"));
        CopyDirectory(resourcesPath, Path.Combine(payloadDir, "resources"));

        var logPath = Path.Combine(taskDir, "install.log");
        if (target == "antigravity")
        {
            var agInstaller = Path.Combine(payloadDir, "scripts", "install_antigravity_windows.ps1");
            return await RunAntigravityDirectAsync(agInstaller, action, language, logPath, log, cancellationToken);
        }

        var installer = Path.Combine(payloadDir, "scripts", "install_windows.ps1");
        var scriptParams = BuildScriptParams(installer, action, language, allowTakeOwnership, logPath);

        // 勾选接管所有权时直接提权；否则先普通权限跑，权限不足再自动提权重试。
        if (allowTakeOwnership)
        {
            return await RunElevatedAsync(scriptParams, logPath, log, cancellationToken);
        }

        var result = await RunDirectAsync(scriptParams, logPath, log, cancellationToken);
        if (result.ExitCode != 0 && action == "install" && NeedsElevation(result.Output))
        {
            log("普通权限无法写入 Store 目录，正在请求 Windows 管理员权限…");
            result = await RunElevatedAsync(scriptParams, logPath, log, cancellationToken);
        }
        return result;
    }

    /// <summary>
    /// install_windows.ps1 的参数集合。
    /// 日志路径与跳过更新检查用显式参数传，不依赖环境变量继承
    /// （提权进程不保证拿到调用方的环境变量）。
    /// </summary>
    private static Dictionary<string, object> BuildScriptParams(
        string installer, string action, string language, bool allowTakeOwnership, string logPath, bool selfTest = false)
    {
        var parameters = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["Script"] = installer,
            ["Action"] = action,
            ["Language"] = language,
            ["OriginalUserSid"] = CurrentUserSid(),
            ["OriginalUserProfile"] = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ["OriginalAppData"] = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ["OriginalLocalAppData"] = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ["StageLogPath"] = logPath,
            ["SkipUpdateCheck"] = true,
        };
        if (allowTakeOwnership) parameters["AllowTakeOwnership"] = true;
        if (selfTest) parameters["SelfTest"] = true;
        return parameters;
    }

    private static bool NeedsElevation(string output) =>
        output.Contains("拒绝访问", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("无法写入", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("写入权限", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("Access is denied", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("UnauthorizedAccessException", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("接管所有权", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("takeown", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 普通权限执行：直接 -File 调用脚本，实时接管 stdout/stderr。
    /// </summary>
    private static async Task<InstallResult> RunDirectAsync(
        Dictionary<string, object> parameters, string logPath, Action<string> log, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-File");
        foreach (var argument in BuildCliArguments(parameters)) psi.ArgumentList.Add(argument);

        psi.Environment["CLAUDE_ZH_STAGE_LOG"] = logPath;
        psi.Environment["CLAUDE_ZH_SKIP_UPDATE_CHECK"] = "1";
        psi.Environment["PYTHONUTF8"] = "1";
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        psi.Environment["CLAUDE_ZH_ORIGINAL_USER_SID"] = CurrentUserSid();

        var output = new StringBuilder();
        try
        {
            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) { output.AppendLine(e.Data); log(e.Data); } };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) { output.AppendLine(e.Data); log(e.Data); } };
            if (!process.Start()) throw new InvalidOperationException("无法启动 PowerShell 安装器");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(cancellationToken);

            var fileLog = ReadSharedText(logPath);
            if (!string.IsNullOrWhiteSpace(fileLog) && !output.ToString().Contains(fileLog.Trim()))
            {
                output.Append(fileLog);
                log(fileLog);
            }
            return new InstallResult(process.ExitCode, output.ToString(), logPath, false);
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return new InstallResult(1223, "已取消 Windows 管理员授权，未继续提权安装。", logPath, false);
        }
    }

    /// <summary>
    /// 提权执行。
    /// UseShellExecute=true（runas 必需）时 .NET 不允许设置 Environment，
    /// 所以环境变量全部写进生成的包装脚本里；退出码由包装脚本原样传出。
    /// </summary>
    private static async Task<InstallResult> RunElevatedAsync(
        Dictionary<string, object> parameters, string logPath, Action<string> log, CancellationToken cancellationToken)
    {
        var wrapperPath = Path.Combine(Path.GetDirectoryName(logPath)!, "elevated-wrapper.ps1");
        File.WriteAllText(wrapperPath, BuildWrapperScript(parameters, logPath), new UTF8Encoding(true));

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Normal,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(wrapperPath);

        var output = new StringBuilder();
        try
        {
            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            if (!process.Start()) throw new InvalidOperationException("无法启动提权安装器");

            // 提权进程在独立窗口运行，输出回不来，靠共享 transcript 实时回传界面。
            await PumpLogAsync(logPath, log, () => process.HasExited, cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            var fileLog = ReadSharedText(logPath);
            if (!string.IsNullOrWhiteSpace(fileLog)) output.Append(fileLog);
            return new InstallResult(process.ExitCode, output.ToString(), logPath, true);
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return new InstallResult(1223, "已取消 Windows 管理员授权，未继续提权安装。", logPath, true);
        }
    }

    /// <summary>
    /// 自检用：只读模式跑一遍脚本（-SelfTest），验证参数解析与探测链路，
    /// 不会关闭 Claude，也不会改写任何文件。
    /// </summary>
    public static async Task<InstallResult> RunDirectSelfTest(string installer, string workDir, Action<string> log)
    {
        Directory.CreateDirectory(workDir);
        var logPath = Path.Combine(workDir, "direct.log");
        var parameters = BuildScriptParams(installer, "uninstall", "zh-CN", false, logPath, selfTest: true);

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-File");
        foreach (var argument in BuildCliArguments(parameters)) psi.ArgumentList.Add(argument);
        psi.Environment["CLAUDE_ZH_STAGE_LOG"] = logPath;
        psi.Environment["CLAUDE_ZH_SKIP_UPDATE_CHECK"] = "1";

        var output = new StringBuilder();
        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) { output.AppendLine(e.Data); log(e.Data); } };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) { output.AppendLine(e.Data); log(e.Data); } };
        if (!process.Start()) throw new InvalidOperationException("无法启动直连自检");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();
        return new InstallResult(process.ExitCode, output.ToString(), logPath, false);
    }

    /// <summary>
    /// 自检用：生成提权包装脚本，但不真正提权，直接运行它验证参数拼装、
    /// 环境变量写入与退出码传出链路。
    /// </summary>
    public static async Task<InstallResult> RunWrapperSelfTest(string installer, string workDir, Action<string> log)
    {
        Directory.CreateDirectory(workDir);
        var logPath = Path.Combine(workDir, "wrapper.log");
        var parameters = BuildScriptParams(installer, "uninstall", "zh-CN", false, logPath, selfTest: true);
        var wrapperPath = Path.Combine(workDir, "elevated-wrapper.ps1");
        File.WriteAllText(wrapperPath, BuildWrapperScript(parameters, logPath), new UTF8Encoding(true));

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(wrapperPath);

        var output = new StringBuilder();
        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) { output.AppendLine(e.Data); log(e.Data); } };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) { output.AppendLine(e.Data); log(e.Data); } };
        if (!process.Start()) throw new InvalidOperationException("无法启动包装脚本自检");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();
        return new InstallResult(process.ExitCode, output.ToString(), logPath, true);
    }

    /// <summary>
    /// 生成提权包装脚本。用 splatting 传参，避免命令行引号转义问题。
    /// </summary>
    private static string BuildWrapperScript(Dictionary<string, object> parameters, string logPath)
    {
        var assignments = parameters
            .Where(p => p.Key != "Script")
            .Select(p => p.Value is bool flag
                ? $"    {p.Key} = {(flag ? "$true" : "$false")}"
                : $"    {p.Key} = {PsLiteral(p.Value as string ?? "")}");

        var scriptPath = parameters.TryGetValue("Script", out var value) ? value as string ?? "" : "";
        var sb = new StringBuilder();
        sb.AppendLine("$ErrorActionPreference = 'Stop'");
        sb.AppendLine("[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new()");
        sb.AppendLine("try { Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class QEC { [DllImport(\"kernel32.dll\")] static extern IntPtr GetStdHandle(int n); [DllImport(\"kernel32.dll\")] static extern bool GetConsoleMode(IntPtr h, out uint m); [DllImport(\"kernel32.dll\")] static extern bool SetConsoleMode(IntPtr h, uint m); public static void Off(){ IntPtr h=GetStdHandle(-10); uint m; if(GetConsoleMode(h,out m)) SetConsoleMode(h,(m & ~0x0040u)|0x0080u); } }'; [QEC]::Off() } catch {}");
        sb.AppendLine($"$env:CLAUDE_ZH_STAGE_LOG = {PsLiteral(logPath)}");
        sb.AppendLine("$env:CLAUDE_ZH_SKIP_UPDATE_CHECK = '1'");
        sb.AppendLine("$env:PYTHONUTF8 = '1'");
        sb.AppendLine("$env:PYTHONIOENCODING = 'utf-8'");
        sb.AppendLine($"$env:CLAUDE_ZH_ORIGINAL_USER_SID = {PsLiteral(CurrentUserSid())}");
        sb.AppendLine("$scriptParams = @{");
        foreach (var assignment in assignments) sb.AppendLine(assignment);
        sb.AppendLine("}");
        sb.AppendLine($"& {PsLiteral(scriptPath)} @scriptParams");
        sb.AppendLine("exit $LASTEXITCODE");
        return sb.ToString();
    }

    /// <summary>把参数集合转成命令行参数（普通权限路径使用 ArgumentList，无引号转义风险）。</summary>
    private static List<string> BuildCliArguments(Dictionary<string, object> parameters)
    {
        var arguments = new List<string>
        {
            parameters["Script"] as string ?? "",
            parameters["Action"] as string ?? "install",
            parameters["Language"] as string ?? "zh-CN",
            "-OriginalUserSid", parameters["OriginalUserSid"] as string ?? "",
            "-OriginalUserProfile", parameters["OriginalUserProfile"] as string ?? "",
            "-OriginalAppData", parameters["OriginalAppData"] as string ?? "",
            "-OriginalLocalAppData", parameters["OriginalLocalAppData"] as string ?? "",
            "-StageLogPath", parameters["StageLogPath"] as string ?? "",
            "-SkipUpdateCheck",
        };
        if (parameters.ContainsKey("AllowTakeOwnership")) arguments.Add("-AllowTakeOwnership");
        if (parameters.ContainsKey("SelfTest")) arguments.Add("-SelfTest");
        return arguments;
    }

    /// <summary>PowerShell 单引号字符串字面量。</summary>
    private static string PsLiteral(string value) => "'" + value.Replace("'", "''") + "'";

    /// <summary>提权期间持续读取共享日志，把进度回传给界面。</summary>
    private static async Task PumpLogAsync(string logPath, Action<string> log, Func<bool> finished, CancellationToken cancellationToken)
    {
        var emitted = 0;
        while (!finished())
        {
            await Task.Delay(400, cancellationToken);
            var text = ReadSharedText(logPath);
            if (text.Length < emitted) emitted = 0;
            if (text.Length <= emitted) continue;
            var chunk = text[emitted..];
            emitted = text.Length;
            log(chunk.TrimEnd('\r', '\n'));
        }
    }

    private static string ReadSharedText(string path)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                return DecodeText(buffer.ToArray());
            }
            catch (IOException) { Thread.Sleep(150); }
            catch (UnauthorizedAccessException) { Thread.Sleep(150); }
        }
        return "";
    }

    /// <summary>PowerShell transcript 可能是 UTF-8 或 UTF-16，按字节判断。</summary>
    private static string DecodeText(byte[] bytes)
    {
        if (bytes.Length >= 2)
        {
            if (bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            if (bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }
        var nulCount = 0;
        for (var i = 0; i + 1 < Math.Min(bytes.Length, 400); i += 2) if (bytes[i] == 0) nulCount++;
        var text = nulCount > 20 ? Encoding.Unicode.GetString(bytes) : new UTF8Encoding(false).GetString(bytes);
        return text.TrimStart('﻿');
    }

    private static async Task<InstallResult> RunAntigravityDirectAsync(
        string installer,
        string action,
        string language,
        string logPath,
        Action<string> log,
        CancellationToken cancellationToken,
        bool selfTest = false)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(installer);
        psi.ArgumentList.Add(action);
        psi.ArgumentList.Add(language);
        psi.ArgumentList.Add("-OriginalLocalAppData");
        psi.ArgumentList.Add(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        psi.ArgumentList.Add("-StageLogPath");
        psi.ArgumentList.Add(logPath);
        if (selfTest) psi.ArgumentList.Add("-SelfTest");

        psi.Environment["CLAUDE_ZH_STAGE_LOG"] = logPath;

        var output = new StringBuilder();
        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) { output.AppendLine(e.Data); log(e.Data); } };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) { output.AppendLine(e.Data); log(e.Data); } };
        if (!process.Start()) throw new InvalidOperationException("无法启动 Antigravity PowerShell 安装器");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);

        var fileLog = ReadSharedText(logPath);
        if (!string.IsNullOrWhiteSpace(fileLog) && !output.ToString().Contains(fileLog.Trim()))
        {
            output.Append(fileLog);
            log(fileLog);
        }
        return new InstallResult(process.ExitCode, output.ToString(), logPath, false);
    }

    public static async Task<InstallResult> RunAntigravitySelfTest(string installer, string workDir, Action<string> log)
    {
        Directory.CreateDirectory(workDir);
        var logPath = Path.Combine(workDir, "antigravity-selftest.log");
        return await RunAntigravityDirectAsync(installer, "uninstall", "zh-CN", logPath, log, CancellationToken.None, selfTest: true);
    }

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
