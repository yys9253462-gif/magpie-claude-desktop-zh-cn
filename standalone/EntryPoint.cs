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
        var agResources = Path.Combine(resources, "antigravity");
        if (!File.Exists(Path.Combine(scripts, "install_windows.ps1")) ||
            !File.Exists(Path.Combine(scripts, "install_antigravity_windows.ps1")) ||
            !File.Exists(Path.Combine(resources, "frontend-zh-CN.json")) ||
            !File.Exists(Path.Combine(resources, "frontend-hardcoded-zh-CN.json")) ||
            !File.Exists(Path.Combine(agResources, "dom-translator.js")) ||
            !File.Exists(Path.Combine(agResources, "menu-translator.js")))
        {
            Console.Error.WriteLine("内嵌资源不完整。");
            return 1;
        }

        // 1) Claude 直连路径（-SelfTest 只读模式）
        var direct = await Installer.RunDirectSelfTest(Path.Combine(scripts, "install_windows.ps1"), Path.Combine(root, "direct"), Console.WriteLine);
        Console.WriteLine($"SELF_TEST_DIRECT_EXIT={direct.ExitCode}");
        if (direct.ExitCode != 0)
        {
            Console.Error.WriteLine("直连执行路径失败。");
            return 1;
        }

        // 2) Claude 提权包装路径（非提权自检）
        var wrapperDir = Path.Combine(root, "wrapper");
        Directory.CreateDirectory(wrapperDir);
        var wrapperCheck = await Installer.RunWrapperSelfTest(Path.Combine(scripts, "install_windows.ps1"), wrapperDir, Console.WriteLine);
        Console.WriteLine($"SELF_TEST_WRAPPER_EXIT={wrapperCheck.ExitCode}");
        if (wrapperCheck.ExitCode != 0)
        {
            Console.Error.WriteLine("提权包装路径失败。");
            return 1;
        }

        // 3) Antigravity 只读自检路径
        var agCheck = await Installer.RunAntigravitySelfTest(Path.Combine(scripts, "install_antigravity_windows.ps1"), Path.Combine(root, "antigravity"), Console.WriteLine);
        Console.WriteLine($"SELF_TEST_ANTIGRAVITY_EXIT={agCheck.ExitCode}");
        if (agCheck.ExitCode != 0)
        {
            Console.Error.WriteLine("Antigravity 自检路径失败。");
            return 1;
        }

        Console.WriteLine("SELF_TEST_OK");
        return 0;
    }
}
