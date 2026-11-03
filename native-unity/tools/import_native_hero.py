#!/usr/bin/env python3
"""Import *locally supplied*, licensed/provenanced original LoL model data.

This never downloads or invents Riot art. The existing
scripts/convert-native-champion.py performs the documented lmesh/lanim
conversion. This wrapper checks the hero identity, clips and source hashes
before publishing an optional native Unity skinned rig.

Example:
  python native-unity/tools/import_native_hero.py \
    --hero Riven --source /private/provenance/riven --stem 92_0

The generated files remain in StreamingAssets/Champions/<Hero>, which is
ignored by git. Original Riot art is not added to a distributable repository.
"""
import argparse
import hashlib
import json
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path
from typing import Optional

NATIVE = Path(__file__).resolve().parents[1]
REPO = NATIVE.parent
CONVERTER = REPO / "scripts/convert-native-champion.py"
HEROES = ("Blitzcrank", "Riven", "Yasuo")


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as reader:
        for block in iter(lambda: reader.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def import_source(hero: str, source: Path, stem: str, destination: Path,
                  bindings: Optional[Path] = None) -> dict:
    if hero not in HEROES:
        raise ValueError("Optional imports are only Blitzcrank, Riven or Yasuo")
    source = source.resolve(strict=True)
    if not source.is_dir():
        raise ValueError("Expected a local source directory")
    if not stem or "/" in stem or "\\" in stem or stem.startswith("."):
        raise ValueError("Invalid model stem")
    mesh = source / (stem + ".lmesh")
    if not mesh.is_file():
        raise FileNotFoundError("Original source .lmesh missing: " + str(mesh))
    if mesh.stat().st_size < 100:
        raise ValueError("Source model buffer is unexpectedly small")
    if not CONVERTER.is_file():
        raise FileNotFoundError("Repository converter missing")

    with tempfile.TemporaryDirectory(prefix="hextech-import-") as workspace:
        staged = Path(workspace)
        subprocess.run(
            [sys.executable, str(CONVERTER), str(source), str(staged),
             "--stem", stem, "--hero", hero],
            check=True, timeout=120,
        )
        manifest_path = staged / (hero.lower() + ".json")
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        if manifest.get("hero") != hero or manifest.get("version") != 1:
            raise ValueError("Converter emitted incorrect champion identity")
        texture = manifest.get("texture", "")
        binary = manifest.get("binary", "")
        if not texture or Path(texture).name != texture or not texture.endswith(".png"):
            raise ValueError("Unsafe or unsupported texture reference")
        if binary != hero.lower() + ".bin":
            raise ValueError("Incorrect champion buffer name")
        clips = [item.get("name") for item in manifest.get("clips", [])]
        if not any(x in clips for x in ("idle", "idle1")) or "run" not in clips or "attack1" not in clips:
            raise ValueError("Original idle, run and attack1 clips are required")
        source_provenance = manifest.get("provenance", {})
        if source_provenance.get("meshSHA256") != sha256(mesh):
            raise ValueError("Source mesh SHA256 mismatch after conversion")

        # A clip can only be bound when its original track is actually in
        # the converted source. No guessed spell1=Q mapping is permitted.
        animation_binding = None
        if bindings is not None:
            animation_binding = json.loads(bindings.read_text(encoding="utf-8"))
            if (animation_binding.get("version") != 1 or
                animation_binding.get("sourceVerified") is not True or
                animation_binding.get("hero") != hero):
                raise ValueError("Skill animation bindings need hero-specific verified provenance")
            keys = ("q1", "q2", "q3", "w", "e")
            if not any(animation_binding.get(key) for key in keys):
                raise ValueError("Skill animation bindings must specify an actual source clip")
            for key in keys:
                value = animation_binding.get(key) or ""
                if value and (not isinstance(value, str) or value not in clips):
                    raise ValueError(f"Skill animation binding {key} is not an original imported clip: {value}")

        # Verify every file *before* modifying a live Unity input directory.
        files = [hero.lower() + ".json", binary, texture]
        for name in files:
            p = staged / name
            if not p.is_file() or not p.stat().st_size:
                raise ValueError("Converter output missing: " + name)

        destination.mkdir(parents=True, exist_ok=True)
        for name in files:
            shutil.copy2(staged / name, destination / name)
            if sha256(staged / name) != sha256(destination / name):
                raise IOError("Copied Unity source failed SHA256 verification: " + name)

        binding_target = destination / "animation-bindings.json"
        if animation_binding is not None:
            binding_target.write_text(
                json.dumps(animation_binding, indent=2, ensure_ascii=False) + "\n",
                encoding="utf-8",
            )
            files.append(binding_target.name)
        elif binding_target.exists():
            # A previous import's verified binding may no longer match the new
            # clips. Never retain stale skill mappings after a model replacement.
            binding_target.unlink()

        report = {
            "hero": hero,
            "sourceMeshSha256": source_provenance["meshSHA256"],
            "sourceAnimationSha256": source_provenance["animationSHA256"],
            "clips": clips,
            "verifiedAbilityBindings": animation_binding is not None,
            "bundleSha256": {name: sha256(destination / name) for name in files},
            "source": str(source),
            "status": "SOURCE_PREPARED_NOT_VISUALLY_VALIDATED",
        }
        (destination / "local-import-provenance.json").write_text(
            json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
        )
        return report


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--hero", choices=HEROES, required=True)
    p.add_argument("--source", type=Path, required=True)
    p.add_argument("--stem", required=True)
    p.add_argument("--output", type=Path)
    p.add_argument("--bindings", type=Path,
                   help="Optional, manually checked animation-bindings.json with original clip names")
    args = p.parse_args()
    target = args.output or (
        NATIVE / "Assets" / "StreamingAssets" / "Champions" / args.hero
    )
    report = import_source(args.hero, args.source, args.stem, target, args.bindings)
    print(json.dumps(report, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
