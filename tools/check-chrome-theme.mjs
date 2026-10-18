import assert from 'node:assert/strict';
import {setTimeout as delay} from 'node:timers/promises';
import {sidebarHost} from './sidebar-check-host.mjs';

const host=await sidebarHost(process.argv[2]??'src/Browser.App/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Sol.exe');let ui;
const samples=[
 {name:'yellow',bg:'#e7b222',accent:'#f5c518',dark:false},
 {name:'red',bg:'#db1919',accent:'#e33131',dark:false},
 {name:'blue',bg:'#2563d2',accent:'#3975ee',dark:false},
 {name:'neutral',bg:'#f3f3f6',accent:null,dark:false,expected:'#eef0e2'},
 {name:'white',bg:'rgb(255, 255, 255)',accent:null,dark:false,expected:'#eef0e2'},
 {name:'near-white',bg:'rgb(245, 245, 247)',accent:'#3975ee',dark:false,expected:'#eef0e2'},
 {name:'notes',bg:'rgb(238, 240, 226)',accent:null,dark:false},
 {name:'gray',bg:'rgb(220, 220, 220)',accent:null,dark:false},
 {name:'dark',bg:'#101424',accent:'#315adf',dark:true},
 {name:'forced-light',bg:'#080a15',accent:'#315adf',dark:true,mode:'light',expected:'rgb(243, 243, 246)'},
 {name:'forced-dark',bg:'#e7b222',accent:'#f5c518',dark:false,mode:'dark',expected:'rgb(9, 9, 11)'},
 {name:'private',bg:'#e7b222',accent:'#f5c518',dark:false,privateMode:true,expected:'#eee7f7'},
 {name:'no-tint',bg:'#e7b222',accent:'#f5c518',dark:false,tint:{sidebar:false,background:false,topbar:false,menus:false}},
];
try{
 ui=await host.launch(720);
 await host.wait(()=>ui.evaluate('window.__sidebarState.tabs.every(t=>!t.loading)'),'initial local tabs loaded');await delay(800);
 for(const {name,expected,...theme} of samples){
  // Measure in the same task: periodic native theme messages can replace a fixture between CDP calls.
  const colors=await ui.evaluate(`(()=>{chrome.webview.dispatchEvent(new MessageEvent('message',{data:${JSON.stringify({event:'theme',mode:'page',...theme})}}));const canvas=document.createElement('canvas');canvas.width=canvas.height=1;const c=canvas.getContext('2d'),pixel=color=>{c.clearRect(0,0,1,1);c.fillStyle=color;c.fillRect(0,0,1,1);return [...c.getImageData(0,0,1,1).data]},side=getComputedStyle(document.querySelector('.workspace-sidebar'));return {applied:document.documentElement.style.getPropertyValue('--page-bg'),top:pixel(getComputedStyle(document.querySelector('.workspace')).backgroundColor),side:pixel(side.backgroundColor),text:pixel(side.color),image:side.backgroundImage}})()`);
  assert.equal(colors.applied,expected??theme.bg,`${name}: the requested theme was applied`);
  assert.deepEqual(colors.top,colors.side,`${name}: sidebar and top have the same surface color`);
  assert.equal(colors.image,'none',`${name}: no gradient creates a seam at the top`);
  assert.equal(colors.top[3],255,`${name}: chrome stays opaque`);
  const luminance=rgb=>rgb.slice(0,3).map(v=>v/255).map(v=>v<=.04045?v/12.92:((v+.055)/1.055)**2.4).reduce((sum,v,i)=>sum+v*[.2126,.7152,.0722][i],0);
  const a=luminance(colors.side),b=luminance(colors.text);
  assert.ok((Math.max(a,b)+.05)/(Math.min(a,b)+.05)>=4.5,`${name}: sidebar text stays readable`);
 }
 console.log(JSON.stringify({passed:'matching sidebar and top colors; yellow/red/blue/neutral/dark; forced themes; private mode; tint disabled; opaque surfaces and text contrast',evidence:host.base}));
}finally{if(ui)await host.close(ui).catch(()=>{});await host.dispose()}
