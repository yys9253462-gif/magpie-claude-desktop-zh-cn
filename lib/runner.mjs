// Windows 执行器：参数固定，不接受网页传入 PowerShell 命令、路径或密钥。
// 每个任务复制完整上游包到用户临时目录，避免权限提升时访问插件缓存目录。
import { spawn } from "node:child_process";
import { randomUUID } from "node:crypto";
import { cp, mkdir, readFile, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { fileURLToPath } from "node:url";

const ROOT = fileURLToPath(new URL("..", import.meta.url));
const LANGS = new Set(["zh-CN", "zh-TW", "zh-HK"]);
const wait = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

function run(command, args, options = {}) {
  return new Promise((resolveRun) => {
    const child = spawn(command, args, {
      windowsHide: true,
      shell: false,
      ...options,
      env: {
        ...process.env,
        PYTHONIOENCODING: "utf-8",
        PYTHONUTF8: "1",
        ...options.env,
      },
    });
    child.stdout.setEncoding("utf8");
    child.stderr.setEncoding("utf8");
    let output = "";
    child.stdout.on("data", (data) => { output += data; });
    child.stderr.on("data", (data) => { output += data; });
    child.on("error", (error) => resolveRun({ code: -1, output: error.message }));
    child.on("close", (code) => resolveRun({ code, output }));
  });
}

function psQuote(value) {
  return `'${String(value).replaceAll("'", "''")}'`;
}

export function createRunner({
  platform = process.platform,
  root = ROOT,
  stateDir = join(tmpdir(), "magpie-claude-desktop-zh-cn"),
  execute = run,
  sleep = wait,
  now = () => new Date().toISOString(),
} = {}) {
  let active = null;
  let lastResult = null;
  let detection = null;

  function snapshot() {
    return {
      platform,
      supported: platform === "win32",
      busy: Boolean(active),
      task: active ? {
        id: active.id,
        action: active.action,
        language: active.language,
        elevated: active.elevated,
        startedAt: active.startedAt,
        output: active.output.slice(-16000),
        elevatedNotice: active.elevated
          ? "Windows 可能已显示 UAC 提示。安装会在另一个 PowerShell 窗口执行；完成后请回到这里刷新。"
          : "",
      } : null,
      result: lastResult,
    };
  }

  async function detect() {
    detection ??= detectUncached().finally(() => { detection = null; });
    return detection;
  }

  async function detectUncached() {
    if (platform !== "win32") {
      return { installed: false, platform, message: "此插件只能修改运行它的 Windows 电脑上的 Claude Desktop。Linux 服务器版 Magpie 无法修改你的 Windows 客户端。" };
    }
    const script = `[Console]::OutputEncoding=[System.Text.UTF8Encoding]::new(); $OutputEncoding=[System.Text.UTF8Encoding]::new(); $p = Get-AppxPackage -ErrorAction SilentlyContinue | Where-Object { $_.Name -eq 'Claude' -or $_.Name -eq 'AnthropicPBC.Claude' } | Select-Object -First 1; if ($p) { [pscustomobject]@{ installed=$true; kind='Microsoft Store / MSIX'; version=$p.Version.ToString(); path=$p.InstallLocation; packageFamilyName=$p.PackageFamilyName } | ConvertTo-Json -Compress } else { [pscustomobject]@{ installed=$false; message='未检测到 Microsoft Store 版；安装时也会检查官网安装版。' } | ConvertTo-Json -Compress }`;
    const result = await execute("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", script]);
    if (result.code !== 0) return { installed: false, error: result.output.slice(-1000) };
    try { return { platform, ...JSON.parse(result.output.trim()) }; }
    catch { return { installed: false, error: "无法解析 Claude 安装信息。" }; }
  }

  function begin({ action, language, elevated = false, allowTakeOwnership = false }) {
    if (!["install", "uninstall"].includes(action)) throw new Error("不支持的操作。");
    if (!LANGS.has(language)) throw new Error("语言必须是 zh-CN、zh-TW 或 zh-HK。");
    if (platform !== "win32") throw new Error("只能在 Windows 本机执行。");
    if (active) throw new Error("已有任务在执行，请等待完成。");
    if (action === "uninstall" && allowTakeOwnership) throw new Error("恢复时不能额外接管目录所有权。");
    active = {
      id: randomUUID(), action, language, elevated, allowTakeOwnership,
      startedAt: now(), output: "",
    };
    // 立即开始准备；begin 返回 busy=true，测试和调用方都能等待 idle。
    const task = active;
    const done = perform(task).catch((error) => finish({ ok: false, error: error.message }));
    task.done = done;
    return snapshot();
  }

  function finish(result) {
    lastResult = { ...result, finishedAt: now() };
    active = null;
    return lastResult;
  }

  async function perform(task) {
    const base = join(stateDir, "tasks", task.id);
    const stage = join(base, "payload");
    const log = join(base, "result.log");
    await mkdir(stage, { recursive: true });
    await cp(join(root, "scripts"), join(stage, "scripts"), { recursive: true });
    await cp(join(root, "resources"), join(stage, "resources"), { recursive: true });
    const installer = join(stage, "scripts", "install_windows.ps1");
    const args = [
      "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", installer,
      task.action, task.language,
      "-OriginalUserProfile", process.env.USERPROFILE || "",
      "-OriginalAppData", process.env.APPDATA || "",
      "-OriginalLocalAppData", process.env.LOCALAPPDATA || "",
    ];
    if (task.allowTakeOwnership) args.push("-AllowTakeOwnership");
    const commonEnv = {
      ...process.env,
      CLAUDE_ZH_SKIP_UPDATE_CHECK: "1",
      CLAUDE_ZH_STAGE_LOG: log,
      PYTHONIOENCODING: "utf-8",
      PYTHONUTF8: "1",
    };

    if (!task.elevated) {
      const result = await execute("powershell.exe", args, { env: commonEnv });
      const output = result.output || "";
      await writeFile(log, output, "utf8");
      finish({ ok: result.code === 0, code: result.code, output: output.slice(-30000), log, elevated: false });
      return;
    }

    // Start-Process 会把 ArgumentList 数组用空格重新拼接。给路径参数补上
    // Windows 命令行双引号；数组仍固定，用户不能传入命令或脚本路径。
    const quotedArgs = args.map((arg) => arg.includes(" ") ? `"${arg.replaceAll('"', '\\"')}"` : arg);
    const psArgs = quotedArgs.map(psQuote).join(",");
    const driver = `$ErrorActionPreference='Stop'; $a=@(${psArgs}); $env:CLAUDE_ZH_STAGE_LOG=${psQuote(log)}; $p=Start-Process -FilePath 'powershell.exe' -ArgumentList $a -Verb RunAs -Wait -PassThru; exit $p.ExitCode`;
    const launch = await execute("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", driver], { env: commonEnv });
    if (launch.code !== 0) {
      finish({ ok: false, code: launch.code, output: launch.output, error: "提权安装未完成。若你取消了 UAC，可重新点击安装。", log, elevated: true });
      return;
    }
    // 管理员窗口的输出不会自动回到插件。若安装器未写 stage 日志，读取它自己的 transcript。
    let output = "";
    for (let i = 0; i < 10 && !output; i++) {
      output = await readFile(log, "utf8").catch(() => readFile(join(stage, "scripts", "install-windows.log"), "utf8").catch(() => ""));
      if (!output) await sleep(500);
    }
    finish({ ok: true, code: launch.code, output: output.slice(-30000), log, elevated: true });
  }

  return { snapshot, detect, begin, stateDir, get active() { return active; } };
}
