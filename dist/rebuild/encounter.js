// Issue 003: world-space actor movement and melee capture. Independent of rendering.
import { catchHider } from './match.js';

export const MOVE_SPEED = 340; // Same provisional movement speed as current Garen explorer.
export const CAPTURE_RANGE = 180; // Provisional range pending LoL-scale validation.
export const CAPTURE_ARC_COS = Math.cos(Math.PI * 0.42); // ~151 degree forward cone.

export function createActors(match, nav, spawn) {
  if (!match?.players || !nav?.nearest || !nav?.path) throw new Error('Match and navigation required');
  const actors = new Map();
  for (const player of match.players) {
    const requested = spawn(player);
    const p = requested && nav.nearest(requested.x, requested.z, 12);
    if (!p || !nav.canWalk(p.x,p.z)) throw new Error('Invalid spawn: '+player.id);
    actors.set(player.id,{id:player.id,role:player.role,x:p.x,z:p.z,
      facing:{x:0,z:1},speed:MOVE_SPEED,route:[],spawn:{...p},
      active:true,routeRevision:0});
  }
  return actors;
}
export function setDestination(actor, nav, destination, match) {
  if (!actor?.active || !destination || !nav.canWalk(destination.x,destination.z)) return false;
  if(match && (match.phase==='finished' || match.phase==='prepare'&&actor.role==='hunter'))return false;
  const route=nav.path(actor,destination);
  if (!route?.length)return false;
  actor.route=route.map(p=>({x:p.x,z:p.z}));actor.routeRevision++;
  return true;
}
export function stepActor(actor, nav, seconds, match) {
  if (!Number.isFinite(seconds)||seconds<0)throw new RangeError('Invalid step');
  if(!actor.active || match?.phase==='finished' || (match?.phase==='prepare'&&actor.role==='hunter'))return;
  let budget=actor.speed*Math.min(seconds,0.25);
  while(actor.route.length && budget>0){
    const target=actor.route[0],dx=target.x-actor.x,dz=target.z-actor.z,dist=Math.hypot(dx,dz);
    if(dist<0.001){actor.route.shift();continue;}
    const ux=dx/dist,uz=dz/dist;
    actor.facing={x:ux,z:uz};
    const distance=Math.min(dist,budget);
    const nx=actor.x+ux*distance,nz=actor.z+uz*distance;
    // Stop instead of moving through a newly invalid nav cell.
    if(!nav.canWalk(nx,nz)){actor.route=[];break;}
    actor.x=nx;actor.z=nz;budget-=distance;
    if(distance>=dist-0.001)actor.route.shift();
  }
}
export function meleeHit(attacker,target,match,{visible,clearPath,range=CAPTURE_RANGE,hitbox=null}={}) {
  if(match.phase!=='chase'||!attacker?.active||!target?.active)return {ok:false,reason:'inactive'};
  if(attacker.role!=='hunter'||target.role!=='hider')return {ok:false,reason:'wrong-role'};
  if(typeof visible!=='function'||!visible(attacker,target))return {ok:false,reason:'not-visible'};
  const dx=target.x-attacker.x,dz=target.z-attacker.z,dist=Math.hypot(dx,dz);
  if(!Number.isFinite(dist))return {ok:false,reason:'invalid-position'};
  if(hitbox){
    const reach=Number(hitbox.range),halfWidth=Number(hitbox.halfWidth);
    if(!Number.isFinite(reach)||reach<=0||!Number.isFinite(halfWidth)||halfWidth<=0)
      throw new RangeError('Invalid melee hitbox');
    const facingLength=Math.hypot(attacker.facing?.x||0,attacker.facing?.z||0);
    if(facingLength<1e-6)return {ok:false,reason:'outside-facing'};
    const fx=attacker.facing.x/facingLength,fz=attacker.facing.z/facingLength;
    // Front-facing capsule: flat-sided middle with a rounded far tip.
    // Keep a hard front hemisphere guard so a target behind the attacker
    // cannot be caught by the capsule's rounded base.
    const along=dx*fx+dz*fz;
    if(along<0)return {ok:false,reason:'outside-facing'};
    if(along>reach+halfWidth)return {ok:false,reason:'out-of-range'};
    const lateral=Math.abs(dx*fz-dz*fx);
    const tipOverhang=Math.max(0,along-reach);
    if(Math.hypot(lateral,tipOverhang)>halfWidth)
      return {ok:false,reason:'outside-hitbox'};
  }else{
    if(dist>range)return {ok:false,reason:'out-of-range'};
    if(dist>0.001 && (dx*attacker.facing.x+dz*attacker.facing.z)/dist<CAPTURE_ARC_COS)return {ok:false,reason:'outside-facing'};
  }
  if(typeof clearPath!=='function'||!clearPath(attacker,target))return {ok:false,reason:'obstructed'};
  if(!catchHider(match,target.id))return {ok:false,reason:'already-caught'};
  target.active=false;target.route=[];
  return {ok:true,reason:'captured',target:target.id};
}
// AI pathing only receives knowledge explicitly supplied by a visibility callback.
// No vision implementation => no target acquisition or omniscient pursuit.
export function chooseAiDestination(actor, allActors, nav, match, {visible,patrol}={}) {
  if(actor.active===false||match.phase==='finished')return false;
  if(match.phase==='prepare'&&actor.role==='hunter')return false;
  if(actor.route.length)return false;
  const enemies=[...allActors.values()].filter(other=>other.active && other.role!==actor.role);
  const seen=typeof visible==='function'?enemies.filter(enemy=>visible(actor,enemy)):[];
  let destination=null;
  if(actor.role==='hunter'&&seen.length){
    destination=seen.reduce((a,b)=>Math.hypot(a.x-actor.x,a.z-actor.z)<Math.hypot(b.x-actor.x,b.z-actor.z)?a:b);
  }else if(actor.role==='hider'&&seen.length){
    const threat=seen[0],dx=actor.x-threat.x,dz=actor.z-threat.z;
    const length=Math.hypot(dx,dz)||1;
    destination=nav.nearest(actor.x+dx/length*750,actor.z+dz/length*750,8);
  }else if(typeof patrol==='function')destination=patrol(actor);
  return destination?setDestination(actor,nav,destination,match):false;
}
