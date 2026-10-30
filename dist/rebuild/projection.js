export const PITCH=55*Math.PI/180,SIN=Math.sin(PITCH),COS=Math.cos(PITCH);
export function project(meta,p){return {x:(p.x-meta.minX)*meta.ppu,y:(p.z*SIN-(p.y??0)*COS-meta.minPY)*meta.ppu};}
export function unproject(meta,x,y,depth){const py=y/meta.ppu+meta.minPY;return {x:x/meta.ppu+meta.minX,z:py*SIN+depth*COS,y:-py*COS+depth*SIN};}
export function viewDepth(p){return (p.y??0)*SIN+p.z*COS;}
export function decodeDepth(r,g,offset=12000,scale=4){return ((r<<8)|g)/scale-offset;}
export function directionIndex(angle,n=16){return ((Math.round(angle/(Math.PI*2)*n)%n)+n)%n;}
