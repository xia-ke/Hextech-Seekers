#!/usr/bin/env python3
"""Prepare source-pinned original map GLB and existing binary navigation for Unity.
Download is atomic, SHA-256 verified, and never executes remote code.
Does not redistribute Riot assets in the Git repository.
"""
import argparse
import hashlib
import json
import shutil
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
REPO = ROOT.parent
TARGET = ROOT / 'Assets/StreamingAssets/Rift'
MAP_SHA = 'bdabbf3eb0c37dd2bb8567035c018d0da0f79c849c9353e5192a58279369a69e'
MAP_COMMIT = '557a9d5352af12bee8bf3db879c22d04d0a0954e'
MAP_URL = f'https://media.githubusercontent.com/media/iamywl/Web_game/{MAP_COMMIT}/public/assets/maps/summoners-rift/map.glb'
NAV_COUNT = 295 * 296 * 6


def sha(path):
    h = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024*1024), b''):
            h.update(block)
    return h.hexdigest()


def prepare(repo=REPO, target=TARGET, skip_download=False):
    target.mkdir(parents=True, exist_ok=True)
    nav_src = repo / 'dist/assets/visual/summoners-rift/navigation.bin'
    if not nav_src.is_file() or nav_src.stat().st_size != NAV_COUNT:
        raise RuntimeError('Original navigation.bin not found or size mismatch in preserved web project')
    nav_dest = target / 'navigation.bin'
    if not nav_dest.exists() or sha(nav_src) != sha(nav_dest):
        shutil.copyfile(nav_src, nav_dest)
    model = target / 'map.glb'
    if not model.exists() or sha(model) != MAP_SHA:
        if skip_download:
            raise RuntimeError('Pinned map.glb is absent: download is disabled')
        tmp = model.with_suffix('.glb.part')
        try:
            with urllib.request.urlopen(MAP_URL, timeout=90) as response, tmp.open('wb') as out:
                while block := response.read(1024 * 1024):
                    out.write(block)
            if sha(tmp) != MAP_SHA:
                raise RuntimeError('Pinned original GLB SHA-256 mismatch')
            tmp.replace(model)
        finally:
            tmp.unlink(missing_ok=True)
    # Keep the original converted 3D hero source buffers as binary, never sprites.
    hero_source = repo / 'dist/assets/native'
    hero_target = target.parent / 'Champions/Garen'
    hero_target.mkdir(parents=True,exist_ok=True)
    for name in ('garen.json','garen.bin','garen_base_tx_cm.png'):
        source_file = hero_source / name
        if not source_file.is_file():
            raise RuntimeError('Missing original skinned Garen asset: ' + str(source_file))
        output = hero_target / name
        if not output.exists() or sha(source_file)!=sha(output):
            shutil.copyfile(source_file,output)
    hero_meta = json.loads((hero_target / 'garen.json').read_text())
    if hero_meta.get('hero')!='Garen' or not any(c.get('name')=='attack1' for c in hero_meta.get('clips',[])):
        raise RuntimeError('Original Garen rig is missing the attack1 animation')
    manifest = {'mapSource': MAP_URL, 'mapCommit': MAP_COMMIT, 'mapSha256': MAP_SHA,
                'navigationBytes': NAV_COUNT, 'navigationSha256': sha(nav_dest),
                'heroStage': 'ORIGINAL_GAREN_SKINNED_SOURCE_PREPARED', 'format': 'original glTF2 binary'}
    (target / 'asset-provenance.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2)+'\n')
    print('PASS: original Rift GLB, navigation and Garen skinned native buffers verified:', target)

if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--repo', type=Path, default=REPO)
    parser.add_argument('--target', type=Path, default=TARGET)
    parser.add_argument('--skip-download', action='store_true')
    args = parser.parse_args()
    prepare(args.repo, args.target, args.skip_download)