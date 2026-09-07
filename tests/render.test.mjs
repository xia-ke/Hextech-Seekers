import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import {VisionSystem} from '../dist/vision.js';
// Exercise the actual render-to-vision integration with only the GPU and DOM replaced.
// This checks hidden models/labels and spectator filtering, not pixels or driver shaders.
class Element{constructor(){this.children=[];this.style={};this.classList={toggle(){},add(){}};this.width=1200;this.height=800;}append(...x){this.children.push(...x)}replaceChildren(){this.children=[]}remove(){}getBoundingClientRect(){return{width:1200,height:800,left:0,top:0}}getContext(){return new Proxy({createImageData:(w,h)=>({data:new Uint8ClampedArray(w*h*4)})},{get:(o,k)=>o[k]||(()=>{})})}addEventListener(){}}
test('hidden enemies have no model, label or picking target; sweep and spectator rules persist',async()=>{
 const nodes=new Map();globalThis.document={getElementById:id=>{if(!nodes.has(id))nodes.set(id,new Element());return nodes.get(id)},createElement:()=>new Element()};globalThis.window={devicePixelRatio:1,innerWidth:1200};globalThis.__riftTestRenderer={shadowMap:{},setPixelRatio(){},getPixelRatio(){return 1},setRenderTarget(){},setSize(){},render(scene){scene.updateMatrixWorld(true)}};
 let source=fs.readFileSync(new URL('../dist/rift3d.js',import.meta.url),'utf8').replace("'./vendor/three.module.min.js'",JSON.stringify(new URL('../dist/vendor/three.module.min.js',import.meta.url).href)).replace("'./graphics.js'",JSON.stringify(new URL('../dist/graphics.js',import.meta.url).href)).replace("new T.WebGLRenderer({canvas,antialias:true,alpha:false,powerPreference:'high-performance'})","globalThis.__riftTestRenderer").replace('this.environment=makeEnvironment(this.renderer)','this.environment={texture:new T.Texture()}');
 const{RiftScene}=await import('data:text/javascript;base64,'+Buffer.from(source).toString('base64'));const context={};vm.createContext(context);vm.runInContext(fs.readFileSync(new URL('../dist/data.js',import.meta.url),'utf8'),context);const config=vm.runInContext('({camps:CAMPS,brushes:BRUSHES,walls:WALLS,champions:CHAMPIONS})',context);config.map={complete:false};const rift=new RiftScene(new Element(),config);
 const unit=(id,role,x,y)=>({id,role,x,y,name:'test',hero:'Garen',active:true,face:{x:1,y:0},cd:{a:0},immune:0,frozen:0,recalling:0,marked:0,hidden:0,lastMove:10,hp:3,scan:0});const a=unit(0,'seeker',880,750),b=unit(1,'hider',900,760),vision=new VisionSystem(config),state={vision,players:[a,b],now:10,phase:'hunt',clock:200,duel:false,paused:false,visible:(p,t)=>vision.unitVisible(p.role,t),effects:[],wards:[],traps:[],fruits:[],tide:false};const render=()=>{vision.sync(state);rift.render(state)};
 render();assert.equal(rift.models.get(1).root.visible,true);
 b.y=300;render();assert.equal(rift.models.get(1).root.visible,false);assert.equal(rift.labels.get(1).hidden,true);assert.notEqual(rift.pickUnit(600,400),1);
 a.x=800;a.y=840;b.x=843;b.y=841;render();assert.equal(rift.models.get(1).root.visible,false);
 a.x=830;a.y=841;render();assert.equal(rift.models.get(1).root.visible,true);
 b.hero='Teemo';b.hidden=30;a.scan=20;render();assert.equal(rift.models.get(1).root.visible,false);assert.equal(rift.labels.get(1).hidden,true);assert(rift.silhouettes.has(1));assert.equal(vision.unitVisible(a.role,b),false);
 a.active=false;a.scan=0;b.hidden=0;b.lastMove=10;render();assert.equal(rift.models.get(1).root.visible,false);assert.equal(rift.labels.get(1).hidden,true);
 rift.resetFollow();assert.equal(rift.models.size,0);assert.equal(rift.post.field.explored.some(Boolean),false);
 delete globalThis.__riftTestRenderer;delete globalThis.document;delete globalThis.window;
});
