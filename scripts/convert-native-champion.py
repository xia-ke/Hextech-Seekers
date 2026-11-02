"""Convert the documented LoL Model Viewer mesh/animation format to local buffers.

Format reference: tengge1/lol-model-viewer, MIT, Copyright (c) 2018 Li Teng.
Champion art and animations remain Riot Games assets. No model-viewer runtime is
required by the page. Input files are kept outside the distributable directory.
"""
import argparse
import hashlib
import json
import math
import shutil
import struct
import zlib
from pathlib import Path


class Reader:
    def __init__(self, data):
        self.data, self.offset = data, 0

    def take(self, fmt, count=1):
        code = '<' + str(count) + fmt
        result = struct.unpack_from(code, self.data, self.offset)
        self.offset += struct.calcsize(code)
        return result[0] if count == 1 else list(result)

    def string(self):
        size = self.take('H')
        result = self.data[self.offset:self.offset + size].decode('utf-8')
        self.offset += size
        return result.lower()


def convert(source, destination, stem, hero):
    mesh_bytes = (source / (stem + '.lmesh')).read_bytes()
    r = Reader(mesh_bytes)
    if r.take('I') != 604210091:
        raise ValueError('Not a supported native mesh')
    version = r.take('I')
    animation_file, texture_file = r.string(), r.string()
    groups = []
    for _ in range(r.take('I')):
        groups.append(dict(name=r.string(), vertexStart=r.take('I'), vertexCount=r.take('I'), start=r.take('I'), count=r.take('I')))
    positions, normals, uv, joints, weights = [], [], [], [], []
    for _ in range(r.take('I')):
        positions.extend(r.take('f', 3))
        normals.extend(r.take('f', 3))
        uv.extend(r.take('f', 2))
        joints.extend(r.take('B', 4))
        weights.extend(r.take('f', 4))
    indices = [r.take('H') for _ in range(r.take('I'))]
    bones, used_names = [], set()
    for _ in range(r.take('I')):
        name, parent, scale = r.string(), r.take('i'), r.take('f')
        if name in used_names:
            name += '2'
        used_names.add(name)
        bones.append(dict(name=name, parent=parent, scale=scale, bind=r.take('f', 16), local=r.take('f', 16) if version >= 2 else None))
    if r.offset != len(mesh_bytes):
        raise ValueError(f'Unexpected mesh bytes: {len(mesh_bytes) - r.offset}')
    if any(j >= len(bones) for j in joints):
        raise ValueError('Vertex references missing bone')
    if not all(math.isfinite(v) for v in positions + weights):
        raise ValueError('Invalid vertex data')

    binary = bytearray()
    def buffer(values, kind='f'):
        while len(binary) % 4:
            binary.append(0)
        record = dict(offset=len(binary), count=len(values), type=kind)
        binary.extend(struct.pack('<' + str(len(values)) + kind, *values))
        return record

    geometry = dict(position=buffer(positions), normal=buffer(normals), uv=buffer(uv), skinIndex=buffer(joints, 'H'), skinWeight=buffer(weights), index=buffer(indices, 'H'))
    animation_bytes = (source / (animation_file + '.lanim')).read_bytes()
    ar = Reader(animation_bytes)
    if ar.take('I') != 604210092:
        raise ValueError('Not a supported native animation')
    animation_version = ar.take('I')
    ar = Reader(zlib.decompress(animation_bytes[8:])) if animation_version >= 2 else ar
    clips, available = [], []
    lookup = {b['name']: i for i, b in enumerate(bones)}
    for _ in range(ar.take('I')):
        name, fps, track_count = ar.string(), ar.take('i'), ar.take('I')
        available.append(name)
        selected = name in ('idle', 'idle1', 'idle2', 'run', 'run1', 'walk', 'attack1', 'attack2') or name.startswith('spell')
        tracks, duration = [], 0
        for _ in range(track_count):
            frame_count, bone_name, flags = ar.take('I'), ar.string(), ar.take('I')
            ps, qs, ss = [], [], []
            for _ in range(frame_count):
                ps.extend(ar.take('f', 3))
                qs.extend(ar.take('f', 4))
                ss.extend(ar.take('f', 3) if animation_version >= 3 else (1, 1, 1))
            if selected and bone_name in lookup and frame_count:
                tracks.append(dict(bone=lookup[bone_name], times=buffer([i / fps for i in range(frame_count)]), position=buffer(ps), quaternion=buffer(qs), scale=buffer(ss)))
                duration = max(duration, frame_count / fps)
        if selected and tracks:
            clips.append(dict(name=name, duration=duration, fps=fps, tracks=tracks))
    if not any(c['name'].startswith('idle') for c in clips) or not any(c['name'].startswith('run') for c in clips):
        raise ValueError(f'Missing required animation, available={available}')
    manifest = dict(version=1, hero=hero, texture=texture_file + '.png', binary=hero.lower() + '.bin', geometry=geometry, groups=groups, bones=bones, clips=clips,
                    provenance=dict(repository='https://github.com/tengge1/lol-model-viewer', commit='ef5635c5fbe0c315275c9bcccf83558cfe7ee436',
                                    meshSHA256=hashlib.sha256(mesh_bytes).hexdigest(), animationSHA256=hashlib.sha256(animation_bytes).hexdigest()))
    destination.mkdir(parents=True, exist_ok=True)
    (destination / (hero.lower() + '.json')).write_text(json.dumps(manifest, separators=(',', ':')))
    (destination / (hero.lower() + '.bin')).write_bytes(binary)
    shutil.copyfile(source / (texture_file + '.png'), destination / (texture_file + '.png'))
    print(json.dumps(dict(hero=hero, vertices=len(positions)//3, triangles=len(indices)//3, bones=len(bones), clips=[c['name'] for c in clips], available=available, binaryBytes=len(binary))))


if __name__ == '__main__':
    ap = argparse.ArgumentParser()
    ap.add_argument('source', type=Path)
    ap.add_argument('destination', type=Path)
    ap.add_argument('--stem', default='86_0')
    ap.add_argument('--hero', default='Garen')
    args = ap.parse_args()
    convert(args.source, args.destination, args.stem, args.hero)
