import test from 'node:test';
import assert from 'node:assert/strict';
import {createLiveSession} from '../dist/rebuild/live-session.js';

function grid(){
 const width=36,height=36,walkable=new Uint8Array(width*height).fill(1),bushes=new Int8Array(width*height);
 return {width,height,walkable,bushes,cellSize:100,
  cell(x,z){return {x:Math.floor(x/100),y:Math.floor(z/100)};},
  canWalk(x,z){const p=this.cell(x,z);return p.x>=0&&p.y>=0&&p.x<width&&p.y<height;},
  nearest(x,z){return this.canWalk(x,z)?{x,z}:null;},
  path(a,b){return this.canWalk(b.x,b.z)?[{x:b.x,z:b.z}]:null;}
 };
}
const spawn=(player)=>player.role==='hunter'?{x:150,z:150}:{x:250,z:150};
test('live match ticks from one clock and locks player hunter during preparation',()=>{
 const nav=grid(),s=createLiveSession(nav,{start:{x:150,z:150},spawns:spawn});
 assert.equal(s.snapshot().phase,'prepare');
 assert.equal(s.syncPlayer({x:350,z:150},{x:1,z:0}),false);
 for(let i=0;i<120;i++)s.tick(1);
 assert.equal(s.snapshot().phase,'chase');
 assert.equal(s.syncPlayer({x:160,z:150},{x:1,z:0}),true);
});
test('live hunter A capture uses real facing and visibility, and ends round',()=>{
 const nav=grid(),s=createLiveSession(nav,{start:{x:150,z:150},spawns:spawn});
 for(let i=0;i<120;i++)s.tick(1);
 const enemy=s.actors.get('ai-1');enemy.x=250;enemy.z=150;enemy.route=[];
 assert.equal(s.playerAttack().ok,false);
 s.syncPlayer({x:150,z:150},{x:1,z:0});
 assert.equal(s.playerAttack().ok,true);
 assert.equal(s.snapshot().phase,'finished');
 assert.equal(s.snapshot().visible.length,0);
 assert.equal(s.snapshot().result.entries.find(p=>p.role==='hunter').score,2);
});
test('hider player may move during preparation; enemy AI hunter stays in place',()=>{
 const nav=grid(),s=createLiveSession(nav,{start:{x:250,z:150},playerRole:'hider',spawns:spawn});
 const hunter=s.actors.get('ai-1'),startX=hunter.x;
 assert.equal(s.syncPlayer({x:350,z:150},{x:1,z:0}),true);
 for(let i=0;i<10;i++)s.tick(1);
 assert.equal(hunter.x,startX);
 assert.equal(s.snapshot().phase,'prepare');
});
test('AI hunter can catch player after chase start, not earlier',()=>{
 const nav=grid(),s=createLiveSession(nav,{start:{x:250,z:150},playerRole:'hider',spawns:spawn});
 const hunter=s.actors.get('ai-1'),player=s.actors.get('player');
 hunter.x=150;hunter.z=150;
 for(let i=0;i<119;i++)s.tick(1);
 assert.equal(player.active,true);
 s.tick(1);
 for(let i=0;i<2&&player.active;i++)s.tick(.5);
 assert.equal(player.active,false);
 assert.equal(s.snapshot().phase,'finished');
});
test('no vision through blocking wall and hidden targets never leak to player view',()=>{
 const nav=grid();nav.walkable[1*36+2]=0;
 const s=createLiveSession(nav,{start:{x:150,z:150},spawns:spawn});
 const enemy=s.actors.get('ai-1');enemy.x=350;enemy.z=150;enemy.route=[];
 assert.equal(s.snapshot().visible.length,0);
});
test('all four original-asset heroes can be selected for live round',()=>{
 for(const playerHero of ['盖伦','布里茨','锐雯','亚索']){
   const session=createLiveSession(grid(),{playerHero,start:{x:150,z:150},spawns:spawn});
   assert.equal(session.match.players[0].hero,playerHero);
   assert.equal(session.snapshot().phase,'prepare');
   assert.equal(session.actors.size,2);
 }
});
test('invalid input and invalid phase stepping are rejected',()=>{
 const s=createLiveSession(grid(),{start:{x:150,z:150},spawns:spawn});
 assert.throws(()=>s.tick(-1),RangeError);
});
