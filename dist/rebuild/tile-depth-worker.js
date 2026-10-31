import {decodeDepthPixels} from './depth-codec.js';
// An explicit queue keeps tile PNG readbacks off the main thread without
// starting dozens of large GPU/Canvas conversions simultaneously.
const queue=[];
let running=false;
self.onmessage=event=>{queue.push(event.data);if(!running)void pump();};
async function pump(){
  running=true;
  while(queue.length){
    const {id,url,size}=queue.shift();
    try{
      const response=await fetch(url);
      if(!response.ok)throw new Error('Depth fetch HTTP '+response.status);
      const bitmap=await createImageBitmap(await response.blob());
      try{
        if(bitmap.width!==size||bitmap.height!==size)throw new Error('Depth size mismatch');
        const canvas=new OffscreenCanvas(size,size),ctx=canvas.getContext('2d',{willReadFrequently:true});
        if(!ctx)throw new Error('OffscreenCanvas 2d unavailable');
        ctx.drawImage(bitmap,0,0);
        const pixels=ctx.getImageData(0,0,size,size).data;
        const depth=decodeDepthPixels(pixels,size,size);
        self.postMessage({id,buffer:depth.buffer},[depth.buffer]);
      }finally{bitmap.close();}
    }catch(error){self.postMessage({id,error:String(error?.message||error)});}
  }
  running=false;
}
