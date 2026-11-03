"""Isolated tests for opt-in original champion asset staging (no Riot files)."""
import hashlib
import json
import tempfile
import unittest
from pathlib import Path

from stage_native_champions import install_one, stage_available, validate_bundle


class ChampionStagingTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix="hextech-source-fixture-")
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.source = self.root / "external"
        self.target = self.root / "unity" / "Champions"
        self.source.mkdir()

    @staticmethod
    def sha(data):
        return hashlib.sha256(data).hexdigest()

    def bundle(self, hero="Blitzcrank", bindings=False):
        folder = self.source / hero
        folder.mkdir()
        stem = hero.lower()
        fake_binary = b"source-geometry-and-animations-not-real-art"
        fake_png = b"\x89PNG\r\n\x1a\n" + b"fixture only"
        manifest = {
            "version": 1, "hero": hero,
            "texture": "source_texture.png", "binary": stem + ".bin",
            "geometry": {"position": {"offset": 0, "count": 3, "type": "f"}},
            "bones": [{"name": "root", "parent": -1}],
            "clips": [{"name": name} for name in ("idle1", "run", "attack1", "spell1")],
            "provenance": {"meshSHA256": self.sha(b"original-mesh"),
                           "animationSHA256": self.sha(b"original-animations")}
        }
        (folder / (stem + ".json")).write_text(json.dumps(manifest), encoding="utf-8")
        (folder / (stem + ".bin")).write_bytes(fake_binary)
        (folder / "source_texture.png").write_bytes(fake_png)
        names = [stem + ".json", stem + ".bin", "source_texture.png"]
        if bindings:
            (folder / "animation-bindings.json").write_text(json.dumps({
                "version": 1, "hero": hero, "sourceVerified": True,
                "q1": "spell1", "q2": "", "q3": "", "w": "", "e": ""
            }), encoding="utf-8")
            names.append("animation-bindings.json")
        proof = {
            "hero": hero,
            "status": "SOURCE_PREPARED_NOT_VISUALLY_VALIDATED",
            "sourceMeshSha256": manifest["provenance"]["meshSHA256"],
            "sourceAnimationSha256": manifest["provenance"]["animationSHA256"],
            "verifiedAbilityBindings": bindings,
            "bundleSha256": {name: self.sha((folder / name).read_bytes()) for name in names}
        }
        (folder / "local-import-provenance.json").write_text(json.dumps(proof), encoding="utf-8")
        return folder

    def test_missing_optional_sources_preserve_existing_garen_baseline(self):
        self.assertEqual(stage_available(self.source, self.target), [])
        self.assertFalse(self.target.exists())

    def test_installs_source_checked_bundle_and_preserves_skill_mapping(self):
        self.bundle("Riven", bindings=True)
        results = stage_available(self.source, self.target, ["Riven"])
        self.assertEqual([result["hero"] for result in results], ["Riven"])
        self.assertTrue(results[0]["verifiedAbilityBindings"])
        self.assertTrue((self.target / "Riven" / "animation-bindings.json").is_file())
        self.assertFalse((self.target / "Garen").exists())
        self.assertEqual(stage_available(self.source, self.target)[0]["hero"], "Riven")

    def test_replaces_stale_assets_without_leaving_old_binding(self):
        folder = self.bundle()
        destination = self.target / "Blitzcrank"
        destination.mkdir(parents=True)
        (destination / "animation-bindings.json").write_text("old mapping", encoding="utf-8")
        (destination / "unrelated.dat").write_bytes(b"stale")
        install_one(folder, self.target, "Blitzcrank")
        self.assertFalse((destination / "animation-bindings.json").exists())
        self.assertFalse((destination / "unrelated.dat").exists())
        self.assertEqual(validate_bundle(destination, "Blitzcrank")["hero"], "Blitzcrank")

    def test_bad_sha_aborts_without_mutating_unity(self):
        folder = self.bundle()
        (folder / "blitzcrank.bin").write_bytes(b"tampered")
        with self.assertRaisesRegex(ValueError, "SHA256 mismatch"):
            stage_available(self.source, self.target)
        self.assertFalse((self.target / "Blitzcrank").exists())

    def test_missing_required_original_model_aborts(self):
        with self.assertRaises(FileNotFoundError):
            stage_available(self.source, self.target, ["Yasuo"])

    def test_preflight_checks_all_heroes_before_copying_any(self):
        self.bundle("Blitzcrank")
        folder = self.bundle("Yasuo")
        (folder / "yasuo.json").write_text("{}", encoding="utf-8")
        with self.assertRaises(ValueError):
            stage_available(self.source, self.target)
        self.assertFalse((self.target / "Blitzcrank").exists())

    def test_rejects_unverified_or_mismatched_original_skill_map(self):
        folder = self.bundle("Riven", bindings=True)
        binding = folder / "animation-bindings.json"
        data = json.loads(binding.read_text())
        data["sourceVerified"] = False
        binding.write_text(json.dumps(data))
        proof = folder / "local-import-provenance.json"
        receipt = json.loads(proof.read_text())
        receipt["bundleSha256"]["animation-bindings.json"] = self.sha(binding.read_bytes())
        proof.write_text(json.dumps(receipt))
        with self.assertRaisesRegex(ValueError, "Skill binding"):
            validate_bundle(folder, "Riven")

    def test_rejects_unsafe_texture_reference_even_if_hash_updated(self):
        folder = self.bundle()
        manifest = folder / "blitzcrank.json"
        data = json.loads(manifest.read_text())
        data["texture"] = "../leaked.png"
        manifest.write_text(json.dumps(data))
        with self.assertRaisesRegex(ValueError, "Unsafe file name"):
            validate_bundle(folder, "Blitzcrank")


if __name__ == "__main__":
    unittest.main()
