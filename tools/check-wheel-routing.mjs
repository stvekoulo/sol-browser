import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import vm from 'node:vm';

const script=await readFile('src/Browser.App/inject/page-wheel.js','utf8');
const listeners=[],messages=[],pending=[];
const context=vm.createContext({
  addEventListener:(type,handler)=>listeners.push(handler),
  chrome:{webview:{postMessage:message=>messages.push(message)}},
  setTimeout:handler=>pending.push(handler),
});
vm.runInContext(script.replaceAll('__SOL_WHEEL_KEY__','test-key'),context);
const wheel=(overrides={},consume=false)=>{
  const event={isTrusted:true,ctrlKey:true,deltaY:-120,deltaX:0,defaultPrevented:false,...overrides};
  for(const handler of listeners)handler(event);
  if(consume)event.defaultPrevented=true;
  while(pending.length)pending.shift()();
};
wheel({},true);assert.equal(messages.length,0,'editor owns cancelled zoom');
wheel();assert.equal(messages.pop(),'sol.wheel:test-key:in','normal page zooms in');
wheel({deltaY:120});assert.equal(messages.pop(),'sol.wheel:test-key:out');
wheel({ctrlKey:false});wheel({isTrusted:false});wheel({deltaY:0,deltaX:120});
assert.equal(messages.length,0,'ordinary scrolling, synthetic and horizontal gestures ignored');
console.log('PASS editor priority and normal Ctrl+wheel routing');
