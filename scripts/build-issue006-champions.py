#!/usr/bin/env python3
"""Reproducibly build three original champion sprites, refusing missing animation assets."""
import argparse
import json
import subprocess
import sys
from PIL import Image
from pathlib import Path

CHAMPIONS={'blitzcrank':('53_0','Blitzcrank'),'riven':('92_0','Riven'),'yasuo':('157_0','Yasuo')}
ROOT=Path(__file__).resolve().parent.parent

def run(command):
    print('+',' '.join(str(c) for c in command),flush=True)
    subprocess.run([str(c) for c in command],check=True)

def main():
    p=argparse.ArgumentParser()
    p.add_argument('--source',type=Path,required=True,help='Cache for pinned original resources')
    p.add_argument('--output',type=Path,default=ROOT/'dist/assets/visual')
    p.add_argument('--native-output',type=Path,default=ROOT/'dist/assets/native')
    p.add_argument('--skip-fetch',action='store_true')
    args=p.parse_args()
    if not args.skip_fetch:
        run([sys.executable,ROOT/'scripts/fetch-visual-sources.py',args.source,'--issue006'])
    source_manifest=args.source/'champions/source-manifest.json'
    if not source_manifest.exists():raise SystemExit('Missing source-manifest.json; fetch pinned originals first')
    sources=json.loads(source_manifest.read_text())
    for hero,(stem,label) in CHAMPIONS.items():
        if hero not in sources:raise SystemExit('Missing source record: '+hero)
        run([sys.executable,ROOT/'scripts/convert-native-champion.py',
             args.source/'champions'/hero,args.native_output,'--stem',stem,'--hero',hero])
        run([sys.executable,ROOT/'scripts/render-visual-assets.py',
             args.source/'rift',args.output,'--champion-source',args.native_output,
             '--hero',hero,'--only','champion'])
        meta=json.loads((args.output/hero/'sprite.json').read_text())
        if set(meta['clips'])!={'idle1','run','attack1'}:raise RuntimeError('Missing actual clips for '+hero)
        for clip in meta['clips'].values():
            for key in ['image','depth']:
                if not (args.output/hero/clip[key]).is_file():raise RuntimeError('Missing raster asset: '+hero+'/'+clip[key])
        print('VERIFIED',label,meta['provenance'],flush=True)

    # Normalize visible character height from the first idle frame, rather
    # than assuming independently exported meshes share the same world scale.
    reference_path=args.output/'garen'/'idle1.webp'
    if not reference_path.is_file():raise SystemExit('Missing Garen reference idle atlas')
    with Image.open(reference_path) as image:
        reference_box=image.crop((0,0,256,256)).getchannel('A').getbbox()
    if not reference_box:raise SystemExit('Empty Garen reference idle frame')
    reference_height=reference_box[3]-reference_box[1]
    for hero in ['garen',*CHAMPIONS]:
        folder=args.output/hero
        manifest_path=folder/'sprite.json'
        if not manifest_path.is_file():raise SystemExit('Missing sprite manifest: '+hero)
        with Image.open(folder/'idle1.webp') as image:
            bounds=image.crop((0,0,256,256)).getchannel('A').getbbox()
        if not bounds:raise SystemExit('Empty idle frame: '+hero)
        height=bounds[3]-bounds[1]
        target=reference_height*(1.15 if hero=='blitzcrank' else 1.0)
        meta=json.loads(manifest_path.read_text())
        meta['visualScale']=round(target/height,4)
        meta['scaleReference']={'hero':'garen','referenceHeightPx':reference_height,
                                'sourceHeightPx':height,'targetHeightPx':round(target,2)}
        manifest_path.write_text(json.dumps(meta,separators=(',',':')))
        print('NORMALIZED',hero,'visualScale=',meta['visualScale'],
              'height=',height,'target=',round(target,2),flush=True)

if __name__=='__main__':main()
