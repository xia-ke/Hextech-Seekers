#!/usr/bin/env python3
"""Convert embedded EXT_texture_webp glTF textures to PNG in a local derived GLB.

The source must be SHA-pinned. Geometry, materials, indices and animations
are preserved; WebP images are decoded to PNG without additional quality loss.
Do not distribute outputs containing third-party copyrighted artwork.
"""
import argparse
import base64
import hashlib
import io
import json
import struct
from pathlib import Path

GLB_MAGIC = b'glTF'
JSON_CHUNK = 0x4E4F534A
BIN_CHUNK = 0x004E4942
WEBP_EXTENSION = 'EXT_texture_webp'


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def parse_glb(data):
    if len(data) < 20 or data[:4] != GLB_MAGIC:
        raise ValueError('Not a GLB 2.0 file')
    magic, version, length = struct.unpack_from('<4sII', data)
    if version != 2 or length != len(data):
        raise ValueError('GLB header version/length mismatch')
    cursor, chunks = 12, []
    while cursor < len(data):
        if cursor + 8 > len(data):
            raise ValueError('Truncated chunk header')
        size, kind = struct.unpack_from('<II', data, cursor)
        cursor += 8
        if size % 4 or cursor + size > len(data):
            raise ValueError('Malformed GLB chunk length')
        chunks.append((kind, data[cursor:cursor + size]))
        cursor += size
    if len(chunks) != 2 or chunks[0][0] != JSON_CHUNK or chunks[1][0] != BIN_CHUNK:
        raise ValueError('Expected exactly one JSON and one BIN chunk')
    document = json.loads(chunks[0][1].decode('utf-8'))
    if len(document.get('buffers', [])) != 1 or 'uri' in document['buffers'][0]:
        raise ValueError('Expected a single embedded GLB buffer')
    return document, chunks[1][1]


def padded(data, pad):
    return data + pad * (-len(data) % 4)


def create_glb(document, binary):
    jb = padded(json.dumps(document, ensure_ascii=False, separators=(',', ':')).encode('utf-8'), b' ')
    bb = padded(bytes(binary), b'\x00')
    total = 12 + 8 + len(jb) + 8 + len(bb)
    return (struct.pack('<4sII', GLB_MAGIC, 2, total) +
            struct.pack('<II', len(jb), JSON_CHUNK) + jb +
            struct.pack('<II', len(bb), BIN_CHUNK) + bb)


def convert(source_bytes, expected_sha=None):
    actual_sha = sha256(source_bytes)
    if expected_sha and actual_sha.lower() != expected_sha.lower():
        raise ValueError('Pinned source map GLB SHA-256 mismatch')
    document, original_binary = parse_glb(source_bytes)
    textures = document.get('textures', [])
    images = document.get('images', [])
    extension_indices = set()
    for tex in textures:
        ext = tex.get('extensions', {}).get(WEBP_EXTENSION)
        if ext is not None:
            if not isinstance(ext, dict) or not isinstance(ext.get('source'), int):
                raise ValueError('Invalid EXT_texture_webp texture source')
            extension_indices.add(ext['source'])
    # glTFast may inspect unused image definitions, so convert all embedded WebP images.
    webp_indices = {
        i for i, entry in enumerate(images)
        if entry.get('mimeType', '').lower() == 'image/webp' or
        entry.get('uri', '').lower().startswith('data:image/webp')
    } | extension_indices
    if not webp_indices:
        if WEBP_EXTENSION in document.get('extensionsRequired', []):
            raise ValueError('Required EXT_texture_webp but no convertible texture images found')
        return source_bytes, {'sourceSha256': actual_sha, 'convertedImages': 0, 'status': 'NO_WEBP'}
    try:
        from PIL import Image
    except ImportError as exc:
        raise RuntimeError('Pillow is required: python -m pip install pillow') from exc
    binary = bytearray(original_binary)
    views = document.setdefault('bufferViews', [])
    for index in sorted(webp_indices):
        if index < 0 or index >= len(images):
            raise ValueError(f'Invalid WebP image index: {index}')
        img = images[index]
        if 'bufferView' in img:
            view = views[img['bufferView']]
            if view.get('buffer', 0) != 0:
                raise ValueError('Embedded WebP image is not in buffer zero')
            begin = view.get('byteOffset', 0)
            end = begin + view['byteLength']
            if begin < 0 or end > len(original_binary):
                raise ValueError(f'Out-of-range WebP buffer view: {index}')
            image_bytes = original_binary[begin:end]
        elif img.get('uri', '').startswith('data:'):
            image_bytes = base64.b64decode(img['uri'].split(',', 1)[1], validate=True)
        else:
            raise ValueError(f'WebP image {index} is external; expected self-contained GLB')
        with Image.open(io.BytesIO(image_bytes)) as im:
            if im.format != 'WEBP':
                raise ValueError(f'Image {index} marked WebP but is {im.format}')
            im = im.convert('RGBA' if 'A' in im.getbands() else 'RGB')
            png = io.BytesIO()
            im.save(png, format='PNG')
            png_bytes = png.getvalue()
        while len(binary) % 4:
            binary.append(0)
        views.append({'buffer': 0, 'byteOffset': len(binary), 'byteLength': len(png_bytes)})
        binary.extend(png_bytes)
        img.pop('uri', None)
        img['bufferView'] = len(views) - 1
        img['mimeType'] = 'image/png'
    rewritten_textures = 0
    for tex in textures:
        ext = tex.get('extensions', {}).pop(WEBP_EXTENSION, None)
        if ext is not None:
            tex['source'] = ext['source']
            rewritten_textures += 1
            if not tex['extensions']:
                del tex['extensions']
    for key in ('extensionsUsed', 'extensionsRequired'):
        if key in document:
            document[key] = [x for x in document[key] if x != WEBP_EXTENSION]
            if not document[key]:
                del document[key]
    document['buffers'][0]['byteLength'] = len(binary)
    output = create_glb(document, binary)
    verified, _ = parse_glb(output)
    if WEBP_EXTENSION in verified.get('extensionsRequired', []):
        raise AssertionError('Conversion still has unsupported required extension')
    return output, {
        'status': 'CONVERTED_WEBP_TO_PNG',
        'sourceSha256': actual_sha,
        'outputSha256': sha256(output),
        'convertedImages': len(webp_indices),
        'rewrittenTextures': rewritten_textures,
        'sourceBytes': len(source_bytes),
        'outputBytes': len(output),
        'retainsOriginalGeometry': True,
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('source', type=Path)
    parser.add_argument('output', type=Path)
    parser.add_argument('--sha256', required=True)
    args = parser.parse_args()
    if args.source.resolve() == args.output.resolve():
        parser.error('Do not overwrite the SHA-pinned original source')
    output, report = convert(args.source.read_bytes(), args.sha256)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    pending = args.output.with_suffix('.glb.part')
    try:
        pending.write_bytes(output)
        pending.replace(args.output)
    finally:
        pending.unlink(missing_ok=True)
    report_file = args.output.with_suffix('.conversion.json')
    report_file.write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(report, indent=2))


if __name__ == '__main__':
    main()
