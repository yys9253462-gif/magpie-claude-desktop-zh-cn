import test from "node:test";
import assert from "node:assert/strict";
import { readFile, stat } from "node:fs/promises";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const read = (path) => readFile(fileURLToPath(new URL(`../${path}`, import.meta.url)), "utf8");

test("Magpie 清单声明页面、本机服务、MIT 和发现标签", async () => {
  const pkg = JSON.parse(await read("package.json"));
  assert.equal(pkg.license, "MIT");
  assert.ok(pkg.keywords.includes("magpie-plugin"));
  assert.equal(pkg.magpie.page.service.option, "port");
  assert.equal(pkg.magpie.page.service.port, pkg.magpie.options.port);
  assert.equal(pkg.magpie.permissions, undefined);
  assert.equal(pkg.main, "./index.mjs");
  for (const file of ["page/index.html", "page/icon.svg", "scripts/install_windows.ps1", "resources/frontend-zh-CN.json", "scripts/LICENSE", "LICENSE"]) {
    assert.ok((await stat(fileURLToPath(new URL(`../${file}`, import.meta.url)))).isFile(), file);
  }
});

test("上游脚本只做 Magpie 日志适配，保留原始补丁逻辑和许可", async () => {
  const script = await read("scripts/install_windows.ps1");
  assert.match(script, /CLAUDE_ZH_STAGE_LOG/);
  assert.match(script, /AllowTakeOwnership/);
  assert.match(script, /Invoke-PreInstallCleanup/);
  const lock = JSON.parse(await read("UPSTREAM.lock.json"));
  assert.equal(lock.commit, "6b611ab50e90ba546a90af2dffa00f5c2ae5a19b");
  assert.match(await read("scripts/LICENSE"), /Copyright \(c\) 2025 javaht/);
});

test("发布页无内联脚本，安装动作不由 GET 触发", async () => {
  const html = await read("lib/index.html");
  assert.doesNotMatch(html, /<script(?![^>]*src=)[^>]*>/);
  const app = await read("lib/app.js");
  assert.match(app, /method: "POST"/);
  assert.doesNotMatch(app, /\bconfirm\s*\(/, "Magpie 沙箱内不依赖浏览器原生确认框");
  assert.match(html, /id="confirm-action"/);
  assert.match(app, /confirm-action.*onclick/s);
  for (const file of ["lib/app.js", "page/boot-page.js", "page/boot.js", "index.mjs", "serve.mjs", "lib/server.mjs", "lib/runner.mjs"]) {
    const result = spawnSync(process.execPath, ["--check", fileURLToPath(new URL(`../${file}`, import.meta.url))]);
    assert.equal(result.status, 0, `${file}: ${result.stderr}`);
  }
});

test("Windows PowerShell 安装器语法有效", { skip: process.platform !== "win32" }, () => {
  const file = fileURLToPath(new URL("../scripts/install_windows.ps1", import.meta.url));
  const result = spawnSync("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", `$null = [scriptblock]::Create((Get-Content -LiteralPath '${file.replaceAll("'", "''")}' -Raw))`], { encoding: "utf8" });
  assert.equal(result.status, 0, result.stderr);
});
