import assert from 'node:assert/strict';
import {readFile, writeFile, access} from 'node:fs/promises';
import {resolve} from 'node:path';
import {sidebarHost} from './sidebar-check-host.mjs';

const script=await readFile('src/Browser.App/inject/agent.js','utf8');
const fixture=`<!doctype html><html lang="fr"><title>Actions Sol AI</title><style>body{font:16px system-ui;padding:30px}label{display:block;margin:10px}iframe{height:80px}#filler{height:1400px}</style>
<main><h1>Formulaire de vérification</h1><form id="form">
<label>Nom complet <input name="name" maxlength="60" value="private-unsubmitted-name"></label>
<label>Courriel <input name="email" type="email"></label><label>Message <textarea name="message"></textarea></label>
<label>Pays <select name="country"><option value="">Choisir</option><option value="bj">Bénin</option><option value="fr">France</option><option disabled value="bad">Indisponible</option></select></label>
<label><input type="checkbox" name="newsletter">Recevoir les nouvelles</label>
<label><input type="radio" name="choice" value="one">Premier choix</label>
<label>Mot de passe <input type="password" value="private-password"></label>
<label>Code OTP <input name="otp" value="private-otp"></label><input type="file" name="attachment">
<label>Champ désactivé <input disabled name="disabled"></label><label>Lecture seule <input readonly name="readonly"></label>
<button id="submit">Envoyer</button></form><button id="menu">Menu</button><div id="status"></div><div id="shadow"></div>
<iframe title="Formulaire intégré" src="/frame"></iframe><div id="filler"></div><p>Fin de la page</p></main>
<script>window.events=[];for(const type of ['input','change'])document.addEventListener(type,e=>events.push(type+':'+e.target.name));
window.submissions=0;form.addEventListener('submit',e=>{e.preventDefault();submissions++;status.textContent='Formulaire reçu'});
menu.addEventListener('mouseenter',()=>menu.textContent='Menu ouvert');shadow.attachShadow({mode:'open'}).innerHTML='<label>Ville<input name="city"></label>';</script></html>`;
const host=await sidebarHost('src/Browser.App/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Sol.exe',{serve:(req,res)=>{
  res.writeHead(200,{'Content-Type':'text/html; charset=utf-8'});
  res.end(req.url==='/frame'?'<label>Organisation <input name="organization"></label>':fixture);return true;
}});
const results=[];
try {
  const ui=await host.launch(900,1250);
  const page=await host.connect(host.origin+'/2');
  const action=expression=>page.evaluate(`(async()=>{const A=${script};return await ${expression}})()`);
  const snapshot=await action('A.snapshot()');
  const ref=name=>{const entry=snapshot.controls.find(c=>c.label===name);assert(entry,`control ${name} exists`);return entry.ref;};
  assert(!JSON.stringify(snapshot).includes('private-'),'existing field contents and credentials stay out of snapshots');
  assert(snapshot.controls.some(c=>c.label==='Ville'),'open shadow DOM controls are exposed');
  assert(snapshot.controls.some(c=>c.label==='Organisation'),'same-origin iframe controls are exposed');
  assert(!snapshot.controls.some(c=>/password|otp/i.test(c.kind+' '+c.label)),'sensitive inputs are excluded');
  const fields=[{ref:ref('Nom complet'),kind:'text',value:'Steven Test'},{ref:ref('Courriel'),kind:'text',value:'test@example.invalid'},
    {ref:ref('Message'),kind:'text',value:'Préparer un espace de travail.'},{ref:ref('Pays'),kind:'select',value:'Bénin'},
    {ref:ref('Recevoir les nouvelles'),kind:'checked',value:'true'},{ref:ref('Premier choix'),kind:'checked',value:'true'},
    {ref:ref('Ville'),kind:'text',value:'Cotonou'},{ref:ref('Organisation'),kind:'text',value:'Sol'}];
  const filled=await action(`A.fillForm(${JSON.stringify(fields)})`);
  assert.equal(filled.ok,true);assert.equal(filled.submitted,false);assert.equal(filled.completed.length,8);
  assert.equal(await page.evaluate('submissions'),0,'batch never submits a form');
  assert.deepEqual(await page.evaluate(`[form.elements.name.value,form.elements.email.value,form.elements.country.value,form.elements.newsletter.checked,document.querySelector('#shadow').shadowRoot.querySelector('input').value,document.querySelector('iframe').contentDocument.querySelector('input').value]`),
    ['Steven Test','test@example.invalid','bj',true,'Cotonou','Sol']);
  assert((await page.evaluate('events')).includes('input:name'),'framework input events fire');
  results.push('form batch: text, email, textarea, select, checkbox, radio, shadow root, iframe; no submission');
  const invalid=await action(`A.fillForm(${JSON.stringify([{ref:ref('Nom complet'),kind:'text',value:'Unwanted change'},{ref:'stale',kind:'text',value:'x'}])})`);
  assert.equal(invalid.ok,false);assert.equal(await page.evaluate('form.elements.name.value'),'Steven Test','invalid batch is rejected before writes');
  assert.equal((await action(`A.fill(${JSON.stringify(ref('Nom complet'))},${JSON.stringify('a'.repeat(61))})`)).ok,false,'maxlength is respected');
  assert.equal((await action(`A.fill(${JSON.stringify(ref('Lecture seule'))},'x')`)).ok,false,'readonly is protected');
  assert.equal((await action(`A.select(${JSON.stringify(ref('Pays'))},'bad')`)).ok,false,'disabled options are protected');
  assert.equal((await action(`A.check(${JSON.stringify(ref('Premier choix'))},false)`)).ok,false,'radio cannot be unchecked directly');
  assert.equal((await action(`A.fill(${JSON.stringify(ref('attachment'))},'C:/secret.txt')`)).ok,false,'file uploads cannot be filled');
  results.push('invalid refs, overlong values, readonly fields, disabled options and file inputs rejected');
  const point=await action(`A.hoverPoint(${JSON.stringify(ref('Menu'))})`);
  await page.call('Input.dispatchMouseEvent',{type:'mouseMoved',x:point.x,y:point.y});
  await host.wait(()=>page.evaluate('document.querySelector("#menu").textContent==="Menu ouvert"'),'native hover');
  assert.equal((await action(`A.focus(${JSON.stringify(ref('Envoyer'))})`)).ok,true);
  await page.call('Input.dispatchKeyEvent',{type:'keyDown',key:'Enter',code:'Enter',windowsVirtualKeyCode:13,text:'\r'});
  await page.call('Input.dispatchKeyEvent',{type:'keyUp',key:'Enter',code:'Enter',windowsVirtualKeyCode:13});
  assert.equal(await page.evaluate('submissions'),1,'native Enter submits only as a separate action');
  results.push('native pointer hover and keyboard Enter');
  await action(`A.scroll('bottom')`);assert(await page.evaluate('scrollY>0'));
  await action(`A.scroll('top')`);assert.equal(await page.evaluate('scrollY'),0);
  await action('A.snapshot()');assert.equal((await action(`A.fill(${JSON.stringify(ref('Nom complet'))},'stale')`)).ok,false,'snapshot invalidates old references');
  results.push('scrolling and rejection of stale snapshot references');
  await writeFile(resolve(host.base,'agent-actions.png'),Buffer.from((await page.call('Page.captureScreenshot',{format:'png'})).data,'base64'));
  await writeFile(resolve(host.base,'agent-actions.json'),JSON.stringify({results,realChatGptInference:false},null,2));
  await host.close(ui);
  console.log('PASS '+results.join('; ')+'; '+host.base);
} finally {await host.dispose();await assert.rejects(access(host.profile));}
