#!/bin/bash
# MIT; uses the pinned javaht/claude-desktop-zh-cn patcher bundled alongside this file.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
PATCHER="$ROOT/scripts/patch_claude_zh_cn.py"

# This mode checks bundled files only. It never runs the patcher or touches Claude.
if [ "${1:-}" = "--check" ]; then
    test -f "$PATCHER"
    test -f "$ROOT/scripts/LICENSE"
    for lang in zh-CN zh-TW zh-HK; do
        test -f "$ROOT/resources/frontend-$lang.json"
        test -f "$ROOT/resources/frontend-hardcoded-$lang.json"
    done
    printf '%s\n' 'PACKAGE_CHECK_OK (resources only; no installation performed)'
    exit 0
fi

if [ "$(uname -s)" != "Darwin" ]; then
    printf '%s\n' '此启动器只能在 macOS 上执行。未修改 Claude。' >&2
    exit 1
fi

# Fixed system/Homebrew locations only; do not run an executable from the download folder.
PYTHON=""
for candidate in /opt/homebrew/bin/python3 /usr/local/bin/python3 /usr/bin/python3; do
    if [ -x "$candidate" ] && "$candidate" -I -c 'import sys; raise SystemExit(0 if sys.version_info >= (3, 10) else 1)' >/dev/null 2>&1; then
        PYTHON="$candidate"
        break
    fi
done
if [ -z "$PYTHON" ]; then
    printf '%s\n' '需要 Python 3.10 或更新版本。请从 https://www.python.org/downloads/macos/ 安装后再打开本工具。'
    /usr/bin/osascript -e 'display dialog "需要 Python 3.10 或更新版本。请从 python.org 安装后重试。尚未关闭或修改 Claude。" with title "Claude Desktop 中文助手" buttons {"知道了"} default button "知道了"' >/dev/null
    exit 1
fi

/bin/bash "$0" --check
APP="/Applications/Claude.app"
if [ ! -d "$APP" ]; then
    APP="$HOME/Applications/Claude.app"
fi
if [ ! -d "$APP/Contents" ]; then
    /usr/bin/osascript -e 'display dialog "未找到 Claude.app。请先将 Claude 安装到 /Applications 或用户 Applications 文件夹。" with title "Claude Desktop 中文助手" buttons {"知道了"} default button "知道了"' >/dev/null
    exit 1
fi

# Cancellations occur before sudo and before any installation process is started.
CHOICE="$(/usr/bin/osascript <<'APPLESCRIPT'
set selected to choose from list {"简体中文 · zh-CN", "繁體中文（台灣） · zh-TW", "繁體中文（香港） · zh-HK", "恢复官方文件（使用备份）"} with title "Claude Desktop 中文助手" with prompt "选择界面语言或恢复操作，然后点击好。此时不会修改 Claude。" default items {"简体中文 · zh-CN"} OK button name "下一步" cancel button name "取消"
if selected is false then return "cancel"
return item 1 of selected
APPLESCRIPT
)" || exit 0
case "$CHOICE" in
    '简体中文 · zh-CN') ACTION=install; LANG_CODE=zh-CN ;;
    '繁體中文（台灣） · zh-TW') ACTION=install; LANG_CODE=zh-TW ;;
    '繁體中文（香港） · zh-HK') ACTION=install; LANG_CODE=zh-HK ;;
    '恢复官方文件（使用备份）') ACTION=restore; LANG_CODE=zh-CN ;;
    *) exit 0 ;;
esac

if [ "$ACTION" = restore ]; then
    backups=("$(dirname "$APP")"/Claude.backup-before-zh-CN-*.app)
    if [ ! -d "${backups[0]}" ]; then
        /usr/bin/osascript -e 'display dialog "未找到安装前备份，无需执行恢复。Claude 没有被关闭。" with title "Claude Desktop 中文助手" buttons {"知道了"} default button "知道了"' >/dev/null
        exit 0
    fi
fi

/usr/bin/osascript <<'APPLESCRIPT' >/dev/null || exit 0
display dialog "即将关闭 Claude Desktop 并修改本机应用。请先保存正在进行的工作。\n\n安装会保留原应用备份，并使用本机临时签名重新签署修改后的应用。恢复会使用最近一次备份，可能恢复到较早版本。\n\n本工具尚未在真实 Mac 上验收。确认继续后，请在终端输入这台 Mac 的登录密码（输入时不会显示字符）。" with title "确认执行" buttons {"取消", "开始执行"} default button "取消" cancel button "取消" with icon caution
APPLESCRIPT

LOG_DIR="$HOME/Library/Logs/ClaudeDesktop-ZH-CN"
mkdir -p "$LOG_DIR"
# macOS mkdir is atomic. Keep concurrent install/restore jobs from sharing the target app.
LOCK="$LOG_DIR/task.lock"
if ! mkdir "$LOCK" 2>/dev/null; then
    printf '%s\n' "已有任务锁：$LOCK。请等待任务结束；异常退出后请先确认没有安装任务，再删除这个空目录。" >&2
    exit 1
fi
trap 'rmdir "$LOCK" 2>/dev/null || true' EXIT
LOG="$LOG_DIR/task-$(date +%Y%m%d-%H%M%S)-$$.log"
printf '目标：%s\n操作：%s %s\n日志：%s\n' "$APP" "$ACTION" "$LANG_CODE" "$LOG"
printf '%s\n' '正在请求管理员权限；接下来可能关闭 Claude Desktop。'

# Password input belongs to sudo, before output is piped into the log.
/usr/bin/sudo -v
set +e
if [ "$ACTION" = restore ]; then
    /usr/bin/sudo "$PYTHON" -I -u "$PATCHER" --app "$APP" --user-home "$HOME" --restore 2>&1 | /usr/bin/tee "$LOG"
    RESULT=${PIPESTATUS[0]}
else
    # Match upstream pre-install restoration, but only after language selection and confirmation.
    /usr/bin/sudo "$PYTHON" -I -u "$PATCHER" --app "$APP" --user-home "$HOME" --restore-if-backup-exists 2>&1 | /usr/bin/tee "$LOG"
    RESULT=${PIPESTATUS[0]}
    if [ "$RESULT" -eq 0 ]; then
        /usr/bin/sudo "$PYTHON" -I -u "$PATCHER" --app "$APP" --user-home "$HOME" --lang "$LANG_CODE" 2>&1 | /usr/bin/tee -a "$LOG"
        RESULT=${PIPESTATUS[0]}
    fi
fi
set -e

if [ "$RESULT" -eq 0 ]; then
    # Launch as the original GUI user rather than from the root Python process.
    /usr/bin/open "$APP" || true
    printf '\n%s\n' '执行完成。请在 Claude 内检查语言与常用页面。'
    /usr/bin/osascript -e 'display dialog "安装器已返回成功，已尝试重新打开 Claude。请检查语言是否切换及常用页面是否正常。" with title "执行完成" buttons {"知道了"} default button "知道了"' >/dev/null || true
else
    printf '\n执行失败，退出码：%s\n日志：%s\n' "$RESULT" "$LOG" >&2
    /usr/bin/osascript -e 'display dialog "执行未完成。请查看终端日志；原应用备份由上游安装器保留。可重新打开本工具选择恢复官方文件。" with title "执行失败" buttons {"知道了"} default button "知道了" with icon caution' >/dev/null || true
fi
if [ -t 0 ]; then
    printf '%s' '按回车关闭此终端任务…'
    read -r _ || true
fi
exit "$RESULT"
