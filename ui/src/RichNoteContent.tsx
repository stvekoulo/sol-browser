import { useEffect,useRef } from 'react';
import Vditor from 'vditor';
import iconsSource from 'vditor/dist/js/icons/ant.js?raw';
import { t,useLang } from './i18n';
import { attachNoteBlob } from './NoteAudio';
import 'vditor/dist/index.css';
export default function RichNoteContent({value,mode,noteId,change,save}:{value:string;mode:'wysiwyg'|'ir';noteId:string;change:(text:string)=>void;save:()=>void}){
  const el=useRef<HTMLDivElement>(null),engine=useRef<Vditor|null>(null),ready=useRef(false),latest=useRef({value,noteId,change,save});latest.current={value,noteId,change,save};
  const lang=useLang();
  useEffect(()=>{
    if(!el.current)return;
    // Vditor loads this sprite as inline script; install the bundled SVG under Sol's CSP instead.
    if(!document.getElementById('vditorIconScript')){
      const svg=iconsSource.match(/<svg[\s\S]*<\/svg>/)?.[0];
      if(!svg)throw new Error('Missing bundled editor icons');
      const holder=document.createElement('div');holder.innerHTML=svg.replace(/style="[^"]*"/,'id="vditorIconScript" aria-hidden="true"');
      document.body.prepend(holder.firstElementChild!);
    }
    const editor=new Vditor(el.current,{mode,lang:lang==='fr'?'fr_FR':'en_US',cdn:new URL('./vditor',location.href).href,
      value:latest.current.value,width:'100%',minHeight:260,cache:{enable:false},placeholder:t('Écrivez votre note…','Write your note…'),
      toolbar:['headings','bold','italic','strike','list','ordered-list','check','quote','line','code','inline-code','link','table','undo','redo','edit-mode','outline'],
      toolbarConfig:{pin:false},counter:{enable:true,max:50000},outline:{enable:false,position:'right'},theme:document.documentElement.dataset.theme==='dark'?'dark':'classic',
      preview:{mode:'editor',markdown:{sanitize:true,codeBlockPreview:false,mathBlockPreview:false}},
      input:text=>{if(text.length<=50000)latest.current.change(text)},ctrlEnter:()=>latest.current.save(),
      upload:{handler:files=>{for(const file of files)attachNoteBlob(file,file.name,latest.current.noteId);return null}},
      after:()=>{ready.current=true;if(engine.current&&engine.current.getValue()!==latest.current.value)engine.current.setValue(latest.current.value);el.current?.querySelectorAll('[contenteditable=true]').forEach(node=>{node.setAttribute('aria-label',t('Contenu riche','Rich content'));node.setAttribute('role','textbox');node.setAttribute('aria-multiline','true')})},
    });engine.current=editor;
    const theme=new MutationObserver(()=>editor.setTheme(document.documentElement.dataset.theme==='dark'?'dark':'classic'));theme.observe(document.documentElement,{attributes:true,attributeFilter:['data-theme']});
    return()=>{theme.disconnect();ready.current=false;editor.destroy();engine.current=null};
  },[mode,lang]);
  useEffect(()=>{if(ready.current&&engine.current&&engine.current.getValue()!==value)engine.current.setValue(value)},[value]);
  return <div className="note-rich-editor" ref={el}/>;
}
