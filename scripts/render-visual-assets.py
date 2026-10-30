"""Render original geometry to fixed-camera 2D assets using a standalone EGL context.

This is an asset build step, not a second browser/preview implementation.
References: iamywl/Web_game GLB assets; tengge1/lol-model-viewer animations.
Source projects carry MIT licenses. Art remains attributed to Riot Games.
"""
import argparse
import io
import json
import math
import struct
from pathlib import Path

import moderngl
import numpy as np
from PIL import Image
from scipy.spatial.transform import Rotation

PITCH = math.radians(55)
SIN, COS = math.sin(PITCH), math.cos(PITCH)

VERTEX = '''#version 330
in vec3 position; in vec3 normal; in vec2 texcoord; in vec2 texcoord1;
uniform mat4 projection; out vec2 uv; out vec2 uv1; out float height;
out float worldDepth; out float up;
void main(){gl_Position=projection*vec4(position,1);uv=texcoord;uv1=texcoord1;
height=position.y;up=abs(normal.y);worldDepth=dot(position,vec3(0,0.8191520443,0.5735764364));}
'''
FRAGMENT = '''#version 330
in vec2 uv;in vec2 uv1;in float height;in float worldDepth;in float up;
uniform sampler2D tex0;uniform sampler2D tex1;uniform sampler2D tex2;
uniform sampler2D tex3;uniform sampler2D tex4;
uniform int blendMode;uniform int navMode;uniform float ground;
uniform vec4 diffuseFactor;uniform float alphaCutoff;
layout(location=0)out vec4 color;layout(location=1)out float depthValue;
void main(){vec4 c=texture(tex0,uv);if(blendMode==1){
vec4 b=texture(tex1,uv1);c.rgb=mix(c.rgb,texture(tex2,uv).rgb,b.r);
c.rgb=mix(c.rgb,texture(tex3,uv).rgb,b.g);c.rgb=mix(c.rgb,texture(tex4,uv).rgb,b.b);c.a=1.;}
vec3 linearRGB=mix(c.rgb/12.92,pow((c.rgb+0.055)/1.055,vec3(2.4)),step(vec3(0.04045),c.rgb));
linearRGB*=diffuseFactor.rgb;
c.rgb=mix(linearRGB*12.92,1.055*pow(max(linearRGB,vec3(0)),vec3(1./2.4))-0.055,step(vec3(0.0031308),linearRGB));
c.a*=diffuseFactor.a;if(c.a<alphaCutoff)discard;
color=navMode==1?vec4(height,ground*step(0.55,up),0,1):vec4(c.rgb,1);
depthValue=worldDepth;}
'''


def projection(width, height, ppu, min_x, min_py, tile_x=0, tile_y=0):
    m = np.zeros((4, 4), np.float32)
    m[0, 0] = 2 * ppu / width
    m[0, 3] = -1 - 2 * (min_x * ppu + tile_x) / width
    m[1, 1] = 2 * ppu * COS / height
    m[1, 2] = -2 * ppu * SIN / height
    m[1, 3] = 1 + 2 * (min_py * ppu + tile_y) / height
    m[2, 1], m[2, 2], m[3, 3] = -SIN / 30000, -COS / 30000, 1
    return m


def make_target(ctx, size, float_color=False):
    color = ctx.texture(size, 4, dtype='f4' if float_color else 'f1')
    depth_value = ctx.texture(size, 1, dtype='f4')
    depth = ctx.depth_renderbuffer(size)
    return ctx.framebuffer([color, depth_value], depth)


def depth_image(depth, rgba, offset, scale):
    q = np.clip(np.round((depth + offset) * scale), 0, 65535).astype(np.uint16)
    out = np.zeros((*q.shape, 4), np.uint8)
    out[:, :, 0], out[:, :, 1] = q >> 8, q & 255
    out[:, :, 3] = rgba[:, :, 3]
    return Image.fromarray(out)


class Renderer:
    def __init__(self):
        self.ctx = moderngl.create_standalone_context(backend='egl')
        self.program = self.ctx.program(vertex_shader=VERTEX, fragment_shader=FRAGMENT)
        self.ctx.enable(moderngl.DEPTH_TEST)
        self.ctx.disable(moderngl.CULL_FACE)
        self.textures = {}
        for i in range(5):
            self.program['tex' + str(i)] = i
        self.program['navMode'] = 0

    def texture(self, path):
        key = str(path)
        if key not in self.textures:
            image = Image.open(path).convert('RGBA')
            texture = self.ctx.texture(image.size, 4, image.tobytes())
            texture.build_mipmaps()
            texture.filter = (moderngl.LINEAR_MIPMAP_LINEAR, moderngl.LINEAR)
            self.textures[key] = texture
        return self.textures[key]

    def vao(self, data):
        buf = self.ctx.buffer(data.astype('f4').tobytes())
        return self.ctx.vertex_array(self.program, [(buf, '3f 3f 2f 2f', 'position', 'normal', 'texcoord', 'texcoord1')]), buf

    def bind(self, paths, blend=False, ground=0, tint=(1,1,1,1), cutoff=.35):
        if blend:
            chosen = [paths[i] for i in (0, 1, 2, 4, 6)]
        else:
            chosen = [paths[0]] * 5
        for i, path in enumerate(chosen):
            self.texture(path).use(i)
        self.program['blendMode'] = int(blend)
        self.program['ground'] = ground
        self.program['diffuseFactor'] = tint
        self.program['alphaCutoff'] = cutoff


def read_glb(source):
    raw=(source/'map.glb').read_bytes()
    magic,version,size=struct.unpack_from('<III',raw)
    if magic!=0x46546c67 or version!=2 or size!=len(raw):raise ValueError('Invalid map GLB')
    json_size,_=struct.unpack_from('<II',raw,12)
    gltf=json.loads(raw[20:20+json_size]);bin_size,kind=struct.unpack_from('<II',raw,20+json_size)
    if kind!=0x004e4942:raise ValueError('GLB binary chunk missing')
    data=memoryview(raw)[28+json_size:28+json_size+bin_size]
    types={5121:np.dtype('u1'),5123:np.dtype('<u2'),5125:np.dtype('<u4'),5126:np.dtype('<f4')}
    sizes={'SCALAR':1,'VEC2':2,'VEC3':3,'VEC4':4,'MAT4':16}
    def accessor(i):
        a=gltf['accessors'][i];view=gltf['bufferViews'][a['bufferView']];dtype=types[a['componentType']];n=sizes[a['type']]
        offset=view.get('byteOffset',0)+a.get('byteOffset',0);stride=view.get('byteStride',n*dtype.itemsize)
        return np.ndarray((a['count'],n),dtype,buffer=data,offset=offset,strides=(stride,dtype.itemsize)).copy()
    cache=source/'extracted-map-textures';cache.mkdir(exist_ok=True)
    images=[]
    for i,image in enumerate(gltf['images']):
        view=gltf['bufferViews'][image['bufferView']];start=view.get('byteOffset',0)
        path=cache/f'{i}.png'
        valid=False
        if path.exists():
            try:
                with Image.open(path) as cached:cached.load()
                valid=True
            except OSError:pass
        if not valid:
            temporary=path.with_suffix('.tmp.png')
            Image.open(io.BytesIO(data[start:start+view['byteLength']])).convert('RGBA').save(temporary)
            temporary.replace(path)
        images.append(path)
    materials=[]
    for m in gltf['materials']:
        pbr=m.get('pbrMetallicRoughness',{});t=gltf['textures'][pbr['baseColorTexture']['index']]
        image=t.get('extensions',{}).get('EXT_texture_webp',{}).get('source',t.get('source'))
        name=m.get('name','');lower=name.lower()
        materials.append(dict(name=name,kind=0,ground=('ground' in lower or 'floor' in lower or 'river' in lower),textures=[images[image]]*8,
                              tint=pbr.get('baseColorFactor',[1,1,1,1]),cutoff=m.get('alphaCutoff',.05) if m.get('alphaMode')!='OPAQUE' else -.1))
    pieces=[[]for _ in materials]
    def visit(index,parent):
        node=gltf['nodes'][index]
        if 'matrix' in node:local=np.array(node['matrix']).reshape(4,4).T
        else:
            local=np.eye(4);local[:3,:3]=Rotation.from_quat(node.get('rotation',[0,0,0,1])).as_matrix()@np.diag(node.get('scale',[1,1,1]));local[:3,3]=node.get('translation',[0,0,0])
        world=parent@local
        if 'mesh'in node:
            for primitive in gltf['meshes'][node['mesh']]['primitives']:
                if primitive.get('mode',4)!=4:raise ValueError('Map contains non-triangle geometry')
                attr=primitive['attributes'];pos=accessor(attr['POSITION']);normal=accessor(attr['NORMAL']);uv=accessor(attr['TEXCOORD_0'])
                pos=(np.column_stack([pos,np.ones(len(pos))])@world.T)[:,:3];normal=normal@np.linalg.inv(world[:3,:3]);normal/=np.maximum(np.linalg.norm(normal,axis=1)[:,None],1e-8)
                pos[:,2]*=-1;normal[:,2]*=-1
                indices=accessor(primitive['indices']).reshape(-1)
                vertices=np.column_stack([pos,normal,uv,uv]).astype('f4')
                pieces[primitive['material']].append(vertices[indices])
        for child in node.get('children',[]):visit(child,world)
    for root in gltf['scenes'][gltf.get('scene',0)]['nodes']:visit(root,np.eye(4))
    arrays=[np.concatenate(parts) if parts else np.empty((0,10),'f4')for parts in pieces]
    print('Modern Map11:',len(gltf['meshes']),'meshes;',sum(len(a)//3 for a in arrays),'triangles;',len(images),'embedded textures',flush=True)
    return materials,arrays


def render_map(renderer, source, output):
    ctx, program = renderer.ctx, renderer.program
    materials, data = read_glb(source)
    objects = []
    for material, points in zip(materials, data):
        if len(points):
            vao, vbo = renderer.vao(points)
            objects.append((material, vao, vbo))
    tile, ppu, min_x, min_py = 1024, .5, -500, -12500
    width, height = 8192, 7168
    folder = output / 'summoners-rift';folder.mkdir(parents=True, exist_ok=True)
    framebuffer = make_target(ctx, (tile, tile))
    thumbnail = Image.new('RGBA', (width//8, height//8))
    completed = []
    for row in range(math.ceil(height/tile)):
        for col in range(math.ceil(width/tile)):
            framebuffer.use();framebuffer.clear(0, 0, 0, 0, depth=1)
            program['projection'].write(projection(tile, tile, ppu, min_x, min_py, col*tile, row*tile).T.tobytes())
            program['navMode'] = 0
            for material, vao, _ in objects:
                renderer.bind(material['textures'], material['kind'] == 3, int(material['ground']),material.get('tint',(1,1,1,1)),material.get('cutoff',.35))
                vao.render(moderngl.TRIANGLES)
            color = np.frombuffer(framebuffer.read(components=4, alignment=1), np.uint8).reshape(tile, tile, 4)[::-1].copy()
            if np.count_nonzero(color[:, :, 3]) < 10:
                continue
            depth = np.frombuffer(framebuffer.read(attachment=1, components=1, dtype='f4', alignment=1), np.float32).reshape(tile, tile)[::-1]
            image = Image.fromarray(color)
            image.save(folder/f'{col}-{row}.webp', quality=94, method=4)
            depth_image(depth, color, 12000, 4).save(folder/f'{col}-{row}-depth.png', optimize=True)
            thumbnail.paste(image.resize((tile//8, tile//8), Image.Resampling.LANCZOS), (col*tile//8, row*tile//8))
            completed.append([col, row])
        print('Map tiles row', row+1, '/', math.ceil(height/tile), flush=True)
    thumbnail.save(folder/'overview.webp', quality=94)
    mini=make_target(ctx,(1024,1024));mini.use();mini.clear(.025,.06,.06,1,depth=1)
    m=np.zeros((4,4),'f4');m[0,0]=2/15000;m[0,3]=-1;m[1,2]=-2/15000;m[1,3]=-1;m[2,1]=-1/15000;m[3,3]=1
    program['projection'].write(m.T.tobytes());program['navMode']=0
    for material,vao,_ in objects:
        renderer.bind(material['textures'],material['kind']==3,int(material['ground']),material.get('tint',(1,1,1,1)),material.get('cutoff',.35));vao.render(moderngl.TRIANGLES)
    pixels=np.frombuffer(mini.read(components=4,alignment=1),np.uint8).reshape(1024,1024,4)[::-1].copy()
    Image.fromarray(pixels).save(folder/'minimap.webp',quality=90);mini.release()
    framebuffer.release()
    nav=json.loads((source/'navigation.json').read_text());scale=nav['source']['scale'];count=nav['cols']*nav['rows']
    flags=np.array(nav['flags'],dtype='<u2');walk=np.array(nav['walkable'],dtype='u1');walk[((flags&4)!=0)&((flags&2)==0)]=1
    heights=np.round(np.array(nav['heights'])/scale).astype('<i2');bush=np.array(nav['bushIds'],dtype='i1')
    (folder/'navigation.bin').write_bytes(walk.tobytes()+heights.tobytes()+flags.tobytes()+bush.tobytes())
    meta=dict(version=1,label='召唤师峡谷 · Map11',edition='map11-base-srx',tileSize=tile,ppu=ppu,width=width,height=height,minX=min_x,minPY=min_py,pitch=55,tiles=completed,
              nav=dict(width=nav['cols'],height=nav['rows'],minX=nav['originX']/scale,minZ=nav['originY']/scale-15000,cellSize=nav['cellSize']/scale,binary='navigation.bin',flagsOffset=count*3,bushOffset=count*5),
              provenance=dict(repository='https://github.com/iamywl/Web_game',commit='557a9d5352af12bee8bf3db879c22d04d0a0954e',scene='public/assets/maps/summoners-rift/map.glb',sha256='bdabbf3eb0c37dd2bb8567035c018d0da0f79c849c9353e5192a58279369a69e',coordinateTransform='x,y,-z'))
    (folder/'map.json').write_text(json.dumps(meta,separators=(',',':')))
    print('Original navigation:',nav['cols'],nav['rows'],'walkable',int(walk.sum()),flush=True)
    for _,vao,vbo in objects:vao.release();vbo.release()
    return


def render_champion(renderer, source_assets, output, hero='garen'):
    ctx, program = renderer.ctx, renderer.program
    manifest=json.loads((source_assets/(hero+'.json')).read_text());raw=(source_assets/(hero+'.bin')).read_bytes()
    def arr(d):return np.frombuffer(raw,'<u2' if d['type']=='H' else '<f4',d['count'],d['offset'])
    geo=manifest['geometry'];vertices=arr(geo['position']).reshape(-1,3);uv=arr(geo['uv']).reshape(-1,2)
    normals=arr(geo['normal']).reshape(-1,3);weights=arr(geo['skinWeight']).reshape(-1,4);joints=arr(geo['skinIndex']).reshape(-1,4);indices=arr(geo['index'])
    bind=np.array([b['bind'] for b in manifest['bones']]).reshape(-1,4,4);inverse=np.linalg.inv(bind)
    # Older mesh v1 variants have bind world matrices but no local matrices.
    # Reconstruct the local rest pose from parent^-1 @ child world bind.
    local_bind=np.empty_like(bind)
    for i,b in enumerate(manifest['bones']):
        if b.get('local') is not None:
            local_bind[i]=np.asarray(b['local'],dtype=np.float64).reshape(4,4)
        elif b['parent']>=0:
            local_bind[i]=inverse[b['parent']]@bind[i]
        else:
            local_bind[i]=bind[i]
    homogeneous=np.column_stack([vertices,np.ones(len(vertices))])
    def pose(clip,t):
        local=local_bind.copy()
        for track in clip['tracks']:
            ps=arr(track['position']).reshape(-1,3);qs=arr(track['quaternion']).reshape(-1,4)
            f=int(t*clip['fps'])%len(ps);m=np.eye(4);m[:3,:3]=Rotation.from_quat(qs[f]).as_matrix();m[:3,3]=ps[f];local[track['bone']]=m
        world=np.zeros_like(local)
        for i,b in enumerate(manifest['bones']):world[i]=local[i] if b['parent']<0 else world[b['parent']]@local[i]
        skin=world@inverse
        transformed=np.einsum('nvij,nj->nvi',skin[joints],homogeneous)
        return (transformed*weights[:,:,None]).sum(axis=1)[:,:3]
    tile,ppu,pivot,directions=256,.6,[128,205],16
    target=make_target(ctx,(tile,tile));folder=output/hero;folder.mkdir(parents=True,exist_ok=True)
    # The same camera projection is used for the scene and every sprite direction.
    matrix=projection(tile,tile,ppu,-pivot[0]/ppu,-pivot[1]/ppu)
    program['projection'].write(matrix.T.tobytes());program['navMode']=0
    texture=source_assets/manifest['texture'];renderer.bind([texture]*8)
    frames_meta={}
    clip_names={c['name']:c for c in manifest['clips']}
    # Preserve original animation clips, while exporting stable runtime slots.
    chosen={slot:next((clip_names[key] for key in candidates if key in clip_names),None)
            for slot,candidates in {'idle1':['idle1','idle','idle2'],
                                    'run':['run','run1','walk'],
                                    'attack1':['attack1']}.items()}
    if any(v is None for v in chosen.values()):
        raise ValueError(f'{hero}: required real animation missing: '+str({k:v is not None for k,v in chosen.items()}))
    for name,count in [('idle1',8),('run',16),('attack1',12)]:
        clip=chosen[name]
        atlas=Image.new('RGBA',(tile*directions,tile*count));depth_atlas=Image.new('RGBA',atlas.size)
        for direction in range(directions):
            a=direction*2*math.pi/directions;rot=np.array([[math.cos(a),0,math.sin(a)],[0,1,0],[-math.sin(a),0,math.cos(a)]])
            for frame in range(count):
                points=pose(clip,frame/count*clip['duration'])@rot.T
                data=np.column_stack([points,normals,uv,uv])[indices]
                vao,vbo=renderer.vao(data)
                target.use();target.clear(0,0,0,0,depth=1);vao.render(moderngl.TRIANGLES)
                rgba=np.frombuffer(target.read(components=4,alignment=1),np.uint8).reshape(tile,tile,4)[::-1].copy()
                depth=np.frombuffer(target.read(attachment=1,components=1,dtype='f4',alignment=1),np.float32).reshape(tile,tile)[::-1]
                atlas.paste(Image.fromarray(rgba),(direction*tile,frame*tile))
                depth_atlas.paste(depth_image(depth,rgba,1024,16),(direction*tile,frame*tile))
                vao.release();vbo.release()
        atlas.save(folder/f'{name}.webp',lossless=True,method=4)
        depth_atlas.save(folder/f'{name}-depth.png',optimize=True)
        frames_meta[name]=dict(frames=count,duration=clip['duration'],image=name+'.webp',depth=name+'-depth.png')
        print('Champion atlas',name,atlas.size,flush=True)
        if name=='idle1':atlas.crop((0,0,tile,tile)).resize((512,512)).save(folder/'inspection.png')
    meta=dict(version=1,hero=manifest['hero'],tileSize=tile,ppu=ppu,pivot=pivot,directions=directions,pitch=55,clips=frames_meta,provenance=manifest['provenance'])
    (folder/'sprite.json').write_text(json.dumps(meta,separators=(',',':')))


if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('source',type=Path);p.add_argument('output',type=Path);p.add_argument('--champion-source',type=Path,required=True);p.add_argument('--only',choices=['map','champion','all'],default='all');p.add_argument('--hero',default='garen');args=p.parse_args()
    renderer=Renderer();print('Asset renderer:',renderer.ctx.info['GL_RENDERER'],flush=True)
    if args.only in ('champion','all'):render_champion(renderer,args.champion_source,args.output,args.hero)
    if args.only in ('map','all'):render_map(renderer,args.source,args.output)
