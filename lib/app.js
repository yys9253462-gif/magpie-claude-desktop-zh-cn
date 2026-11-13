const $ = (id) => document.getElementById(id);
const token = document.querySelector('meta[name="claude-zh-token"]').content;
const base = document.querySelector('meta[name="claude-zh-base"]').content.replace(/\/$/, "");
let state = null;
let busy = false;
let timer = null;
let currentTarget = "claude";

async function api(path, options = {}) {
  const response = await fetch(`${base}${path}`, {
    ...options,
    headers: {
      "content-type": "application/json",
      "x-claude-zh-token": token,
      ...options.headers,
    },
  });
  const data = await response.json().catch(() => ({ error: "无法解析本机响应。" }));
  if (!response.ok) {
    const error = new Error(data.error || `请求失败 (${response.status})`);
    error.data = data;
    throw error;
  }
  return data;
}

function setStatus(text, kind = "") {
  $("status").textContent = text;
  $("status").dataset.kind = kind;
}

function getTargetDetected(data) {
  if (data?.detected?.targets?.[currentTarget]) {
    return data.detected.targets[currentTarget];
  }
  return currentTarget === "claude" ? (data?.detected || {}) : { installed: false };
}

function updateTargetUI() {
  const isAg = currentTarget === "antigravity";
  $("target-claude").classList.toggle("active", !isAg);
  $("target-antigravity").classList.toggle("active", isAg);
  $("version-label").textContent = isAg ? "Antigravity 版本" : "Claude 版本";
  $("upstream-source").textContent = isAg ? "MIMICTE/Antigravity-zh-CN · v2.21.1" : "javaht/claude-desktop-zh-cn · 1.4.23";
  $("lang-tw-row").hidden = isAg;
  $("lang-hk-row").hidden = isAg;
  $("advanced").hidden = isAg;
  $("post-install-hint").textContent = isAg
    ? "Antigravity 上游目前仅提供简体中文（zh-CN），注入后将自动加载中文界面。"
    : "安装后若未自动切换，请在 Claude 左下角 Language 中选择中文。";

  if (isAg) {
    document.querySelector('input[name="language"][value="zh-CN"]').checked = true;
    $("elevated").checked = false;
    $("take-ownership").checked = false;
    $("take-ownership").disabled = true;
  }
  if (state) render(state);
}

function render(data) {
  state = data;
  busy = Boolean(data.busy);
  const det = getTargetDetected(data);
  const isAg = currentTarget === "antigravity";
  const targetLabel = isAg ? "Antigravity" : "Claude";

  $("platform").textContent = data.platform === "win32" ? `Windows (${data.platform})` : data.platform;
  $("support").textContent = data.supported ? "本机可执行" : "当前主机不支持";
  $("version").textContent = det?.version || (det?.installed ? "已安装，读取版本失败" : "未检测到");
  $("kind").textContent = det?.kind || "—";
  $("detect-message").textContent = det?.message || det?.error || (data.supported
    ? `点击安装后才会修改 ${targetLabel}；当前仅完成环境检测。`
    : `请在安装 ${targetLabel} 的 Windows 电脑上运行 Magpie 与本插件。`);
  $("install").disabled = busy || !data.supported;
  $("uninstall").disabled = busy || !data.supported;
  $("refresh").disabled = false;
  $("task-card").hidden = !data.task && !data.result;

  const task = data.task;
  const result = data.result;
  $("task-tag").textContent = task ? "执行中" : result?.ok ? "已完成" : "未完成";
  const taskTargetName = task?.target === "antigravity" ? "Antigravity" : "Claude";
  $("task-title").textContent = task ? `${taskTargetName} · ${task.action === "install" ? "安装" : "恢复"} · ${task.language}` : "最近一次任务";
  $("task-message").textContent = task
    ? (task.elevatedNotice || `正在关闭 ${taskTargetName}、备份并写入中文资源，请稍候。`)
    : result?.error || (result?.ok ? "执行器已返回成功。请检查目标客户端的实际界面。" : "安装器返回失败，请查看日志；不要反复重试同一错误。");

  const permissionFailure = !result?.ok && /写入权限|Access.*denied|拒绝访问|AllowTakeOwnership|无法写入/.test(result?.output || "");
  $("permission-help").hidden = isAg || !permissionFailure;
  if (!isAg && permissionFailure) {
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
  const isAg = currentTarget === "antigravity";
  const elevated = isAg ? false : $("elevated").checked;
  const takeOwnership = isAg ? false : $("take-ownership").checked;
  const language = isAg ? "zh-CN" : document.querySelector('input[name="language"]:checked').value;
  if (action === "uninstall") $("take-ownership").checked = false;
  const actionName = action === "install" ? "安装中文补丁" : "恢复官方文件";
  const targetName = isAg ? "Antigravity" : "Claude Desktop";
  const warnings = [action === "install"
    ? `将关闭并重新打开 ${targetName}，修改其本地资源并保留备份。请先保存当前工作。`
    : `将关闭 ${targetName}，并使用已有备份恢复官方文件。`];
  if (elevated) warnings.push("将弹出 Windows UAC 提示，并在管理员 PowerShell 窗口执行。插件无法替你确认 UAC。");
  if (takeOwnership) warnings.push("将接管 WindowsApps 所有权，改变受保护目录的 ACL；卸载补丁不会自动恢复所有权。");
  $("confirm-text").textContent = warnings.join(" ");
  $("confirm-box").hidden = false;
  $("confirm-action").textContent = `确认${actionName}`;
  $("confirm-action").onclick = () => run(action, { target: currentTarget, language, elevated, takeOwnership, actionName });
  $("confirm-action").focus();
}

async function run(action, { target, language, elevated, takeOwnership, actionName }) {
  if (busy) return;
  $("confirm-box").hidden = true;
  setStatus("正在启动本机安装器…");
  $("install").disabled = $("uninstall").disabled = true;
  try {
    render(await api("/api/task", {
      method: "POST",
      body: JSON.stringify({ target, action, language, elevated, allowTakeOwnership: takeOwnership }),
    }));
    setStatus(`${actionName}已开始。`, "");
  } catch (error) {
    setStatus(error.message, "error");
    if (error.data) render(error.data);
    await load();
  }
}

$("target-claude").addEventListener("click", () => { currentTarget = "claude"; updateTargetUI(); });
$("target-antigravity").addEventListener("click", () => { currentTarget = "antigravity"; updateTargetUI(); });
$("install").addEventListener("click", () => start("install"));
$("uninstall").addEventListener("click", () => start("uninstall"));
$("refresh").addEventListener("click", load);
$("cancel-action").addEventListener("click", () => { $("confirm-box").hidden = true; });
$("elevated").addEventListener("change", () => {
  $("take-ownership").disabled = !$("elevated").checked;
  if (!$("elevated").checked) $("take-ownership").checked = false;
});
updateTargetUI();
load();
timer = setInterval(() => { if (busy) load(); }, 2000);
addEventListener("pagehide", () => clearInterval(timer));
