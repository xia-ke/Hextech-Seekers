import {HEROES, setupRound, advanceRound, catchHider, finishRound, adjustChaseDuration} from './match.js';
import {createActors,chooseAiDestination,stepActor} from './encounter.js';
import {makeVision} from './vision-baseline.js';
const root=document.getElementById('round-lab');
if(root){
  const heroes=HEROES.map(h=>'<option value="'+h+'">'+h+'</option>').join('');
  root.innerHTML=`
    <button id="round-toggle" class="quiet" type="button" aria-expanded="false">对局规则预演</button>
    <section id="round-panel" class="round-panel" hidden aria-label="第二阶段对局规则预演">
      <h2>单局规则预演 <small>ISSUE 002 · 尚非完整对局</small></h2>
      <p class="round-notice">在真实峡谷探索画面旁验证阵营、计时和积分；抓捕按钮仅是手动模拟事件，不会命中游戏角色。正式抓捕和完整原版视野尚未接入；本页可运行导航与基础视线诊断。</p>
      <div class="round-form">
        <label>总人数<select id="round-count"><option value="2">2 人</option><option value="3">3 人</option><option value="4">4 人</option></select></label>
        <label>玩家阵营<select id="round-role"><option value="hunter">追捕者</option><option value="hider">躲藏者</option></select></label>
        <label>玩家英雄<select id="round-hero">${heroes}</select></label>
      </div>
      <button id="round-start" class="round-primary" type="button">创建预演对局</button>
      <div id="round-status" role="status" aria-live="polite"></div>
      <div id="round-actions" hidden>
        <div class="round-buttons">
          <button id="round-step" type="button">模拟经过 30 秒</button>
          <button id="round-skip" type="button">模拟至阶段结束</button>
          <button id="round-shorten" type="button">模拟时光发条 −1 分钟</button>
          <button id="round-extend" type="button">模拟时光发条 +1 分钟</button>
        </div>
        <div id="round-players"></div>
        <div class="round-ai" id="round-ai"><p>实际峡谷导航与独立视线 AI 演练：需要峡谷加载完成；不包含完整原版视野、攻击或角色模型渲染。</p><button type="button" id="round-nav-init">初始化导航角色</button><button type="button" id="round-nav-step" disabled>模拟移动 5 秒</button><pre id="round-nav-status">尚未初始化</pre></div>
        <div id="round-result"></div>
        <div class="round-ai">
          <p>实时集成演练：使用原峡谷盖伦操控、真实经过时间、AI 导航及独立视线。其他角色只显示视野内诊断圈，不是英雄模型；准备阶段追捕者不能移动。</p>
          <button type="button" id="round-live-start">启动实时演练（盖伦）</button>
          <button type="button" id="round-live-stop" disabled>停止实时演练</button>
          <pre id="round-live-status">尚未启动</pre>
        </div>
      </div>
    </section>`;
  const el=id=>document.getElementById(id);let state=null,clockUsed=false,worldActors=null,vision=null;
  const toggle=el('round-toggle'),panel=el('round-panel');
  toggle.onclick=()=>{panel.hidden=!panel.hidden;toggle.setAttribute('aria-expanded',String(!panel.hidden));};
  const time=seconds=>Math.floor(seconds/60)+':'+String(Math.floor(seconds%60)).padStart(2,'0');
  const redraw=()=>{
    if(!state)return;
    el('round-actions').hidden=false;
    el('round-status').textContent=state.phase==='prepare'?'躲藏准备：'+time(state.remaining)+' · 追捕者被限制在泉水':
      state.phase==='chase'?'追捕进行：剩余 '+time(state.remaining)+' · 提前全抓奖励门槛 '+time(state.bonusThreshold):
      '本局结束 · 积分结算';
    el('round-players').innerHTML=state.players.map(p=>'<div class="round-player"><span>'+p.hero+' · '+(p.role==='hunter'?'追捕者':'躲藏者')+(p.human?'（玩家）':'（AI 占位）')+'</span>'+
      (p.role==='hider'&&state.phase==='chase'&&!p.caught?'<button data-catch="'+p.id+'" type="button">模拟抓获</button>':'<small>'+(p.caught?'已抓获':'未抓获')+'</small>')+'</div>').join('');
    el('round-players').querySelectorAll('[data-catch]').forEach(b=>b.onclick=()=>{if(catchHider(state,b.dataset.catch)&&worldActors){const a=worldActors.get(b.dataset.catch);if(a){a.active=false;a.route=[];}}redraw();if(worldActors)showPositions();});
    el('round-result').innerHTML=state.result?'<h3>单局积分排名</h3>'+
      state.result.entries.map(p=>'<div class="round-player"><b>第 '+p.rank+' 名</b><span>'+p.hero+' · '+p.score+' 分</span></div>').join('')+
      '<p>全抓奖励：'+state.result.bonus+' 分；重新创建即可开始新局。</p>':'';
    el('round-step').disabled=state.phase==='finished';
    el('round-nav-step').disabled=!worldActors||state.phase==='finished';
    el('round-skip').disabled=state.phase==='finished';
    el('round-shorten').disabled=state.phase!=='chase'||clockUsed;
    el('round-extend').disabled=state.phase!=='chase'||clockUsed;
  };
  el('round-start').onclick=()=>{
    state=setupRound({totalPlayers:Number(el('round-count').value),playerRole:el('round-role').value,playerHero:el('round-hero').value});
    clockUsed=false;worldActors=null;vision=null;el('round-nav-step').disabled=true;el('round-nav-status').textContent='尚未初始化';redraw();
  };
  el('round-nav-init').onclick=()=>{
    if(!state){el('round-nav-status').textContent='请先创建规则预演对局';return;}
    const nav=window.__hextechNavigation;
    if(!nav){el('round-nav-status').textContent='峡谷尚未加载完成，暂无法使用原始导航网格';return;}
    try{
      const start=nav.playerStart();
      // Dedicated diagnostic positions; not the actual champion placements or models.
      worldActors=createActors(state,nav,p=>{
        if(p.human)return start;
        const i=Number(p.id.slice(3));
        return {x:start.x+(i%2?650:-650),z:start.z+(i*250)};
      });
      vision=makeVision(nav);
      el('round-nav-step').disabled=false;showPositions();
    }catch(error){worldActors=null;vision=null;el('round-nav-status').textContent='导航初始化失败：'+error.message;}
  };
  function showPositions(){
    if(!worldActors)return;
    el('round-nav-status').textContent=[...worldActors.values()].map(a=>{
      const p=state.players.find(p=>p.id===a.id);
      return p.hero+' · '+(a.active?'可行动':'已淘汰')+' · X '+Math.round(a.x)+' / Z '+Math.round(a.z)+' · 路径点 '+a.route.length+' · 视野内敌人 '+(vision?vision.visibleOpponents(a,worldActors).map(v=>state.players.find(p=>p.id===v.id)?.hero).join('、')||'无':'未启用');
    }).join('\n');
  }
  el('round-nav-step').onclick=()=>{
    if(!worldActors||!state||state.phase==='finished')return;
    const nav=window.__hextechNavigation;
    // Independent grid-based vision is calculated from each AI actor's viewpoint.
    const start=nav.playerStart();
    const destinations=[{x:start.x+600,z:start.z+600},{x:start.x-600,z:start.z+300},{x:start.x+200,z:start.z+800}];
    for(let step=0;step<20 && state.phase!=='finished';step++){
      advanceRound(state,.25);
      for(const a of worldActors.values()){
        if(a.id==='player')continue;
        const index=Number(a.id.slice(3))-1;
        chooseAiDestination(a,worldActors,nav,state,{
          visible:(viewer,target)=>vision.canSee(viewer,target),
          patrol:()=>{const p=destinations[index%destinations.length];return nav.nearest(p.x,p.z,20);}
        });
        stepActor(a,nav,.25,state);
      }
    }
    redraw();showPositions();
  };
  el('round-live-start').onclick=()=>{
    const api=window.__hextechLive;
    if(!api){el('round-live-status').textContent='峡谷尚未加载完成';return;}
    if(el('round-hero').value!=='盖伦'){el('round-live-status').textContent='当前只有盖伦完成正式动作资源；请选择盖伦';return;}
    try{
      const snapshot=api.start({
        totalPlayers:Number(el('round-count').value),
        playerRole:el('round-role').value,playerHero:'盖伦'
      });
      el('round-live-start').disabled=true;el('round-live-stop').disabled=false;
      el('round-live-status').textContent='运行中：'+snapshot.phase+'。右键移动、A 普攻；仅可攻击视线内的诊断目标。';
    }catch(error){el('round-live-status').textContent='无法启动：'+error.message;}
  };
  el('round-live-stop').onclick=()=>{
    window.__hextechLive?.stop();el('round-live-start').disabled=false;
    el('round-live-stop').disabled=true;el('round-live-status').textContent='已退出实时演练';
  };
  window.addEventListener('hextech:live-update',event=>{
    const view=event.detail;
    el('round-live-status').textContent=
      '阶段：'+(view.phase==='prepare'?'准备':view.phase==='chase'?'追捕':'结算')+
      ' · 剩余 '+time(view.remaining)+' · '+(view.playerActive?'可行动':'已被抓获')+
      ' · 本人视野内敌人：'+(view.visible.map(x=>x.hero).join('、')||'无')+
      (view.result?'\n积分：'+view.result.entries.map(p=>p.hero+' '+p.score+'分').join(' / '):'');
    if(view.phase==='finished'){
      el('round-live-stop').disabled=false;
    }
  });
  el('round-step').onclick={if(state){advanceRound(state,30);redraw();}};
  el('round-skip').onclick=()=>{if(state){advanceRound(state,state.remaining);redraw();}};
  for(const [id,amount] of [['round-shorten',-60],['round-extend',60]]){
    el(id).onclick=()=>{if(state&&!clockUsed&&adjustChaseDuration(state,amount)){clockUsed=true;redraw();}};
  }
}
