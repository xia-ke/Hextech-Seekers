export class NavGrid {
  constructor(meta, binary, lowerHalf = true) {
    Object.assign(this, meta);
    this.lowerHalf = lowerHalf;
    const n = meta.width * meta.height;
    if (binary.byteLength < n * 3) throw new Error('原始移动网格不完整');
    this.walkable = new Uint8Array(binary, 0, n);
    this.heights = new Int16Array(binary, n, n);
    this.flags = meta.flagsOffset == null ? null : new Uint16Array(binary, meta.flagsOffset, n);
    this.bushes = meta.bushOffset == null ? null : new Int8Array(binary, meta.bushOffset, n);
    this.cellSize = meta.cellSize ?? meta.size / meta.width;
    this.clearance = new Uint8Array(n);
    for (let y = 1; y < this.height - 1; y++) for (let x = 1; x < this.width - 1; x++) {
      let clear = true;
      for (let dy=-1;dy<=1;dy++) for(let dx=-1;dx<=1;dx++) if(!this.walkable[(y+dy)*this.width+x+dx]) clear=false;
      this.clearance[y*this.width+x] = clear ? 1 : 0;
    }
  }
  cell(x,z) {return {x:Math.floor((x-this.minX)/this.cellSize),y:Math.floor((z-this.minZ)/this.cellSize)};}
  center(x,y) {return {x:this.minX+(x+.5)*this.cellSize,z:this.minZ+(y+.5)*this.cellSize};}
  valid(x,y) {
    if(x<0||y<0||x>=this.width||y>=this.height||!this.clearance[y*this.width+x])return false;
    const p=this.center(x,y);
    return !this.lowerHalf||p.x+p.z>=-120;
  }
  canWalk(x,z) {const c=this.cell(x,z);return this.valid(c.x,c.y);}
  heightAt(x,z) {
    const c=this.cell(x,z),cx=Math.max(0,Math.min(this.width-1,c.x)),cy=Math.max(0,Math.min(this.height-1,c.y));
    return this.heights[cy*this.width+cx];
  }
  nearest(x,z,radius=15) {
    const c=this.cell(x,z);let best=null,score=Infinity;
    for(let r=0;r<=radius;r++) {
      for(let y=c.y-r;y<=c.y+r;y++)for(let px=c.x-r;px<=c.x+r;px++){
        if(r&&Math.abs(px-c.x)!==r&&Math.abs(y-c.y)!==r)continue;
        if(!this.valid(px,y))continue;
        const p=this.center(px,y),d=(p.x-x)**2+(p.z-z)**2;
        if(d<score){best=p;score=d;}
      }
      if(best&&r>1)return best;
    }
    return best;
  }
  lineClear(a,b) {
    const n=Math.max(1,Math.ceil(Math.hypot(b.x-a.x,b.z-a.z)/(this.cellSize*.3)));
    for(let i=0;i<=n;i++)if(!this.canWalk(a.x+(b.x-a.x)*i/n,a.z+(b.z-a.z)*i/n))return false;
    return true;
  }
  path(start,target) {
    const a=this.nearest(start.x,start.z,6),b=this.nearest(target.x,target.z);
    if(!a||!b)return null;
    if(this.lineClear(start,b))return [b];
    const ac=this.cell(a.x,a.z),bc=this.cell(b.x,b.z),n=this.width*this.height;
    const first=ac.y*this.width+ac.x,last=bc.y*this.width+bc.x;
    const scores=new Float32Array(n);scores.fill(Infinity);scores[first]=0;
    const previous=new Int32Array(n);previous.fill(-1);const closed=new Uint8Array(n),heap=[];
    const heuristic=i=>{const dx=Math.abs(i%this.width-bc.x),dy=Math.abs(Math.floor(i/this.width)-bc.y);return Math.max(dx,dy)+(Math.SQRT2-1)*Math.min(dx,dy);};
    function push(v){let i=heap.length;heap.push(v);while(i){const p=(i-1)>>1;if(heap[p].f<=v.f)break;heap[i]=heap[p];i=p;}heap[i]=v;}
    function pop(){const v=heap[0],tail=heap.pop();if(heap.length){let i=0;while(i*2+1<heap.length){let c=i*2+1;if(c+1<heap.length&&heap[c+1].f<heap[c].f)c++;if(heap[c].f>=tail.f)break;heap[i]=heap[c];i=c;}heap[i]=tail;}return v;}
    push({i:first,f:heuristic(first)});let visits=0;
    while(heap.length&&visits++<60000){
      const current=pop().i;if(closed[current])continue;closed[current]=1;
      if(current===last){
        const cells=[];let at=last;
        while(at!==first){cells.push(this.center(at%this.width,Math.floor(at/this.width)));at=previous[at];if(at<0)return null;}
        cells.reverse();const result=[];let origin=start,index=0;
        while(index<cells.length){let far=index;while(far+1<cells.length&&this.lineClear(origin,cells[far+1]))far++;result.push(cells[far]);origin=cells[far];index=far+1;}
        return result;
      }
      const x=current%this.width,y=Math.floor(current/this.width);
      for(let dy=-1;dy<=1;dy++)for(let dx=-1;dx<=1;dx++){
        if(!dx&&!dy||!this.valid(x+dx,y+dy))continue;
        if(dx&&dy&&(!this.valid(x+dx,y)||!this.valid(x,y+dy)))continue;
        const next=(y+dy)*this.width+x+dx;if(closed[next])continue;
        const cost=scores[current]+(dx&&dy?Math.SQRT2:1);
        if(cost<scores[next]){scores[next]=cost;previous[next]=current;push({i:next,f:cost+heuristic(next)});}
      }
    }
    return null;
  }
}
