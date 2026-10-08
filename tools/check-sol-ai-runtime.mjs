import {spawn} from 'node:child_process';
import {once} from 'node:events';
import assert from 'node:assert/strict';
const [exe,cache,model]=process.argv.slice(2);
if(!exe||!cache||!model)throw Error('Usage: node tools/check-sol-ai-runtime.mjs <exe> <cache> <model-id>');
async function run(request,cancel=false) {
  const p=spawn(exe,['generate',cache,model],{windowsHide:true,stdio:['pipe','pipe','pipe'],env:{...process.env,SOL_AI_OFFLINE_TEST:'1'}});
  let buffer='',sent=false;const messages=[];const started=Date.now();
  const timeout=setTimeout(()=>p.kill(),240000);
  p.stderr.resume();p.stdout.on('data',chunk=>{
    timeout.refresh();buffer+=chunk;
    for(let end;(end=buffer.indexOf('\n'))>=0;) {
      const line=buffer.slice(0,end).trim();buffer=buffer.slice(end+1);
      if(!line.startsWith('@sol-ai:'))continue;
      const message=JSON.parse(line.slice(8));messages.push(message);
      if(cancel&&!sent&&message.kind==='phase'&&message.phase==='generating'){sent=true;p.stdin.write('cancel\n');}
      if(message.kind==='progress')console.log('Completed '+message.percent+'%');
    }
  });
  p.stdin.write(JSON.stringify(request)+'\n');
  const [code]=await once(p,'exit');clearTimeout(timeout);
  if(cancel){assert.equal(sent,true);assert.equal(code,1);assert.ok(messages.some(m=>m.kind==='cancelled'));assert.ok(!messages.some(m=>m.kind==='generated'));}
  else {assert.equal(code,0,JSON.stringify(messages.filter(m=>m.kind==='error')));assert.ok(messages.filter(m=>m.kind==='progress').length>=3);const result=messages.find(m=>m.kind==='generated');assert.equal(result?.incomplete,false);assert.match(result.text,/cerisier/i);}
  console.log(cancel?'PASS cancellation after real token generation':'PASS complete long translation; '+Math.round((Date.now()-started)/1000)+'s');
}
const text=Array.from({length:60},(_,i)=>`Section ${i+1}. The community garden is open to everyone. Volunteers plant apple trees and water the flowers every Saturday.\n`).join('')+'The last tree is a cherry tree.';
assert.ok(text.length>6000);
await run({action:'translate',text,language:'fr'},true);
await run({action:'translate',text,language:'fr'});
