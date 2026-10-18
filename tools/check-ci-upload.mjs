import assert from 'node:assert/strict';
import {createServer} from 'node:http';
import {spawn} from 'node:child_process';
import {once} from 'node:events';
import {mkdir,readFile,writeFile} from 'node:fs/promises';
import {resolve} from 'node:path';

const dir=resolve(`bin/ci-upload-check-${Date.now()}`);
const payloads={'payload.bin':Buffer.alloc(65536,7),'next file.bin':Buffer.alloc(98304,11)};
await mkdir(dir,{recursive:true});
for(const [name,bytes] of Object.entries(payloads))await writeFile(resolve(dir,name),bytes);
const attempts={},times={};
const server=createServer(async(req,res)=>{
  try{
    const [,mode,encodedName]=req.url.split('/'),name=decodeURIComponent(encodedName);
    const key=`${mode}/${name}`,chunks=[];
    for await(const chunk of req)chunks.push(chunk);
    assert.equal(req.method,'PUT');
    assert.equal(req.headers['content-type'],'application/octet-stream');
    assert.equal(req.headers['job-token'],'fixture');
    assert.deepEqual(Buffer.concat(chunks),payloads[name],'every retry must resend the complete file');
    attempts[key]=(attempts[key]??0)+1;
    (times[key]??=[]).push(Date.now());
    const count=attempts[key];
    if((mode==='reset'&&count<3)||(mode==='multi'&&name==='next file.bin'&&count<3)){
      res.destroy();return;
    }
    const status=mode==='unauthorized'?401:mode==='forbidden'?403:
      mode==='fail'||(mode==='recover'&&count<5)?503:
      mode==='http500'&&count<3?500:mode==='limited'&&count===1?429:201;
    res.writeHead(status,{'Content-Type':'application/json',...(status===429?{'Retry-After':'1'}:{})});
    res.end(JSON.stringify({message:String(status)}));
  }catch(error){res.destroy(error);server.emit('error',error);}
}).listen(0,'127.0.0.1');
await once(server,'listening');
const origin=`http://127.0.0.1:${server.address().port}`;
const yaml=await readFile('.gitlab-ci.yml','utf8');
const command=yaml.split('\n').find(line=>line.trim().startsWith('- ./tools/ci-upload.ps1')).trim().slice(2);
const quote=value=>"'"+value.replaceAll("'","''")+"'";
const run=async(mode,delay=0)=>{
  const app=spawn('pwsh',['-NoProfile','-Command',
    `$ErrorActionPreference='Stop'; $files=@(Get-Item -LiteralPath ${Object.keys(payloads).map(name=>quote(resolve(dir,name))).join(',')}); `+
    `$headers=@{'JOB-TOKEN'='fixture'}; $base=${quote(origin+'/'+mode)}; ${command} -RetryDelaySeconds ${delay}; Write-Host 'UPLOAD_COMPLETE'`
  ],{windowsHide:true});
  let output='';
  app.stdout.on('data',data=>output+=data);app.stderr.on('data',data=>output+=data);
  const [code]=await once(app,'close');return {code,output};
};
try{
  for(const mode of ['reset','http500','multi','limited','recover']){
    const result=await run(mode,mode==='recover'?1:0);
    assert.equal(result.code,0,`${mode} must recover: ${result.output}`);
    assert.match(result.output,/UPLOAD_COMPLETE/);
  }
  assert.equal(attempts['reset/payload.bin'],3);
  assert.equal(attempts['http500/payload.bin'],3);
  assert.equal(attempts['multi/payload.bin'],1,'a later failure must not resend completed files');
  assert.equal(attempts['multi/next file.bin'],3);
  assert.equal(attempts['limited/payload.bin'],2);
  assert.ok(times['limited/payload.bin'][1]-times['limited/payload.bin'][0]>=950,'respect Retry-After');
  for(const name of Object.keys(payloads)){
    assert.equal(attempts[`recover/${name}`],5);
    const intervals=times[`recover/${name}`];
    for(let i=1;i<5;i++)assert.ok(intervals[i]-intervals[i-1]>=1000*2**(i-1)-50,'progressive retry delay');
  }
  for(const [mode,count] of [['fail',5],['unauthorized',1],['forbidden',1]]){
    const result=await run(mode);
    assert.notEqual(result.code,0,`${mode} must stop publication`);
    assert.doesNotMatch(result.output,/UPLOAD_COMPLETE/,'no release after an unsuccessful upload');
    assert.equal(attempts[`${mode}/payload.bin`],count,'retry count stays bounded');
    assert.equal(attempts[`${mode}/next file.bin`],undefined,'do not publish remaining files after failure');
  }
  console.log(JSON.stringify({passed:'actual CI upload: resets, HTTP 500/503/429, complete bytes, progressive delays, per-file retries, bounded failure, immediate 401/403 stop',evidence:dir}));
}finally{await new Promise(resolve=>server.close(resolve));}
