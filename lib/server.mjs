// 仅监听回环地址。状态和控制接口需要随机口令；跨源、Host 伪造和简单 GET 写操作均拒绝。
import { createServer } from "node:http";
import { randomBytes, timingSafeEqual } from "node:crypto";
import { readFile, stat } from "node:fs/promises";
import { dirname, extname, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";
import { createRunner } from "./runner.mjs";

const UI_ROOT = dirname(fileURLToPath(import.meta.url));
const TYPES = {
  ".html": "text/html; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".svg": "image/svg+xml",
};
const HEADERS = {
  "x-content-type-options": "nosniff",
  "referrer-policy": "no-referrer",
  "content-security-policy": "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self' http://127.0.0.1:* http://localhost:* https://127.0.0.1:* https://localhost:*; object-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors *",
};

export function sameSecret(received, expected) {
  const a = Buffer.from(String(received || ""));
  const b = Buffer.from(expected);
  return a.length === b.length && timingSafeEqual(a, b);
}

export function allowedOrigin(origin, own) {
  return origin === undefined || origin === "null" || own.has(origin);
}

export function createApp({ runner = createRunner(), token = randomBytes(32).toString("hex") } = {}) {
  let own = new Set();
  let hosts = new Set();
  let listening = false;

  async function readJson(req) {
    const chunks = [];
    let size = 0;
    for await (const chunk of req) {
      size += chunk.length;
      if (size > 8192) throw Object.assign(new Error("请求过大。"), { status: 413 });
      chunks.push(chunk);
    }
    try { return JSON.parse(Buffer.concat(chunks).toString("utf8") || "{}"); }
    catch { throw Object.assign(new Error("请求必须是 JSON。"), { status: 400 }); }
  }

  function send(req, res, status, body, type = "application/json; charset=utf-8") {
    const data = typeof body === "string" || Buffer.isBuffer(body) ? body : JSON.stringify(body);
    res.writeHead(status, {
      ...HEADERS,
      ...(String(req.url || "").startsWith("/api/") ? {
        "access-control-allow-origin": req.headers.origin === "null" ? "null" : req.headers.origin || "*",
        vary: "Origin",
      } : {}),
      "content-type": type,
      "content-length": Buffer.byteLength(data),
      "cache-control": "no-store",
    });
    res.end(data);
  }

  async function handle(req, res) {
    const path = new URL(req.url || "/", "http://127.0.0.1").pathname;
    if (!listening) return send(req, res, 503, { error: "本机助手尚未监听，请稍后重试。" });
    if (!hosts.has(String(req.headers.host || "").toLowerCase())) return send(req, res, 403, { error: "Host 不属于本机助手。" });
    if (!allowedOrigin(req.headers.origin, own)) return send(req, res, 403, { error: "拒绝跨源请求。" });
    if (req.method === "OPTIONS" && path.startsWith("/api/")) {
      res.writeHead(204, {
        "access-control-allow-origin": req.headers.origin === "null" ? "null" : req.headers.origin || "*",
        "access-control-allow-headers": "content-type, x-claude-zh-token",
        "access-control-allow-methods": "GET,POST,OPTIONS",
        "access-control-max-age": "300",
        vary: "Origin",
      });
      return res.end();
    }
    if (path.startsWith("/api/") && !sameSecret(req.headers["x-claude-zh-token"], token)) {
      return send(req, res, 403, { error: "会话口令无效，请重新打开页面。", reload: true });
    }
    try {
      if (path === "/api/state" && req.method === "GET") {
        const detected = await runner.detect().catch((error) => ({ installed: false, error: error.message }));
        return send(req, res, 200, { ...runner.snapshot(), detected, upstream: "1.4.23" });
      }
      if (path === "/api/task" && req.method === "POST") {
        const body = await readJson(req);
        return send(req, res, 202, runner.begin({
          action: body.action,
          language: body.language,
          elevated: body.elevated === true,
          allowTakeOwnership: body.allowTakeOwnership === true,
        }));
      }
      if (path.startsWith("/api/")) return send(req, res, 404, { error: "没有这个接口。" });
      if (req.method !== "GET" && req.method !== "HEAD") return send(req, res, 405, { error: "不支持的请求方法。" });

      let relative;
      try { relative = decodeURIComponent(path); } catch { return send(req, res, 400, { error: "路径无效。" }); }
      if (relative.includes("\0") || relative.includes("\\")) return send(req, res, 403, { error: "路径无效。" });
      const file = resolve(UI_ROOT, `.${relative === "/" ? "/index.html" : relative}`);
      if (file !== UI_ROOT && !file.startsWith(UI_ROOT + sep)) return send(req, res, 403, { error: "路径越界。" });
      const info = await stat(file).catch(() => null);
      if (!info?.isFile()) return send(req, res, 404, { error: "文件不存在。" });
      let data = await readFile(file);
      if (file === resolve(UI_ROOT, "index.html")) {
        data = Buffer.from(data.toString("utf8")
          .replace("__CLAUDE_ZH_TOKEN__", token)
          .replace("__CLAUDE_ZH_BASE__", new URL(req.url || "/", `http://${req.headers.host}`).href));
      }
      return send(req, res, 200, data, TYPES[extname(file)] || "application/octet-stream");
    } catch (error) {
      return send(req, res, error.status || 400, { ...runner.snapshot(), error: error.message });
    }
  }

  return {
    async listen(port = 3485) {
      if (!Number.isInteger(port) || port < 0 || port > 65535) throw new Error("端口无效。");
      const server = createServer(handle);
      await new Promise((resolveListen, reject) => {
        server.once("error", reject);
        server.listen(port, "127.0.0.1", resolveListen);
      });
      const actualPort = server.address().port;
      own = new Set([`http://127.0.0.1:${actualPort}`, `http://localhost:${actualPort}`]);
      hosts = new Set([`127.0.0.1:${actualPort}`, `localhost:${actualPort}`]);
      listening = true;
      return { port: actualPort, server, close: () => new Promise((done) => { listening = false; server.closeAllConnections?.(); server.close(done); }) };
    },
    handle,
  };
}

export async function startServer({ port = 3485, runner } = {}) {
  const app = createApp({ runner });
  return app.listen(port);
}
