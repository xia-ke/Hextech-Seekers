import test from 'node:test';
import assert from 'node:assert/strict';
import {decodeDepthPixels} from '../dist/rebuild/depth-codec.js';

test('Rift depth codec preserves full uint16 data and transparent sentinel',()=>{
 const values=new Uint8ClampedArray([
   0,4,99,255,
   250,254,12,255,
   3,7,0,0,
   22,11,88,255
 ]);
 assert.deepEqual([...decodeDepthPixels(values,2,2)],[4,64254,65535,5643]);
});
test('depth codec detects invalid tile dimensions before readback conversion',()=>{
 assert.throws(()=>decodeDepthPixels(new Uint8Array(4),2,2),RangeError);
 assert.throws(()=>decodeDepthPixels(new Uint8Array(0),0,0),RangeError);
});
test('depth codec does not alias the source input buffer',()=>{
 const v=new Uint8Array([1,2,0,255]),decoded=decodeDepthPixels(v,1,1);
 v[0]=42;
 assert.equal(decoded[0],258);
});
