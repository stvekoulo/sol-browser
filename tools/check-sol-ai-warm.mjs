// Two requests, one native model; no trace or navigation content written to disk.
import {spawn} from 'node:child_process';
import {once} from 'node:events';
import assert from 'node:assert/strict';
const [exe,cache,model]=process.argv.slice(2);
if(!model)throw Error('Usage: node tools/check-sol-ai-warm.mjs <exe> <cache> <model>');
const processAI=spawn(exe,['generate',cache,model],{windowsHide:true,stdio:['pipe','pipe','pipe'],env:{...process.env,SOL_AI_OFFLINE_TEST:'1',SOL_AI_KEEP_ALIVE:'1',SOL_AI_IDLE_MINUTES:'45'}});
const requests=[{action:'summary',text:'The garden opens Saturday. Volunteers plant ten apple trees.',language:'fr'}, {action:'translate',text:'Hello, the meeting starts tomorrow at ten.',language:'fr',thinking:false}];
let buffer='',index=0,loads=0,thinking=0,results=[];
const timeout=setTimeout(()=>processAI.kill(),240000);processAI.stderr.resume();
processAI.stdout.on('data',chunk=>{
 timeout.refresh();buffer+=chunk;
 for(let end;(end=buffer.indexOf('\n'))>=0;){const line=buffer.slice(0,end).trim();buffer=buffer.slice(end+1);if(!line.startsWith('@sol-ai:'))continue;const m=JSON.parse(line.slice(8));
  if(m.kind==='error')console.log(m);
  if(m.kind==='phase'&&m.phase==='loading')loads++;
  if(m.kind==='phase'&&m.phase==='thinking')thinking++;
  if(m.kind==='generated'){results.push(m);console.log(JSON.stringify({answerCharacters:m.text.length,loadMs:m.loadMs,elapsedMs:m.elapsedMs,incomplete:m.incomplete,memoryMb:m.memoryMb}));index++;}
  if(m.kind==='phase'&&m.phase==='ready'){if(index<requests.length)processAI.stdin.write(JSON.stringify(requests[index])+'\n');else processAI.stdin.write('cancel\n');}
 }
});
processAI.stdin.write(JSON.stringify(requests[0])+'\n');
const [code]=await once(processAI,'exit');clearTimeout(timeout);
assert.equal(code,1);assert.equal(results.length,2);assert.equal(loads,1);assert.ok(thinking>=2);
assert.ok(results.every(m=>!m.incomplete&&!/<think>|analysis|reasoning mode/i.test(m.text)));
assert.match(results[0].text,/samedi/i);assert.match(results[0].text,/pommier|pommes/i);
assert.doesNotMatch(results[0].text,/terre|okay|let me|the user/i);
assert.match(results[1].text,/bonjour[\s\S]*demain[\s\S]*(dix|10)/i);
assert.equal(results[1].loadMs,0);console.log('PASS Think responses, one model load, warm reuse and cancellation/shutdown');
