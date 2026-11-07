#!/usr/bin/env python3
"""Add (or replace) one version in the Jellyfin plugin repository manifest (manifest.json).

Used by scripts/build.sh --release-url and by the GitHub release workflow. Standard library only.
"""
import argparse
import json
from pathlib import Path

GUID = "dc9c7d6f-2de4-4ee4-9187-6a87ca998510"


def main() -> None:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--manifest", required=True)
    p.add_argument("--version", required=True)
    p.add_argument("--url", required=True, help="Download URL of the plugin zip")
    p.add_argument("--checksum", required=True, help="MD5 of the zip")
    p.add_argument("--abi", default="10.11.0.0", help="Minimum Jellyfin version (targetAbi)")
    p.add_argument("--timestamp", required=True)
    p.add_argument("--changelog", default="")
    a = p.parse_args()

    path = Path(a.manifest)
    data = json.loads(path.read_text(encoding="utf-8")) if path.exists() else []
    pkg = next((x for x in data if x.get("guid") == GUID), None)
    if pkg is None:
        raise SystemExit(f"{path}: no package with guid {GUID}")

    entry = {
        "version": a.version,
        "changelog": a.changelog,
        "targetAbi": a.abi,
        "sourceUrl": a.url,
        "checksum": a.checksum,
        "timestamp": a.timestamp,
    }
    versions = [v for v in pkg.get("versions", []) if v.get("version") != a.version]
    pkg["versions"] = [entry] + versions
    path.write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
