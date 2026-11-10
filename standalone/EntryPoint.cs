namespace ClaudeZhStandalone;

internal static class EntryPoint
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--self-test"))
        {
            return SelfTest().GetAwaiter().GetResult();
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }

    private static async Task<int> SelfTest()
    {
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
            var result = await Installer.RunAsync("uninstall", "zh-CN", false, root, scripts, resources, Console.WriteLine);
            Console.WriteLine($"SELF_TEST_EXIT={result.ExitCode}");
            return result.ExitCode == 0 ? 0 : 1;
        }
    }
}
