// 按 Magpie 官方 page service 协议：宿主提供回环服务，入口握手后导航。
import { startServer } from "./lib/server.mjs";

let started;
export default {
  id: "magpie-claude-desktop-zh-cn",
  server: async (_input, options) => {
    started ??= startServer({ port: Number(options?.port) || 3485 }).catch((error) => {
      console.error(`[claude-desktop-zh-cn] ${error.message}`);
      return null;
    });
    await started;
    return {};
  },
};
