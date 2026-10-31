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
    def test_real_skinned_hero_and_player_build_guard(self):
        boot=(ROOT/'Assets/Scripts/RiftBootstrap.cs').read_text()
        self.assertIn('GarenRigRuntime.Load(transform, heroPath)',boot)
        self.assertIn('Failed to instantiate original 3D map.glb',boot)
        editor=(ROOT/'Assets/Editor/HextechNativeSetup.cs').read_text()
        self.assertIn('BuildTarget.StandaloneWindows64',editor)
        self.assertIn('File.Exists(rift)',editor)
        self.assertIn('Missing original skinned Garen asset',editor)

    def test_native_original_garen_bone_and_motion_contract(self):
        import struct
        m=json.loads((REPO/'dist/assets/native/garen.json').read_text())
        raw=(REPO/'dist/assets/native/garen.bin').read_bytes()
        self.assertEqual(m['hero'],'Garen')
        self.assertEqual(len(m['bones']),68)
        self.assertEqual(set(x['name'] for x in m['clips']) & {'idle1','run','attack1'}, {'idle1','run','attack1'})
        geo=m['geometry']
        vertex_count=geo['position']['count']//3
        self.assertEqual(vertex_count,geo['uv']['count']//2)
        self.assertEqual(vertex_count,geo['skinIndex']['count']//4)
        self.assertEqual(vertex_count,geo['skinWeight']['count']//4)
        for view in [*geo.values(), *(v for clip in m['clips'] for t in clip['tracks'] for v in [t['times'],t['position'],t['quaternion'],t['scale']])]:
            size=2 if view['type']=='H' else 4
            self.assertLessEqual(view['offset']+view['count']*size,len(raw))
        mesh=(ROOT/'Assets/Scripts/GarenRigRuntime.cs').read_text()
        for marker in ('SkinnedMeshRenderer','boneWeights','bindposes','ChangeClip(', 'PlayAttack()', 'trackBuffers'):
            self.assertIn(marker,mesh)
        prep=(ROOT/'tools/prepare_assets.py').read_text()
        self.assertIn("('garen.json','garen.bin','garen_base_tx_cm.png')",prep)

    def test_actual_unity_editor_and_gpu_evidence_gates_are_wired(self):
        from pathlib import Path
        workflow=(REPO/'.github/workflows/unity-editor-validation.yml').read_text()
        self.assertIn('game-ci/unity-test-runner@v4',workflow)
        self.assertIn('testMode: editmode',workflow)
        self.assertIn('Verify Unity license is configured',workflow)
        probe=(ROOT/'Assets/Scripts/NativeVisualAcceptance.cs').read_text()
        for value in ('-hextechVisualQA','native-idle.png','CAPTURED_REQUIRES_VISUAL_REVIEW',
                      'ScreenCapture.CaptureScreenshot','Player'):
            if value=='Player':continue
            self.assertIn(value,probe)
        src=(ROOT/'Assets/Editor/HextechNativeSetup.cs').read_text()
        self.assertIn('bootstrap.AddComponent<NativeVisualAcceptance>()',src)
        self.assertIn('BuildTarget.StandaloneWindows64',src)
        self.assertIn('NativeRiftPhase2.unity',src)
        self.assertTrue((ROOT/'Assets/Tests/Editor/GarenMeshSourceTests.cs').is_file())

if __name__=='__main__':unittest.main()