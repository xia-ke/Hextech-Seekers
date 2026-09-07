import * as T from './vendor/three.module.min.js';
import {VisionField,VISION_BOUNDS} from './vision.js';

// Small tileable physical surface maps. Textures stay local and need no network request.
const textureCache=new Map();
function texture(data,n,repeat,color=false){const t=new T.DataTexture(data,n,n,T.RGBAFormat);t.wrapS=t.wrapT=T.RepeatWrapping;t.repeat.set(repeat,repeat);t.magFilter=T.LinearFilter;t.minFilter=T.LinearMipmapLinearFilter;t.generateMipmaps=true;t.anisotropy=8;if(color)t.colorSpace=T.SRGBColorSpace;t.needsUpdate=true;return t;}
export function surfaceMaps(kind='soil',repeat=1){
  const key=kind+'-'+repeat;if(textureCache.has(key))return textureCache.get(key);
  const N=256,heights=new Float32Array(N*N),color=new Uint8Array(N*N*4),normal=new Uint8Array(N*N*4),rough=new Uint8Array(N*N*4);
  const salt=kind==='stone'?7:kind==='cloth'?19:kind==='metal'?31:3;
  const rand=(x,y)=>{let h=Math.sin(x*127.1+y*311.7+salt)*43758.5453;return h-Math.floor(h)};
  const value=(u,v,k)=>{const x=u*k,y=v*k,a=Math.floor(x),b=Math.floor(y),fx=x-a,fy=y-b,s=fx*fx*(3-2*fx),t=fy*fy*(3-2*fy);const h=(xx,yy)=>rand((xx+k)%k,(yy+k)%k);return (h(a,b)*(1-s)+h(a+1,b)*s)*(1-t)+(h(a,b+1)*(1-s)+h(a+1,b+1)*s)*t;};
  for(let y=0;y<N;y++)for(let x=0;x<N;x++){const u=x/N,v=y/N;let h=value(u,v,4)*.43+value(u,v,12)*.30+value(u,v,32)*.18+value(u,v,64)*.09;if(kind==='cloth')h=.5+.13*Math.sin(u*Math.PI*128)*Math.sin(v*Math.PI*128)+h*.2;if(kind==='metal')h=.55+Math.sin(v*Math.PI*220)*.045+value(u,v,32)*.1;heights[y*N+x]=h;}
  for(let y=0;y<N;y++)for(let x=0;x<N;x++){const i=y*N+x,j=i*4,h=heights[i],dx=heights[y*N+(x+1)%N]-heights[y*N+(x+N-1)%N],dy=heights[((y+1)%N)*N+x]-heights[((y+N-1)%N)*N+x];const strength=kind==='stone'?3:kind==='soil'?2:kind==='cloth'?1:.4;let nx=-dx*strength,ny=-dy*strength,nz=1,mag=Math.hypot(nx,ny,nz);normal[j]=(nx/mag*.5+.5)*255;normal[j+1]=(ny/mag*.5+.5)*255;normal[j+2]=(nz/mag*.5+.5)*255;normal[j+3]=255;const tone=Math.round(183+h*70);color[j]=tone;color[j+1]=tone;color[j+2]=kind==='soil'?tone-6:tone;color[j+3]=255;rough[j]=rough[j+1]=rough[j+2]=Math.round(210+h*45);rough[j+3]=255;}
  const maps={map:texture(color,N,repeat,true),normalMap:texture(normal,N,repeat),roughnessMap:texture(rough,N,repeat)};textureCache.set(key,maps);return maps;
}
export function makeEnvironment(renderer){const env=new T.Scene();env.background=new T.Color('#52695e');const panel=(x,y,z,w,h,color,rx=0,ry=0)=>{const m=new T.Mesh(new T.PlaneGeometry(w,h),new T.MeshBasicMaterial({color,side:T.DoubleSide}));m.position.set(x,y,z);m.rotation.set(rx,ry,0);env.add(m)};panel(0,12,0,24,24,'#d6dfcb',Math.PI/2);panel(-12,2,0,20,18,'#8dadab',0,Math.PI/2);panel(12,2,0,20,18,'#c9b68a',0,Math.PI/2);panel(0,-8,0,28,28,'#203e31',Math.PI/2);panel(0,4,-10,18,10,'#78978b');const pmrem=new T.PMREMGenerator(renderer);const result=pmrem.fromScene(env,.05,.1,100);pmrem.dispose();env.traverse(o=>{o.geometry?.dispose();o.material?.dispose()});return result;}

const postVertex=`varying vec2 uvScreen;void main(){uvScreen=uv;gl_Position=vec4(position.xy,0.,1.);}`;
export const postFragment=`
  uniform sampler2D sceneColor;
  uniform sampler2D sceneDepth;
  uniform sampler2D visionMask;
  uniform sampler2D exploredMask;
  uniform mat4 inverseProjection;
  uniform mat4 cameraWorld;
  uniform vec2 texel;
  uniform vec2 visionTexel;
  uniform float boundsMin;
  uniform float boundsSize;
  uniform float aoStrength;
  varying vec2 uvScreen;
  vec3 reconstruct(vec2 uv,float depth){vec4 view=inverseProjection*vec4(uv*2.-1.,depth*2.-1.,1.);view/=view.w;return (cameraWorld*view).xyz;}
  void main(){
    vec3 color=texture2D(sceneColor,uvScreen).rgb;
    float depth=texture2D(sceneDepth,uvScreen).r;
    float sight=0.;float explored=0.;float occlusion=0.;
    if(depth<.99999){
      vec3 world=reconstruct(uvScreen,depth);
      vec2 fogUV=(world.xz-vec2(boundsMin))/boundsSize;
      if(all(greaterThanEqual(fogUV,vec2(0.)))&&all(lessThanEqual(fogUV,vec2(1.)))){
        sight=texture2D(visionMask,fogUV).r*.5;
        sight+=texture2D(visionMask,fogUV+vec2(visionTexel.x*.55,0.)).r*.125;
        sight+=texture2D(visionMask,fogUV-vec2(visionTexel.x*.55,0.)).r*.125;
        sight+=texture2D(visionMask,fogUV+vec2(0.,visionTexel.y*.55)).r*.125;
        sight+=texture2D(visionMask,fogUV-vec2(0.,visionTexel.y*.55)).r*.125;
        sight=smoothstep(.12,.88,sight);
        explored=texture2D(exploredMask,fogUV).r;
      }
      if(aoStrength>0.&&sight>.01){
        vec3 normal=normalize(cross(dFdx(world),dFdy(world)));vec3 towardCamera=cameraWorld[3].xyz-world;if(dot(normal,towardCamera)<0.)normal=-normal;
        for(int i=0;i<8;i++){
          float angle=float(i)*.785398;vec2 stepUV=vec2(cos(angle),sin(angle))*texel*(4.+float(i%3)*3.);
          vec2 otherUV=clamp(uvScreen+stepUV,vec2(.001),vec2(.999));float otherDepth=texture2D(sceneDepth,otherUV).r;
          vec3 delta=reconstruct(otherUV,otherDepth)-world;float len=length(delta);
          if(len>.08&&len<18.)occlusion+=max(0.,dot(normal,delta)/len-.1)*(1.-smoothstep(1.,18.,len));
        }
        color*=1.-clamp(occlusion*aoStrength/8.,0.,.23);
      }
    }
    gl_FragColor=vec4(color,1.);
    #include <tonemapping_fragment>
    #include <colorspace_fragment>
    float luminance=dot(gl_FragColor.rgb,vec3(.2126,.7152,.0722));
    vec3 dark=mix(gl_FragColor.rgb,vec3(luminance),.65)*mix(.14,.22,explored)+vec3(.005,.011,.02);
    gl_FragColor.rgb=mix(dark,gl_FragColor.rgb,sight);
    vec2 edge=uvScreen*(1.-uvScreen);float vignette=smoothstep(0.,.12,pow(edge.x*edge.y,.35));
    gl_FragColor.rgb*=mix(.94,1.,vignette);
  }
`;
export class RiftPost {
  constructor(renderer){this.renderer=renderer;this.field=new VisionField(192);this.lastUpdate=-Infinity;this.lastSource=null;this.target=new T.WebGLRenderTarget(1,1,{depthBuffer:true,samples:4});this.target.depthTexture=new T.DepthTexture(1,1,T.UnsignedIntType);this.target.texture.minFilter=this.target.texture.magFilter=T.LinearFilter;this.mask=new T.DataTexture(this.field.current,this.field.size,this.field.size,T.RedFormat,T.UnsignedByteType);this.explored=new T.DataTexture(this.field.explored,this.field.size,this.field.size,T.RedFormat,T.UnsignedByteType);for(const tex of[this.mask,this.explored]){tex.minFilter=tex.magFilter=T.LinearFilter;tex.generateMipmaps=false;tex.needsUpdate=true;}
    this.material=new T.ShaderMaterial({vertexShader:postVertex,fragmentShader:postFragment,depthTest:false,depthWrite:false,uniforms:{sceneColor:{value:this.target.texture},sceneDepth:{value:this.target.depthTexture},visionMask:{value:this.mask},exploredMask:{value:this.explored},inverseProjection:{value:new T.Matrix4()},cameraWorld:{value:new T.Matrix4()},texel:{value:new T.Vector2(1,1)},visionTexel:{value:new T.Vector2(1/this.field.size,1/this.field.size)},boundsMin:{value:VISION_BOUNDS.min},boundsSize:{value:VISION_BOUNDS.size},aoStrength:{value:.9}}});this.quadScene=new T.Scene();this.quadScene.add(new T.Mesh(new T.PlaneGeometry(2,2),this.material));this.camera=new T.Camera();this.fogCanvas=document.createElement('canvas');this.fogCanvas.width=this.fogCanvas.height=this.field.size;this.fogContext=this.fogCanvas.getContext('2d');this.fogImage=this.fogContext.createImageData(this.field.size,this.field.size);
  }
  resize(w,h,ratio){const width=Math.max(1,Math.floor(w*ratio)),height=Math.max(1,Math.floor(h*ratio));this.target.setSize(width,height);this.material.uniforms.texel.value.set(1/width,1/height);}
  reset(role){this.field.reset(role);this.lastUpdate=-Infinity;this.lastSource=null;}
  update(vision,role,time,force=false){const sources=vision.sources(role),leader=sources[0],jump=leader&&this.lastSource&&Math.hypot(leader.x-this.lastSource.x,leader.y-this.lastSource.y)>12;if(!force&&time-this.lastUpdate<.07&&!jump&&this.field.role===role)return;this.lastUpdate=time;this.lastSource=leader?{x:leader.x,y:leader.y}:null;this.field.update(vision,role);this.mask.needsUpdate=this.explored.needsUpdate=true;const data=this.fogImage.data;for(let i=0;i<this.field.current.length;i++){const j=i*4;data[j]=5;data[j+1]=11;data[j+2]=21;data[j+3]=this.field.current[i]?0:this.field.explored[i]?199:221;}this.fogContext.putImageData(this.fogImage,0,0);}
  render(scene,camera){this.material.uniforms.inverseProjection.value.copy(camera.projectionMatrixInverse);this.material.uniforms.cameraWorld.value.copy(camera.matrixWorld);this.renderer.setRenderTarget(this.target);this.renderer.render(scene,camera);this.renderer.setRenderTarget(null);this.renderer.render(this.quadScene,this.camera);}
  clipEffect(material){if(material.userData.visionClipped)return;material.userData.visionClipped=true;const prior=material.onBeforeCompile;material.onBeforeCompile=shader=>{prior?.(shader);shader.uniforms.effectVision={value:this.mask};shader.vertexShader='varying vec3 effectWorld;\n'+shader.vertexShader;shader.vertexShader=shader.vertexShader.replace('#include <project_vertex>','#include <project_vertex>\n effectWorld=(modelMatrix*vec4(transformed,1.)).xyz;');shader.fragmentShader='uniform sampler2D effectVision;varying vec3 effectWorld;\n'+shader.fragmentShader;shader.fragmentShader=shader.fragmentShader.replace('#include <clipping_planes_fragment>','#include <clipping_planes_fragment>\n vec2 evUV=(effectWorld.xz+vec2(60.))/1180.;if(texture2D(effectVision,evUV).r<.5)discard;');};material.customProgramCacheKey=()=> 'vision-clipped-effect-v1';}
  drawMinimap(ctx,size){const start=-VISION_BOUNDS.min/VISION_BOUNDS.size*this.field.size,length=1000/VISION_BOUNDS.size*this.field.size;ctx.drawImage(this.fogCanvas,start,start,length,length,0,0,size,size);}
  quality(high){this.target.samples=high?4:0;this.material.uniforms.aoStrength.value=high?.9:.45;this.target.dispose();}
}

export function addWorldDetails(scene,config,height){let seed=54932;const random=()=>{seed=(seed*1664525+1013904223)>>>0;return seed/4294967296};const geo=new T.DodecahedronGeometry(1,0),stone=surfaceMaps('stone',1),material=new T.MeshStandardMaterial({color:'#879080',roughness:.95,normalMap:stone.normalMap,normalScale:new T.Vector2(.4,.4)}),positions=[];for(let i=0;i<2200;i++){const x=170+random()*760,z=170+random()*760;if(x+z<1060||x+z>1790||Math.abs(x-z)<70)continue;positions.push({x,z,r:.6+random()*1.9});}const pebbles=new T.InstancedMesh(geo,material,positions.length),dummy=new T.Object3D();pebbles.receiveShadow=true;positions.forEach((p,i)=>{dummy.position.set(p.x,height(p.x,p.z)+p.r*.22,p.z);dummy.scale.set(p.r,.4*p.r,p.r*(.7+random()*.5));dummy.rotation.set(random(),random()*6,random());dummy.updateMatrix();pebbles.setMatrixAt(i,dummy.matrix);pebbles.setColorAt(i,new T.Color().setHSL(.14+random()*.08,.10,.4+random()*.15))});scene.add(pebbles);
  const leafPositions=[];for(const w of config.walls)for(let n=0;n<11;n++){const a=random()*Math.PI*2;leafPositions.push({x:w.x+Math.cos(a)*w.rx*.7,z:w.y+Math.sin(a)*w.ry*.7,a});}const leafGeo=new T.SphereGeometry(1,8,5),leafMat=new T.MeshStandardMaterial({color:'#648351',roughness:.95}),leaves=new T.InstancedMesh(leafGeo,leafMat,leafPositions.length*5);leafPositions.forEach((p,i)=>{for(let n=0;n<5;n++){const a=p.a+n/5*Math.PI*2;dummy.position.set(p.x+Math.sin(a)*2,height(p.x,p.z)+4+Math.sin(n)*.5,p.z+Math.cos(a)*2);dummy.rotation.set(Math.cos(a)*.45,a,Math.sin(a)*.45);dummy.scale.set(1.4,.4,4);dummy.updateMatrix();leaves.setMatrixAt(i*5+n,dummy.matrix);}});leaves.receiveShadow=true;scene.add(leaves);
}
