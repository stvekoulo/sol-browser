import assert from 'node:assert/strict';
import {createServer} from 'node:http';
import {spawn} from 'node:child_process';
import {once} from 'node:events';
import {mkdir,readFile,writeFile} from 'node:fs/promises';
import {resolve} from 'node:path';
const dir=resolve(`bin/ci-upload-check-${Date.now()}`),payload=Buffer.alloc(65536,7);
await mkdir(dir,{recursive:true});const file=resolve(dir,'payload.bin');await writeFile(file,payload);
const attempts={recover:0,fail:0};
const server=createServer(async(req,res)=>{
  try{
    const mode=req.url.split('/')[1],chunks=[];for await(const chunk of req)chunks.push(chunk);
    assert.equal(req.method,'PUT');assert.equal(req.headers['content-type'],'application/octet-stream');
    assert.deepEqual(Buffer.concat(chunks),payload,'every retry must resend the complete file');
    attempts[mode]++;const ok=mode==='recover'&&attempts[mode]===3;
    res.writeHead(ok?201:500,{'Content-Type':'application/json'});res.end(JSON.stringify({message:ok?'201 Created':'500 Internal Server Error'}));
  }catch(error){res.destroy(error);server.emit('error',error);}
}).listen(0,'127.0.0.1');await once(server,'listening');
const origin=`http://127.0.0.1:${server.address().port}`;
const yaml=await readFile('.gitlab-ci.yml','utf8');
const command=yaml.split('\n').find(line=>line.trim().startsWith('- foreach ($f in $files)')).trim().slice(2).replace('-RetryIntervalSec 15','-RetryIntervalSec 1');
const quote=value=>"'"+value.replaceAll("'","''")+"'";
const run=async(mode)=>{
  const app=spawn('pwsh',['-NoProfile','-Command',`$ErrorActionPreference='Stop'; $files=@(Get-Item -LiteralPath ${quote(file)}); $headers=@{'JOB-TOKEN'='fixture'}; $base=${quote(origin+'/'+mode)}; ${command}`],{windowsHide:true});
  let output='';app.stdout.on('data',data=>output+=data);app.stderr.on('data',data=>output+=data);
  const [code]=await once(app,'exit');return {code,output};
};
try{
  const recovered=await run('recover');assert.equal(recovered.code,0,recovered.output);assert.equal(attempts.recover,3);
  const failed=await run('fail');assert.notEqual(failed.code,0,'persistent server errors must stop publication');assert.equal(attempts.fail,3,'retry count stays bounded');
  console.log(JSON.stringify({passed:'actual CI upload command: recovers after two HTTP 500s; exact complete bytes on every retry; persistent failure stops after three attempts',evidence:dir}));
}finally{await new Promise(resolve=>server.close(resolve));}