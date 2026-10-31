// Issue 005/006: single-clock Rift simulation with original champion assets.
// Vision remains a conservative baseline; attack contact timing is explicitly provisional.
import {setupRound,advanceRound} from './match.js';
import {createActors,chooseAiDestination,stepActor,meleeHit,CAPTURE_RANGE} from './encounter.js';
import {makeVision} from './vision-baseline.js';

export function createLiveSession(nav,{totalPlayers=2,playerRole='hunter',playerHero='盖伦',start,spawns,visionRange}={}) {
  // All four roster heroes share the same round rules; original visual assets are checked by the UI before starting.
  if(!start || !nav?.canWalk(start.x,start.z))throw new Error('Valid player start required');
  const match=setupRound({totalPlayers,playerRole,playerHero});
  const vision=makeVision(nav,visionRange?{range:visionRange}:{});
  const actors=createActors(match,nav,p=>{
    if(p.human)return start;
    if(typeof spawns==='function')return spawns(p,start);
    const index=Number(p.id.slice(3));
    const offsets=[[600,400],[-550,650],[850,-200]][index-1]||[500,500];
    return {x:start.x+offsets[0],z:start.z+offsets[1]};
  });
  const player=actors.get('player');
  let aiDecisionAccumulator=0,aiAttackAccumulator=0,lastPlayerCaptureAt=-Infinity;
  let nextPlayerAttackAt=-Infinity,attackSequence=0,attackState=null;
  function syncPlayer(position,facing) {
    if(!player.active||match.phase==='finished')return false;
    if(match.phase==='prepare'&&player.role==='hunter')return false;
    if(!position||!nav.canWalk(position.x,position.z))return false;
    player.x=position.x;player.z=position.z;
    const length=Math.hypot(facing?.x||0,facing?.z||0);
    if(length>0.001)player.facing={x:facing.x/length,z:facing.z/length};
    return true;
  }
  function visibleToPlayer() {
    if(!player.active)return [];
    return vision.visibleOpponents(player,actors).map(a=>({
      id:a.id,hero:match.players.find(p=>p.id===a.id).hero,x:a.x,z:a.z
    }));
  }
  function playerAttack({hitbox=null}={}) {
    if(match.phase!=='chase'||!player.active||player.role!=='hunter')
      return {ok:false,reason:'not-hunter-or-not-chasing'};
    const targets=vision.visibleOpponents(player,actors)
      .sort((a,b)=>Math.hypot(a.x-player.x,a.z-player.z)-Math.hypot(b.x-player.x,b.z-player.z));
    if(!targets.length)return {ok:false,reason:'no-visible-target-in-range'};
    if(match.elapsed-lastPlayerCaptureAt<0.8)return {ok:false,reason:'attack-cooldown'};
    let firstFailure={ok:false,reason:'no-visible-target-in-range'};
    for(const target of targets){
      const hit=meleeHit(player,target,match,{visible:vision.canSee,clearPath:vision.hasLineOfSight,hitbox});
      if(hit.ok){lastPlayerCaptureAt=match.elapsed;return hit;}
      if(firstFailure.reason==='no-visible-target-in-range')firstFailure=hit;
    }
    return firstFailure;
  }
  function beginPlayerAttack({duration=.7,hitAt=.35,hitbox=null}={}) {
    if(match.phase!=='chase'||!player.active||player.role!=='hunter')
      return {ok:false,reason:'not-hunter-or-not-chasing'};
    if(attackState&&!attackState.finished)return {ok:false,reason:'attack-in-progress'};
    if(match.elapsed<nextPlayerAttackAt)return {ok:false,reason:'attack-cooldown'};
    if(!Number.isFinite(duration)||!Number.isFinite(hitAt)||duration<=0||hitAt<0||hitAt>duration)
      throw new RangeError('Invalid attack timing');
    nextPlayerAttackAt=match.elapsed+.8;
    if(hitbox&&(!Number.isFinite(hitbox.range)||hitbox.range<=0||!Number.isFinite(hitbox.halfWidth)||hitbox.halfWidth<=0))throw new RangeError('Invalid attack hitbox');
    attackState={id:++attackSequence,elapsed:0,duration,hitAt,hitbox:hitbox?{...hitbox}:null,resolved:false,finished:false,result:null};
    return {ok:true,reason:'started',attackId:attackState.id,duration,hitAt};
  }
  function tick(seconds,{freezeAI=false}={}) {
    if(!Number.isFinite(seconds)||seconds<0)throw new RangeError('Invalid delta');
    if(match.phase==='finished')return snapshot();
    // Bounded substeps prevent crossing both a phase boundary and an AI attack
    // without a matching position / visibility update.
    let remaining=Math.min(seconds,1);
    while(remaining>1e-9 && match.phase!=='finished') {
      const dt=Math.min(.05,remaining,match.remaining);
      if(dt<=0)break;
      advanceRound(match,dt);
      remaining-=dt;
      if(attackState&&!attackState.finished){
        const before=attackState.elapsed;
        attackState.elapsed=Math.min(attackState.duration,attackState.elapsed+dt);
        if(!attackState.resolved&&before<attackState.hitAt&&attackState.elapsed>=attackState.hitAt){
          attackState.result=playerAttack({hitbox:attackState.hitbox});
          attackState.resolved=true;
        }
        if(attackState.elapsed>=attackState.duration)attackState.finished=true;
      }
      if(match.phase==='finished')break;
      aiDecisionAccumulator+=dt;aiAttackAccumulator+=dt;
      if(!freezeAI&&aiDecisionAccumulator>=.5){
        aiDecisionAccumulator=0;
        for(const a of actors.values()){
          if(a.id==='player'||!a.active)continue;
          const origin=a.spawn;
          const phase=Math.floor(match.elapsed/8)+(Number(a.id.slice(3))||0);
          const offsets=[[350,0],[0,350],[-350,0],[0,-350]];
          const [dx,dz]=offsets[phase%offsets.length];
          chooseAiDestination(a,actors,nav,match,{
            visible:vision.canSee,
            patrol:()=>nav.nearest(origin.x+dx,origin.z+dz,10)
          });
        }
      }
      if(!freezeAI)for(const a of actors.values()){
        if(a.id!=='player'&&a.active)stepActor(a,nav,dt,match);
      }
      if(!freezeAI&&aiAttackAccumulator>=.25&&match.phase==='chase') {
        aiAttackAccumulator=0;
        for(const a of actors.values()){
          if(!a.active||a.id==='player'||a.role!=='hunter')continue;
          const targets=vision.visibleOpponents(a,actors).filter(t=>t.role==='hider');
          for(const target of targets){
            // An AI attack has its own facing requirement, not a magical capture.
            const dx=target.x-a.x,dz=target.z-a.z,d=Math.hypot(dx,dz);
            if(d>CAPTURE_RANGE||d<0.001)continue;
            a.facing={x:dx/d,z:dz/d};
            const hit=meleeHit(a,target,match,{visible:vision.canSee,clearPath:vision.hasLineOfSight});
            if(hit.ok)break;
          }
        }
      }
    }
    return snapshot();
  }
  function snapshot(){
    return {phase:match.phase,remaining:match.remaining,elapsed:match.elapsed,
      playerActive:player.active,playerRole:player.role,
      visible:visibleToPlayer(),
      result:match.result,
      attack:attackState?{...attackState}:null,
      players:match.players.map(p=>({id:p.id,hero:p.hero,role:p.role,caught:p.caught}))};
  }
  return {match,actors,vision,tick,syncPlayer,playerAttack,beginPlayerAttack,snapshot};
}
