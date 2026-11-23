#!/usr/bin/env python3
"""Publishes a GitHub release with the installable zip attached.

The zip itself is built by tools/release.ps1, so this script only talks to GitHub:

    set GH_TOKEN=...            (a token with the `repo` scope)
    python tools/publish-release.py --version 0.8.3 --notes-file notes.md

If the release for that tag already exists it is reused, and an asset with the same name is
replaced. Requires Python 3.9+ (uses urllib only, no extra packages).
"""

import argparse
import json
import os
import pathlib
import sys
import urllib.error
import urllib.request

OWNER = "Icantfindone"
REPO = "Act-Floor-Customizer"
GAME_BUILD = "v0.113.1"


def api(url, method="GET", payload=None, token=""):
    data = None if payload is None else json.dumps(payload).encode()
    request = urllib.request.Request(
        url,
        data=data,
        method=method,
        headers={
            "Authorization": "token " + token,
            "Accept": "application/vnd.github+json",
            "Content-Type": "application/json",
            "User-Agent": "act-floor-customizer-release",
        },
    )
    with urllib.request.urlopen(request, timeout=60) as response:
        body = response.read()
    # DELETE answers 204 No Content, so an empty body is expected and normal.
    return json.loads(body) if body.strip() else None


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--version", required=True, help="for example 0.8.3")
    parser.add_argument("--notes-file", required=True, help="markdown body of the release")
    parser.add_argument("--zip", default=None, help="defaults to the zip built by release.ps1")
    parser.add_argument("--draft", action="store_true")
    args = parser.parse_args()

    token = os.environ.get("GH_TOKEN", "").strip()
    if not token:
        sys.exit("GH_TOKEN is not set")

    root = pathlib.Path(__file__).resolve().parent.parent
    zip_path = pathlib.Path(args.zip) if args.zip else (
        root / "releases" / f"ActFloorCustomizer-v{args.version}-sts2-{GAME_BUILD}.zip")
    if not zip_path.exists():
        sys.exit(f"missing {zip_path}; run tools/release.ps1 first")
    notes = pathlib.Path(args.notes_file).read_text(encoding="utf-8")

    tag = "v" + args.version
    base = f"https://api.github.com/repos/{OWNER}/{REPO}"
    try:
        release = api(f"{base}/releases/tags/{tag}", token=token)
        print(f"reusing release {tag} ({release['id']})")
        release = api(f"{base}/releases/{release['id']}", "PATCH",
                      {"body": notes, "name": f"{tag}", "draft": args.draft}, token)
    except urllib.error.HTTPError as error:
        if error.code != 404:
            raise
        release = api(f"{base}/releases", "POST", {
            "tag_name": tag,
            "target_commitish": "main",
            "name": tag,
            "body": notes,
            "draft": args.draft,
            "prerelease": False,
        }, token)
        print(f"created release {tag} ({release['id']})")

    # Replace an existing asset with the same name so a rebuild can be republished.
    for asset in release.get("assets", []):
        if asset["name"] == zip_path.name:
            api(f"{base}/releases/assets/{asset['id']}", "DELETE", token=token)
            print("removed the previous asset of the same name")

    upload = (f"https://uploads.github.com/repos/{OWNER}/{REPO}/releases/"
              f"{release['id']}/assets?name={zip_path.name}")
    request = urllib.request.Request(
        upload,
        data=zip_path.read_bytes(),
        method="POST",
        headers={
            "Authorization": "token " + token,
            "Content-Type": "application/zip",
            "User-Agent": "act-floor-customizer-release",
        },
    )
    asset = json.load(urllib.request.urlopen(request, timeout=180))
    print(f"asset {asset['name']} ({asset['size']} bytes, {asset['state']})")
    print(release["html_url"])


if __name__ == "__main__":
    main()
