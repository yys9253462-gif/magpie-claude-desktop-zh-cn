const $ = (id) => document.getElementById(id);
const token = document.querySelector('meta[name="claude-zh-token"]').content;
let state = null;
let busy = false;
let timer;

async function api(path, options = {}) {
  const response = await fetch(new URL(path, document.querySelector('meta[name="claude-zh-base"]').content), {
    ...options,
    headers: { "content-type": "application/json", "x-claude-zh-token": token },
  });
  const data = await response.json().catch(() => ({ error: `HTTP ${response.status}` }));
  if (!response.ok) throw Object.assign(new Error(data.error || `HTTP ${response.status}`), { data });
  return data;
}

function setStatus(text, kind = "") {
  $("status").textContent = text;
  $("status").dataset.kind = kind;
}

function render(data) {
  state = data;
  busy = Boolean(data.busy);
  $("platform").textContent = data.platform === "win32" ? `Windows (${data.platform})` : data.platform;
  $("support").textContent = data.supported ? "本机可执行" : "当前主机不支持";
  $("version").textContent = data.detected?.version || (data.detected?.installed ? "已安装，读取版本失败" : "未检测到");
  $("kind").textContent = data.detected?.kind || "—";
  $("detect-message").textContent = data.detected?.message || data.detected?.error || (data.supported ? "点击安装后才会修改 Claude；当前仅完成环境检测。" : "请在安装 Claude Desktop 的 Windows 电脑上运行 Magpie 与本插件。");
  $("install").disabled = busy || !data.supported;
  $("uninstall").disabled = busy || !data.supported;
  $("refresh").disabled = false;
  $("task-card").hidden = !data.task && !data.result;
  const task = data.task;
  const result = data.result;
  $("task-tag").textContent = task ? "执行中" : result?.ok ? "已完成" : "未完成";
  $("task-title").textContent = task ? `${task.action === "install" ? "安装" : "恢复"} · ${task.language}` : "最近一次任务";
  $("task-message").textContent = task ? (task.elevatedNotice || "正在关闭 Claude、备份并写入中文资源，请稍候。") : result?.error || (result?.ok ? "执行器已返回成功。请检查 Claude 的实际界面，未自动切换时在 Language 中选择中文。" : "安装器返回失败，请查看日志；不要反复重试同一错误。");
  const permissionFailure = !result?.ok && /写入权限|Access.*denied|拒绝访问|AllowTakeOwnership|无法写入/.test(result?.output || "");
  $("permission-help").hidden = !permissionFailure;
  if (permissionFailure) {
    $("advanced").open = true;
    $("elevated").checked = true;
    $("take-ownership").disabled = false;
    setStatus("检测到写入权限失败。可勾选管理员权限后重试；接管目录所有权仍需另行明确选择。", "error");
  }
  $("log").textContent = task?.output || result?.output || (task ? "正在准备独立安装目录…" : "没有可显示的日志。");
  if (result?.finishedAt) $("task-foot").textContent = `结束时间：${result.finishedAt} · 结果仅表示执行器退出状态，不代表中文界面已经目视验证。`;
}

async function load() {
  try { render(await api("/api/state")); }
  catch (error) { setStatus(error.message, "error"); }
}

async function start(action) {
  if (busy) return;
  const elevated = $("elevated").checked;
  const takeOwnership = $("take-ownership").checked;
  const language = document.querySelector('input[name="language"]:checked').value;
  if (action === "uninstall") $("take-ownership").checked = false;
  const actionName = action === "install" ? "安装中文补丁" : "恢复官方文件";
  const warnings = [action === "install"
    ? "将关闭并重新打开 Claude Desktop，修改其本地资源并保留备份。请先保存当前工作。"
    : "将关闭 Claude Desktop，并使用已有备份恢复官方文件。"];
  if (elevated) warnings.push("将弹出 Windows UAC 提示，并在管理员 PowerShell 窗口执行。插件无法替你确认 UAC。");
  if (takeOwnership) warnings.push("将接管 WindowsApps 所有权，改变受保护目录的 ACL；卸载补丁不会自动恢复所有权。");
  $("confirm-text").textContent = warnings.join(" ");
  $("confirm-box").hidden = false;
  $("confirm-action").textContent = `确认${actionName}`;
  $("confirm-action").onclick = () => run(action, { language, elevated, takeOwnership, actionName });
  $("confirm-action").focus();
}

async function run(action, { language, elevated, takeOwnership, actionName }) {
  if (busy) return;
  $("confirm-box").hidden = true;
  setStatus("正在启动本机安装器…");
  $("install").disabled = $("uninstall").disabled = true;
  try {
    render(await api("/api/task", {
      method: "POST",
      body: JSON.stringify({ action, language, elevated, allowTakeOwnership: takeOwnership }),
    }));
    setStatus(`${actionName}已开始。`, "");
  } catch (error) {
    setStatus(error.message, "error");
    if (error.data) render(error.data);
    await load();
  }
}

$("install").addEventListener("click", () => start("install"));
$("uninstall").addEventListener("click", () => start("uninstall"));
$("refresh").addEventListener("click", load);
$("cancel-action").addEventListener("click", () => { $("confirm-box").hidden = true; });
$("elevated").addEventListener("change", () => {
  $("take-ownership").disabled = !$("elevated").checked;
  if (!$("elevated").checked) $("take-ownership").checked = false;
});
load();
timer = setInterval(() => { if (busy) load(); }, 2000);
addEventListener("pagehide", () => clearInterval(timer));
