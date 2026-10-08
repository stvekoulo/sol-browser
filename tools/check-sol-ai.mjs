import {spawn} from 'node:child_process';
import {once} from 'node:events';
import assert from 'node:assert/strict';
import {writeFile} from 'node:fs/promises';
const [exe,cache,model]=process.argv.slice(2); if(!exe||!cache||!model)throw Error('Usage: node tools/check-sol-ai.mjs <Sol.AI.exe> <cache> <model-id>');
const runs=[]; const failures=[];
for(const [Action,Text,Language,Instruction] of [
 ['summary','The community garden opens Saturday. Volunteers will plant ten apple trees. Everyone is welcome.','fr',''],
 ['explain','A browser cookie stores a small piece of information for a website.','fr',''],
 ['improve','bonjour je voudrai savoir si vous ete disponible demain','fr',''],
 ['rephrase','We would like to arrange a meeting tomorrow.','en',''],
 ['write','','fr','Écris une courte invitation à une réunion demain.'],
 ['write','','fr','Rédige un message pour demander à Léa si elle peut relire mon document.'],
 ['translate','Hello, the meeting starts tomorrow at ten.','fr','']]) {
 const proc=spawn(exe,['generate',cache,model],{windowsHide:true,stdio:['pipe','pipe','pipe'],env:{...process.env,SOL_AI_OFFLINE_TEST:'1'}});
 let output='';proc.stdout.on('data',c=>output+=c);proc.stderr.resume();
 proc.stdin.write(JSON.stringify({action:Action,text:Text,language:Language,instruction:Instruction})+'\n');
 const timer=setTimeout(()=>proc.kill(),240000); proc.stdout.on('data',()=>timer.refresh());
 const [code]=await once(proc,'exit');clearTimeout(timer);
 const data=output.split(/\r?\n/).filter(x=>x.startsWith('@sol-ai:')).map(x=>JSON.parse(x.slice(8)));
 assert.equal(code,0,JSON.stringify(data));const generated=data.find(x=>x.kind==='generated');assert.ok(generated?.text.length);assert.ok(!/<think>/i.test(generated.text));assert.equal(generated.incomplete,false);
 const checks={summary: /pommier|pommes/i, explain:/cookie|témoin/i, improve:/voudrais[\s\S]*êtes/i, rephrase:/meeting|meet/i, write:Instruction.includes('Léa')?/Léa|relire|relecture/i:/réunion/i, translate:/bonjour[\s\S]*demain[\s\S]*(dix|10)/i}; try { assert.match(generated.text,checks[Action]); if(Action==='write'){assert.ok(!generated.text.includes('Écris une courte'));assert.ok(!/10h|10 h|projet/i.test(generated.text));} if(Action==='summary')assert.ok(!/sapin|préambule|analyse|faits inventés/i.test(generated.text)); } catch(e) { failures.push(Action+': '+e.message); } runs.push({action:Action,...generated});console.log(Action+': '+generated.text+' [peak '+generated.memoryMb+' MB]');
}
await writeFile('bin/sol-ai-validation.json',JSON.stringify(runs,null,2));
assert.deepEqual(failures,[]); console.log('PASS offline actions with semantic checks');
