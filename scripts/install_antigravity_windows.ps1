# Antigravity 桌面端简体中文补丁安装与还原脚本（纯 PowerShell，零 Python 依赖）
# 基于 MIMICTE/Antigravity-zh-CN (MIT) 的补丁逻辑与资源实现

param(
    [Parameter(Position = 0)]
    [ValidateSet("install", "uninstall")]
    [string]$Action = "install",

    [Parameter(Position = 1)]
    [string]$Language = "zh-CN",

    [string]$OriginalLocalAppData = "",
    [string]$AppDirOverride = "",
    [string]$StageLogPath = "",
    [switch]$SkipLaunch,
    [switch]$SkipProcessStop,
    [switch]$SelfTest
)

$ErrorActionPreference = "Stop"

try {
    [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
} catch {}

if (-not $StageLogPath -and $env:CLAUDE_ZH_STAGE_LOG) {
    $StageLogPath = $env:CLAUDE_ZH_STAGE_LOG
}

function Write-Host {
    param(
        [Parameter(Position = 0, ValueFromPipeline = $true)]
        [object]$Object = "",
        [System.ConsoleColor]$ForegroundColor,
        [System.ConsoleColor]$BackgroundColor,
        [switch]$NoNewline
    )
    process {
        $text = if ($null -eq $Object) { "" } else { [string]$Object }
        try {
            $params = @{ Object = $text }
            if ($PSBoundParameters.ContainsKey("ForegroundColor")) { $params.ForegroundColor = $ForegroundColor }
            if ($PSBoundParameters.ContainsKey("BackgroundColor")) { $params.BackgroundColor = $BackgroundColor }
            if ($NoNewline) { $params.NoNewline = $true }
            Microsoft.PowerShell.Utility\Write-Host @params
        } catch {
            # 输出通道彻底不可用时静默忽略，避免管道断开抛 0xE9 中断安装流程
        }
        if ($script:StageLogPath) {
            try {
                $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
                [System.IO.File]::AppendAllText($script:StageLogPath, $text + "`r`n", $utf8NoBom)
            } catch {}
        }
    }
}

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent $ScriptDir
$AntigravityResDir = Join-Path $RepoRoot "resources\antigravity"
$DomTranslatorPath = Join-Path $AntigravityResDir "dom-translator.js"
$MenuTranslatorPath = Join-Path $AntigravityResDir "menu-translator.js"

if ($Language -ne "zh-CN") {
    Write-Host "[错误] Antigravity 汉化当前仅支持简体中文（zh-CN），不支持: $Language" -ForegroundColor Red
    exit 1
}

function Resolve-AntigravityAppDir {
    if ($AppDirOverride) {
        return $AppDirOverride
    }
    $localApp = if ($OriginalLocalAppData) { $OriginalLocalAppData } else { $env:LOCALAPPDATA }
    if (-not $localApp) {
        return ""
    }
    return (Join-Path $localApp "Programs\antigravity")
}

$AppDir = Resolve-AntigravityAppDir
$ResourcesDir = if ($AppDir) { Join-Path $AppDir "resources" } else { "" }
$AsarPath = if ($ResourcesDir) { Join-Path $ResourcesDir "app.asar" } else { "" }
$DisabledAsarPath = if ($AsarPath) { "$AsarPath.disabled" } else { "" }
$UnpackedAppDir = if ($ResourcesDir) { Join-Path $ResourcesDir "app" } else { "" }
$ExePath = if ($AppDir) { Join-Path $AppDir "Antigravity.exe" } else { "" }

function Get-AntigravityStatus {
    $installed = $false
    $patched = $false
    $version = ""

    if ($ExePath -and (Test-Path -LiteralPath $ExePath)) {
        $installed = $true
        try {
            $verInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($ExePath)
            if ($verInfo.ProductVersion) { $version = $verInfo.ProductVersion }
            elseif ($verInfo.FileVersion) { $version = $verInfo.FileVersion }
        } catch {}
    } elseif ($AsarPath -and ((Test-Path -LiteralPath $AsarPath) -or (Test-Path -LiteralPath $DisabledAsarPath))) {
        $installed = $true
    }

    if ($DisabledAsarPath -and (Test-Path -LiteralPath $DisabledAsarPath) -and $UnpackedAppDir -and (Test-Path -LiteralPath $UnpackedAppDir)) {
        $patched = $true
    }

    return [pscustomobject]@{
        installed = $installed
        patched = $patched
        version = $version
        appDir = $AppDir
        asarExists = [bool]($AsarPath -and (Test-Path -LiteralPath $AsarPath))
        disabledAsarExists = [bool]($DisabledAsarPath -and (Test-Path -LiteralPath $DisabledAsarPath))
        unpackedExists = [bool]($UnpackedAppDir -and (Test-Path -LiteralPath $UnpackedAppDir))
    }
}

function Invoke-SelfTest {
    Write-Host "[自检] 模式: 只读自检（不关闭 Antigravity，不写入任何文件）"
    if (-not (Test-Path -LiteralPath $DomTranslatorPath)) {
        Write-Host "[错误] 缺少内嵌资源: $DomTranslatorPath" -ForegroundColor Red
        exit 1
    }
    if (-not (Test-Path -LiteralPath $MenuTranslatorPath)) {
        Write-Host "[错误] 缺少内嵌资源: $MenuTranslatorPath" -ForegroundColor Red
        exit 1
    }
    $st = Get-AntigravityStatus
    Write-Host "[自检] 动作: $Action | 语言: $Language"
    Write-Host "[自检] 目标目录: $($st.appDir)"
    Write-Host "[自检] 已安装: $($st.installed) | 已汉化: $($st.patched) | 版本: $($st.version)"
    Write-Host "[自检] 资源文件就绪，只读自检通过。"
    exit 0
}

if ($SelfTest) {
    Invoke-SelfTest
}

function Stop-AntigravityProcesses {
    if ($SkipProcessStop -or $AppDirOverride) {
        Write-Host "[跳过] 沙箱或指定跳过模式，不关闭本机 Antigravity 进程。"
        return
    }
    $procs = @(Get-Process -Name "Antigravity" -ErrorAction SilentlyContinue)
    if ($procs.Count -eq 0) {
        return
    }
    Write-Host "[执行] 正在关闭运行中的 Antigravity 进程（共 $($procs.Count) 个）..."
    foreach ($p in $procs) {
        try {
            if ($p.MainWindowHandle -ne [IntPtr]::Zero) {
                $null = $p.CloseMainWindow()
            }
        } catch {}
    }

    $deadline = (Get-Date).AddSeconds(4)
    while ((Get-Date) -lt $deadline) {
        $alive = @(Get-Process -Name "Antigravity" -ErrorAction SilentlyContinue)
        if ($alive.Count -eq 0) { return }
        Start-Sleep -Milliseconds 150
    }

    $remaining = @(Get-Process -Name "Antigravity" -ErrorAction SilentlyContinue)
    foreach ($p in $remaining) {
        try { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue } catch {}
    }

    $killDeadline = (Get-Date).AddSeconds(6)
    while ((Get-Date) -lt $killDeadline) {
        $alive = @(Get-Process -Name "Antigravity" -ErrorAction SilentlyContinue)
        if ($alive.Count -eq 0) { return }
        Start-Sleep -Milliseconds 150
    }
    throw "无法完全关闭 Antigravity.exe，请手动完全退出后重试。"
}

function Expand-AsarNode {
    param(
        [System.IO.BinaryReader]$Reader,
        [long]$BaseOffset,
        [object]$Node,
        [string]$CurrentPath
    )
    if (-not (Test-Path -LiteralPath $CurrentPath)) {
        $null = New-Item -ItemType Directory -Path $CurrentPath -Force
    }
    foreach ($prop in $Node.PSObject.Properties) {
        $name = $prop.Name
        $info = $prop.Value
        $targetPath = Join-Path $CurrentPath $name
        if ($null -ne $info.PSObject.Properties["files"]) {
            Expand-AsarNode -Reader $Reader -BaseOffset $BaseOffset -Node $info.files -CurrentPath $targetPath
        } elseif ($null -ne $info.PSObject.Properties["offset"]) {
            $offset = [long]$info.offset
            $size = [int]$info.size
            $parent = Split-Path -Parent $targetPath
            if (-not (Test-Path -LiteralPath $parent)) {
                $null = New-Item -ItemType Directory -Path $parent -Force
            }
            $Reader.BaseStream.Seek($BaseOffset + $offset, [System.IO.SeekOrigin]::Begin) | Out-Null
            if ($size -gt 0) {
                $bytes = $Reader.ReadBytes($size)
                [System.IO.File]::WriteAllBytes($targetPath, $bytes)
            } else {
                [System.IO.File]::WriteAllBytes($targetPath, [byte[]]@())
            }
        }
    }
}

function Expand-AsarArchive {
    param(
        [string]$ArchivePath,
        [string]$DestDir
    )
    $fs = [System.IO.File]::OpenRead($ArchivePath)
    $reader = [System.IO.BinaryReader]::new($fs)
    try {
        $magic = $reader.ReadUInt32()
        $size = $reader.ReadUInt32()
        $u1 = $reader.ReadUInt32()
        $headerSize = $reader.ReadUInt32()
        if ($magic -ne 4 -or $headerSize -le 0) {
            throw "ASAR 文件头格式异常 (magic=$magic, headerSize=$headerSize)"
        }
        $headerBytes = $reader.ReadBytes([int]$headerSize)
        $headerJson = [System.Text.Encoding]::UTF8.GetString($headerBytes)
        $header = $headerJson | ConvertFrom-Json
        $baseOffset = [long]8 + [long]$size
        if ($null -ne $header.files) {
            Expand-AsarNode -Reader $reader -BaseOffset $baseOffset -Node $header.files -CurrentPath $DestDir
        }
    } finally {
        $reader.Dispose()
        $fs.Dispose()
    }
}

function Add-IdempotentInjection {
    param(
        [string]$FilePath,
        [string]$PayloadContent,
        [string]$Marker,
        [string]$Label
    )
    if (-not (Test-Path -LiteralPath $FilePath)) {
        Write-Host "[警告] 未找到文件 $(Split-Path -Leaf $FilePath)，已跳过注入 $Label。"
        return
    }
    $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
    $existing = [System.IO.File]::ReadAllText($FilePath, $utf8NoBom)
    $idx = $existing.IndexOf("// " + $Marker, [System.StringComparison]::Ordinal)
    if ($idx -lt 0) {
        $idx = $existing.IndexOf($Marker, [System.StringComparison]::Ordinal)
    }
    if ($idx -ge 0) {
        Write-Host "[状态] 发现已存在的 $Label，正在热更新词典..."
        $prefix = $existing.Substring(0, $idx).TrimEnd()
        $updated = $prefix + "`n" + $PayloadContent.Trim() + "`n"
        [System.IO.File]::WriteAllText($FilePath, $updated, $utf8NoBom)
        Write-Host "[成功] 词典热更新完成：$Label ($(Split-Path -Leaf $FilePath))"
        return
    }
    $appended = $existing.TrimEnd() + "`n`n" + $PayloadContent.Trim() + "`n"
    [System.IO.File]::WriteAllText($FilePath, $appended, $utf8NoBom)
    Write-Host "[成功] 注入完成：$Label ($(Split-Path -Leaf $FilePath))"
}

function Update-TextExact {
    param(
        [string]$FilePath,
        [string]$Target,
        [string]$Replacement
    )
    if (-not (Test-Path -LiteralPath $FilePath)) {
        return
    }
    $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
    $content = [System.IO.File]::ReadAllText($FilePath, $utf8NoBom)
    if ($content.Contains($Replacement)) {
        Write-Host "[跳过] 目标已替换 ($(Split-Path -Leaf $FilePath))"
        return
    }
    if ($content.Contains($Target)) {
        $updated = $content.Replace($Target, $Replacement)
        [System.IO.File]::WriteAllText($FilePath, $updated, $utf8NoBom)
        Write-Host "[成功] 执行目标替换 ($(Split-Path -Leaf $FilePath))"
    }
}

function Start-AntigravityIfAllowed {
    if ($SkipLaunch) {
        Write-Host "[跳过] 已指定 -SkipLaunch，不自动启动 Antigravity。"
        return
    }
    if ($ExePath -and (Test-Path -LiteralPath $ExePath)) {
        Write-Host "[执行] 正在启动 Antigravity..."
        Start-Process -FilePath $ExePath | Out-Null
    }
}

function Invoke-AntigravityInstall {
    if (-not (Test-Path -LiteralPath $DomTranslatorPath) -or -not (Test-Path -LiteralPath $MenuTranslatorPath)) {
        throw "缺少 Antigravity 汉化资源文件：$AntigravityResDir"
    }
    if (-not $ResourcesDir -or -not (Test-Path -LiteralPath $ResourcesDir)) {
        throw "未检测到 Antigravity 安装目录：$AppDir"
    }

    Stop-AntigravityProcesses

    $isFreshInstall = (Test-Path -LiteralPath $AsarPath) -and (-not (Test-Path -LiteralPath $DisabledAsarPath))
    if ($isFreshInstall -and (Test-Path -LiteralPath $UnpackedAppDir)) {
        Write-Host "[状态] 检测到客户端可能已更新，正在清理旧的解包缓存..."
        Remove-Item -LiteralPath $UnpackedAppDir -Recurse -Force
    }

    if (-not (Test-Path -LiteralPath $UnpackedAppDir)) {
        $sourceAsar = if (Test-Path -LiteralPath $AsarPath) { $AsarPath } elseif (Test-Path -LiteralPath $DisabledAsarPath) { $DisabledAsarPath } else { "" }
        if (-not $sourceAsar) {
            throw "未找到 app.asar：$AsarPath"
        }
        Write-Host "[执行] 正在解包 app.asar 核心文件（纯 PowerShell 原生解析器）..."
        Expand-AsarArchive -ArchivePath $sourceAsar -DestDir $UnpackedAppDir
        Write-Host "[完成] app.asar 解包完成。"
    } else {
        Write-Host "[状态] 发现已解包工作目录：$UnpackedAppDir"
    }

    $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
    $domJs = [System.IO.File]::ReadAllText($DomTranslatorPath, $utf8NoBom)
    $menuJs = [System.IO.File]::ReadAllText($MenuTranslatorPath, $utf8NoBom)

    $preloadPath = Join-Path $UnpackedAppDir "dist\preload.js"
    $menuPath = Join-Path $UnpackedAppDir "dist\menu.js"
    $trayPath = Join-Path $UnpackedAppDir "dist\tray.js"
    $loadingPath = Join-Path $UnpackedAppDir "dist\loadingOverlay.js"

    Add-IdempotentInjection -FilePath $preloadPath -PayloadContent $domJs -Marker "Antigravity Chinese Localization Engine" -Label "Web UI Injection"
    Add-IdempotentInjection -FilePath $menuPath -PayloadContent $menuJs -Marker "Antigravity Chinese Localization Engine - Menu" -Label "Menu Translator Injection"

    Update-TextExact -FilePath $trayPath -Target "'Show Antigravity'" -Replacement "'显示 Antigravity'"
    Update-TextExact -FilePath $trayPath -Target "'Quit'" -Replacement "'退出'"
    Update-TextExact -FilePath $loadingPath -Target ">Loading Antigravity<" -Replacement ">正在加载 Antigravity<"

    if (Test-Path -LiteralPath $AsarPath) {
        Write-Host "[执行] 正在禁用官方 app.asar，以强制读取汉化目录..."
        if (Test-Path -LiteralPath $DisabledAsarPath) {
            Remove-Item -LiteralPath $DisabledAsarPath -Force
        }
        Move-Item -LiteralPath $AsarPath -Destination $DisabledAsarPath -Force
        Write-Host "[成功] app.asar -> app.asar.disabled"
    }

    Write-Host "[完成] Antigravity 简体中文补丁注入成功！"
    Start-AntigravityIfAllowed
}

function Invoke-AntigravityUninstall {
    if (-not $ResourcesDir -or -not (Test-Path -LiteralPath $ResourcesDir)) {
        throw "未检测到 Antigravity 安装目录：$AppDir"
    }

    $hasDisabled = Test-Path -LiteralPath $DisabledAsarPath
    $hasUnpacked = Test-Path -LiteralPath $UnpackedAppDir
    if (-not $hasDisabled -and -not $hasUnpacked) {
        Write-Host "[状态] 未发现已禁用的 app.asar.disabled 或解包残留，Antigravity 已是官方原版状态，无需恢复。"
        return
    }

    Stop-AntigravityProcesses

    if ($hasDisabled) {
        Write-Host "[执行] 正在恢复官方 app.asar..."
        if (Test-Path -LiteralPath $AsarPath) {
            Remove-Item -LiteralPath $AsarPath -Force
        }
        Move-Item -LiteralPath $DisabledAsarPath -Destination $AsarPath -Force
        if (Test-Path -LiteralPath $UnpackedAppDir) {
            Remove-Item -LiteralPath $UnpackedAppDir -Recurse -Force
        }
        Write-Host "[完成] 已无损恢复 Antigravity 官方原版文件。"
        Start-AntigravityIfAllowed
        return
    }

    if ($hasUnpacked) {
        Write-Host "[执行] 正在清理残留解包目录：$UnpackedAppDir"
        Remove-Item -LiteralPath $UnpackedAppDir -Recurse -Force
        Write-Host "[完成] 已清理汉化残留，当前为原版状态。"
    }
}

try {
    switch ($Action) {
        "install" { Invoke-AntigravityInstall }
        "uninstall" { Invoke-AntigravityUninstall }
    }
    exit 0
} catch {
    Write-Host "[错误] $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
