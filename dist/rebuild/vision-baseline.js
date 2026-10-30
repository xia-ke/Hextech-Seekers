// Issue 004: conservative visibility adapter for the current Rift navigation data.
// This is a testable baseline, NOT a claim of pixel-exact League of Legends vision.
export const DEFAULT_VISION_RANGE=1150; // provisional world units, subject to client comparison
export function makeVision(nav,{range=DEFAULT_VISION_RANGE}={}) {
  if(!nav || typeof nav.cell!=='function'||!nav.walkable) throw new Error('Vision requires raw nav cells');
  if(!Number.isFinite(range)||range<=0)throw new RangeError('Vision radius must be positive');
  const inBounds=(x,y)=>x>=0&&y>=0&&x<nav.width&&y<nav.height;
  const index=(x,y)=>y*nav.width+x;
  function bushAt(pos){
    if(!nav.bushes)return 0;
    const c=nav.cell(pos.x,pos.z);
    if(!inBounds(c.x,c.y))return 0;
    const id=nav.bushes[index(c.x,c.y)];
    return id>0?id:0;
  }
  function hasLineOfSight(a,b){
    const start=nav.cell(a.x,a.z),end=nav.cell(b.x,b.z);
    if(!inBounds(start.x,start.y)||!inBounds(end.x,end.y))return false;
    // Supercover grid walk. Corner-touching walls block vision; unlike nav.path,
    // this uses source raw collision and does not treat walkable clearance as LOS.
    const dx=end.x-start.x,dy=end.y-start.y;
    const nx=Math.abs(dx),ny=Math.abs(dy),sx=Math.sign(dx),sy=Math.sign(dy);
    let x=start.x,y=start.y,ix=0,iy=0;
    const open=(cx,cy)=>inBounds(cx,cy)&&nav.walkable[index(cx,cy)]!==0;
    while(ix<nx||iy<ny){
      const ax=(1+2*ix)*ny,ay=(1+2*iy)*nx;
      if(ax===ay){
        if(!open(x+sx,y)||!open(x,y+sy))return false;
        x+=sx;y+=sy;ix++;iy++;
      }else if(ax<ay){x+=sx;ix++;}
      else{y+=sy;iy++;}
      if(!open(x,y))return false;
    }
    return true;
  }
  function canSee(observer,target){
    if(!observer||!target||observer.active===false||target.active===false)return false;
    if(observer.id===target.id)return true;
    if(Math.hypot(target.x-observer.x,target.z-observer.z)>range)return false;
    const targetBush=bushAt(target),observerBush=bushAt(observer);
    if(targetBush && targetBush!==observerBush)return false;
    return hasLineOfSight(observer,target);
  }
  function visibleOpponents(observer,actors){
    return [...actors.values()].filter(target=>target.role!==observer.role&&canSee(observer,target));
  }
  return Object.freeze({range,bushAt,hasLineOfSight,canSee,visibleOpponents});
}
