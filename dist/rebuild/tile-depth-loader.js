import {decodeDepthPixels} from './depth-codec.js';

// Dedicated worker keeps high-resolution PNG readback and Uint16 conversion
// out of requestAnimationFrame. Fallback is byte-identical.
export class TileDepthLoader {
  constructor(loadImage){
    this.loadImage=loadImage;
    this.worker=null;this.jobs=new Map();this.nextId=0;
    this.stats={workerDecoded:0,workerColors:0,fallbackDecoded:0,fallbackColors:0,workerErrors:0};
    if(typeof Worker!=='undefined')try{
      this.worker=new Worker(new URL('./tile-depth-worker.js',import.meta.url),{type:'module'});
      this.worker.onmessage=({data})=>{
        const job=this.jobs.get(data.id);
        if(!job)return;
        this.jobs.delete(data.id);
        data.error?job.reject(new Error(data.error)):
          job.resolve(data.bitmap||new Uint16Array(data.buffer));
      };
      this.worker.onerror=()=>{
        const worker=this.worker;this.worker=null;worker?.terminate();
        for(const job of this.jobs.values())job.reject(new Error('Tile depth worker failed'));
        this.jobs.clear();
      };
    }catch{this.worker=null;}
  }
  request(url,size,kind){
    if(!this.worker)return Promise.reject(new Error('Tile worker unavailable'));
    return new Promise((resolve,reject)=>{
      const id=++this.nextId;
      this.jobs.set(id,{resolve,reject});
      try{this.worker.postMessage({id,url:new URL(url,document.baseURI).href,size,kind});}
      catch(error){this.jobs.delete(id);reject(error);}
    });
  }
  async loadColor(url,size){
    if(this.worker)try{
      const bitmap=await this.request(url,size,'color');
      this.stats.workerColors++;
      return bitmap;
    }catch{this.stats.workerErrors++;}
    this.stats.fallbackColors++;
    return this.loadImage(url);
  }
  async decode(url,size){
    if(this.worker)try{
      const result=await this.request(url,size,'depth');
      this.stats.workerDecoded++;return result;
    }catch{this.stats.workerErrors++;}
    const image=await this.loadImage(url);
    const canvas=document.createElement('canvas');canvas.width=canvas.height=size;
    const cx=canvas.getContext('2d',{willReadFrequently:true});
    cx.drawImage(image,0,0);
    const depth=decodeDepthPixels(cx.getImageData(0,0,size,size).data,size,size);
    this.stats.fallbackDecoded++;
    return depth;
  }
  report(){return {...this.stats,workerEnabled:!!this.worker};}
}
