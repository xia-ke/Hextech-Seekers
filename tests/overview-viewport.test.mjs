import test from 'node:test';
import assert from 'node:assert/strict';
import {overviewViewport} from '../dist/rebuild/overview-viewport.js';
const map={width:8192,height:7168};
test('overview source crops precisely map onto existing camera transform',()=>{
 const p=overviewViewport(map,2048,1792,{x:4200,y:3600},1,1600,900);
 assert.ok(p&&p.sx>0&&p.sy>0);
 assert.equal(p.dw,1600);assert.equal(p.dh,900);
 for(const [source,destination,scale]of [['sx','dx',map.width/2048],['sy','dy',map.height/1792]]){
   assert.ok(Number.isFinite(p[source]));assert.ok(Number.isFinite(p[destination]));
 }
 assert.ok(Math.abs((p.sx/2048*map.width-4200)+800-p.dx)<1e-9);
});
test('overview near-map-edge crop clips without stretching or black edges',()=>{
 const p=overviewViewport(map,2048,1792,{x:50,y:30},.8,1600,900);
 assert.equal(p.sx,0);assert.equal(p.sy,0);
 assert.ok(p.dx>0&&p.dy>0);
 assert.ok(p.dw>0&&p.dw<=1600&&p.dh<=900);
 assert.ok(Math.abs(p.sw*map.width/2048*.8-p.dw)<1e-8);
 assert.ok(Math.abs(p.sh*map.height/1792*.8-p.dh)<1e-8);
});
test('overview entirely outside the original Rift bounds is skipped',()=>{
 assert.equal(overviewViewport(map,2048,1792,{x:-6000,y:-6000},1,1600,900),null);
 assert.throws(()=>overviewViewport(map,2048,1792,{x:20,y:30},0,1600,900),RangeError);
});
