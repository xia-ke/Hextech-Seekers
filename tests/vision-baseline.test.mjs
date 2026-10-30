import test from 'node:test';
import assert from 'node:assert/strict';
import {makeVision} from '../dist/rebuild/vision-baseline.js';
import {setupRound,advanceRound} from '../dist/rebuild/match.js';
import {createActors,chooseAiDestination} from '../dist/rebuild/encounter.js';
function grid(w=12,h=12) {
 const walkable=new Uint8Array(w*h).fill(1),bushes=new Int8Array(w*h);
 return {width:w,height:h,cellSize:100,walkable,bushes,
   cell(x,z){return {x:Math.floor(x/100),y:Math.floor(z/100)};},
   canWalk(x,z){const c=this.cell(x,z);return c.x>=0&&c.y>=0&&c.x<w&&c.y<h&&this.walkable[c.y*w+c.x]!==0;},
   nearest(x,z){return this.canWalk(x,z)?{x,z}:null;},
   path(a,b){return this.canWalk(b.x,b.z)?[{x:b.x,z:b.z}]:null;}
 };
}
const at=(id,role,x,z)=>({id,role,x,z,active:true});
test('range cutoff and no opponents without sight',()=>{
 const nav=grid(),v=makeVision(nav,{range:300});
 assert.equal(v.canSee(at('a','hunter',50,50),at('b','hider',350,50)),true);
 assert.equal(v.canSee(at('a','hunter',50,50),at('b','hider',450,50)),false);
});
test('raw blocking cells stop sight even when endpoints are open',()=>{
 const nav=grid();nav.walkable[3*12+3]=0;
 const v=makeVision(nav);
 assert.equal(v.hasLineOfSight(at('a','hunter',150,350),at('b','hider',550,350)),false);
 assert.equal(v.hasLineOfSight(at('a','hunter',150,250),at('b','hider',550,250)),true);
});
test('diagonal corner obstruction is conservative',()=>{
 const nav=grid();nav.walkable[1]=0;
 const v=makeVision(nav);
 assert.equal(v.hasLineOfSight(at('a','hunter',50,50),at('b','hider',150,150)),false);
});
test('brush hides target from outside but reveals to same bush group',()=>{
 const nav=grid();nav.bushes[2*12+2]=5;nav.bushes[2*12+3]=5;
 const v=makeVision(nav);
 const hidden=at('h','hider',250,250);
 assert.equal(v.canSee(at('hunter','hunter',150,250),hidden),false);
 assert.equal(v.canSee(at('hunter','hunter',350,250),hidden),true);
});
test('different brush groups do not share vision',()=>{
 const nav=grid();nav.bushes[2*12+2]=1;nav.bushes[2*12+3]=2;
 const v=makeVision(nav);
 assert.equal(v.canSee(at('a','hunter',350,250),at('b','hider',250,250)),false);
});
test('each hider has independent visibility of hunter',()=>{
 const nav=grid();nav.walkable[3*12+3]=0;
 const v=makeVision(nav);
 const hunter=at('hunter','hunter',550,350),h1=at('h1','hider',450,350),h2=at('h2','hider',150,350);
 const actors=new Map([hunter,h1,h2].map(x=>[x.id,x]));
 assert.equal(v.visibleOpponents(h1,actors).length,1);
 assert.equal(v.visibleOpponents(h2,actors).length,0);
});
test('AI must not acquire blocked enemy without line of sight',()=>{
 const nav=grid();nav.walkable[3*12+3]=0;
 const match=setupRound({totalPlayers:2});advanceRound(match,120);
 const actors=createActors(match,nav,p=>p.role==='hunter'?{x:150,z:350}:{x:550,z:350});
 const vision=makeVision(nav);
 const hunter=actors.get('player');
 assert.equal(chooseAiDestination(hunter,actors,nav,match,{visible:vision.canSee}),false);
 assert.equal(hunter.route.length,0);
});
test('AI acquires opponent only after clear sight',()=>{
 const nav=grid(),match=setupRound({totalPlayers:2});advanceRound(match,120);
 const actors=createActors(match,nav,p=>p.role==='hunter'?{x:150,z:350}:{x:550,z:350});
 const v=makeVision(nav),hunter=actors.get('player');
 assert.equal(chooseAiDestination(hunter,actors,nav,match,{visible:v.canSee}),true);
 assert.equal(hunter.route[0].x,550);
});
