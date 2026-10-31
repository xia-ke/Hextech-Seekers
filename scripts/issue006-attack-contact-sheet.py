#!/usr/bin/env python3
"""Build a frame-indexed contact sheet for per-hero attack timing review."""
import json
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT=Path(__file__).resolve().parent.parent
HEROES=['garen','blitzcrank','riven','yasuo']
DIRECTIONS=[0,4,8,12]
FRAMES=12
CELL=112
HEADER=42
GAP=8
OUT=ROOT/'artifacts/issue006/attack-contact-sheet.png'
OUT.parent.mkdir(parents=True,exist_ok=True)
font=ImageFont.load_default()
panels=[]
for hero in HEROES:
    folder=ROOT/'dist/assets/visual'/hero
    meta=json.loads((folder/'sprite.json').read_text())
    clip=meta['clips'].get('attack1')
    if not clip or clip['frames']!=FRAMES:
        raise SystemExit(f'{hero}: expected {FRAMES} attack1 frames')
    atlas=Image.open(folder/clip['image']).convert('RGBA')
    if atlas.size!=(meta['tileSize']*meta['directions'],meta['tileSize']*FRAMES):
        raise SystemExit(f'{hero}: attack1 atlas dimensions mismatch')
    panel=Image.new('RGBA',(FRAMES*CELL,HEADER+len(DIRECTIONS)*(CELL+GAP)),(28,31,38,255))
    draw=ImageDraw.Draw(panel)
    draw.text((8,8),f"{hero} | clip={clip['duration']:.3f}s | frames={FRAMES}",font=font,fill='white')
    for row,direction in enumerate(DIRECTIONS):
        y=HEADER+row*(CELL+GAP)
        draw.text((4,y+4),f"D{direction}",font=font,fill=(255,220,120,255))
        for frame in range(FRAMES):
            x=frame*CELL
            crop=atlas.crop((direction*meta['tileSize'],frame*meta['tileSize'],
                             (direction+1)*meta['tileSize'],(frame+1)*meta['tileSize']))
            # Fit the complete original frame without changing its proportions.
            crop.thumbnail((CELL-8,CELL-22),Image.Resampling.LANCZOS)
            tile=Image.new('RGBA',(CELL-8,CELL-22),(44,48,58,255))
            tile.alpha_composite(crop,((tile.width-crop.width)//2,(tile.height-crop.height)//2))
            panel.alpha_composite(tile,(x+4,y+18))
            draw.text((x+5,y+4),str(frame+1),font=font,fill=(190,205,220,255))
    panels.append(panel)
width=2*FRAMES*CELL+GAP
height=2*panels[0].height+GAP
sheet=Image.new('RGBA',(width,height),(17,19,24,255))
for i,panel in enumerate(panels):
    x=(i%2)*(FRAMES*CELL+GAP)
    y=(i//2)*(panel.height+GAP)
    sheet.alpha_composite(panel,(x,y))
sheet.convert('RGB').save(OUT,optimize=True)
print('CONTACT_SHEET',OUT,sheet.size)
