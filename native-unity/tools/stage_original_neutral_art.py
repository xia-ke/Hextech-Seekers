#!/usr/bin/env python3
"""Stage one SHA-verified original Map11 blue sentinel GLB for native Unity.

This material is fetched only into the local build, never committed to the
Hextech-Seekers repository. A pinned source commit and SHA are mandatory.
An unverified, partially downloaded or malformed model aborts staging.
"""
import argparse
import base64
import hashlib
import json
import os
import time
import urllib.request
from pathlib import Path
from typing import Optional

from convert_webp_glb import convert, parse_glb, sha256

ROOT = Path(__file__).resolve().parents[1]
TARGET = ROOT / "Assets" / "StreamingAssets" / "Neutrals"
SOURCE_COMMIT = "557a9d5352af12bee8bf3db879c22d04d0a0954e"
SOURCE_REPO = "iamywl/Web_game"
SOURCE_PATH = "public/assets/units/jungle/buff_blue.glb"
# This GLB is a full ordinary Git blob, not an LFS pointer. The GitHub
# /media/ endpoint returned HTTP 404 on the Windows GPU runner.
SOURCE_URL = ("https://raw.githubusercontent.com/" + SOURCE_REPO +
              "/" + SOURCE_COMMIT + "/" + SOURCE_PATH)
SOURCE_BLOB_SHA = "faaf2c3d4d0b5e2abd7791c126cbf2021cb653b3"
SOURCE_BLOB_API = ("https://api.github.com/repos/" + SOURCE_REPO +
                   "/git/blobs/" + SOURCE_BLOB_SHA)
SOURCE_SHA256 = "6d3181d21998e13783f0bd05ff571217b86714d0ec0f3bbaab424d5979fee1c9"
SOURCE_BYTES = 3179272
IDLE_CLIP = "sru_blue_idle_normal"
NAME = "buff_blue"


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def verified_source(data: bytes) -> bytes:
    if len(data) != SOURCE_BYTES or digest(data) != SOURCE_SHA256:
        raise ValueError("Original blue sentinel GLB size/SHA256 mismatch")
    gltf, _ = parse_glb(data)
    if not gltf.get("skins") or not gltf.get("meshes"):
        raise ValueError("Original blue sentinel is missing skinned meshes")
    clips = {clip.get("name") for clip in gltf.get("animations", [])}
    if IDLE_CLIP not in clips:
        raise ValueError("Original blue sentinel idle animation is absent")
    return data


def source_bytes(cache_root: Optional[Path]) -> bytes:
    cached = cache_root / (NAME + ".glb") if cache_root else None
    if cached and cached.is_file():
        if cached.is_symlink():
            raise ValueError("Refusing linked neutral source")
        data = cached.read_bytes()
        return verified_source(data)
    last = None
    # First try GitHub's normal raw Git blob endpoint; the /media/ path used
    # for some LFS assets does NOT resolve this regular 3,179,272-byte blob.
    # If raw CDN access fails, GitHub's Git Blob API can serve base64 with an
    # independently pinned blob SHA. Neither response is trusted until it
    # passes the original byte-length, SHA-256 and GLB structural checks.
    for url, mode in ((SOURCE_URL, "raw"), (SOURCE_BLOB_API, "git-blob-api")):
        for attempt in range(3):
            try:
                request = urllib.request.Request(
                    url, headers={"User-Agent": "HextechSeekers-asset-verifier",
                                  "Accept": "application/vnd.github+json" if mode == "git-blob-api" else "application/octet-stream"})
                with urllib.request.urlopen(request, timeout=90) as response:
                    payload = response.read(SOURCE_BYTES * 2 + 4096)
                if mode == "git-blob-api":
                    decoded = json.loads(payload.decode("utf-8"))
                    if (decoded.get("sha") != SOURCE_BLOB_SHA or
                            decoded.get("size") != SOURCE_BYTES or
                            decoded.get("encoding") != "base64"):
                        raise ValueError("Git blob metadata/encoding mismatch")
                    data = base64.b64decode(decoded["content"], validate=False)
                else:
                    data = payload
                verified_source(data)
                if cached:
                    if cache_root.is_symlink():
                        raise ValueError("Refusing linked cache root")
                    cache_root.mkdir(parents=True, exist_ok=True)
                    pending = cached.with_suffix(".part")
                    try:
                        pending.write_bytes(data)
                        pending.replace(cached)
                    finally:
                        pending.unlink(missing_ok=True)
                print("NATIVE_NEUTRAL_ORIGINAL_FETCH_OK: mode=" + mode +
                      " bytes=" + str(len(data)) +
                      " sha256=" + SOURCE_SHA256)
                return data
            except (OSError, ValueError, KeyError, TypeError) as exc:
                last = exc
                print("NATIVE_NEUTRAL_ORIGINAL_FETCH_RETRY: mode=" + mode +
                      " attempt=" + str(attempt + 1) +
                      " reason=" + type(exc).__name__ + ": " + str(exc))
                if attempt < 2:
                    time.sleep(2 * (attempt + 1))
    raise RuntimeError("Cannot retrieve pinned original blue sentinel Git blob from raw or API") from last


def stage(target: Path = TARGET, cache_root: Optional[Path] = None) -> dict:
    original = source_bytes(cache_root)
    converted, audit = convert(original, SOURCE_SHA256)
    document, _ = parse_glb(converted)
    if not document.get("skins") or not document.get("meshes"):
        raise ValueError("Converted blue sentinel lost original geometry")
    if IDLE_CLIP not in {clip.get("name") for clip in document.get("animations", [])}:
        raise ValueError("Converted blue sentinel lost original idle clip")
    if target.is_symlink():
        raise ValueError("Refusing linked neutral output directory")
    target.mkdir(parents=True, exist_ok=True)
    output = target / (NAME + ".glb")
    part = output.with_suffix(".glb.part")
    try:
        part.write_bytes(converted)
        part.replace(output)
    finally:
        part.unlink(missing_ok=True)
    receipt = {
        "version": 1,
        "kind": NAME,
        "sourceRepo": SOURCE_REPO,
        "sourceCommit": SOURCE_COMMIT,
        "sourcePath": SOURCE_PATH,
        "sourceSha256": SOURCE_SHA256,
        "sourceBytes": SOURCE_BYTES,
        "convertedSha256": sha256(converted),
        "convertedBytes": len(converted),
        "idleClip": IDLE_CLIP,
        "sourceVerified": True,
        "skinnedMeshCount": len(document["skins"]),
        "animationCount": len(document["animations"]),
        "webpConverted": audit["convertedImages"],
        "note": "Original third-party artwork; local runtime import only, not committed or redistributed.",
    }
    part_receipt = target / (NAME + ".json.part")
    try:
        part_receipt.write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
        part_receipt.replace(target / (NAME + ".json"))
    finally:
        part_receipt.unlink(missing_ok=True)
    print("NATIVE_NEUTRAL_SOURCE_STAGED: " + json.dumps(receipt, sort_keys=True))
    return receipt


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--target", type=Path, default=TARGET)
    parser.add_argument("--cache-root", type=Path)
    args = parser.parse_args()
    stage(args.target, args.cache_root)


if __name__ == "__main__":
    main()
