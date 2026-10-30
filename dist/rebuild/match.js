// Issue 002: deterministic single-round rules. No rendering dependencies.
export const HEROES = Object.freeze(['盖伦','布里茨','锐雯','亚索']);
export const PREP_SECONDS = 120;
export function durationSeconds(totalPlayers) {
  checkCount(totalPlayers);
  return (3 + 2 * (totalPlayers - 2)) * 60;
}
export function bonusThresholdSeconds(totalPlayers) {
  checkCount(totalPlayers);
  return (3 + 2 * (totalPlayers - 3)) * 60;
}
function checkCount(count) {
  if (!Number.isInteger(count) || count < 2 || count > 4) throw new RangeError('总人数仅允许 2–4');
}
export function setupRound({totalPlayers=2,playerRole='hunter',playerHero='盖伦'}={}) {
  checkCount(totalPlayers);
  if (!['hunter','hider'].includes(playerRole)) throw new Error('无效阵营');
  if (!HEROES.includes(playerHero)) throw new Error('无效英雄');
  const others = HEROES.filter(hero=>hero!==playerHero);
  const players = [{id:'player',hero:playerHero,role:playerRole,human:true,caught:false}];
  for (let i=1;i<totalPlayers;i++) {
    players.push({id:'ai-'+i,hero:others[i-1],role:playerRole==='hunter'?'hider':i===1?'hunter':'hider',human:false,caught:false});
  }
  const hunters=players.filter(p=>p.role==='hunter');
  if(hunters.length!==1 || new Set(players.map(p=>p.hero)).size!==totalPlayers)throw new Error('阵营或英雄重复');
  return {players,phase:'prepare',elapsed:0,remaining:PREP_SECONDS,
    chaseElapsed:0,chaseDuration:durationSeconds(totalPlayers),
    bonusThreshold:bonusThresholdSeconds(totalPlayers),result:null};
}
export function catchHider(state,id) {
  if(state.phase!=='chase')return false;
  const target=state.players.find(p=>p.id===id);
  if(!target || target.role!=='hider' || target.caught)return false;
  target.caught=true;
  if(state.players.every(p=>p.role==='hunter'||p.caught))finishRound(state);
  return true;
}
export function advanceRound(state,seconds) {
  if(!Number.isFinite(seconds)||seconds<0)throw new RangeError('时间增量无效');
  if(state.phase==='finished')return state;
  let remaining=seconds;
  while(remaining>0 && state.phase!=='finished') {
    const chunk=Math.min(remaining,state.remaining);
    state.elapsed+=chunk;
    if(state.phase==='chase')state.chaseElapsed+=chunk;
    state.remaining=Math.max(0,state.remaining-chunk);
    remaining-=chunk;
    if(state.remaining<=1e-9) {
      if(state.phase==='prepare'){state.phase='chase';state.remaining=state.chaseDuration;}
      else finishRound(state);
    }
  }
  return state;
}
export function adjustChaseDuration(state,deltaSeconds) {
  if(state.phase!=='chase'||![-60,60].includes(deltaSeconds))return false;
  state.chaseDuration=Math.max(0,state.chaseDuration+deltaSeconds);
  state.remaining=Math.max(0,state.chaseDuration-state.chaseElapsed);
  if(state.remaining===0)finishRound(state);
  return true;
}
export function finishRound(state) {
  if(state.phase==='finished')return state.result;
  const hiders=state.players.filter(p=>p.role==='hider'),X=hiders.length;
  const survivors=hiders.filter(p=>!p.caught).length;
  const allCaught=survivors===0;
  const bonus=allCaught && state.chaseElapsed<=state.bonusThreshold ? 1:0;
  const entries=state.players.map(p=>{
    let score;
    if(p.role==='hunter')score=X-survivors+bonus;
    else if(survivors===0)score=0;
    else if(survivors===1)score=p.caught?1:Math.ceil(X/2);
    else score=p.caught?0:survivors;
    return {id:p.id,hero:p.hero,role:p.role,caught:p.caught,score};
  }).sort((a,b)=>b.score-a.score);
  let rank=0,last=null;
  entries.forEach((entry,i)=>{if(entry.score!==last){rank=i+1;last=entry.score;}entry.rank=rank;});
  state.phase='finished';state.remaining=0;
  state.result={entries,survivors,bonus,elapsed:state.chaseElapsed};
  return state.result;
}
