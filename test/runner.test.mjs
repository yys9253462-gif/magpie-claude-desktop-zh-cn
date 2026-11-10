import test from "node:test";
import assert from "node:assert/strict";
import { mkdtemp, readFile, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { createRunner } from "../lib/runner.mjs";

const tick = () => new Promise((resolve) => setTimeout(resolve, 15));
const idle = async (runner) => { if (runner.active?.done) await runner.active.done; };

test("非 Windows 明确拒绝执行，不伪装成已汉化", () => {
  const runner = createRunner({ platform: "linux" });
  assert.equal(runner.snapshot().supported, false);
  assert.throws(() => runner.begin({ action: "install", language: "zh-CN" }), /Windows/);
});

test("只接受固定动作和三种语言，且禁止并发写入", async () => {
  let finish;
  const runner = createRunner({ platform: "win32", execute: () => new Promise((r) => { finish = r; }) });
  assert.throws(() => runner.begin({ action: "delete", language: "zh-CN" }), /不支持/);
  assert.throws(() => runner.begin({ action: "install", language: "en-US" }), /语言/);
  runner.begin({ action: "install", language: "zh-CN" });
  await tick();
  assert.equal(runner.snapshot().busy, true);
  assert.throws(() => runner.begin({ action: "uninstall", language: "zh-CN" }), /已有任务/);
  await new Promise((resolve) => {
    const check = () => finish ? resolve() : setTimeout(check, 10);
    check();
  });
  finish({ code: 0, output: "安装完成" });
  await idle(runner);
});

test("普通安装复制固定上游资源，并保留原始日志", async () => {
  const stateDir = await mkdtemp(join(tmpdir(), "claude-zh-test-"));
  let call;
  const runner = createRunner({
    platform: "win32", stateDir,
    execute: async (command, args, options) => {
      call = { command, args, options };
      assert.equal(command, "powershell.exe");
      assert.equal(args[5], "install");
      assert.equal(args[6], "zh-TW");
      assert.equal(args.includes("-Interactive"), false);
      assert.equal(args.includes("-AllowTakeOwnership"), false);
      assert.equal(options.env.CLAUDE_ZH_SKIP_UPDATE_CHECK, "1");
      return { code: 0, output: "安装完成。Language 已注册。" };
    },
  });
  runner.begin({ action: "install", language: "zh-TW" });
  await idle(runner);
  assert.equal(runner.snapshot().result?.ok, true, JSON.stringify(runner.snapshot().result));
  const outputLog = await readFile(call.options.env.CLAUDE_ZH_STAGE_LOG, "utf8");
  assert.match(outputLog, /安装完成/);
  const script = await readFile(call.args[4], "utf8");
  assert.match(script, /CLAUDE_ZH_STAGE_LOG/);
});

test("失败返回非零与日志，不宣称成功", async () => {
  const runner = createRunner({ platform: "win32", execute: async () => ({ code: 1, output: "未找到前端 JS bundle" }) });
  runner.begin({ action: "install", language: "zh-CN" });
  await idle(runner);
  assert.equal(runner.snapshot().result.ok, false);
  assert.equal(runner.snapshot().result.code, 1);
  assert.match(runner.snapshot().result.output, /未找到/);
});

test("提权任务回传独立日志，不读取或发送 API 密钥", async () => {
  let driver;
  const runner = createRunner({
    platform: "win32", sleep: async () => {},
    execute: async (_command, args, options) => {
      driver = args.at(-1);
      await writeFile(options.env.CLAUDE_ZH_STAGE_LOG, "UAC 安装日志", "utf8");
      return { code: 0, output: "" };
    },
  });
  runner.begin({ action: "install", language: "zh-CN", elevated: true, allowTakeOwnership: true });
  await idle(runner);
  assert.match(driver, /RunAs/);
  assert.match(driver, /-Wait/);
  assert.match(driver, /AllowTakeOwnership/);
  assert.equal(runner.snapshot().result.output, "UAC 安装日志");
  assert.equal(runner.snapshot().result.elevated, true);
});

test("UAC 被取消或提权失败时显示未完成", async () => {
  const runner = createRunner({ platform: "win32", execute: async () => ({ code: 1, output: "The operation was canceled by the user" }) });
  runner.begin({ action: "install", language: "zh-CN", elevated: true });
  await idle(runner);
  assert.equal(runner.snapshot().result.ok, false);
  assert.match(runner.snapshot().result.error, /UAC/);
});

test("检测只读取包信息，不读 API 配置", async () => {
  let command;
  const runner = createRunner({ platform: "win32", execute: async (_command, args) => {
    command = args.at(-1);
    return { code: 0, output: JSON.stringify({ installed: true, version: "4.23118.1.0", kind: "Microsoft Store / MSIX" }) };
  } });
  const result = await runner.detect();
  assert.equal(result.version, "4.23118.1.0");
  assert.doesNotMatch(command, /ApiKey|configLibrary|config\.json/);
});
