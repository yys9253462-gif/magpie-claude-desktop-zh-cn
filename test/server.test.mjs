import test from "node:test";
import assert from "node:assert/strict";
import { createApp, allowedOrigin, sameSecret } from "../lib/server.mjs";
import { request } from "node:http";

function fakeRunner() {
  return {
    snapshot: () => ({ platform: "win32", supported: true, busy: false, task: null, result: null }),
    detect: async () => ({ installed: true, version: "4.23118.1.0" }),
    begin: (body) => ({ busy: true, received: body }),
  };
}

async function setup() {
  const app = createApp({ runner: fakeRunner(), token: "test-token" });
  const service = await app.listen(0);
  return {
    service,
    url: `http://127.0.0.1:${service.port}`,
    close: () => service.close(),
  };
}

test("回环控制台提供界面、随机口令和本地文件", async () => {
  const { service, url, close } = await setup();
  try {
    const response = await fetch(url);
    const html = await response.text();
    assert.equal(response.status, 200);
    assert.match(html, /Claude Desktop 中文/);
    assert.match(html, /content="test-token"/);
    assert.match(html, new RegExp(`content="http://127.0.0.1:${new URL(url).port}/"`));
    assert.doesNotMatch(html, /__CLAUDE_ZH_TOKEN__/);
    assert.match(response.headers.get("content-security-policy"), /connect-src 'self'/);
    const js = await fetch(`${url}/app.js`);
    assert.equal(js.status, 200);
    assert.equal((await fetch(`${url}/%2e%2e%2fpackage.json`)).status, 403);
  } finally { await close(); }
});

test("状态接口要求口令，Host 与跨源检查不可绕过", async () => {
  const { url, close } = await setup();
  try {
    assert.equal((await fetch(`${url}/api/state`)).status, 403);
    const good = { "x-claude-zh-token": "test-token" };
    assert.equal((await fetch(`${url}/api/state`, { headers: good })).status, 200);
    assert.equal((await fetch(`${url}/api/state`, { headers: { ...good, origin: "https://attacker.invalid" } })).status, 403);
    const badHost = await new Promise((resolve, reject) => {
      const req = request(url, { headers: { ...good, host: "attacker.invalid" } }, (res) => {
        res.resume();
        resolve(res.statusCode);
      });
      req.on("error", reject);
      req.end();
    });
    assert.equal(badHost, 403);
  } finally { await close(); }
});

test("操作只接受 JSON POST，不能通过网页 img 触发", async () => {
  const { url, close } = await setup();
  try {
    const headers = { "x-claude-zh-token": "test-token", "content-type": "application/json" };
    const get = await fetch(`${url}/api/task`, { headers });
    assert.equal(get.status, 404);
    const post = await fetch(`${url}/api/task`, {
      method: "POST", headers,
      body: JSON.stringify({ action: "install", language: "zh-CN", elevated: false, allowTakeOwnership: false }),
    });
    assert.equal(post.status, 202);
    const result = await post.json();
    assert.equal(result.received.language, "zh-CN");
  } finally { await close(); }
});

test("请求口令比较与 Origin 白名单", () => {
  assert.equal(sameSecret("secret", "secret"), true);
  assert.equal(sameSecret("wrong", "secret"), false);
  assert.equal(sameSecret(undefined, "secret"), false);
  assert.equal(allowedOrigin(undefined, new Set()), true);
  assert.equal(allowedOrigin("null", new Set()), true);
  assert.equal(allowedOrigin("https://evil.invalid", new Set()), false);
});
