import test from 'node:test';
import assert from 'node:assert/strict';
import {setupRound,advanceRound} from '../dist/rebuild/match.js';
import {createActors,setDestination,stepActor,meleeHit,chooseAiDestination} from '../dist/rebuild/encounter.js';

const nav={
  nearest(x,z){return {x:Math.max(0,Math.min(900,x)),z:Math.max(0,Math.min(900,z))};},
  canWalk(x,z){return x>=0&&x<=900&&z>=0&&z<=900&&!(x>=400&&x<=500&&z>=400&&z<=500);},
  path(start,end){return this.canWalk(end.x,end.z)?[{x:end.x,z:end.z}]:null;},
  lineClear(a,b){for(let t=0;t<=1;t+=0.02)if(!this.canWalk(a.x+(b.x-a.x)*t,a.z+(b.z-a.z)*t))return false;return true;}
};
const make=(role='hunter')=>{
 const match=setupRound({totalPlayers:2,playerRole:role});
 const actors=createActors(match,nav,p=>p.role==='hunter'?{x:100,z:100}:{x:250,z:100});
 return {match,actors,hunter:[...actors.values()].find(a=>a.role==='hunter'),hider:[...actors.values()].find(a=>a.role==='hider')};
};
test('actors initialize only in legal nav cells',()=>{
 const {actors}=make();
 assert.equal(actors.size,2);
 assert.throws(()=>createActors(setupRound(),nav,()=>({x:450,z:450})),/Invalid spawn/);
});
test('hunter cannot leave spawn or attack during preparation',()=>{
 const {match,hunter,hider}=make();
 assert.equal(setDestination(hunter,nav,{x:200,z:100},match),false);
 stepActor(hunter,nav,0.2,match);
 assert.equal(hunter.x,100);
 assert.equal(meleeHit(hunter,hider,match,{visible:()=>true,clearPath:()=>true}).ok,false);
});
test('movement obeys speed, destination and navigation',()=>{
 const {match,hunter}=make();advanceRound(match,120);
 assert.equal(setDestination(hunter,nav,{x:300,z:100},match),true);
 stepActor(hunter,nav,0.25,match);
 assert.ok(hunter.x>100&&hunter.x<300);
 for(let i=0;i<6;i++)stepActor(hunter,nav,0.25,match);
 assert.equal(hunter.x,300);
 assert.equal(setDestination(hunter,nav,{x:450,z:450},match),false);
});
test('capture rejects invisible targets, blocked line and wrong facing',()=>{
 const {match,hunter,hider}=make();advanceRound(match,120);
 hunter.facing={x:1,z:0};
 assert.equal(meleeHit(hunter,hider,match,{visible:()=>false,clearPath:()=>true}).reason,'not-visible');
 assert.equal(meleeHit(hunter,hider,match,{visible:()=>true,clearPath:()=>false}).reason,'obstructed');
 hunter.facing={x:-1,z:0};
 assert.equal(meleeHit(hunter,hider,match,{visible:()=>true,clearPath:()=>true}).reason,'outside-facing');
 assert.equal(hider.active,true);
});
test('valid melee hit eliminates exactly one target and ends round',()=>{
 const {match,hunter,hider}=make();advanceRound(match,120);
 hunter.facing={x:1,z:0};
 const result=meleeHit(hunter,hider,match,{visible:()=>true,clearPath:(a,b)=>nav.lineClear(a,b)});
 assert.equal(result.ok,true);
 assert.equal(hider.active,false);
 assert.equal(match.phase,'finished');
 assert.equal(meleeHit(hunter,hider,match,{visible:()=>true,clearPath:()=>true}).ok,false);
});
test('no omniscient AI acquisition without vision; patrol is explicit',()=>{
 const {match,actors,hunter,hider}=make();advanceRound(match,120);
 assert.equal(chooseAiDestination(hunter,actors,nav,match,{}),false);
 assert.equal(hunter.route.length,0);
 assert.equal(chooseAiDestination(hunter,actors,nav,match,{visible:()=>false,patrol:()=>({x:200,z:100})}),true);
 assert.equal(hunter.route[0].x,200);
 hunter.route=[];
 assert.equal(chooseAiDestination(hunter,actors,nav,match,{visible:()=>true}),true);
 assert.equal(hunter.route[0].x,hider.x);
});
test('hider AI flees visible hunter, and cannot use out-of-map target',()=>{
 const {match,actors,hider}=make();advanceRound(match,120);
 assert.equal(chooseAiDestination(hider,actors,nav,match,{visible:()=>true}),true);
 assert.ok(hider.route[0].x>=hider.x);
});

test('rounded weapon tip accepts diagonal contact inside the capsule',()=>{
 const {match,hunter,hider}=make();advanceRound(match,120);
 hunter.facing={x:1,z:0};
 hider.x=302;hider.z=124; // along 202; 22 beyond reach=180, lateral=24
 const result=meleeHit(hunter,hider,match,{visible:()=>true,clearPath:()=>true,
   hitbox:{range:180,halfWidth:36}});
 assert.equal(result.ok,true);
});
test('outside rounded weapon tip cannot catch a diagonal target',()=>{
 const {match,hunter,hider}=make();advanceRound(match,120);
 hunter.facing={x:1,z:0};
 hider.x=313;hider.z=132; // 33 beyond reach=180, 32 lateral; radius 36
 const result=meleeHit(hunter,hider,match,{visible:()=>true,clearPath:()=>true,
   hitbox:{range:180,halfWidth:36}});
 assert.equal(result.reason,'outside-hitbox');
 assert.equal(hider.active,true);
});
test('back-facing, extreme range and lateral boundary have independent results',()=>{
 const {match,hunter,hider}=make();advanceRound(match,120);
 hunter.facing={x:1,z:0};
 const opts={visible:()=>true,clearPath:()=>true,hitbox:{range:180,halfWidth:36}};
 hider.x=80;hider.z=100;
 assert.equal(meleeHit(hunter,hider,match,opts).reason,'outside-facing');
 hider.x=317;hider.z=100;
 assert.equal(meleeHit(hunter,hider,match,opts).reason,'out-of-range');
 hider.x=250;hider.z=137;
 assert.equal(meleeHit(hunter,hider,match,opts).reason,'outside-hitbox');
 hider.z=136;
 assert.equal(meleeHit(hunter,hider,match,opts).ok,true);
});
test('line-of-sight and vision both remain mandatory for capsule attacks',()=>{
 const {match,hunter,hider}=make();advanceRound(match,120);
 hunter.facing={x:1,z:0};
 const box={range:180,halfWidth:36};
 assert.equal(meleeHit(hunter,hider,match,{hitbox:box,visible:()=>false,clearPath:()=>true}).reason,'not-visible');
 assert.equal(meleeHit(hunter,hider,match,{hitbox:box,visible:()=>true,clearPath:()=>false}).reason,'obstructed');
 assert.equal(hider.active,true);
});
test('non-finite position is rejected before any capture side effect',()=>{
 const {match,hunter,hider}=make();advanceRound(match,120);
 hunter.facing={x:1,z:0};hider.x=NaN;
 assert.equal(meleeHit(hunter,hider,match,{visible:()=>true,clearPath:()=>true,
  hitbox:{range:180,halfWidth:36}}).reason,'invalid-position');
 assert.equal(hider.active,true);
});
