namespace ClaudeZhStandalone;

internal static class EntryPoint
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--self-test"))
        {
            try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }
            return SelfTest().GetAwaiter().GetResult();
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }

    private static async Task<int> SelfTest()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClaudeDesktop-ZH-CN-self-test");
        Directory.CreateDirectory(root);
        var scripts = Path.Combine(Installer.PayloadRoot, "scripts");
        var resources = Path.Combine(Installer.PayloadRoot, "resources");
        if (!File.Exists(Path.Combine(scripts, "install_windows.ps1")) ||
            !File.Exists(Path.Combine(resources, "frontend-zh-CN.json")) ||
            !File.Exists(Path.Combine(resources, "frontend-hardcoded-zh-CN.json")))
        {
            Console.Error.WriteLine("内嵌资源不完整。");
            return 1;
        }

        // 1) 直连路径：ProcessStartInfo -File + 环境变量。
        //    使用 -SelfTest 只读模式：不会关闭 Claude，也不会改写任何文件。
        var direct = await Installer.RunDirectSelfTest(Path.Combine(scripts, "install_windows.ps1"), Path.Combine(root, "direct"), Console.WriteLine);
        Console.WriteLine($"SELF_TEST_DIRECT_EXIT={direct.ExitCode}");
        if (direct.ExitCode != 0)
        {
            Console.Error.WriteLine("直连执行路径失败。");
            return 1;
        }

        // 2) 提权路径：验证包装脚本生成、参数拼装与退出码传出链路。
        //    这里不真正提权，只跑包装脚本里的非提权分支。
        var wrapperDir = Path.Combine(root, "wrapper");
        Directory.CreateDirectory(wrapperDir);
        var wrapperCheck = await Installer.RunWrapperSelfTest(Path.Combine(scripts, "install_windows.ps1"), wrapperDir, Console.WriteLine);
        Console.WriteLine($"SELF_TEST_WRAPPER_EXIT={wrapperCheck.ExitCode}");
        if (wrapperCheck.ExitCode != 0)
        {
            Console.Error.WriteLine("提权包装路径失败。");
            return 1;
        }

        Console.WriteLine("SELF_TEST_OK");
        return 0;
    }
}
