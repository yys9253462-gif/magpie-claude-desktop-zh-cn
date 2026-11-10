// 不使用 Magpie 时也可启动：node serve.mjs，然后打开 http://127.0.0.1:3485
import { startServer } from "./lib/server.mjs";

const service = await startServer({ port: Number(process.env.PORT) || 3485 });
console.log(`Claude Desktop 中文助手：http://127.0.0.1:${service.port}`);
console.log("关闭此窗口即可停止服务。");
process.on("SIGINT", async () => { await service.close(); process.exit(0); });
