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
test('player capture resolves only when the attack contact frame is reached',()=>{
 const s=createLiveSession(grid(),{start:{x:150,z:150},spawns:spawn});
 for(let i=0;i<120;i++)s.tick(1);
 const enemy=s.actors.get('ai-1');enemy.x=250;enemy.z=150;enemy.route=[];
 s.syncPlayer({x:150,z:150},{x:1,z:0});
 const started=s.beginPlayerAttack({duration:1,hitAt:.5});
 assert.equal(started.ok,true);
 assert.equal(s.snapshot().phase,'chase');
 assert.equal(enemy.active,true);
 s.tick(.49);
 assert.equal(enemy.active,true);
 assert.equal(s.snapshot().attack.resolved,false);
 s.tick(.02);
 assert.equal(s.snapshot().attack.resolved,true);
 assert.equal(s.snapshot().attack.result.ok,true);
 assert.equal(enemy.active,false);
});
test('attack recovery remains locked after contact until the original clip ends',()=>{
 const s=createLiveSession(grid(),{totalPlayers:3,start:{x:150,z:150},spawns:spawn});
 for(let i=0;i<120;i++)s.tick(1);
 const enemy=s.actors.get('ai-1');enemy.x=250;enemy.z=150;enemy.route=[];
 s.syncPlayer({x:150,z:150},{x:1,z:0});
 s.beginPlayerAttack({duration:1,hitAt:.5});
 s.tick(.51);
 assert.equal(s.snapshot().phase,'chase');
 assert.equal(s.snapshot().attack.resolved,true);
 assert.equal(s.snapshot().attack.finished,false);
 assert.equal(s.beginPlayerAttack({duration:1,hitAt:.5}).reason,'attack-in-progress');
 s.tick(.5);
 assert.equal(s.snapshot().attack.finished,true);
});
test('attack rechecks target range at the contact frame instead of capturing at click time',()=>{
 const s=createLiveSession(grid(),{start:{x:150,z:150},spawns:spawn});
 for(let i=0;i<120;i++)s.tick(1);
 const enemy=s.actors.get('ai-1');enemy.x=250;enemy.z=150;enemy.route=[];
 s.syncPlayer({x:150,z:150},{x:1,z:0});
 assert.equal(s.beginPlayerAttack({duration:1,hitAt:.5}).ok,true);
 enemy.x=500;
 s.tick(.51);
 assert.equal(enemy.active,true);
 assert.equal(s.snapshot().attack.result.reason,'out-of-range');
});
test('player cannot start attacks during preparation or overlap attack windups',()=>{
 const s=createLiveSession(grid(),{start:{x:150,z:150},spawns:spawn});
 assert.equal(s.beginPlayerAttack().reason,'not-hunter-or-not-chasing');
 for(let i=0;i<120;i++)s.tick(1);
 assert.equal(s.beginPlayerAttack({duration:1,hitAt:.5}).ok,true);
 assert.equal(s.beginPlayerAttack({duration:1,hitAt:.5}).reason,'attack-in-progress');
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

import {ATTACK_PROFILES,attackTimingFor} from '../dist/rebuild/attack-profile.js';

test('each original hero uses an individually calibrated attack contact frame',()=>{
 const expected={'盖伦':4,'布里茨':8,'锐雯':4,'亚索':4};
 assert.deepEqual(Object.fromEntries(Object.entries(ATTACK_PROFILES).map(([hero,p])=>[hero,p.contactFrame])),expected);
});
test('attack timing starts at the selected visible atlas frame, not one frame later',()=>{
 const durations={'盖伦':2,'布里茨':14/15,'锐雯':17/10,'亚索':5/3};
 for(const [hero,contactFrame] of Object.entries({'盖伦':4,'布里茨':8,'锐雯':4,'亚索':4})){
   const timing=attackTimingFor(hero,{frames:12,duration:durations[hero]});
   assert.equal(timing.contactFrame,contactFrame);
   assert.ok(Math.abs(timing.hitAt/durations[hero]-((contactFrame-1)/12+1e-6))<1e-12);
   assert.equal(Math.floor(timing.hitAt/durations[hero]*12)+1,contactFrame);
   assert.ok(timing.hitbox.range>0&&timing.hitbox.halfWidth>0);
 }
});
test('attack profile rejects unknown heroes and mismatched animation atlases',()=>{
 assert.throws(()=>attackTimingFor('未知英雄',{frames:12,duration:1}),/Unsupported attack profile/);
 assert.throws(()=>attackTimingFor('盖伦',{frames:8,duration:1}),/12-frame/);
 assert.throws(()=>attackTimingFor('盖伦',{frames:12,duration:0}),/Invalid attack clip/);
});

test('hero-specific attack hitboxes use a forward capsule and reject lateral misses',()=>{
 const s=createLiveSession(grid(),{start:{x:150,z:150},spawns:spawn});
 for(let i=0;i<120;i++)s.tick(1);
 const enemy=s.actors.get('ai-1');enemy.x=250;enemy.z=150;enemy.route=[];
 s.syncPlayer({x:150,z:150},{x:1,z:0});
 assert.equal(s.beginPlayerAttack({duration:1,hitAt:.5,hitbox:{range:140,halfWidth:25}}).ok,true);
 s.tick(.51);
 assert.equal(s.snapshot().attack.result.ok,true);
});
test('hero-specific attack capsule rejects targets beside the weapon path',()=>{
 const s=createLiveSession(grid(),{start:{x:150,z:150},spawns:spawn});
 for(let i=0;i<120;i++)s.tick(1);
 const enemy=s.actors.get('ai-1');enemy.x=200;enemy.z=190;enemy.route=[];
 s.syncPlayer({x:150,z:150},{x:1,z:0});
 s.beginPlayerAttack({duration:1,hitAt:.5,hitbox:{range:140,halfWidth:25}});
 s.tick(.51);
 assert.equal(enemy.active,true);
 assert.equal(s.snapshot().attack.result.reason,'outside-hitbox');
});
test('hero attack profiles have independent weapon reach and width',()=>{
 const values=Object.values(ATTACK_PROFILES).map(p=>[p.range,p.halfWidth]);
 assert.equal(new Set(values.map(v=>v.join(':'))).size,4);
});
