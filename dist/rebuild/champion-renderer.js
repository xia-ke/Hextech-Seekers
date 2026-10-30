// Original champion atlas renderer. Never substitute a different champion's art.
import {project,viewDepth,decodeDepth,directionIndex} from './projection.js';

export const HERO_ASSET_NAMES=Object.freeze({
  '盖伦':'garen','布里茨':'blitzcrank','锐雯':'riven','亚索':'yasuo'
});
export function renderableOpponents(session) {
  if(!session)return [];
  // No direct access to hidden target locations is allowed in the drawing pipeline.
  const ids=new Set(session.snapshot().visible.map(p=>p.id));
  return [...session.actors.values()].filter(actor=>actor.id!=='player'&&actor.active&&ids.has(actor.id));
}
export class ChampionAtlasRenderer {
  constructor({map,tiles,ctx,screen,loadImage,heightAt,base='assets/visual/'}) {
    Object.assign(this,{map,tiles,ctx,screen,loadImage,heightAt,base});
    this.resources=new Map();this.failures=new Map();this.cache=new Map();
    this.work=document.createElement('canvas');this.work.width=this.work.height=256;
    this.wctx=this.work.getContext('2d',{willReadFrequently:true});
    this.mask=document.createElement('canvas');this.mask.width=this.mask.height=256;
    this.maskctx=this.mask.getContext('2d');
    this.maskData=this.maskctx.createImageData(256,256);
    this.maskKey='';
  }
  async load(hero) {
    const slug=HERO_ASSET_NAMES[hero];
    if(!slug)throw new Error('Unsupported champion: '+hero);
    if(this.resources.has(hero))return true;
    try {
      const root=this.base+slug+'/';
      const response=await fetch(root+'sprite.json');
      if(!response.ok)throw new Error('Missing original sprite manifest');
      const meta=await response.json();
      if(meta.hero.toLowerCase()!==slug||meta.pitch!==this.map.pitch||meta.tileSize!==256||meta.directions!==16)
        throw new Error('Incompatible original hero sprite metadata');
      for(const clip of ['idle1','run','attack1']){
        if(!meta.clips[clip])throw new Error('Missing original animation: '+clip);
      }
      const atlases=new Map();
      await Promise.all(Object.entries(meta.clips).map(async ([clip,info])=>{
        const [image,depth]=await Promise.all([this.loadImage(root+info.image),this.loadImage(root+info.depth)]);
        atlases.set(clip,{image,depth});
      }));
      this.resources.set(hero,{meta,atlases});this.failures.delete(hero);
      return true;
    }catch(error){this.failures.set(hero,String(error.message||error));return false;}
  }
  async loadRoster(heroes){
    const unique=[...new Set(heroes)];
    await Promise.all(unique.map(hero=>this.load(hero)));
    return Object.fromEntries(unique.map(hero=>[hero,this.resources.has(hero)]));
  }
  frame(hero,clip,direction,index){
    const key=hero+':'+clip+':'+direction+':'+index;
    if(this.cache.has(key))return this.cache.get(key);
    const {meta,atlases}=this.resources.get(hero);
    const atlas=atlases.get(clip),size=meta.tileSize;
    this.wctx.clearRect(0,0,size,size);
    this.wctx.drawImage(atlas.image,direction*size,index*size,size,size,0,0,size,size);
    const rgba=this.wctx.getImageData(0,0,size,size).data;
    this.wctx.clearRect(0,0,size,size);
    this.wctx.drawImage(atlas.depth,direction*size,index*size,size,size,0,0,size,size);
    const depth=this.wctx.getImageData(0,0,size,size).data;
    const opaque=[];
    for(let i=0;i<rgba.length;i+=4)if(rgba[i+3])opaque.push(i);
    const frame={rgba,depth,opaque,key};
    this.cache.set(key,frame);
    if(this.cache.size>48)this.cache.delete(this.cache.keys().next().value);
    return frame;
  }
  draw(actor,hero,{zoom,animTime=0,selected=false}={}){
    const resource=this.resources.get(hero);
    if(!resource||!actor?.active)return false;
    const {meta}=resource;
    const clip=actor.route?.length?'run':'idle1',info=meta.clips[clip];
    const frameIndex=Math.min(info.frames-1,Math.floor((animTime%info.duration)/info.duration*info.frames));
    const heading=Math.atan2(actor.facing?.x||0,actor.facing?.z||1);
    const frame=this.frame(hero,clip,directionIndex(heading,meta.directions),frameIndex);
    const world={x:actor.x,y:this.heightAt(actor.x,actor.z),z:actor.z};
    const point=project(this.map,world),p=this.screen(point);
    const ratio=this.map.ppu/meta.ppu,scale=ratio*zoom;
    if(p.x<-256||p.x>this.ctx.canvas.width+256||p.y<-256||p.y>this.ctx.canvas.height+256)return false;
    const maskKey=frame.key+':'+actor.x.toFixed(2)+':'+actor.z.toFixed(2)+':'+this.tiles.version;
    if(maskKey!==this.maskKey){
      const pixels=this.maskData.data;pixels.set(frame.rgba);
      const baseDepth=viewDepth(world),size=meta.tileSize;
      for(const i of frame.opaque){
        const index=i>>2,x=index%size,y=(index/size)|0;
        const terrain=this.tiles.depthAt(point.x+(x-meta.pivot[0])*ratio,point.y+(y-meta.pivot[1])*ratio);
        const local=decodeDepth(frame.depth[i],frame.depth[i+1],1024,16);
        if(terrain!=null&&terrain>baseDepth+local+5)pixels[i+3]=0;
      }
      this.maskctx.putImageData(this.maskData,0,0);this.maskKey=maskKey;
    }
    this.ctx.drawImage(this.mask,p.x-meta.pivot[0]*scale,p.y-meta.pivot[1]*scale,sizeScale(meta,scale),sizeScale(meta,scale));
    return true;
  }
}
function sizeScale(meta,scale){return meta.tileSize*scale;}
