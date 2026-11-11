# Claude Desktop 中文助手 · macOS 便携版 0.1.0

这是 Mac 专用的启动器和语言资源包，不使用 Windows EXE 或 Magpie。
支持简体中文、繁體中文（台灣）、繁體中文（香港），面向 Claude Desktop 第三方 API / Gateway 模式。

**验证状态：已在 Windows 完成打包、资源完整性与脚本语法检查；尚未在真实 macOS 上完成启动、安装、签名或恢复验收。**

## 使用前提

- macOS 11 或更新版本；Apple Silicon / Intel 使用相同脚本包，实际兼容性待 Mac 验证。
- Claude Desktop 位于 `/Applications/Claude.app` 或 `~/Applications/Claude.app`。
- 已安装 Python **3.10 或更新版本**。本包不内嵌 Python；macOS 不保证自带可用版本。
- 支持 python.org 的 `/usr/local/bin/python3`、Homebrew 的 `/opt/homebrew/bin/python3` 以及满足版本要求的 `/usr/bin/python3`。
- 安装需要管理员账户密码。密码只由系统 sudo 提示读取，不会被本工具保存到日志。

本包内嵌固定上游 1.4.23 的脚本和三种语言资源，运行时不从 GitHub 下载补丁，也不读取 API 密钥。

## 使用

1. 解压 ZIP，保持 `.app` 与其内部资源完整。
2. 双击 `ClaudeDesktop-ZH-CN.app`。它会打开终端，并显示 macOS 语言选择窗口。也可以双击旁边的 `打开中文助手.command`。
3. 选择简体、台湾繁体、香港繁体，点击「下一步」。此时尚未修改或关闭 Claude。
4. 保存 Claude 中的工作。在第二个窗口点击「开始执行」才会请求管理员权限并执行补丁；点击取消不会安装。
5. 在终端输入 Mac 登录密码，输入时不会显示字符。安装过程中 Claude 会退出，当前 Claude 对话也会暂时中断；不要同时用 Claude Desktop 驱动这次安装。
6. 完成后工具尝试重新打开 Claude；检查语言及常用页面。脚本返回成功不代表所有页面均已验证。

本工具没有 Apple Developer ID 签名或公证。若 macOS 提示无法验证开发者，请先核对来源、阅读源码，并按 macOS「隐私与安全性」中的允许打开流程处理；受管理设备可能禁止打开。不要全局关闭 Gatekeeper。

如果解压工具未保留可执行权限，可在终端以 `/bin/bash` 运行 `打开中文助手.command`（可把该文件拖入终端补全完整路径）。

## 恢复与日志

重新打开工具，选择「恢复官方文件（使用备份）」。如果没有找到备份，直接退出，不关闭 Claude。

原应用备份保存在 Claude.app 同级目录，名称形如 `Claude.backup-before-zh-CN-时间戳.app`。上游恢复逻辑会恢复最近备份并删除其他备份；备份可能早于当前 Claude 版本，执行前请核对。

日志位于 `~/Library/Logs/ClaudeDesktop-ZH-CN/`，失败时终端会打印完整路径。安装会修改前端资源并使用本机 ad-hoc 签名重新签署 Claude，官方更新后可能需要重新安装补丁。不要把未验证的最新 Claude 版本视为自动兼容。

同时只能执行一个安装或恢复任务。异常退出后若残留 `~/Library/Logs/ClaudeDesktop-ZH-CN/task.lock` 空目录，先确认没有正在运行的任务，再删除该空目录。

## 开发与打包

在源码仓库执行：

```bash
python -I macos/package.py
```

产物：`artifacts/macos/ClaudeDesktop-ZH-CN-macOS-0.1.0.zip` 和 SHA-256 文件。

仅检查资源、不调用补丁：

```bash
bash "ClaudeDesktop-ZH-CN.app/Contents/Resources/launch.command" --check
```

`--check` 不代表 Mac 安装测试。上游 `--dry-run` 会复制、修改及重新签署临时 Claude.app，并非纯粹的只读检查，本启动器不会自动执行它。

## 版权

MIT。上游 javaht/claude-desktop-zh-cn 1.4.23，固定提交 `6b611ab50e90ba546a90af2dffa00f5c2ae5a19b`。许可见包内 `LICENSE` 与 `scripts/LICENSE`，来源见 `UPSTREAM.lock.json`。本项目为非官方工具。
