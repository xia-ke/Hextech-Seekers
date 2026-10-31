#!/usr/bin/env python3
"""Chromium visual proof for Issue 006. Must run only after real atlas build."""
import json
import base64
import io
import subprocess
import time
from pathlib import Path
from PIL import Image
from playwright.sync_api import sync_playwright

ROOT=Path(__file__).resolve().parent.parent
OUT=ROOT/'artifacts/issue006'
OUT.mkdir(parents=True,exist_ok=True)
heroes=['garen','blitzcrank','riven','yasuo']
manifest={}
for hero in heroes:
    path=ROOT/'dist/assets/visual'/hero
    meta=json.loads((path/'sprite.json').read_text())
    frames={}
    for clip,info in meta['clips'].items():
        color=path/info['image']
        depth=path/info['depth']
        with Image.open(color) as image:
            image.load()
            if image.size!=(meta['tileSize']*meta['directions'],meta['tileSize']*info['frames']):
                raise AssertionError(f'{hero}/{clip} atlas dimensions incorrect')
            # Non-empty pixels in the first actual animation tile.
            frame=image.crop((0,0,meta['tileSize'],meta['tileSize'])).convert('RGBA')
            occupied=frame.getchannel('A').getbbox()
            if occupied is None:raise AssertionError(f'{hero}/{clip} empty original sprite')
            frames[clip]={'size':image.size,'first_frame_bbox':occupied}
        with Image.open(depth) as dep:
            dep.load()
            if dep.size!=image.size:raise AssertionError(f'{hero}/{clip} depth mismatch')
    if set(['idle1','run','attack1'])-set(meta['clips']):
        raise AssertionError(hero+' missing clips')
    manifest[hero]=frames
(OUT/'atlas-check.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2))

server=subprocess.Popen(['node','scripts/serve.mjs'],cwd=ROOT,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True)
try:
    with sync_playwright() as p:
        browser=p.chromium.launch(headless=True,args=['--disable-dev-shm-usage','--use-gl=angle','--use-angle=swiftshader'])
        page=browser.new_page(viewport={'width':1600,'height':900},device_scale_factor=1)
        errors=[]
        page.on('pageerror',lambda error:errors.append(str(error)))
        page.goto('http://127.0.0.1:8000/?visualQA=1',wait_until='domcontentloaded',timeout=60000)
        page.wait_for_function('window.__hextechLive && window.__hextechNavigation',timeout=120000)
        page.wait_for_function("document.querySelector('#loading').hidden",timeout=120000)
        page.locator('#round-toggle').click()
        page.locator('#round-count').select_option('4')
        page.locator('#round-role').select_option('hunter')
        page.locator('#round-hero').select_option('盖伦')
        page.locator('#round-start').click()
        page.locator('#round-live-start').click()
        page.wait_for_function("Object.keys(window.__hextechLive.assets()).length === 4",timeout=120000)
        page.wait_for_timeout(300)
        assets=page.evaluate('window.__hextechLive.assets()')
        if len(assets)!=4 or not all(assets.values()):
            raise AssertionError('Original atlases did not load: '+str(assets))
        if errors:raise AssertionError('Browser page errors: '+str(errors))
        # Exercise every original champion as the actual controllable player,
        # not merely as a loaded NPC atlas.
        for player_hero in ['盖伦','布里茨','锐雯','亚索']:
            status=page.evaluate("""hero=>{
              window.__hextechLive.stop();
              window.__hextechLive.start({
                totalPlayers:4,playerRole:'hunter',playerHero:hero
              });
              return {
                selected:window.__hextechLive.selectedHero(),
                player:window.__hextechLive.snapshot().players[0].hero
              };
            }""",player_hero)
            if status != {'selected':player_hero,'player':player_hero}:
                raise AssertionError('Playable hero mismatch: '+str(status))
            assert page.locator('.champion strong').inner_text()==player_hero
            assert page.locator('.portrait img').get_attribute('alt')==player_hero
            page.locator('#attack').click()
            page.wait_for_timeout(120)
        page.evaluate("""()=>{
          window.__hextechLive.stop();
          window.__hextechLive.start({
            totalPlayers:4,playerRole:'hunter',playerHero:'盖伦'
          });
        }""")
        if errors:raise AssertionError('Browser errors after four playable heroes: '+str(errors))
        staged=page.evaluate('window.__hextechLive.stageForVisualQA()')
        if len(staged)!=3:raise AssertionError('Could not stage 3 visible real enemy sprites: '+str(staged))
        page.wait_for_timeout(250)
        page.screenshot(path=str(OUT/'chromium-game.png'),full_page=True)
        # Small visual evidence that remains readable from GitHub API clients.
        with Image.open(OUT/'chromium-game.png') as screenshot:
            thumbnail=screenshot.convert('RGB')
            thumbnail.thumbnail((1100,620))
            thumbnail.save(OUT/'chromium-game-preview.jpg',quality=72,optimize=True)
        snapshot=page.evaluate('window.__hextechLive.snapshot()')
        perf=page.evaluate('window.__hextechPerf()')
        assert snapshot['phase'] in ('prepare','chase'),snapshot
        assert len(snapshot['visible'])==3,('Visible original sprites missing',snapshot['visible'])
        assert perf['samples']>0,perf
        (OUT/'browser-check.json').write_text(json.dumps(
            {'asset_status':assets,'snapshot':snapshot,'performance':perf,
             'page_errors':errors,'viewport':{'width':1600,'height':900}},
            ensure_ascii=False,indent=2))
        # Separate atlas inspection sheet is a screenshot rendered by real Chromium.
        cards=[]
        for hero in heroes:
            meta=json.loads((ROOT/'dist/assets/visual'/hero/'sprite.json').read_text())
            name=meta.get('hero',hero)
            # Generate a first-frame inspection image from each original sprite
            # rather than depending on optional 'inspection.png' in the repo.
            info=meta['clips']['idle1']
            with Image.open(ROOT/'dist/assets/visual'/hero/info['image']) as atlas:
                first=atlas.crop((0,0,meta['tileSize'],meta['tileSize']))
                buf=io.BytesIO()
                first.save(buf,format='PNG')
            encoded=base64.b64encode(buf.getvalue()).decode('ascii')
            cards.append(f'<div class="card"><h2>{name}</h2><img src="data:image/png;base64,{encoded}"><p>Original 3D model · rasterized EGL sprite</p></div>')
        gallery=browser.new_page(viewport={'width':1400,'height':520})
        gallery.set_content('<html><head><style>body{margin:0;background:#09151c;color:#e9d7a1;font:17px sans-serif;display:flex;gap:12px;padding:18px}.card{padding:15px;border:1px solid #86724c;width:300px;text-align:center;background:#102631}.card img{width:265px;height:265px;object-fit:contain}.card p{font-size:12px;color:#a7c6c1}</style></head><body>'+''.join(cards)+'</body></html>',wait_until='load')
        gallery.wait_for_timeout(2000)
        gallery.screenshot(path=str(OUT/'champion-gallery.png'))
        gallery.close()
        browser.close()
finally:
    server.terminate()
    try:server.wait(timeout=5)
    except subprocess.TimeoutExpired:server.kill()
print('PASS Chromium: rendered four original hero sprites, game viewport, no page exceptions; outputs:',OUT)
