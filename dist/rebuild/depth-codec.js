// Decode native Rift depth RGBA once, with 65535 as the transparent sentinel.
// Keeping this pure makes worker and fallback results bit-identical.
export function decodeDepthPixels(rgba,width,height){
  if(!Number.isInteger(width)||!Number.isInteger(height)||width<=0||height<=0||rgba.length!==width*height*4)
    throw new RangeError('Invalid depth tile dimensions');
  const depth=new Uint16Array(width*height);
  depth.fill(65535);
  for(let i=0,j=0;j<depth.length;i+=4,j++)
    if(rgba[i+3]!==0)depth[j]=rgba[i]<<8|rgba[i+1];
  return depth;
}
