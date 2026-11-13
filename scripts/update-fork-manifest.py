#!/usr/bin/env python3
"""Add (or replace) one version entry in a Jellyfin plugin repository manifest.

Used by .github/workflows/publish-fork-manifest.yml. Stdlib only.

The manifest is a JSON array holding one plugin object. Running this twice for the same
version replaces the entry instead of duplicating it, so a re-run release is safe.
"""
import argparse
import hashlib
import json
import os
import re
import sys
from datetime import datetime, timezone

GUID = "63ba5fcd-c8ce-421a-83e8-ba0b11030d53"  # must match Plugin.cs, so the fork replaces upstream installs
NAME = "Xtream Library"
OVERVIEW = "Sync Xtream VOD and Series content to native Jellyfin libraries via STRM files"
DESCRIPTION = (
    "Creates STRM files from your Xtream provider's VOD and Series content, enabling native "
    "Jellyfin library integration with full metadata support. Fork build."
)


def target_abi(csproj_path):
    """Derive targetAbi from the Jellyfin.Controller package version (12.0.0 -> 12.0.0.0)."""
    text = open(csproj_path, encoding="utf-8").read()
    m = re.search(r'<PackageReference Include="Jellyfin\.Controller" Version="(\d+)\.(\d+)\.(\d+)"', text)
    if not m:
        sys.exit("Could not find the Jellyfin.Controller version in " + csproj_path)
    return f"{m.group(1)}.{m.group(2)}.{m.group(3)}.0"


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--manifest", required=True)
    p.add_argument("--version", required=True, help="numeric version, e.g. 2.0.28.1")
    p.add_argument("--zip", required=True, help="the release asset, for the checksum")
    p.add_argument("--source-url", required=True)
    p.add_argument("--changelog", required=True)
    p.add_argument("--csproj", required=True)
    p.add_argument("--owner", required=True)
    a = p.parse_args()

    if not re.fullmatch(r"\d+\.\d+\.\d+\.\d+", a.version):
        sys.exit(f"Version '{a.version}' is not of the form X.Y.Z.W")

    with open(a.zip, "rb") as f:
        checksum = hashlib.md5(f.read()).hexdigest().upper()

    manifest = []
    if os.path.exists(a.manifest):
        with open(a.manifest, encoding="utf-8") as f:
            manifest = json.load(f)

    plugin = next((x for x in manifest if x.get("guid") == GUID), None)
    if plugin is None:
        plugin = {
            "guid": GUID,
            "name": NAME,
            "overview": OVERVIEW,
            "description": DESCRIPTION,
            "owner": a.owner,
            "category": "General",
            "versions": [],
        }
        manifest.append(plugin)

    entry = {
        "version": a.version,
        "changelog": a.changelog,
        "targetAbi": target_abi(a.csproj),
        "sourceUrl": a.source_url,
        "checksum": checksum,
        "timestamp": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
    }
    plugin["versions"] = [entry] + [v for v in plugin["versions"] if v.get("version") != a.version]

    with open(a.manifest, "w", encoding="utf-8", newline="\n") as f:
        json.dump(manifest, f, indent=4, ensure_ascii=False)
        f.write("\n")
    print(f"Manifest now lists {len(plugin['versions'])} version(s); added {a.version} ({checksum}).")


if __name__ == "__main__":
    main()
