// 官方 page service 路径：先在 Magpie 页面完成 hello，再导航到宿主服务。
(async () => {
  let port = 3485;
  let service = "";
  try {
    await window.magpie?.ready;
    const options = await window.magpie?.options?.get?.();
    const value = Number(options?.port);
    if (Number.isInteger(value) && value > 0 && value < 65536) port = value;
    if (typeof window.magpie?.service === "string") service = window.magpie.service;
  } catch { /* 旧版使用默认本机端口 */ }
  const base = service || `http://127.0.0.1:${port}/`;
  location.replace(base.endsWith("/") ? base : `${base}/`);
  setTimeout(() => {
    const said = document.getElementById("said");
    if (said) said.textContent = "本机助手未响应。请确认 Magpie 在 Windows 上运行，然后重新打开此页面。";
  }, 8000);
})();
