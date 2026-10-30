import test from 'node:test';
import assert from 'node:assert/strict';
import {setupRound, advanceRound, catchHider, finishRound, adjustChaseDuration, durationSeconds, bonusThresholdSeconds} from '../dist/rebuild/match.js';

test('2–4 seats, unique heroes and exactly one hunter, both player roles',()=>{
 for(const totalPlayers of [2,3,4])for(const playerRole of ['hunter','hider']){
  const r=setupRound({totalPlayers,playerRole,playerHero:'亚索'});
  assert.equal(r.players.length,totalPlayers);
  assert.equal(r.players.filter(x=>x.role==='hunter').length,1);
  assert.equal(new Set(r.players.map(x=>x.hero)).size,totalPlayers);
  assert.equal(r.players[0].role,playerRole);
 }
 assert.throws(()=>setupRound({totalPlayers:5}),RangeError);
});
test('preparation, chase and time-out are continuous and deterministic',()=>{
 for(const n of [2,3,4]){
  const r=setupRound({totalPlayers:n});
  assert.equal(r.remaining,120);
  advanceRound(r,119);
  assert.equal(r.phase,'prepare');
  advanceRound(r,1);
  assert.equal(r.phase,'chase');
  assert.equal(r.remaining,durationSeconds(n));
  advanceRound(r,r.remaining);
  assert.equal(r.phase,'finished');
  assert.equal(r.result.survivors,n-1);
 }
});
test('capture only during chase; eliminate all and award early bonus',()=>{
 const r=setupRound({totalPlayers:4});
 assert.equal(catchHider(r,'ai-1'),false);
 advanceRound(r,120);
 for(const id of ['ai-1','ai-2','ai-3'])assert.equal(catchHider(r,id),true);
 assert.equal(r.phase,'finished');
 assert.equal(r.result.bonus,1);
 assert.equal(r.result.entries.find(x=>x.role==='hunter').score,4);
 assert.equal(catchHider(r,'ai-1'),false);
});
test('survivor special branch S=1 gives captured players one point',()=>{
 const r=setupRound({totalPlayers:4});
 advanceRound(r,120);
 catchHider(r,'ai-1');catchHider(r,'ai-2');
 finishRound(r);
 assert.equal(r.result.entries.find(x=>x.id==='ai-3').score,2);
 assert.equal(r.result.entries.find(x=>x.id==='ai-1').score,1);
 assert.equal(r.result.entries.find(x=>x.id==='player').score,2);
});
test('S>=2 gives surviving hiders S, captured zero',()=>{
 const r=setupRound({totalPlayers:4});
 advanceRound(r,120);
 catchHider(r,'ai-1');finishRound(r);
 assert.equal(r.result.entries.find(x=>x.id==='ai-1').score,0);
 assert.equal(r.result.entries.find(x=>x.id==='ai-2').score,2);
});
test('clock modifier cannot alter early capture bonus threshold',()=>{
 const r=setupRound({totalPlayers:4});
 advanceRound(r,120);
 assert.equal(bonusThresholdSeconds(4),300);
 assert.equal(adjustChaseDuration(r,-60),true);
 assert.equal(r.chaseDuration,360);
 assert.equal(r.bonusThreshold,300);
 advanceRound(r,301);
 for(const id of ['ai-1','ai-2','ai-3'])catchHider(r,id);
 assert.equal(r.result.bonus,0);
});
test('time transitions preserve overshoot and new round clears state',()=>{
 const r=setupRound({totalPlayers:2,playerRole:'hider'});
 advanceRound(r,121);
 assert.equal(r.phase,'chase');
 assert.equal(r.chaseElapsed,1);
 const fresh=setupRound({totalPlayers:2,playerRole:'hider'});
 assert.equal(fresh.phase,'prepare');
 assert.equal(fresh.players.some(x=>x.caught),false);
 assert.equal(fresh.result,null);
});
