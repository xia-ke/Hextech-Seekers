// Compute the source and destination rectangles for only the visible
// section of the original Rift overview. Never upscale the entire map
// just to clip all but a single viewport's worth of pixels.
export function overviewViewport(meta,imageWidth,imageHeight,camera,zoom,width,height,anchorY=.47){
  if(!Number.isFinite(zoom)||zoom<=0||imageWidth<=0||imageHeight<=0||width<=0||height<=0)
    throw new RangeError('Invalid overview viewport');
  const left=Math.max(0,camera.x-width/(2*zoom));
  const right=Math.min(meta.width,camera.x+width/(2*zoom));
  const top=Math.max(0,camera.y-height*anchorY/zoom);
  const bottom=Math.min(meta.height,camera.y+height*(1-anchorY)/zoom);
  if(right<=left||bottom<=top)return null;
  return {
    sx:left/meta.width*imageWidth,sy:top/meta.height*imageHeight,
    sw:(right-left)/meta.width*imageWidth,
    sh:(bottom-top)/meta.height*imageHeight,
    dx:(left-camera.x)*zoom+width/2,
    dy:(top-camera.y)*zoom+height*anchorY,
    dw:(right-left)*zoom,dh:(bottom-top)*zoom
  };
}
