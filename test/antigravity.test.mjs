import test from "node:test";
import assert from "node:assert/strict";
import { mkdtemp, mkdir, readFile, rm, stat, writeFile } from "node:fs/promises";
import { spawnSync } from "node:child_process";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import { createRunner } from "../lib/runner.mjs";

const read = (path) => readFile(fileURLToPath(new URL(`../${path}`, import.meta.url)), "utf8");
const agScriptPath = () => fileURLToPath(new URL("../scripts/install_antigravity_windows.ps1", import.meta.url));

function buildMiniAsar(filesMap) {
  const entries = Object.entries(filesMap);
  const filesTree = { dist: { files: {} } };
  const chunks = [];
  let offset = 0;

  for (const [name, content] of entries) {
    const buf = Buffer.from(content, "utf8");
    filesTree.dist.files[name] = { size: buf.length, offset: String(offset) };
    chunks.push(buf);
    offset += buf.length;
  }

  const headerJsonBuf = Buffer.from(JSON.stringify({ files: filesTree }), "utf8");
  const alignedHeaderLen = Math.ceil(headerJsonBuf.length / 4) * 4;
  const headerPadded = Buffer.alloc(alignedHeaderLen, 0);
  headerJsonBuf.copy(headerPadded);

  const headerBuf = Buffer.alloc(16);
  headerBuf.writeUInt32LE(4, 0);
  headerBuf.writeUInt32LE(8 + alignedHeaderLen, 4);
  headerBuf.writeUInt32LE(4 + alignedHeaderLen, 8);
  headerBuf.writeUInt32LE(headerJsonBuf.length, 12);

  return Buffer.concat([headerBuf, headerPadded, ...chunks]);
}

test("Antigravity PowerShell 安装器语法有效且支持只读 SelfTest", { skip: process.platform !== "win32" }, async () => {
  const file = agScriptPath();
  const syntax = spawnSync("powershell.exe", [
    "-NoProfile", "-NonInteractive", "-Command",
    `$null = [scriptblock]::Create((Get-Content -LiteralPath '${file.replaceAll("'", "''")}' -Raw))`
  ], { encoding: "utf8" });
  assert.equal(syntax.status, 0, syntax.stderr);

  const selfTest = spawnSync("powershell.exe", [
    "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", file,
    "uninstall", "zh-CN", "-SelfTest"
  ], { encoding: "utf8" });
  assert.equal(selfTest.status, 0, selfTest.stdout + selfTest.stderr);
  assert.match(selfTest.stdout, /只读自检通过/);
});

test("Antigravity 沙箱端到端：ASAR 解包、幂等注入与无损还原", { skip: process.platform !== "win32" }, async () => {
  const tempRoot = await mkdtemp(join(tmpdir(), "ag-patch-test-"));
  try {
    const appDir = join(tempRoot, "Programs", "antigravity");
    const resDir = join(appDir, "resources");
    await mkdir(resDir, { recursive: true });

    const originalAsar = buildMiniAsar({
      "preload.js": "console.log('preload-origin');\n",
      "menu.js": "const { Menu } = require('electron');\n",
      "tray.js": "const items = [{ label: 'Show Antigravity' }, { label: 'Quit' }];\n",
      "loadingOverlay.js": "const html = '<div>Loading Antigravity</div>';\n",
    });
    const asarPath = join(resDir, "app.asar");
    await writeFile(asarPath, originalAsar);

    // 1. 首次安装
    const installRes = spawnSync("powershell.exe", [
      "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", agScriptPath(),
      "install", "zh-CN", "-AppDirOverride", appDir, "-SkipLaunch", "-SkipProcessStop"
    ], { encoding: "utf8" });
    assert.equal(installRes.status, 0, installRes.stdout + installRes.stderr);

    // app.asar 已被改名为 app.asar.disabled
    await assert.rejects(() => stat(asarPath));
    assert.ok((await stat(asarPath + ".disabled")).isFile());

    const preload = await readFile(join(resDir, "app", "dist", "preload.js"), "utf8");
    const menu = await readFile(join(resDir, "app", "dist", "menu.js"), "utf8");
    const tray = await readFile(join(resDir, "app", "dist", "tray.js"), "utf8");
    const loading = await readFile(join(resDir, "app", "dist", "loadingOverlay.js"), "utf8");

    assert.match(preload, /preload-origin/);
    assert.match(preload, /Antigravity Chinese Localization Engine/);
    assert.match(menu, /Antigravity Chinese Localization Engine - Menu/);
    assert.match(tray, /显示 Antigravity/);
    assert.match(tray, /退出/);
    assert.match(loading, />正在加载 Antigravity</);

    // 2. 重复安装（热更新词典，不重复叠加两份）
    const reinstallRes = spawnSync("powershell.exe", [
      "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", agScriptPath(),
      "install", "zh-CN", "-AppDirOverride", appDir, "-SkipLaunch", "-SkipProcessStop"
    ], { encoding: "utf8" });
    assert.equal(reinstallRes.status, 0, reinstallRes.stdout + reinstallRes.stderr);
    const preloadAfter = await readFile(join(resDir, "app", "dist", "preload.js"), "utf8");
    assert.equal(preloadAfter.split("Antigravity Chinese Localization Engine").length - 1, 1);

    // 3. 还原官方文件
    const uninstallRes = spawnSync("powershell.exe", [
      "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", agScriptPath(),
      "uninstall", "zh-CN", "-AppDirOverride", appDir, "-SkipLaunch", "-SkipProcessStop"
    ], { encoding: "utf8" });
    assert.equal(uninstallRes.status, 0, uninstallRes.stdout + uninstallRes.stderr);

    const restoredBuf = await readFile(asarPath);
    assert.equal(Buffer.compare(restoredBuf, originalAsar), 0, "还原后的 app.asar 应与原版逐字节一致");
    await assert.rejects(() => stat(asarPath + ".disabled"));
    await assert.rejects(() => stat(join(resDir, "app")));
  } finally {
    await rm(tempRoot, { recursive: true, force: true });
  }
});

test("Runner 校验 Antigravity 目标参数与只允许简体中文", async () => {
  const calls = [];
  const runner = createRunner({
    platform: "win32",
    stateDir: await mkdtemp(join(tmpdir(), "ag-runner-test-")),
    execute: async (cmd, args) => {
      calls.push({ cmd, args });
      return { code: 0, output: "ok" };
    },
  });

  assert.throws(() => runner.begin({ target: "unknown", action: "install", language: "zh-CN" }), /目标应用/);
  assert.throws(() => runner.begin({ target: "antigravity", action: "install", language: "zh-TW" }), /仅支持简体中文/);
  assert.throws(() => runner.begin({ target: "antigravity", action: "install", language: "zh-CN", allowTakeOwnership: true }), /WindowsApps/);

  runner.begin({ target: "antigravity", action: "install", language: "zh-CN" });
  await runner.active.done;
  assert.equal(calls.length, 1);
  assert.match(calls[0].args.join(" "), /install_antigravity_windows\.ps1/);
});

test("Antigravity 上游资源与 MIT 许可保留完整", async () => {
  const lock = JSON.parse(await read("UPSTREAM.lock.json"));
  assert.equal(lock.antigravity.project, "MIMICTE/Antigravity-zh-CN");
  assert.match(await read("resources/antigravity/LICENSE"), /Copyright \(c\) 2026 Antigravity-zh Contributors/);
  assert.match(await read("resources/antigravity/dom-translator.js"), /Antigravity Chinese Localization Engine/);
  assert.match(await read("resources/antigravity/menu-translator.js"), /Antigravity Chinese Localization Engine - Menu/);
});
