"""Build an unsigned macOS launcher bundle using only the Python standard library.

Run: python -I macos/package.py
No macOS tools or installed Claude app are invoked by this packaging step.
"""
from __future__ import annotations

import hashlib
import plistlib
import stat
import struct
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
VERSION = "0.1.0"
BUNDLE = "ClaudeDesktop-ZH-CN.app"
PREFIX = f"ClaudeDesktop-ZH-CN-macOS-{VERSION}"


def main() -> None:
    files: dict[str, tuple[bytes, int]] = {}

    def add(name: str, data: bytes, executable: bool = False) -> None:
        files[f"{PREFIX}/{name}"] = (data, 0o755 if executable else 0o644)

    contents = f"{BUNDLE}/Contents"
    payload = f"{contents}/Resources"
    info = {
        "CFBundleIdentifier": "io.github.yys9253462-gif.claude-desktop-zh-cn",
        "CFBundleName": "Claude Desktop 中文助手",
        "CFBundleDisplayName": "Claude Desktop 中文助手",
        "CFBundlePackageType": "APPL",
        "CFBundleExecutable": "ClaudeZhLauncher",
        "CFBundleShortVersionString": VERSION,
        "CFBundleVersion": VERSION,
        "CFBundleIconFile": "AppIcon.icns",
        "LSMinimumSystemVersion": "11.0",
        "NSHighResolutionCapable": True,
    }
    add(f"{contents}/Info.plist", plistlib.dumps(info))
    add(f"{contents}/PkgInfo", b"APPL????")
    add(f"{contents}/MacOS/ClaudeZhLauncher", b'''#!/bin/bash
set -euo pipefail
CONTENTS="$(cd "$(dirname "$0")/.." && pwd)"
exec /usr/bin/open -a Terminal "$CONTENTS/Resources/launch.command"
''', executable=True)
    add(f"{payload}/launch.command", (ROOT / "macos/launch.command").read_bytes(), executable=True)
    add(f"{payload}/scripts/patch_claude_zh_cn.py", (ROOT / "scripts/patch_claude_zh_cn.py").read_bytes())
    add(f"{payload}/scripts/LICENSE", (ROOT / "scripts/LICENSE").read_bytes())
    for path in sorted((ROOT / "resources").iterdir()):
        if path.is_file():
            add(f"{payload}/resources/{path.name}", path.read_bytes())
    for name in ("LICENSE", "NOTICE.md", "UPSTREAM.lock.json"):
        add(name, (ROOT / name).read_bytes())
    add("使用说明.md", (ROOT / "macos/README.md").read_bytes())
    add("打开中文助手.command", b'''#!/bin/bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")" && pwd)"
exec /bin/bash "$ROOT/ClaudeDesktop-ZH-CN.app/Contents/Resources/launch.command" "$@"
''', executable=True)

    # PNG-backed ICNS entries work on modern macOS; no iconutil needed on Windows.
    icon_parts = []
    for size, kind in ((128, b"ic07"), (256, b"ic08")):
        data = (ROOT / f"standalone/Assets/icon-{size}.png").read_bytes()
        icon_parts.append(kind + struct.pack(">I", len(data) + 8) + data)
    icon_body = b"".join(icon_parts)
    add(f"{payload}/AppIcon.icns", b"icns" + struct.pack(">I", len(icon_body) + 8) + icon_body)

    output_dir = ROOT / "artifacts/macos"
    output_dir.mkdir(parents=True, exist_ok=True)
    archive_path = output_dir / f"{PREFIX}.zip"
    with zipfile.ZipFile(archive_path, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for name, (data, mode) in sorted(files.items()):
            entry = zipfile.ZipInfo(name)
            entry.create_system = 3  # Preserve Unix executable modes when unzipping on a Mac.
            entry.external_attr = (stat.S_IFREG | mode) << 16
            entry.compress_type = zipfile.ZIP_DEFLATED
            archive.writestr(entry, data)

    # Validate CRCs, exact payload bytes, plists, and executable modes in the final ZIP.
    with zipfile.ZipFile(archive_path) as archive:
        if archive.testzip() is not None:
            raise RuntimeError("Archive CRC validation failed")
        for name, (data, mode) in files.items():
            if archive.read(name) != data:
                raise RuntimeError(f"Payload mismatch: {name}")
            if (archive.getinfo(name).external_attr >> 16) & 0o777 != mode:
                raise RuntimeError(f"Unix file mode mismatch: {name}")
        plist = plistlib.loads(archive.read(f"{PREFIX}/{contents}/Info.plist"))
        if plist["CFBundleExecutable"] != "ClaudeZhLauncher":
            raise RuntimeError("Invalid bundle entry point")

    checksum = hashlib.sha256(archive_path.read_bytes()).hexdigest()
    archive_path.with_suffix(".zip.sha256").write_text(f"{checksum}  {archive_path.name}\n", encoding="ascii")
    print(f"PACKAGE_OK: {archive_path}")
    print(f"FILES: {len(files)}; BYTES: {archive_path.stat().st_size}")
    print(f"SHA256: {checksum}")
    print("Unsigned macOS package; installation has not been tested on a Mac.")


if __name__ == "__main__":
    main()
