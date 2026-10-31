#!/usr/bin/env python3
"""Static Unity Phase1 contract checks, no Unity license required."""
import json
import re
import unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
REPO=ROOT.parent

class Phase1Tests(unittest.TestCase):
    def test_existing_web_game_untouched(self):
        self.assertTrue((REPO/'dist/index.html').is_file())
        self.assertTrue((REPO/'dist/rebuild/match.js').is_file())
    def test_unity_version_and_packages(self):
        v=(ROOT/'ProjectSettings/ProjectVersion.txt').read_text()
        self.assertRegex(v,r'6000\.0\.\d+f1')
        deps=json.loads((ROOT/'Packages/manifest.json').read_text())['dependencies']
        self.assertIn('com.unity.render-pipelines.universal',deps)
        self.assertIn('com.unity.cloud.gltfast',deps)
    def test_navigation_contract_matches_existing_binary(self):
        raw=(REPO/'dist/assets/visual/summoners-rift/navigation.bin').read_bytes()
        self.assertEqual(len(raw),295*296*6)
        src=(ROOT/'Assets/Scripts/RiftGrid.cs').read_text()
        for value in ['const int Width = 295','const int Height = 296',
                      'const float CellSize = 50f','const int BushOffset = Width * Height * 5',
                      'p.x + p.y >= -120f']:
            self.assertIn(value,src)
        self.assertTrue(any(raw[:295*296]))
    def test_original_source_sha_is_consistent(self):
        fetch=(REPO/'scripts/fetch-visual-sources.py').read_text()
        prep=(ROOT/'tools/prepare_assets.py').read_text()
        sha='bdabbf3eb0c37dd2bb8567035c018d0da0f79c849c9353e5192a58279369a69e'
        self.assertIn(sha,fetch)
        self.assertIn(sha,prep)
        self.assertIn('media.githubusercontent.com/media/iamywl/Web_game',prep)
    def test_no_fake_hero_assets_and_player_build_guard(self):
        boot=(ROOT/'Assets/Scripts/RiftBootstrap.cs').read_text()
        self.assertIn('NAVIGATION_DEBUG_MARKER_NOT_A_CHAMPION',boot)
        self.assertIn('Failed to instantiate original 3D map.glb',boot)
        editor=(ROOT/'Assets/Editor/HextechNativeSetup.cs').read_text()
        self.assertIn('BuildTarget.StandaloneWindows64',editor)
        self.assertIn('File.Exists(rift)',editor)

if __name__=='__main__':unittest.main()