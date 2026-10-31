import { NavGrid } from './navigation.js';
import {createLiveSession} from './live-session.js';
import {ChampionAtlasRenderer,renderableOpponents} from './champion-renderer.js';
import { project, unproject, viewDepth, decodeDepth, directionIndex, SIN, COS } from './projection.js';

const $ = id => document.getElementById(id);
const canvas = $('world'), ctx = canvas.getContext('2d', {alpha:false});
const mini = $('minimap'), mctx = mini.getContext('2d');
const mapBase = 'assets/visual/summoners-rift/', heroBase = 'assets/visual/garen/';
const locations = {red:{x:7765,z:-4020},river:{x:9838,z:-4398},blue:{x:11032,z:-6991}};
let map, nav, sprite, overview, minimap, tiles, actor,liveSession=null,liveLastPhase='',liveNotice=0,championRenderer=null,playerHero='盖伦';
let width=0,height=0,dpr=1,zoom=.8,follow=true,ready=false,last=0,animTime=0,attackAnimUntil=0,lastAttackNotice=0;
let camera={x:0,y:0},path=[],heading=Math.PI,marker=null,toastEnd=0,drag=null;
let fpsFrames=0,fpsTime=0,uiTime=0,miniTime=0,previousFrame=0;
const metrics={rafGaps:[],drawCosts:[],mapCosts:[],actorCosts:[],longGaps:0};
function sample(list,value){list.push(value);if(list.length>120)list.shift();}
function percentile(list,p){if(!list.length)return 0;const sorted=[...list].sort((a,b)=>a-b);return sorted[Math.min(sorted.length-1,Math.floor((sorted.length-1)*p))];}
const atlases = new Map(), frameCache = new Map();
const work=document.createElement('canvas');work.width=work.height=256;
const wctx=work.getContext('2d',{willReadFrequently:true});
const masked=document.createElement('canvas');masked.width=masked.height=256;
const maskctx=masked.getContext('2d');
const maskedPixels=maskctx.createImageData(256,256);
let maskKey='',maskX=NaN,maskZ=NaN,maskedTileVersion=-1;

async function json(url){const r=await fetch(url);if(!r.ok)throw new Error('资源读取失败：'+url);return r.json();}
async function binary(url){const r=await fetch(url);if(!r.ok)throw new Error('资源读取失败：'+url);return r.arrayBuffer();}
function loadImage(url){return new Promise((resolve,reject)=>{const img=new Image();img.onload=()=>resolve(img);img.onerror=()=>reject(new Error('图像读取失败：'+url));img.src=url;});}
function progress(text,value){$('load-status').textContent=text;$('load-progress').style.width=value+'%';}
function toast(text){$('toast').textContent=text;$('toast').classList.add('show');toastEnd=performance.now()+2200;}
function setFollow(value){follow=value;$('follow').setAttribute('aria-pressed',String(value));}
function recenter(){if(!ready)return;setFollow(true);camera=project(map,actor);paint();}
function stop(){const attackActive=!!liveSession&&liveSession.snapshot().attack?.finished===false;path=[];if(!attackActive){attackAnimUntil=0;animTime=0;}updateUI();paint();}
function attack(){
 if(!ready)return;
 const heroSprite=liveSession?championRenderer?.resources.get(playerHero)?.meta:sprite;
 if(liveSession&&!heroSprite){toast('所选英雄原始攻击资源未就绪');return;}
 const clip=heroSprite.clips.attack1,duration=clip.duration;
 path=[];animTime=0;attackAnimUntil=duration;
 if(liveSession){
   // The contact point is currently a provisional midpoint of the original
   // attack clip; per-hero hit-frame calibration remains a visual task.
   const result=liveSession.beginPlayerAttack({duration,hitAt:duration*(7/12)});
   if(!result.ok){attackAnimUntil=0;toast('实时演练：'+result.reason);return;}
   toast('普攻动作开始 · 命中帧待校准');
 }else toast('攻击动作预览 · 当前无伤害或抓捕判定');
 updateUI();paint();
}
function updateHeroHud(hero){
  const display=document.querySelector('.champion strong');
  const portrait=document.querySelector('.portrait img');
  const bottom=document.querySelector('.bottom');
  if(display)display.textContent=hero;
  if(bottom)bottom.setAttribute('aria-label',hero+'操作面板');
  const label=$('unit-label');
  if(label)label.childNodes[0].textContent=hero;
  if(portrait){
    portrait.alt=hero;
    if(hero==='盖伦')portrait.src='assets/Garen.png';
    else{
      const atlas=championRenderer?.resources.get(hero)?.atlases.get('idle1')?.image;
      if(atlas){
        const probe=document.createElement('canvas');probe.width=probe.height=256;
        const pctx=probe.getContext('2d',{willReadFrequently:true});
        pctx.drawImage(atlas,0,0,256,256,0,0,256,256);
        const pixels=pctx.getImageData(0,0,256,256).data;
        let minX=256,minY=256,maxX=-1,maxY=-1;
        for(let y=0;y<256;y++)for(let x=0;x<256;x++)if(pixels[(y*256+x)*4+3]>12){
          minX=Math.min(minX,x);minY=Math.min(minY,y);maxX=Math.max(maxX,x);maxY=Math.max(maxY,y);
        }
        const preview=document.createElement('canvas');preview.width=preview.height=256;
        const out=preview.getContext('2d');
        if(maxX>=minX&&maxY>=minY){
          const bw=maxX-minX+1,bh=maxY-minY+1,scale=Math.min(216/bw,224/bh);
          const w=bw*scale,h=bh*scale;
          out.drawImage(atlas,minX,minY,bw,bh,(256-w)/2,(256-h)/2,w,h);
        }else out.drawImage(atlas,0,0,256,256,0,0,256,256);
        portrait.src=preview.toDataURL('image/png');
      }
    }
  }
  const attackButton=$('attack');
  if(attackButton)attackButton.setAttribute('aria-label',hero+'原始普攻动作（A）；追捕阶段在有效命中帧抓获躲藏者');
}
function screen(p){return{x:(p.x-camera.x)*zoom+width/2,y:(p.y-camera.y)*zoom+height*.47};}
function imagePoint(x,y){return{x:(x-width/2)/zoom+camera.x,y:(y-height*.47)/zoom+camera.y};}
function cameraBounds(){camera.x=Math.max(0,Math.min(map.width,camera.x));camera.y=Math.max(0,Math.min(map.height,camera.y));}

class TileCache {
  constructor(meta){this.meta=meta;this.entries=new Map();this.allowed=new Set(meta.tiles.map(p=>p.join('-')));this.version=0;this.failed=false;}
  async load(col,row){
    const key=col+'-'+row;if(!this.allowed.has(key))return null;
    const prior=this.entries.get(key);if(prior){prior.used=performance.now();return prior.promise;}
    const entry={col,row,used:performance.now()};this.entries.set(key,entry);
    entry.promise=Promise.all([loadImage(mapBase+key+'.webp'),loadImage(mapBase+key+'-depth.png')]).then(([img,depth])=>{
      const c=document.createElement('canvas');c.width=c.height=this.meta.tileSize;
      const cx=c.getContext('2d',{willReadFrequently:true});cx.drawImage(depth,0,0);
      const pixels=cx.getImageData(0,0,c.width,c.height).data;
      entry.depth=new Uint16Array(c.width*c.height);entry.depth.fill(65535);
      for(let i=0;i<entry.depth.length;i++)if(pixels[i*4+3])entry.depth[i]=pixels[i*4]<<8|pixels[i*4+1];
      entry.image=img;this.version++;return entry;
    }).catch(error=>{entry.error=error;if(!this.failed){this.failed=true;toast('部分场景资源未能加载，请刷新页面重试');}throw error;});
    return entry.promise;
  }
  depthAt(x,y){
    const size=this.meta.tileSize,col=Math.floor(x/size),row=Math.floor(y/size),e=this.entries.get(col+'-'+row);
    if(!e?.depth)return null;
    const value=e.depth[(Math.floor(y)-row*size)*size+Math.floor(x)-col*size];
    return value===65535?null:value/4-12000;
  }
  visible(){
    const a=imagePoint(0,0),b=imagePoint(width,height),s=this.meta.tileSize,visible=[];
    for(let row=Math.floor(a.y/s);row<=Math.floor(b.y/s);row++)for(let col=Math.floor(a.x/s);col<=Math.floor(b.x/s);col++){
      if(!this.allowed.has(col+'-'+row))continue;
      this.load(col,row).catch(()=>{});visible.push([col,row]);
    }
    return visible;
  }
  trim(visible){
    if(this.entries.size<22)return;
    const keep=new Set(visible.map(v=>v.join('-')));
    const oldest=[...this.entries.entries()].filter(([k,e])=>e.image&&!keep.has(k)).sort((a,b)=>a[1].used-b[1].used);
    while(this.entries.size>20&&oldest.length)this.entries.delete(oldest.shift()[0]);
  }
}

function frame(clip,direction,index){
  const key=clip+':'+direction+':'+index;if(frameCache.has(key))return frameCache.get(key);
  const atlas=atlases.get(clip),size=sprite.tileSize;
  wctx.clearRect(0,0,size,size);wctx.drawImage(atlas.image,direction*size,index*size,size,size,0,0,size,size);
  const rgba=wctx.getImageData(0,0,size,size).data;
  wctx.clearRect(0,0,size,size);wctx.drawImage(atlas.depth,direction*size,index*size,size,size,0,0,size,size);
  const depth=wctx.getImageData(0,0,size,size).data;
  // Precompute opaque samples once per animation frame, not every scene redraw.
  const opaque=[];
  for(let i=0;i<rgba.length;i+=4)if(rgba[i+3])opaque.push(i);
  const result={key,rgba,depth,opaque};frameCache.set(key,result);
  if(frameCache.size>32)frameCache.delete(frameCache.keys().next().value);return result;
}

function drawActor(now){
  if(liveSession && playerHero!=='盖伦'){
    const entity=liveSession.actors.get('player');
    if(entity?.active && championRenderer?.resources.has(playerHero)){
      championRenderer.draw({...entity,x:actor.x,z:actor.z,facing:{x:Math.sin(heading),z:Math.cos(heading)},route:path},playerHero,{zoom,animTime,attack:animTime<attackAnimUntil});
      $('unit-label').hidden=true;
    }
    return;
  }
  const clip=animTime<attackAnimUntil?'attack1':path.length?'run':'idle1',info=sprite.clips[clip];
  const index=Math.min(info.frames-1,Math.floor((animTime%info.duration)/info.duration*info.frames));
  const direction=directionIndex(heading,sprite.directions),f=frame(clip,direction,index);
  const point=project(map,actor),p=screen(point),ratio=map.ppu/sprite.ppu,scale=ratio*zoom;
  if(p.x<-200||p.x>width+200||p.y<-200||p.y>height+200){$('unit-label').hidden=true;return;}
  // Compare original world depth per pixel; trees and walls can cover the hero.
  if(maskKey!==f.key||maskX!==actor.x||maskZ!==actor.z||maskedTileVersion!==tiles.version){
    const dst=maskedPixels.data;dst.set(f.rgba);const baseDepth=viewDepth(actor),size=sprite.tileSize;
    for(const i of f.opaque){
      const pixel=i>>2,x=pixel%size,y=(pixel/size)|0;
      const terrain=tiles.depthAt(point.x+(x-sprite.pivot[0])*ratio,point.y+(y-sprite.pivot[1])*ratio);
      const local=decodeDepth(f.depth[i],f.depth[i+1],1024,16);
      if(terrain!=null&&terrain>baseDepth+local+5)dst[i+3]=0;
    }
    maskctx.putImageData(maskedPixels,0,0);maskKey=f.key;maskX=actor.x;maskZ=actor.z;maskedTileVersion=tiles.version;
  }
  ctx.fillStyle='#00100d50';ctx.beginPath();ctx.ellipse(p.x,p.y+1,25*zoom,9*zoom,0,0,Math.PI*2);ctx.fill();
  ctx.drawImage(masked,p.x-sprite.pivot[0]*scale,p.y-sprite.pivot[1]*scale,256*scale,256*scale);
  const label=$('unit-label');label.hidden=false;label.style.left=p.x+'px';label.style.top=(p.y-100*zoom)+'px';
}

function drawMap(){
  ctx.fillStyle='#071618';ctx.fillRect(0,0,width,height);
  const top=screen({x:0,y:0});ctx.drawImage(overview,top.x,top.y,map.width*zoom,map.height*zoom);
  const visible=tiles.visible(),s=map.tileSize;
  for(const [col,row]of visible){const e=tiles.entries.get(col+'-'+row);if(!e?.image)continue;
    const p=screen({x:col*s,y:row*s});ctx.drawImage(e.image,p.x,p.y,s*zoom+.25,s*zoom+.25);
  }
  tiles.trim(visible);
}

function drawLiveDiagnostics(layer){
  if(!liveSession)return;
  const visible=renderableOpponents(liveSession);
  const roster=new Map(liveSession.match.players.map(p=>[p.id,p.hero]));
  // Render only opponents the player's own vision can see. Never draw hidden labels.
  visible.sort((a,b)=>project(map,{x:a.x,z:a.z,y:nav.heightAt(a.x,a.z)}).y-
                     project(map,{x:b.x,z:b.z,y:nav.heightAt(b.x,b.z)}).y);
  const playerDepth=project(map,actor).y;
  for(const enemy of visible){
    const enemyDepth=project(map,{x:enemy.x,z:enemy.z,y:nav.heightAt(enemy.x,enemy.z)}).y;
    if((enemyDepth<=playerDepth?'behind':'front')!==layer)continue;
    const hero=roster.get(enemy.id);
    if(championRenderer?.resources.has(hero)){
      championRenderer.draw(enemy,hero,{zoom,animTime});
      continue;
    }
    // Development-only diagnostic marker, explicitly not a champion model.
    const point=project(map,{x:enemy.x,z:enemy.z,y:nav.heightAt(enemy.x,enemy.z)}),p=screen(point);
    if(p.x<0||p.y<0||p.x>width||p.y>height)continue;
    ctx.save();ctx.strokeStyle='#ecb56d';ctx.lineWidth=2;ctx.setLineDash([5,5]);
    ctx.beginPath();ctx.arc(p.x,p.y,15*zoom,0,2*Math.PI);ctx.stroke();
    ctx.setLineDash([]);ctx.fillStyle='#fff0c9';ctx.font='12px sans-serif';
    ctx.fillText('原始模型未安装 · '+hero,p.x+14,p.y-15);ctx.restore();
  }
}
function drawMarker(now){
  if(!marker||now-marker.time>1200)return;
  const p=screen(project(map,marker)),fade=1-(now-marker.time)/1200;
  ctx.save();ctx.globalAlpha=fade;ctx.strokeStyle='#91df82';ctx.lineWidth=1.6;
  ctx.beginPath();ctx.ellipse(p.x,p.y,15*zoom,8*zoom,0,0,Math.PI*2);ctx.stroke();
  for(let i=0;i<4;i++){const a=i*Math.PI/2;ctx.beginPath();ctx.moveTo(p.x+Math.cos(a)*22*zoom,p.y+Math.sin(a)*13*zoom);ctx.lineTo(p.x+Math.cos(a)*14*zoom,p.y+Math.sin(a)*8*zoom);ctx.stroke();}ctx.restore();
}

function drawMinimap(){
  const s=mini.width;mctx.clearRect(0,0,s,s);mctx.drawImage(minimap,0,0,s,s);
  mctx.fillStyle='#001118a6';mctx.beginPath();mctx.moveTo(0,0);mctx.lineTo(s,0);mctx.lineTo(0,s);mctx.fill();
  const mx=p=>p.x/15000*s,my=p=>(15000+p.z)/15000*s;
  const corners=[[0,0],[width,0],[width,height],[0,height]].map(([x,y])=>{
    const p=imagePoint(x,y),py=p.y/map.ppu+map.minPY;
    return{x:p.x/map.ppu+map.minX,z:(py+actor.y*COS)/SIN};
  });
  mctx.strokeStyle='#e7dec3a0';mctx.lineWidth=1.5;mctx.beginPath();corners.forEach((p,i)=>i?mctx.lineTo(mx(p),my(p)):mctx.moveTo(mx(p),my(p)));mctx.closePath();mctx.stroke();
  mctx.fillStyle='#001614';mctx.beginPath();mctx.arc(mx(actor),my(actor),8,0,Math.PI*2);mctx.fill();
  mctx.fillStyle='#f1d69a';mctx.beginPath();mctx.arc(mx(actor),my(actor),5,0,Math.PI*2);mctx.fill();
}

function updateUI(){
  if(!ready)return;const moving=path.length>0,attacking=animTime<attackAnimUntil;
  $('movement-status').textContent=attacking?'动作预览':moving?'正在移动':'自由探索';
  $('unit-state').textContent=attacking?'动作预览':moving?'移动中':'待命';
  $('coords').textContent='X '+Math.round(actor.x)+' · Z '+Math.round(-actor.z);
}

// Animation frames already render continuously; event handlers should not duplicate draws.
function paint(){if(!ready)return;}
function renderFrame(now){
  const start=performance.now();drawMap();
  const afterMap=performance.now();drawMarker(now);drawLiveDiagnostics('behind');drawActor(now);drawLiveDiagnostics('front');
  const afterActor=performance.now();
  if(now-miniTime>=100){drawMinimap();miniTime=now;}
  sample(metrics.mapCosts,afterMap-start);
  sample(metrics.actorCosts,afterActor-afterMap);
  sample(metrics.drawCosts,performance.now()-start);
}

function tick(now){
  requestAnimationFrame(tick);if(!ready)return;
  // Path following consumes distance across waypoints, even at low frame rates.
  // Visibility changes reset the clock, so returning to a tab cannot teleport.
  if(previousFrame){
    const gap=now-previousFrame;sample(metrics.rafGaps,gap);
    if(gap>100)metrics.longGaps++;
  }
  previousFrame=now;
  const dt=last?Math.min((now-last)/1000,1):0;last=now;animTime+=dt;
  if(liveSession){
    const view=liveSession.snapshot();
    if((view.phase==='prepare'&&view.playerRole==='hunter')||!view.playerActive||view.phase==='finished')path=[];
  }
  let remaining=340*dt;
  while(path.length&&remaining>0){const target=path[0],dx=target.x-actor.x,dz=target.z-actor.z,distance=Math.hypot(dx,dz);
    if(distance>.01)heading=Math.atan2(dx,dz);
    if(distance<=remaining){actor.x=target.x;actor.z=target.z;path.shift();remaining-=distance;}
    else{actor.x+=dx/distance*remaining;actor.z+=dz/distance*remaining;remaining=0;}
  }
  actor.y=nav.heightAt(actor.x,actor.z);
  // Freeze only the LOCAL visual test, not the publicly hosted match simulation.
  const qaFrozen=window.__hextechFreezeQA===true &&
    ['127.0.0.1','localhost'].includes(location.hostname) &&
    new URLSearchParams(location.search).has('visualQA');
  if(liveSession&&!qaFrozen){
    liveSession.syncPlayer(actor,{x:Math.sin(heading),z:Math.cos(heading)});
    const qaFreezeAI=window.__hextechFreezeAI===true&&['127.0.0.1','localhost'].includes(location.hostname)&&new URLSearchParams(location.search).has('visualQA');
    const view=liveSession.tick(dt,{freezeAI:qaFreezeAI});
    if(view.attack?.resolved && view.attack.id!==lastAttackNotice){
      lastAttackNotice=view.attack.id;
      const result=view.attack.result;
      toast(result?.ok?'普攻命中 · 抓捕成功':'普攻未命中 · '+(result?.reason||'目标已离开判定范围'));
    }
    if(view.phase!==liveLastPhase){
      liveLastPhase=view.phase;
      toast(view.phase==='chase'?'追捕阶段开始':view.phase==='finished'?'实时演练已结算':'准备阶段开始');
      if(view.phase==='finished')path=[];
    }
    if(now-liveNotice>=200){
      window.dispatchEvent(new CustomEvent('hextech:live-update',{detail:view}));
      liveNotice=now;
    }
  }
  if(follow){const p=project(map,actor),factor=1-Math.exp(-dt*12);camera.x+=(p.x-camera.x)*factor;camera.y+=(p.y-camera.y)*factor;}
  renderFrame(now);
  if(now>toastEnd)$('toast').classList.remove('show');
  if(now-uiTime>200){updateUI();uiTime=now;}
  fpsFrames++;if(now-fpsTime>1000){$('fps').textContent=Math.round(fpsFrames*1000/(now-fpsTime))+' FPS · draw p95 '+percentile(metrics.drawCosts,.95).toFixed(1)+'ms · RAF p95 '+percentile(metrics.rafGaps,.95).toFixed(0)+'ms';fpsFrames=0;fpsTime=now;}
}

async function moveTo(x,y){
  if(!ready)return;
  if(liveSession&&liveSession.snapshot().attack?.finished===false){toast('普攻动作尚未结束，完成后再移动');return;}
  if(liveSession){
    const view=liveSession.snapshot();
    if(view.phase==='finished'||!view.playerActive){toast('本局已结束或已淘汰');return;}
    if(view.phase==='prepare'&&view.playerRole==='hunter'){toast('准备阶段追捕者不得移动');return;}
  }
  const p=imagePoint(x,y),s=map.tileSize;
  try{await tiles.load(Math.floor(p.x/s),Math.floor(p.y/s));}catch{return;}
  const depth=tiles.depthAt(p.x,p.y);if(depth==null){toast('这里不在可移动的地图内');return;}
  const world=unproject(map,p.x,p.y,depth);
  if(world.x+world.z < -120){toast('当前可探索区域为峡谷下半区');return;}
  const target=nav.nearest(world.x,world.z,10);
  if(!target||Math.hypot(target.x-world.x,target.z-world.z)>400){toast('这里是不可通行的地形');return;}
  const route=nav.path(actor,target);if(!route?.length){toast('当前无法到达该位置');return;}
  path=route;attackAnimUntil=0;marker={...target,y:nav.heightAt(target.x,target.z),time:performance.now()};updateUI();paint();
}

function resize(){width=innerWidth;height=innerHeight;dpr=Math.min(devicePixelRatio||1,1.5);canvas.width=Math.round(width*dpr);canvas.height=Math.round(height*dpr);ctx.setTransform(dpr,0,0,dpr,0,0);ctx.imageSmoothingEnabled=true;}
canvas.addEventListener('contextmenu',e=>e.preventDefault());
canvas.addEventListener('pointerdown',e=>{
  if(!ready)return;canvas.focus({preventScroll:true});
  if(e.button===2){moveTo(e.clientX,e.clientY);return;}
  if(e.button!==0)return;drag={startX:e.clientX,startY:e.clientY,cam:{...camera},moved:false,touch:e.pointerType==='touch'};canvas.setPointerCapture(e.pointerId);
});
canvas.addEventListener('pointermove',e=>{if(!drag)return;
  const dx=e.clientX-drag.startX,dy=e.clientY-drag.startY;
  if(Math.hypot(dx,dy)>5){drag.moved=true;setFollow(false);camera.x=drag.cam.x-dx/zoom;camera.y=drag.cam.y-dy/zoom;cameraBounds();paint();}
});
canvas.addEventListener('pointerup',e=>{if(drag?.touch&&!drag.moved)moveTo(e.clientX,e.clientY);drag=null;});
canvas.addEventListener('pointercancel',()=>drag=null);
canvas.addEventListener('wheel',e=>{e.preventDefault();if(!ready)return;
  const p=imagePoint(e.clientX,e.clientY);zoom=Math.max(.55,Math.min(1.8,zoom*Math.exp(-e.deltaY*.001)));
  if(!follow){camera.x=p.x-(e.clientX-width/2)/zoom;camera.y=p.y-(e.clientY-height*.47)/zoom;cameraBounds();}paint();
},{passive:false});
mini.addEventListener('pointerdown',e=>{if(!ready)return;
  const rect=mini.getBoundingClientRect(),x=(e.clientX-rect.left)/rect.width*15000,z=(e.clientY-rect.top)/rect.height*15000-15000;
  camera=project(map,{x,z,y:nav.heightAt(x,z)});setFollow(false);cameraBounds();paint();
});
for(const button of document.querySelectorAll('[data-location]'))button.addEventListener('click',()=>{
  if(!ready)return;const p=locations[button.dataset.location];camera=project(map,{...p,y:nav.heightAt(p.x,p.z)});setFollow(false);toast('查看地图位置 · 空格回到盖伦');paint();
});
$('follow').addEventListener('click',()=>{if(!ready)return;setFollow(!follow);if(follow)recenter();});
$('recenter').addEventListener('click',recenter);$('center-action').addEventListener('click',recenter);
$('stop').addEventListener('click',stop);$('attack').addEventListener('click',attack);
$('help').addEventListener('click',()=>$('help-dialog').showModal());
$('close-help').addEventListener('click',()=>$('help-dialog').close());
$('retry').addEventListener('click',()=>location.reload());
document.addEventListener('keydown',e=>{
  if($('help-dialog').open||e.ctrlKey||e.metaKey||e.altKey)return;
  if(e.code==='Space'){e.preventDefault();recenter();}if(e.code==='KeyS')stop();if(e.code==='KeyA')attack();
  if(e.code==='KeyY'){setFollow(!follow);if(follow)recenter();}
});
document.addEventListener('visibilitychange',()=>{last=0;previousFrame=0;});window.addEventListener('resize',resize);

async function boot(){
  try{
    resize();[map,sprite]=await Promise.all([json(mapBase+'map.json'),json(heroBase+'sprite.json')]);
    if(map.pitch!==sprite.pitch)throw new Error('场景与英雄镜头参数不一致');
    progress('正在读取峡谷地形与移动网格…',20);
    const [navData,over,miniImage]=await Promise.all([binary(mapBase+map.nav.binary),loadImage(mapBase+'overview.webp'),loadImage(mapBase+'minimap.webp')]);
    nav=new NavGrid(map.nav,navData);overview=over;minimap=miniImage;tiles=new TileCache(map);
    actor=nav.nearest(7500,-4400);if(!actor)throw new Error('初始位置不在移动网格内');actor.y=nav.heightAt(actor.x,actor.z);camera=project(map,actor);
    progress('正在读取盖伦模型动作…',45);
    let loaded=0;await Promise.all(Object.entries(sprite.clips).map(async([name,clip])=>{
      const [image,depth]=await Promise.all([loadImage(heroBase+clip.image),loadImage(heroBase+clip.depth)]);atlases.set(name,{image,depth});progress('正在读取盖伦模型动作…',45+(++loaded)*10);
    }));
    progress('正在展开周围的峡谷场景…',80);
    await Promise.all(tiles.visible().map(([x,y])=>tiles.load(x,y)));
    progress('峡谷已就绪',100);ready=true;$('loading').hidden=true;updateUI();
    // Expose read-only navigation services for the separate AI movement diagnostics.
    // Does not grant AI global information or expose hidden opponent locations.
    window.__hextechNavigation={
      nearest:(x,z,radius=12)=>nav.nearest(x,z,radius),
      canWalk:(x,z)=>nav.canWalk(x,z),
      path:(a,b)=>nav.path(a,b),
      lineClear:(a,b)=>nav.lineClear(a,b),
      playerStart:()=>({x:actor.x,z:actor.z}),
      heightAt:(x,z)=>nav.heightAt(x,z),
      cell:(x,z)=>nav.cell(x,z),
      width:nav.width,height:nav.height,
      walkable:nav.walkable,
      bushes:nav.bushes
    };
    championRenderer=new ChampionAtlasRenderer({map,tiles,ctx,screen,loadImage,heightAt:(x,z)=>nav.heightAt(x,z)});
    championRenderer.loadRoster(['盖伦','布里茨','锐雯','亚索'])
      .then(status=>window.dispatchEvent(new CustomEvent('hextech:hero-assets',{detail:status})));
    window.__hextechLive={
      start(config){
        if(!ready)throw new Error('峡谷尚未加载');
        if(!championRenderer?.resources.has(config.playerHero))throw new Error('所选英雄原始动画未加载，请稍后重试');
        playerHero=config.playerHero;
        updateHeroHud(playerHero);
        const start={x:actor.x,z:actor.z};
        window.__hextechFreezeQA=false;window.__hextechFreezeAI=false;
        liveSession=createLiveSession(nav,{...config,start});
        path=[];liveLastPhase='';liveNotice=0;lastAttackNotice=0;attackAnimUntil=0;
        championRenderer.loadRoster(liveSession.match.players.map(p=>p.hero))
          .then(status=>window.dispatchEvent(new CustomEvent('hextech:hero-assets',{detail:status})));
        return liveSession.snapshot();
      },
      stop(){liveSession=null;playerHero='盖伦';updateHeroHud('盖伦');liveLastPhase='';lastAttackNotice=0;attackAnimUntil=0;window.__hextechFreezeQA=false;window.__hextechFreezeAI=false;path=[];return true;},
      snapshot(){return liveSession?.snapshot()??null;},
      selectedHero(){return liveSession?playerHero:null;},
      assets(){return championRenderer?Object.fromEntries([...championRenderer.resources.keys()].map(hero=>[hero,true])):{};},
      scales(){return championRenderer?Object.fromEntries([...championRenderer.resources].map(([hero,r])=>[hero,r.meta.visualScale||1])):{};},
      advanceForVisualQA(seconds){
        if(!['127.0.0.1','localhost'].includes(location.hostname)||!new URLSearchParams(location.search).has('visualQA')||!liveSession)
          throw new Error('Local visual QA mode required');
        if(!Number.isFinite(seconds)||seconds<0||seconds>600)throw new RangeError('Invalid QA advance');
        window.__hextechFreezeQA=true;
        let remaining=seconds;
        while(remaining>1e-9&&liveSession.snapshot().phase!=='finished'){
          const step=Math.min(.05,remaining);
          liveSession.tick(step,{freezeAI:true});
          remaining-=step;
        }
        window.__hextechFreezeQA=false;
        return liveSession.snapshot();
      },
      stageAttackForVisualQA(){
        if(!['127.0.0.1','localhost'].includes(location.hostname)||!new URLSearchParams(location.search).has('visualQA')||!liveSession)
          throw new Error('Local visual QA mode required');
        window.__hextechFreezeAI=true;
        const player=liveSession.actors.get('player');
        if(liveSession.snapshot().phase!=='chase'||player.role!=='hunter')throw new Error('QA attack requires a chasing hunter');
        const target=[...liveSession.actors.values()].find(a=>a.active&&a.role==='hider');
        if(!target)throw new Error('No active hider available');
        const point=nav.nearest(player.x+100,player.z,4);
        if(!point)throw new Error('No valid QA contact position');
        target.x=point.x;target.z=point.z;target.route=[];
        const dx=target.x-player.x,dz=target.z-player.z,length=Math.hypot(dx,dz)||1;
        player.facing={x:dx/length,z:dz/length};heading=Math.atan2(dx,dz);
        return {distance:length,visible:liveSession.vision.canSee(player,target),target:target.id};
      },
      stageForVisualQA(){
        // Only on an expressly requested localhost Chromium QA run.
        if(!['127.0.0.1','localhost'].includes(location.hostname) ||
           !new URLSearchParams(location.search).has('visualQA'))
          throw new Error('Local visual QA mode required');
        if(!liveSession)throw new Error('No live match');
        window.__hextechFreezeQA=true;
        const player=liveSession.actors.get('player');
        const others=[...liveSession.actors.values()].filter(a=>a.id!=='player');
        const spots=[];
        for(const a of others){
          let found=null;
          outer: for(const radius of [140,220,300,400,520]){
            for(let direction=0;direction<24;direction++){
              const radians=(direction/24)*Math.PI*2;
              const p=nav.nearest(player.x+Math.cos(radians)*radius,
                                  player.z+Math.sin(radians)*radius,4);
              if(!p||spots.some(q=>Math.hypot(p.x-q.x,p.z-q.z)<100))continue;
              const sample={...a,x:p.x,z:p.z,active:true};
              if(liveSession.vision.canSee(player,sample)){found=p;break outer;}
            }
          }
          if(!found)throw new Error('Not enough visible Rift positions for all sprites');
          a.x=found.x;a.z=found.z;a.route=[];a.active=true;
          a.facing={x:player.x-a.x,z:player.z-a.z};
          spots.push(found);
        }
        return liveSession.snapshot().visible.map(a=>a.hero);
      }
    };
    fpsTime=performance.now();requestAnimationFrame(tick);
    window.__hextechPerf=()=>({
      fpsText:$('fps').textContent,rafP50:percentile(metrics.rafGaps,.5),
      rafP95:percentile(metrics.rafGaps,.95),drawP95:percentile(metrics.drawCosts,.95),
      mapP95:percentile(metrics.mapCosts,.95),actorP95:percentile(metrics.actorCosts,.95),
      longGaps:metrics.longGaps,samples:metrics.rafGaps.length,
      viewport:{width,height,dpr},visibility:document.visibilityState
    });
  }catch(error){console.error(error);$('loading').classList.add('error');progress('加载中断：'+error.message,0);$('retry').hidden=false;}
}
boot();
