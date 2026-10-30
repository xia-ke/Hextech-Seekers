import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, statSync } from 'node:fs';
import { NavGrid } from '../dist/rebuild/navigation.js';
import { project, unproject, viewDepth, decodeDepth, directionIndex } from '../dist/rebuild/projection.js';

const root=new URL('../dist/assets/visual/',import.meta.url);
const meta=JSON.parse(readFileSync(new URL('summoners-rift/map.json',root)));
const bytes=readFileSync(new URL('summoners-rift/navigation.bin',root));
const nav=new NavGrid(meta.nav,bytes.buffer.slice(bytes.byteOffset,bytes.byteOffset+bytes.byteLength));

test('scene picking round-trips elevations and both screen axes',()=>{
  for(const p of [{x:7765,y:50,z:-4020},{x:9838,y:-82,z:-4398},{x:11032,y:52,z:-6991}]){
    const pixel=project(meta,p),world=unproject(meta,pixel.x,pixel.y,viewDepth(p));
    for(const k of ['x','y','z'])assert.ok(Math.abs(world[k]-p[k])<1e-8,k);
    const encoded=Math.round((viewDepth(p)+12000)*4);
    assert.ok(Math.abs(decodeDepth(encoded>>8,encoded&255)-viewDepth(p))<=.125);
  }
  assert.equal(directionIndex(0),0);assert.equal(directionIndex(Math.PI),8);assert.equal(directionIndex(-Math.PI/2),12);
});

test('original navigation connects red, river and blue without wall or boundary crossing',()=>{
  const start=nav.nearest(7500,-4400);assert.ok(start&&nav.canWalk(start.x,start.z));
  for(const destination of [{x:7765,z:-4020},{x:9838,z:-4398},{x:11032,z:-6991}]){
    const end=nav.nearest(destination.x,destination.z),path=nav.path(start,end);
    assert.ok(path?.length,'destination reachable');let previous=start;
    for(const p of path){assert.ok(nav.lineClear(previous,p));assert.ok(p.x+p.z>=-120);previous=p;}
    assert.deepEqual(path.at(-1),end);
  }
  assert.equal(nav.canWalk(1000,-12000),false,'upper half excluded');
  assert.equal(nav.canWalk(-500,100),false,'out of map excluded');
  assert.equal(nav.nearest(-50000,-50000),null);
});

test('pathfinder takes a detour around a continuous synthetic wall',()=>{
  const width=16,height=16,n=width*height,buffer=new ArrayBuffer(n*3),walk=new Uint8Array(buffer,0,n);walk.fill(1);
  for(let row=0;row<11;row++)walk[row*width+8]=0;
  const grid=new NavGrid({width,height,minX:0,minZ:0,cellSize:50},buffer,false);
  const start=grid.center(3,5),end=grid.center(12,5),path=grid.path(start,end);
  assert.equal(grid.lineClear(start,end),false);assert.ok(path?.length>1);
  assert.ok(path.some(p=>p.z>550),'goes around wall end');
  let previous=start;for(const p of path){assert.ok(grid.lineClear(previous,p));previous=p;}
});

test('all shipped visual frames, depth tiles and navigation payloads are complete',()=>{
  assert.equal(bytes.byteLength,meta.nav.width*meta.nav.height*6);
  assert.equal(meta.edition,'map11-base-srx');assert.equal(meta.tiles.length,56);
  for(const [x,y]of meta.tiles){
    assert.ok(statSync(new URL(`summoners-rift/${x}-${y}.webp`,root)).size>100);
    const depth=readFileSync(new URL(`summoners-rift/${x}-${y}-depth.png`,root));
    assert.equal(depth.readUInt32BE(16),meta.tileSize);assert.equal(depth.readUInt32BE(20),meta.tileSize);
  }
  assert.ok(statSync(new URL('summoners-rift/minimap.webp',root)).size>1000);
  const hero=JSON.parse(readFileSync(new URL('garen/sprite.json',root)));
  assert.equal(hero.pitch,meta.pitch);
  for(const clip of Object.values(hero.clips)){
    assert.ok(clip.frames>=8&&clip.duration>0);
    assert.ok(statSync(new URL('garen/'+clip.image,root)).size>1000);
    const depth=readFileSync(new URL('garen/'+clip.depth,root));
    assert.equal(depth.readUInt32BE(16),hero.tileSize*hero.directions);
    assert.equal(depth.readUInt32BE(20),hero.tileSize*clip.frames);
  }
});
