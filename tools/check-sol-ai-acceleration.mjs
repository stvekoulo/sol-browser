import {spawn} from 'node:child_process';
import {once} from 'node:events';
import assert from 'node:assert/strict';
const [exe,cache,model]=process.argv.slice(2);
if(!model)throw Error('Usage: node tools/check-sol-ai-acceleration.mjs <exe> <cache> <model>');
async function run(operation,id,onMessage) {
  const p=spawn(exe,[operation,cache,id],{windowsHide:true,stdio:['pipe','pipe','pipe'],env:{...process.env,...(operation==='warm'?{SOL_AI_OFFLINE_TEST:'1'}:{}),SOL_AI_KEEP_ALIVE:'1',SOL_AI_IDLE_MINUTES:'45'}});
  let buffer='';const messages=[];const timeout=setTimeout(()=>p.kill(),240000);p.stderr.resume();
  p.stdout.on('data',chunk=>{
    timeout.refresh();buffer+=chunk;
    for(let end;(end=buffer.indexOf('\n'))>=0;){const line=buffer.slice(0,end).trim();buffer=buffer.slice(end+1);if(!line.startsWith('@sol-ai:'))continue;
      const m=JSON.parse(line.slice(8));messages.push(m);onMessage?.(m,p);
    }
  });
  const [code]=await once(p,'exit');clearTimeout(timeout);return {code,messages};
}
const optimized=await run('optimize',model);
assert.equal(optimized.code,0,JSON.stringify(optimized.messages.filter(m=>m.kind==='error')));
const choice=optimized.messages.find(m=>m.kind==='optimized');assert.ok(choice);
assert.ok(['gpu','cpu-faster','gpu-unavailable'].includes(choice.status));
assert.equal(choice.id.includes('-gpu:'),choice.status==='gpu');
if(choice.status==='gpu')assert.ok(choice.gpuMs<choice.cpuMs*.8);
const catalog=optimized.messages.find(m=>m.kind==='catalog');assert.ok(catalog.models.some(m=>m.id===choice.id&&m.cached));
console.log(JSON.stringify(choice));
let sent=false,finished=false;const partials=[];
const warm=await run('warm',choice.id,(m,p)=>{
  if(m.kind==='phase'&&m.phase==='ready'){
    if(!sent){sent=true;p.stdin.write(JSON.stringify({action:'translate',text:'Hello, the meeting starts tomorrow at ten.',language:'fr',thinking:false})+'\n');}
    else if(finished)p.stdin.write('cancel\n');
  }
  if(m.kind==='partial')partials.push(m.text);
  if(m.kind==='generated'){finished=true;assert.equal(m.loadMs,0);assert.equal(m.incomplete,false);assert.match(m.text,/bonjour[\s\S]*demain[\s\S]*(dix|10)/i);console.log(JSON.stringify({device:m.device,elapsedMs:m.elapsedMs,loadMs:m.loadMs,partialCount:partials.length}));}
});
assert.equal(warm.code,1);assert.ok(finished);assert.ok(partials.length>0);assert.ok(partials.every(t=>!/<think>|analysis|the user|let me/i.test(t)));
console.log('PASS measured hardware selection, background preparation, progressive output and cancellation');
