// Shared gameplay vision. Renderer fog and AI use this same source of truth.
export const VISION_BOUNDS={min:-60,size:1180};
export const VISION_RANGE={seeker:225,hider:255,ward:165,sweep:210,camouflage:85};
export class VisionSystem {
  constructor({walls=[],brushes=[]}={}) {this.walls=walls;this.brushes=brushes;this.state={players:[],wards:[],now:0};this.cache=new Map();}
  sync(state){this.state=state;this.cache.clear();}
  brushAt(p){for(let i=0;i<this.brushes.length;i++){const b=this.brushes[i];if((p.x-b.x)**2+(p.y-b.y)**2<b.r*b.r)return i;}return -1;}
  lineClear(a,b){
    const dx=b.x-a.x,dy=b.y-a.y;
    for(const wall of this.walls){const rx=wall.rx+8,ry=wall.ry+8;
      if(Math.max(a.x,b.x)<wall.x-rx||Math.min(a.x,b.x)>wall.x+rx||Math.max(a.y,b.y)<wall.y-ry||Math.min(a.y,b.y)>wall.y+ry)continue;
      const x=(a.x-wall.x)/rx,y=(a.y-wall.y)/ry,vx=dx/rx,vy=dy/ry,A=vx*vx+vy*vy;
      if(A<1e-12)continue;const B=2*(x*vx+y*vy),C=x*x+y*y-1,D=B*B-4*A*C;
      if(D<0)continue;const root=Math.sqrt(D),enter=(-B-root)/(2*A),leave=(-B+root)/(2*A);
      if(leave>0.001&&enter<0.999&&enter<leave)return false;
    }return true;
  }
  distance(a,b){return Math.hypot(a.x-b.x,a.y-b.y);}
  wardDisabled(w){if(w.trueSight||w.until<=this.state.now)return false;return this.state.wards.some(other=>other.trueSight&&other.role!==w.role&&other.until>this.state.now&&this.distance(w,other)<(other.radius||VISION_RANGE.ward))||this.state.players.some(p=>p.active&&p.role!==w.role&&p.scan>this.state.now&&this.distance(p,w)<VISION_RANGE.sweep);}
  sources(role){if(this.cache.has(role))return this.cache.get(role);const sources=[];
    for(const p of this.state.players)if(p.active&&p.role===role)sources.push({x:p.x,y:p.y,radius:VISION_RANGE[p.role],brush:this.brushAt(p),kind:'hero',id:p.id,trueSight:false});
    for(const w of this.state.wards)if(w.role===role&&w.until>this.state.now&&!this.wardDisabled(w))sources.push({x:w.x,y:w.y,radius:w.radius||VISION_RANGE.ward,brush:this.brushAt(w),kind:'ward',trueSight:!!w.trueSight});
    this.cache.set(role,sources);return sources;
  }
  sourceSees(source,point,brush=this.brushAt(point)){
    if((point.x-source.x)**2+(point.y-source.y)**2>source.radius*source.radius)return false;
    // Seeing the exterior of a brush does not reveal its interior, even at melee distance.
    if(brush>=0&&source.brush!==brush)return false;
    return this.lineClear(source,point);
  }
  pointVisible(role,point){const b=this.brushAt(point);return this.sources(role).some(s=>this.sourceSees(s,point,b));}
  stealth(target){if(target.hero==='Teemo'&&(target.hidden>this.state.now||this.state.now-target.lastMove>2))return 'invisible';if(target.hidden>this.state.now)return target.hero==='Twitch'?'camouflage':'invisible';return null;}
  unitVisible(role,target){
    if(target.role===role)return true;
    if(!target.active)return false;
    if(target.revealRole===role&&target.revealUntil>this.state.now)return true;
    const brush=this.brushAt(target),stealth=this.stealth(target);
    if(stealth==='invisible')return false;
    for(const source of this.sources(role)){
      if(!this.sourceSees(source,target,brush))continue;
      if(!stealth)return true;
      if(source.trueSight||source.kind==='hero'&&this.distance(source,target)<=VISION_RANGE.camouflage)return true;
    }return false;
  }
  scanned(role,target){return this.state.players.some(p=>p.active&&p.role===role&&p.scan>this.state.now&&this.distance(p,target)<VISION_RANGE.sweep);}
  unitState(role,target){if(this.unitVisible(role,target))return 'visible';if(target.active&&this.scanned(role,target))return 'silhouette';return 'hidden';}
  propVisible(role,prop){if(prop.role===role)return true;const kind=prop.kind||'ward';if(kind==='ward'&&!prop.trueSight){const revealed=this.scanned(role,prop)||this.sources(role).some(s=>s.trueSight&&this.distance(s,prop)<s.radius);return revealed&&this.pointVisible(role,prop);}return this.pointVisible(role,prop);}
}

// Binary cells decide terrain vision. Narrow shader filtering softens only the visual edge.
export class VisionField {
  constructor(size=192){this.size=size;this.current=new Uint8Array(size*size);this.explored=new Uint8Array(size*size);this.role=null;this.revision=0;this.brushCells=null;this.brushRef=null;}
  reset(role){this.role=role;this.current.fill(0);this.explored.fill(0);this.revision++;}
  update(vision,role){if(this.role!==role)this.reset(role);const N=this.size,step=VISION_BOUNDS.size/N,lo=VISION_BOUNDS.min;
    if(this.brushRef!==vision.brushes){this.brushCells=new Int16Array(N*N);for(let y=0;y<N;y++)for(let x=0;x<N;x++)this.brushCells[y*N+x]=vision.brushAt({x:lo+(x+.5)*step,y:lo+(y+.5)*step});this.brushRef=vision.brushes;}
    this.current.fill(0);
    for(const source of vision.sources(role)){
      const x0=Math.max(0,Math.floor((source.x-source.radius-lo)/step)),x1=Math.min(N-1,Math.ceil((source.x+source.radius-lo)/step)),y0=Math.max(0,Math.floor((source.y-source.radius-lo)/step)),y1=Math.min(N-1,Math.ceil((source.y+source.radius-lo)/step));
      for(let y=y0;y<=y1;y++)for(let x=x0;x<=x1;x++){const i=y*N+x;if(this.current[i])continue;const point={x:lo+(x+.5)*step,y:lo+(y+.5)*step};if(vision.sourceSees(source,point,this.brushCells[i]))this.current[i]=255;}
    }
    for(let i=0;i<this.current.length;i++)if(this.current[i])this.explored[i]=255;
    this.revision++;return this.current;
  }
  sample(x,y){const N=this.size,col=Math.floor((x-VISION_BOUNDS.min)/VISION_BOUNDS.size*N),row=Math.floor((y-VISION_BOUNDS.min)/VISION_BOUNDS.size*N);return col>=0&&col<N&&row>=0&&row<N?this.current[row*N+col]/255:0;}
}
