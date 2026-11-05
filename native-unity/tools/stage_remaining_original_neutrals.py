#!/usr/bin/env python3
"""Stage seven additional pinned Map11 monster rigs into the local Unity Player.

Each source is a regular Git blob at an immutable upstream commit, verified by
byte length, SHA-256, Git blob SHA and source animation name. No model binary
is committed to this repository. Compatible with Windows Runner Python 3.8.
"""
import argparse
import base64
import hashlib
import json
import time
import urllib.request
from pathlib import Path

from convert_webp_glb import convert, parse_glb, sha256
from stage_original_neutral_art import (
    SOURCE_COMMIT, SOURCE_REPO, SOURCE_PATH as BLUE_PATH,
    SOURCE_SHA256 as BLUE_SHA256, SOURCE_BYTES as BLUE_BYTES,
    SOURCE_BLOB_SHA as BLUE_BLOB_SHA, IDLE_CLIP as BLUE_IDLE,
)

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_TARGET = ROOT / "Assets" / "StreamingAssets" / "Neutrals"
SOURCE_DIR = "public/assets/units/jungle"
# The sourceScale fields are extracted from the pinned upstream jungle
# manifest; they are visual guidance, not proof of final in-game fidelity.
SPECS = (
    # name, source bytes, source SHA256, git blob SHA1, source idle clip, sourceScale
    ("buff_red", 2710724, "0a06eed929ba61dec8a4feff2edb44beeb0e6712d573aa4f1c92967f52a31c1f",
     "02c876a48f5d22b3c7184915160ec7288eb3d23c", "sru_red_idle1", 2.075),
    ("gromp", 2494844, "ba8070aa03cdda575e33c5a61deff0b1cdf95dfc5471616b6a477c346521ad3a",
     "8964c85a38414484ee51888699cd3df57a90049b", "sru_gromp_idle1", 1.0),
    ("wolves", 1957580, "9b9992a26de40957eb565c9ca6258b51d569d1491a401c04b5403c3ccc6f7ed5",
     "21d3fe534d0b3cf56362fdad8892f57bd8530923", "sru_murkwolf_idle1", 1.2),
    ("raptors", 1687736, "f0464e241c0e8c3c6ebeb04f136b773b8557a3864157815df8fde6c1295d6696",
     "8dc07de42df7f2ba02f0c065eada593057e80e67", "sru_razorbeak_idle_normal1", 1.4),
    ("krugs", 1512112, "16df469a0c7e68f31cc3850eb6da153d79f20905775cb89e8ae0a8eb9f301b05",
     "5053df3ab7f937b01ac934609d4776f4b831e717", "krug_idle_normal", 1.15),
    ("scuttle", 972092, "40f0563b30d3a454e1d1cabb6bc653ee85889d12a3ea5c5493d73cb919387f26",
     "4b5a0074539d615586ca4d1350afd7d55682fb7e", "sru_crab_idle1", 0.75),
    ("dragon", 4658904, "ded89570c13274d7faa939e5695a43588ba35c57089a520303dee1b2465b30b3",
     "5cae5b6f568c19391da689ccf3b2e4ea07caac36", "sru_dragon_idle_normal", 1.25),
)
RECEIPT_VERSION = 1


def check_raw(data, spec):
    name, size, digest, _, idle, _ = spec
    if len(data) != size or hashlib.sha256(data).hexdigest() != digest:
        raise ValueError("Pinned original " + name + " source size/hash mismatch")
    doc, _ = parse_glb(data)
    if not doc.get("skins") or not doc.get("meshes"):
        raise ValueError("Pinned original " + name + " missing skinned geometry")
    if idle not in {a.get("name") for a in doc.get("animations", [])}:
        raise ValueError("Pinned original " + name + " missing required idle clip")
    return doc


def fetch_source(spec, cache_root):
    name, size, digest, blob_sha, _, _ = spec
    cached = cache_root / (name + ".glb") if cache_root else None
    if cached and cached.exists():
        if cached.is_symlink() or not cached.is_file():
            raise ValueError("Unsafe original monster cache entry: " + name)
        data = cached.read_bytes()
        check_raw(data, spec)
        print("NATIVE_NEUTRAL_SOURCE_CACHE_OK: " + name + " sha256=" + digest)
        return data
    raw = ("https://raw.githubusercontent.com/" + SOURCE_REPO +
           "/" + SOURCE_COMMIT + "/" + SOURCE_DIR + "/" + name + ".glb")
    api = ("https://api.github.com/repos/" + SOURCE_REPO + "/git/blobs/" + blob_sha)
    previous = None
    for url, mode in ((raw, "raw"), (api, "git-blob-api")):
        for attempt in range(3):
            try:
                request = urllib.request.Request(
                    url, headers={"User-Agent": "HextechSeekers-neutral-verifier",
                                  "Accept": "application/vnd.github+json" if mode == "git-blob-api" else "application/octet-stream"})
                with urllib.request.urlopen(request, timeout=90) as response:
                    response_bytes = response.read(size * 2 + 4096)
                if mode == "git-blob-api":
                    entry = json.loads(response_bytes.decode("utf-8"))
                    if (entry.get("sha") != blob_sha or entry.get("size") != size or
                            entry.get("encoding") != "base64"):
                        raise ValueError("GitHub blob provenance mismatch: " + name)
                    data = base64.b64decode(entry["content"], validate=False)
                else:
                    data = response_bytes
                check_raw(data, spec)
                if cached:
                    if cache_root.is_symlink():
                        raise ValueError("Unsafe original monster cache directory")
                    cache_root.mkdir(parents=True, exist_ok=True)
                    pending = cached.with_suffix(".glb.part")
                    try:
                        pending.write_bytes(data)
                        pending.replace(cached)
                    finally:
                        pending.unlink(missing_ok=True)
                print("NATIVE_NEUTRAL_ORIGINAL_FETCH_OK: kind=" + name +
                      " mode=" + mode + " bytes=" + str(len(data)) +
                      " sha256=" + digest)
                return data
            except (OSError, ValueError, KeyError, TypeError) as err:
                previous = err
                print("NATIVE_NEUTRAL_SOURCE_RETRY: " + name + " mode=" + mode +
                      " attempt=" + str(attempt + 1) + " " + str(err))
                if attempt < 2:
                    time.sleep(2 * (attempt + 1))
    raise RuntimeError("Pinned original neutral unavailable: " + name) from previous


def stage_one(spec, target, cache_root):
    name, size, digest, blob_sha, idle, scale = spec
    source = fetch_source(spec, cache_root)
    converted, audit = convert(source, digest)
    document, _ = parse_glb(converted)
    if not document.get("skins") or not document.get("meshes") or idle not in {
            entry.get("name") for entry in document.get("animations", [])}:
        raise ValueError("Converted original neutral lost its rig or idle: " + name)
    if target.is_symlink():
        raise ValueError("Refusing linked Unity neutral asset folder")
    target.mkdir(parents=True, exist_ok=True)
    path = target / (name + ".glb")
    pending = target / (name + ".glb.part")
    try:
        pending.write_bytes(converted)
        pending.replace(path)
    finally:
        pending.unlink(missing_ok=True)
    proof = {
        "version": RECEIPT_VERSION,
        "kind": name,
        "sourceRepo": SOURCE_REPO,
        "sourceCommit": SOURCE_COMMIT,
        "sourcePath": SOURCE_DIR + "/" + name + ".glb",
        "sourceBlobSha": blob_sha,
        "sourceSha256": digest,
        "sourceBytes": size,
        "convertedSha256": sha256(converted),
        "convertedBytes": len(converted),
        "idleClip": idle,
        "sourceScale": scale,
        "sourceVerified": True,
        "skinnedMeshCount": len(document["skins"]),
        "animationCount": len(document.get("animations", [])),
        "webpConverted": audit["convertedImages"],
        "note": "Original third-party artwork; local Player asset only, never committed or redistributed.",
    }
    receipt = target / (name + ".json")
    part = target / (name + ".json.part")
    try:
        part.write_text(json.dumps(proof, indent=2) + "\n", encoding="utf-8")
        part.replace(receipt)
    finally:
        part.unlink(missing_ok=True)
    print("NATIVE_NEUTRAL_SOURCE_STAGED: " + json.dumps(proof, sort_keys=True))
    return proof


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--target", type=Path, default=DEFAULT_TARGET)
    ap.add_argument("--cache-root", type=Path)
    args = ap.parse_args()
    staged = [stage_one(spec, args.target, args.cache_root) for spec in SPECS]
    if len(staged) != 7 or len({x["kind"] for x in staged}) != 7:
        raise AssertionError("Not all seven independent original neutral models staged")
    print("NATIVE_NEUTRAL_ALL_ADDITIONAL_STAGED: count=" + str(len(staged)))


if __name__ == "__main__":
    main()
