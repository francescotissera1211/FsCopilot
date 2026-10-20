"""Overlay this repo's bridge HTML/JS onto a prebuilt fscopilot-bridge package and refresh layout.json.

The bridge's "ui" asset group is a plain Copy of PackageSources/HTML_UI into html_ui/, and the WASM module is
unchanged, so the MSFS package tool is not needed: copy the files and regenerate the file listing the sim uses
to mount the package.

usage: overlay_bridge.py <package_dir> <html_ui_sources_dir>
"""
import json
import os
import shutil
import sys

FILETIME_EPOCH_OFFSET = 116444736000000000  # 100ns intervals between 1601-01-01 and 1970-01-01


def find_child(parent, name):
    for entry in os.listdir(parent):
        if entry.lower() == name.lower():
            return os.path.join(parent, entry)
    return os.path.join(parent, name)


def main(package_dir, sources_dir):
    html_ui = find_child(package_dir, "html_ui")
    copied = 0
    for root, _, files in os.walk(sources_dir):
        for f in files:
            src = os.path.join(root, f)
            rel = os.path.relpath(src, sources_dir)
            dst = os.path.join(html_ui, rel)
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            shutil.copyfile(src, dst)
            copied += 1
            print(f"overlay {rel}")

    layout_path = os.path.join(package_dir, "layout.json")
    content = []
    total = 0
    for root, _, files in os.walk(package_dir):
        for f in files:
            full = os.path.join(root, f)
            rel = os.path.relpath(full, package_dir).replace(os.sep, "/")
            if rel.lower() in ("layout.json", "manifest.json"):
                continue
            st = os.stat(full)
            total += st.st_size
            content.append({
                "path": rel,
                "size": st.st_size,
                "date": int(st.st_mtime * 10_000_000) + FILETIME_EPOCH_OFFSET,
            })
    content.sort(key=lambda e: e["path"].lower())
    with open(layout_path, "w", encoding="utf-8") as fh:
        json.dump({"content": content}, fh, indent=2)
    total += os.path.getsize(layout_path)

    manifest_path = os.path.join(package_dir, "manifest.json")
    if os.path.exists(manifest_path):
        with open(manifest_path, encoding="utf-8-sig") as fh:
            manifest = json.load(fh)
        old = manifest.get("total_package_size")
        if isinstance(old, str):
            manifest["total_package_size"] = str(total).zfill(len(old))
        elif old is not None:
            manifest["total_package_size"] = total
        with open(manifest_path, "w", encoding="utf-8") as fh:
            json.dump(manifest, fh, indent=2)

    print(f"overlaid {copied} files, layout lists {len(content)} files")
    if copied == 0:
        sys.exit("no bridge sources found")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
