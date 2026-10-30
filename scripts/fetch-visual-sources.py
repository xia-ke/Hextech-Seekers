"""Fetch pinned art/data inputs only; never execute third-party source code."""
import argparse
import hashlib
import urllib.request
from pathlib import Path

MAP_COMMIT='557a9d5352af12bee8bf3db879c22d04d0a0954e'
HERO_COMMIT='ef5635c5fbe0c315275c9bcccf83558cfe7ee436'
MAP_RAW=f'https://raw.githubusercontent.com/iamywl/Web_game/{MAP_COMMIT}/public/assets/maps/summoners-rift/'
HERO_RAW=f'https://raw.githubusercontent.com/tengge1/lol-model-viewer/{HERO_COMMIT}/LOLModelViewer/LOLModelViewer/resource/'
SOURCES=[
    ('rift/map.glb',f'https://media.githubusercontent.com/media/iamywl/Web_game/{MAP_COMMIT}/public/assets/maps/summoners-rift/map.glb','bdabbf3eb0c37dd2bb8567035c018d0da0f79c849c9353e5192a58279369a69e'),
    ('rift/navigation.json',MAP_RAW+'navigation.json','efd85a3e66515ac339f83f1fe35acd840e2ec9d925ca8535aedec90d71616bdf'),
    ('champion/86_0.lmesh',HERO_RAW+'models/86_0.lmesh','b35b4cacd57a80752576eabbc023c9b7c2a99b9b99268af807c36ae00adb70d7'),
    ('champion/86_0.lanim',HERO_RAW+'models/86_0.lanim','33be703aae5c40bd7b0a1c1ca8c015ac3bbe5709abb29ad7324723447cbcc815'),
    ('champion/garen_base_tx_cm.png',HERO_RAW+'textures/86/garen_base_tx_cm.png','d365b4f677cb5e72f2fe2f7cf1667f42dfe4a6aeb2871d10825852afd932a1f1'),
]

# Issue 006: source files verified to exist at the same pinned commit.
# Download only requested original meshes/animations/textures; no remote code.
# Hashes for new source files are recorded in the output source manifest after download.
ISSUE006_CHAMPIONS = {
    'blitzcrank': {'id': 53, 'stem': '53_0', 'texture': 'steamgolem'},
    'riven': {'id': 92, 'stem': '92_0', 'texture': 'riven_base_tx_cm'},
    'yasuo': {'id': 157, 'stem': '157_0', 'texture': 'yasuo_base_tx_cm'},
}

def fetch_issue006(root, champions):
    import json
    entries = {}
    for hero in champions:
        if hero not in ISSUE006_CHAMPIONS:
            raise ValueError('Unknown champion: '+hero)
        item = ISSUE006_CHAMPIONS[hero]
        folder = root/'champions'/hero
        folder.mkdir(parents=True, exist_ok=True)
        resources = {
            item['stem']+'.lmesh': HERO_RAW+'models/'+item['stem']+'.lmesh',
            item['stem']+'.lanim': HERO_RAW+'models/'+item['stem']+'.lanim',
            item['texture']+'.png': HERO_RAW+'textures/'+str(item['id'])+'/'+item['texture']+'.png',
        }
        hashes={}
        for name,url in resources.items():
            target=folder/name
            if not target.exists():
                temp=target.with_suffix(target.suffix+'.part')
                try:
                    with urllib.request.urlopen(url,timeout=90) as response, temp.open('wb') as out:
                        while chunk:=response.read(1024*1024):
                            out.write(chunk)
                    temp.replace(target)
                finally:
                    temp.unlink(missing_ok=True)
            hashes[name]=hashlib.sha256(target.read_bytes()).hexdigest()
            print('Verified source',hero,name,hashes[name],flush=True)
        entries[hero]={'commit':HERO_COMMIT,'files':hashes,'stem':item['stem']}
    output=root/'champions'/'source-manifest.json'
    output.write_text(json.dumps(entries,indent=2,ensure_ascii=False))
    return entries


def fetch(root):
    for name,url,expected in SOURCES:
        target=root/name
        if target.exists() and hashlib.sha256(target.read_bytes()).hexdigest()==expected:
            print('Verified',name);continue
        target.parent.mkdir(parents=True,exist_ok=True)
        temporary=target.with_suffix(target.suffix+'.part');digest=hashlib.sha256()
        try:
            with urllib.request.urlopen(url,timeout=60) as response,temporary.open('wb') as out:
                while block:=response.read(1024*1024):out.write(block);digest.update(block)
            if digest.hexdigest()!=expected:raise ValueError('Source checksum mismatch: '+name)
            temporary.replace(target);print('Downloaded and verified',name)
        finally:temporary.unlink(missing_ok=True)

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('destination',type=Path)
    parser.add_argument('--issue006',action='store_true',help='Fetch Blitzcrank, Riven and Yasuo original files')
    args=parser.parse_args()
    if args.issue006:fetch_issue006(args.destination,tuple(ISSUE006_CHAMPIONS))
    else:fetch(args.destination)
