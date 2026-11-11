import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const read = (path) => readFile(fileURLToPath(new URL(`../${path}`, import.meta.url)), "utf8");
const scriptPath = () => fileURLToPath(new URL("../scripts/install_windows.ps1", import.meta.url));

test("安装器提供只读自检模式，不关闭 Claude、不写文件", async () => {
  const script = await read("scripts/install_windows.ps1");
  assert.match(script, /\[switch\]\$SelfTest/, "脚本声明 -SelfTest 开关");
  assert.match(script, /function Invoke-SelfTest/, "脚本实现只读自检函数");

  // 自检分支必须在真正执行 install/uninstall 之前短路。
  const selfTestIndex = script.indexOf("if ($SelfTest) {");
  const switchIndex = script.indexOf('switch ($Action) {');
  assert.ok(selfTestIndex > 0, "存在 SelfTest 分支");
  assert.ok(switchIndex > 0, "存在 Action 分发");
  assert.ok(selfTestIndex < switchIndex, "SelfTest 在 Action 分发之前短路");

  // 自检函数体内不能出现会改动系统状态或关闭 Claude 的调用。
  const body = script.slice(script.indexOf("function Invoke-SelfTest"), script.indexOf("function Invoke-PreInstallCleanup"));
  for (const forbidden of ["Stop-ClaudeProcesses", "Stop-Process", "Remove-Item", "Set-Acl", "takeown", "icacls", "Restart-Claude"]) {
    assert.ok(!body.includes(forbidden), `自检函数不应调用 ${forbidden}`);
  }
});

test("控制台管道断开不会中断安装流程", async () => {
  const script = (await read("scripts/install_windows.ps1")).replaceAll("\r\n", "\n");
  // Write-Host 在控制台管道断掉时抛终止错误 0xE9，会让整个安装流程中断，
  // 连错误提示都打不出来。用一层 try/catch 包住真正的 cmdlet。
  const start = script.indexOf("function Write-Host {");
  const body = script.slice(start, script.indexOf("$BaseLanguageList"));
  assert.ok(start > 0, "包装 Write-Host");
  assert.match(body, /Microsoft\.PowerShell\.Utility\\Write-Host/, "仍调用真正的 Write-Host");
  assert.match(body, /catch \{\n\s*# 输出通道彻底不可用时静默忽略/, "捕获控制台错误");
  // 必须用真正的 cmdlet 而不是 [Console]::Out.Write，否则 transcript 录不到内容，
  // 提权子进程的进度就回传不到界面。
  assert.doesNotMatch(body, /\[Console\]::Out\.Write/, "不要绕过 transcript");
});

test("商店版 exe 在 app 子目录，版本检测要能定位", async () => {
  const script = await read("scripts/install_windows.ps1");
  const start = script.indexOf("function Get-ClaudeExeProductVersion");
  const body = script.slice(start, script.indexOf("function Get-ClaudeExePath"));
  assert.match(body, /app\\Claude\.exe/, "探测 app 子目录");
  assert.match(body, /app\\claude\.exe/, "探测 app 子目录（小写）");
});

test("写入失败区分「被占用」和「权限不足」", async () => {
  const script = (await read("scripts/install_windows.ps1")).replaceAll("\r\n", "\n");
  assert.match(script, /function Get-WriteAccessFailure/, "提供失败原因分类");
  assert.match(script, /"locked"/, "识别被进程占用");
  assert.match(script, /"denied"/, "识别权限不足");
  // 32 = ERROR_SHARING_VIOLATION，33 = ERROR_LOCK_VIOLATION
  assert.match(script, /\$win32 -eq 32 -or \$win32 -eq 33/, "按 Win32 错误码判定占用");

  // 上层必须把 locked 和 denied 分开报告，不能一律说成权限问题。
  assert.match(script, /这?不是权限问题/, "被占用时给出正确提示");
  assert.match(script, /仍被进程占用，无法写入/, "被占用与无权限分开提示");
});

test("强杀 Claude 后等待文件句柄释放再测写权限", async () => {
  const script = (await read("scripts/install_windows.ps1")).replaceAll("\r\n", "\n");
  assert.match(script, /function Wait-WriteAccess/, "提供等待可写的轮询");
  assert.match(script, /Wait-WriteAccess \$Path 20/, "被占用时等待释放");
  // 强杀之后必须等进程真正退出，而不是固定睡 1 秒。
  const stopStart = script.indexOf("function Stop-ClaudeProcessesGracefully");
  const stopBody = script.slice(stopStart, script.indexOf("function Stop-ClaudeProcesses {"));
  assert.match(stopBody, /Wait-ProcessesExit \$remainingDesktop/, "强杀后等待进程真正退出");
});

test("先停 Cowork 服务再关桌面进程，避免 Claude 被拉回", async () => {
  const script = (await read("scripts/install_windows.ps1")).replaceAll("\r\n", "\n");
  const stopStart = script.indexOf("function Stop-ClaudeProcessesGracefully");
  const stopBody = script.slice(stopStart, script.indexOf("function Stop-ClaudeProcesses {"));
  const serviceIndex = stopBody.indexOf("Stop-CoworkVMService");
  const closeIndex = stopBody.indexOf("CloseMainWindow");
  assert.ok(serviceIndex > 0, "有关闭 Cowork 服务");
  assert.ok(closeIndex > 0, "有关闭桌面进程");
  assert.ok(serviceIndex < closeIndex, "必须先停服务再关进程，否则 Claude 会被服务拉回");
});

test("独立版在开始前检测运行中的 Claude 并提醒", async () => {
  const program = await read("standalone/Program.cs");
  assert.match(program, /public static bool IsClaudeRunning\(\)/, "提供运行中检测");
  assert.match(program, /GetProcessesByName\("claude"\)/, "按进程名查找");
  assert.match(program, /WindowsApps|AnthropicClaude/, "只认 Claude Desktop，排除同名 CLI");

  const form = await read("standalone/MainForm.cs");
  assert.match(form, /IsClaudeRunning\(\)/, "界面调用运行中检测");
  assert.match(form, /请先关闭 Claude/, "给出明确提示");
});

test("没有补丁可卸时直接返回，不关闭 Claude、不改写 Claude.exe", async () => {
  const script = await read("scripts/install_windows.ps1");
  const start = script.indexOf("function Uninstall-WindowsLanguagePack");
  const end = script.indexOf("function Remove-LegacyFridaResident");
  // 统一换行符，避免 CRLF 影响位置比较。
  const body = script.slice(start, end).replaceAll("\r\n", "\n");

  // 只看实际调用，不要匹配到注释里的同名函数。
  const guardIndex = body.indexOf("无需恢复。");
  const stopIndex = body.indexOf("\n    Stop-ClaudeProcesses");
  const syncIndex = body.indexOf("Sync-ClaudeExeAsarIntegrity $resourcesPath");
  assert.ok(guardIndex > 0, "存在无补丁可卸的短路分支");
  assert.ok(stopIndex > guardIndex, "关闭 Claude 必须发生在短路分支之后");
  assert.ok(syncIndex > guardIndex, "写回 Claude.exe 完整性哈希必须发生在短路分支之后");
});

test("自动化调用使用显式参数传日志路径与跳过更新检查", async () => {
  const script = await read("scripts/install_windows.ps1");
  assert.match(script, /\[string\]\$StageLogPath/, "声明 -StageLogPath");
  assert.match(script, /\[switch\]\$SkipUpdateCheck/, "声明 -SkipUpdateCheck");
  assert.match(script, /if \(\$StageLogPath\) \{ \$env:CLAUDE_ZH_STAGE_LOG = \$StageLogPath \}/);

  const runner = await read("lib/runner.mjs");
  assert.match(runner, /"-StageLogPath", log/, "Magpie 执行器传显式日志路径");
  assert.match(runner, /"-SkipUpdateCheck"/, "Magpie 执行器传跳过更新检查");
});

test("独立版自检只走只读模式，不触发真实安装或卸载", async () => {
  const entry = await read("standalone/EntryPoint.cs");
  assert.match(entry, /RunDirectSelfTest/, "直连路径使用只读自检");
  assert.match(entry, /RunWrapperSelfTest/, "提权路径使用只读自检");
  assert.ok(
    !/RunAsync\("uninstall"/.test(entry),
    "自检不得调用真实 uninstall 动作"
  );

  const program = await read("standalone/Program.cs");
  assert.match(program, /selfTest: true/, "自检构造时带上 SelfTest 参数");
  assert.match(program, /\["SelfTest"\] = true/, "参数集合包含 SelfTest");
  assert.match(program, /"-SelfTest"/, "命令行参数包含 -SelfTest");
});

test("提权路径不通过 ProcessStartInfo 设置环境变量", async () => {
  const program = await read("standalone/Program.cs");
  // UseShellExecute=true 时 .NET 抛 InvalidOperationException：
  // "The Process object must have the UseShellExecute property set to false
  //  in order to use environment variables."
  // 取 RunElevatedAsync 的方法体范围，而不是文件范围。
  const elevatedStart = program.indexOf("private static async Task<InstallResult> RunElevatedAsync");
  const elevatedEnd = program.indexOf("public static async Task<InstallResult> RunDirectSelfTest");
  const body = program.slice(elevatedStart, elevatedEnd);
  assert.ok(elevatedStart > 0, "存在 RunElevatedAsync");
  assert.ok(!body.includes("psi.Environment["), "提权分支不得设置 psi.Environment");
  assert.match(body, /UseShellExecute = true/, "提权分支使用 ShellExecute");
  assert.match(program, /\$env:CLAUDE_ZH_STAGE_LOG = /, "环境变量改由包装脚本设置");
});
