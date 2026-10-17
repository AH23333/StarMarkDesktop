# -*- coding: utf-8 -*-
"""Phase C leg 18: stage the shippable board assets into assets/boards/ + a frozen manifest.

随包口径＝§九〔裁决 2026-10-17·代裁〕决策 3／4：
  - bundle 一颗（esbuild 单档 IIFE，locales 已内嵌——spike 无 locales 目录而 zh 可用的现读），
    mermaid 插件按决策 4 摘除（--alias 到 stub-mermaid.js）；
  - 字体＝8 个非 Xiaolai 族全随（25 档）＋ Xiaolai 主区 100 片级（unicodeRange 与
    U+4E00..U+9FA5 有交集者；「只随出现过」那档因清单随内容漂移而否掉）。
    裁切规则唯此一处（本脚本）＝那颗裁切判据的主人；Core/测试只验「盘上==清单」。
  - manifest.json＝冻结清单（逐档 bytes+sha256），闸门测试（BoardsAssetGateTests）拿它对盘。

官方命令纪律：本脚本只做「复制＋清单＋一次 esbuild 调用」，esbuild 从钉住的 node_modules 跑。
"""
import argparse
import hashlib
import io
import json
import os
import re
import shutil
import subprocess
import sys
from datetime import datetime, timezone

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
PINNED_BUILD = os.path.join(REPO, "bin", "WI83", "npm0e", "build")
PINNED_PKG = os.path.join(REPO, "bin", "WI83", "npm0e", "pkg", "package", "dist", "prod")
PINNED_TGZ = os.path.join(REPO, "bin", "WI83", "npm0e", "excalidraw-excalidraw-0.18.0.tgz")

REQUIRED_FAMILIES = ["Assistant", "Cascadia", "ComicShanns", "Excalifont", "Liberation", "Lilita", "Nunito", "Virgil"]
XIAOLAI = "Xiaolai"
MAIN_LO, MAIN_HI = 0x4E00, 0x9FA5


def sha256_file(p):
    h = hashlib.sha256()
    with io.open(p, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def range_intersects_main(spec):
    """unicodeRange 一段（如 U+4e00-9fa5,U+3007,…）与主区是否有交集。规范来自 §24.3 那批现读形状。"""
    for tok in spec.split(","):
        tok = tok.strip().lower()
        if not tok.startswith("u+"):
            continue
        body = tok[2:]
        if "-" in body:
            a, _, b = body.partition("-")
            try:
                lo, hi = int(a, 16), int(b, 16)
            except ValueError:
                continue
        else:
            try:
                lo = hi = int(body, 16)
            except ValueError:
                continue
        if lo <= MAIN_HI and hi >= MAIN_LO:
            return True
    return False


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--build", default=PINNED_BUILD)
    ap.add_argument("--pkg", default=PINNED_PKG)
    ap.add_argument("--tgz", default=PINNED_TGZ)
    ap.add_argument("--out", default=os.path.join(REPO, "assets", "boards"))
    args = ap.parse_args()

    esbuild_js = os.path.join(args.build, "node_modules", "esbuild", "bin", "esbuild")
    if not os.path.isfile(esbuild_js):
        raise SystemExit("esbuild 未找到（钉住的位置）：%s —— 先按片7 的口径装好钉住版本" % esbuild_js)
    for d in (args.pkg,):
        if not os.path.isdir(d):
            raise SystemExit("包 dist 未找到：%s" % d)

    out = args.out
    if os.path.isdir(out):
        shutil.rmtree(out)
    os.makedirs(out)

    # 1) bundle：entry + mermaid stub alias（决策 4）。
    #    node 的模块解析从「entry 所在目录」向上找 node_modules ⇒ entry/stub 先落进钉住的 build 树内，编完即删。
    stage_dir = os.path.join(args.build, ".starmark-boards-stage")
    if os.path.isdir(stage_dir):
        shutil.rmtree(stage_dir)
    os.makedirs(stage_dir)
    entry = os.path.join(stage_dir, "entry.js")
    stub = os.path.join(stage_dir, "stub-mermaid.js")
    shutil.copy2(os.path.join(REPO, "tools", "boards", "entry.js"), entry)
    shutil.copy2(os.path.join(REPO, "tools", "boards", "stub-mermaid.js"), stub)
    bundle_out = os.path.join(out, "excalidraw.min.js")
    cmd = ["node", esbuild_js, "entry.js",
           "--bundle", "--minify", "--format=iife",
           '--define:process.env.NODE_ENV="production"',
           "--alias:@excalidraw/mermaid-to-excalidraw=./stub-mermaid.js",
           "--outfile=" + bundle_out]
    r = subprocess.run(cmd, cwd=stage_dir, capture_output=True, text=True, encoding="utf-8", errors="replace")
    shutil.rmtree(stage_dir, ignore_errors=True)
    if r.returncode != 0:
        raise SystemExit("esbuild 失败：\n" + (r.stdout or "") + "\n" + (r.stderr or ""))
    esbuild_version = subprocess.run(["node", esbuild_js, "--version"], capture_output=True, text=True).stdout.strip()
    node_version = subprocess.run(["node", "--version"], capture_output=True, text=True).stdout.strip()

    # 2) 样式表（相对路径点字体，随 bundle 一起走）
    shutil.copy2(os.path.join(args.pkg, "index.css"), os.path.join(out, "index.css"))

    # 3) 字体：8 族全随；Xiaolai 按主区裁（规则主人＝本脚本，裁决 3）
    pkg_fonts = os.path.join(args.pkg, "fonts")
    staged = []
    for fam in REQUIRED_FAMILIES:
        src = os.path.join(pkg_fonts, fam)
        dst = os.path.join(out, "fonts", fam)
        os.makedirs(dst)
        for name in sorted(os.listdir(src)):
            shutil.copy2(os.path.join(src, name), os.path.join(dst, name))

    bundle_text = io.open(bundle_out, encoding="utf-8", errors="replace").read()
    varmap = {}
    for m in re.finditer(r'(\w+)="\./fonts/' + XIAOLAI + r'/([^"]+)"', bundle_text):
        varmap[m.group(1)] = m.group(2)
    kept = {}
    for m in re.finditer(r'uri:(\w+),descriptors:\{unicodeRange:"([^"]+)"', bundle_text):
        v, spec = m.group(1), m.group(2)
        if v in varmap and v not in kept and range_intersects_main(spec):
            kept[v] = varmap[v]
    xiaolai_src = os.path.join(pkg_fonts, XIAOLAI)
    xiaolai_dst = os.path.join(out, "fonts", XIAOLAI)
    os.makedirs(xiaolai_dst)
    for name in sorted(set(kept.values())):
        src = os.path.join(xiaolai_src, name)
        if not os.path.isfile(src):
            raise SystemExit("注册表引用的 Xiaolai 片在包里缺档：%s" % name)
        shutil.copy2(src, os.path.join(xiaolai_dst, name))
    dead = sorted(set(varmap.values()) - set(kept.values()))
    print("Xiaolai: registry=%d kept(main-area)=%d dead consts=%d" % (len(varmap), len(set(kept.values())), len(dead)))

    # 4) manifest：冻结清单（含 bundle/css 自身）
    files = []
    for root, _dirs, names in os.walk(out):
        for name in sorted(names):
            p = os.path.join(root, name)
            rel = os.path.relpath(p, out).replace("\\", "/")
            files.append({"path": rel, "bytes": os.path.getsize(p), "sha256": sha256_file(p)})
    files.sort(key=lambda e: e["path"])
    tgz_sha = sha256_file(args.tgz) if os.path.isfile(args.tgz) else None
    manifest = {
        "schemaVersion": 1,
        "generatedAtUtc": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "source": {
            "package": "@excalidraw/excalidraw@0.18.0",
            "packageTarballSha256": tgz_sha,
            "esbuildVersion": esbuild_version,
            "nodeVersion": node_version,
            "mermaidStub": True,
            "xiaolaiCut": "main-area U+4E00..U+9FA5（§九 裁决 3；规则主人＝tools/boards/stage-assets.py）",
        },
        "files": files,
    }
    with io.open(os.path.join(out, "manifest.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(manifest, f, ensure_ascii=False, indent=1)
        f.write("\n")

    total = sum(e["bytes"] for e in files)
    print("staged %d files, %.2f MB -> %s" % (len(files), total / 1048576.0, out))
    print("bundle: %d bytes sha256=%s" % (os.path.getsize(bundle_out), sha256_file(bundle_out)[:16] + "…"))


if __name__ == "__main__":
    main()
