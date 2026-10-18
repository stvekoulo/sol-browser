import {generateKeyPairSync,createHash} from 'node:crypto';
import {mkdir,writeFile} from 'node:fs/promises';
import {resolve} from 'node:path';

export async function extensionFixture(host,{name='Action fixture',panel=false,popup=false,click=false}={}){
  const {publicKey}=generateKeyPairSync('rsa',{modulusLength:2048});
  const key=publicKey.export({type:'spki',format:'der'});
  const id=[...createHash('sha256').update(key).digest().subarray(0,16)]
    .flatMap(b=>[b>>4,b&15]).map(n=>String.fromCharCode(97+n)).join('');
  const folder=resolve(host.profile,'extensions',id);await mkdir(folder,{recursive:true});
  const manifest={name,version:'1.0',manifest_version:3,key:key.toString('base64'),permissions:['tabs','storage'],
    content_scripts:[{matches:['<all_urls>'],js:['content.js'],run_at:'document_start'}],
    action:popup?{default_popup:'view.html'}:{default_title:name,...(panel?{default_popup:''}:{})},
    ...(panel?{side_panel:{default_path:'view.html'}}:{}),
    update_url:'https://clients2.google.com/service/update2/crx',options_page:'settings.html'};
  if(click){
    manifest.permissions=['activeTab','scripting','storage'];
    manifest.background={service_worker:'background.js'};
    delete manifest.content_scripts;
    await writeFile(resolve(folder,'background.js'),`chrome.contextMenus.removeAll(()=>chrome.contextMenus.create({id:'fixture.open',title:'Fixture context action',contexts:['all']}));
    chrome.contextMenus.onClicked.addListener(async(info,tab)=>{await chrome.scripting.executeScript({target:{tabId:tab.id},func:()=>document.documentElement.dataset.solContext='yes'});});
    chrome.action.onClicked.addListener(async tab=>{
      try{await chrome.scripting.executeScript({target:{tabId:tab.id},func:()=>document.documentElement.dataset.solAction=String(Number(document.documentElement.dataset.solAction??0)+1)});}
      catch(error){await chrome.storage.local.set({error:error.message});}
    });`);
  }
  await writeFile(resolve(folder,'manifest.json'),JSON.stringify(manifest));
  await writeFile(resolve(folder,'view.html'),`<!doctype html><html><head><title>${name}</title></head><body style="${panel?'margin:0;height:100vh':'margin:0;width:320px;height:150px'}"><p>${name}</p><script src="view.js"></script></body></html>`);
  await writeFile(resolve(folder,'view.js'),`chrome.tabs.query({active:true,currentWindow:true}).then(t=>{document.body.dataset.active=t[0]?.url??'';});`);
  await writeFile(resolve(folder,'content.js'),`document.addEventListener('DOMContentLoaded',()=>document.documentElement.dataset.solExtension=chrome.runtime.id);`);
  await writeFile(resolve(folder,'settings.html'),'<!doctype html><title>Fixture settings</title>Settings');
  return {id,folder,manifest};
}
