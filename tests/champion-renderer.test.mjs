import test from 'node:test';
import assert from 'node:assert/strict';
import {HERO_ASSET_NAMES,renderableOpponents,ChampionAtlasRenderer,championVisualScale} from '../dist/rebuild/champion-renderer.js';

test('all four unique roster names map to individual source asset folders',()=>{
 assert.deepEqual(Object.keys(HERO_ASSET_NAMES),['盖伦','布里茨','锐雯','亚索']);
 assert.equal(new Set(Object.values(HERO_ASSET_NAMES)).size,4);
});
test('per-hero scale metadata normalizes original mesh height and rejects invalid values',()=>{
 assert.equal(championVisualScale({visualScale:1.725}),1.725);
 assert.equal(championVisualScale({visualScale:2.2742}),2.2742);
 assert.equal(championVisualScale({visualScale:0}),1);
 assert.equal(championVisualScale({visualScale:NaN}),1);
 assert.equal(championVisualScale({}),1);
});
test('hidden opponents, inactive targets, and player are never drawn',()=>{
 const actors=new Map([
   ['player',{id:'player',role:'hunter',active:true}],
   ['ai-1',{id:'ai-1',role:'hider',active:true}],
   ['ai-2',{id:'ai-2',role:'hider',active:true}],
   ['ai-3',{id:'ai-3',role:'hider',active:false}],
 ]);
 const session={actors,snapshot:()=>({visible:[{id:'ai-1'},{id:'ai-3'}]})};
 assert.deepEqual(renderableOpponents(session).map(x=>x.id),['ai-1']);
 assert.deepEqual(renderableOpponents(null),[]);
});
test('sprite loader refuses absent original assets without constructing fake model',async()=>{
 const previousDocument=globalThis.document,previousFetch=globalThis.fetch;
 try{
   globalThis.document={createElement:()=>({
      width:0,height:0,
      getContext:()=>({createImageData:()=>({data:new Uint8ClampedArray(256*256*4)})})
   })};
   globalThis.fetch=async()=>({ok:false});
   const renderer=new ChampionAtlasRenderer({
     map:{pitch:55},tiles:{},ctx:{},screen:x=>x,loadImage:async()=>null,heightAt:()=>0
   });
   const result=await renderer.loadRoster(['布里茨','锐雯','亚索']);
   assert.deepEqual(result,{'布里茨':false,'锐雯':false,'亚索':false});
   assert.equal(renderer.resources.size,0);
   assert.equal(renderer.failures.size,3);
   assert.equal(renderer.draw({active:true},'锐雯',{zoom:1}),false);
 }finally{
   if(previousDocument===undefined)delete globalThis.document;else globalThis.document=previousDocument;
   if(previousFetch===undefined)delete globalThis.fetch;else globalThis.fetch=previousFetch;
 }
});
