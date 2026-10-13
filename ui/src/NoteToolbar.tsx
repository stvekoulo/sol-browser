import type { RefObject } from 'react';
import { Icon } from './Icons';
import { t } from './i18n';
import { send } from './bridge';
import {Button} from '@/components/motion/button/base';
export function insertNote(editor:HTMLTextAreaElement|null,change:(text:string)=>void,before:string,after='',fallback='',replace=false){
  if(!editor)return;const {selectionStart:start,selectionEnd:end,value}=editor,selected=replace?fallback:value.slice(start,end)||fallback;
  change(value.slice(0,start)+before+selected+after+value.slice(end));
  requestAnimationFrame(()=>{editor.focus();editor.setSelectionRange(start+before.length,start+before.length+selected.length)});
}
export default function NoteToolbar({editor,change,reference}:{editor:RefObject<HTMLTextAreaElement|null>;change:(text:string)=>void;reference:()=>void}){
  const tools=[{label:t('Gras · Ctrl+B','Bold · Ctrl+B'),text:'B',before:'**',after:'**'},{label:t('Italique · Ctrl+I','Italic · Ctrl+I'),text:'I',before:'_',after:'_'},
    {label:t('Titre','Heading'),text:'H',before:'\n## '},{label:t('Liste','List'),text:'☷',before:'\n- '},{label:t('Liste de tâches','Checklist'),text:'☑',before:'\n- [ ] '},
    {label:t('Citation','Quote'),text:'❝',before:'\n> '},{label:t('Code','Code'),text:'</>',before:'\n```\n',after:'\n```'},{label:t('Lien','Link'),text:'↗',before:'[',after:'](https://)'},
    {label:t('Tableau','Table'),text:'▦',before:'\n| Colonne | Valeur |\n| --- | --- |\n| ',after:' | |\n'}];
  return <div className="note-format" role="toolbar" aria-label={t('Mise en forme','Formatting')}>{tools.map(tool=><Button variant="ghost" size="icon" key={tool.label} className="notes-tool" title={tool.label} aria-label={tool.label} onMouseDown={e=>e.preventDefault()} onClick={()=>insertNote(editor.current,change,tool.before,tool.after,'texte')}>{tool.text}</Button>)}
    <Button variant="ghost" size="icon" className="notes-tool" title={t('Lier une note','Link a note')} aria-label={t('Lier une note','Link a note')} onClick={reference}><Icon d="M6 6l4 4M6 9l-2 2a2 2 0 0 1-3-3l2-2M10 7l2-2a2 2 0 0 1 3 3l-2 2"/></Button>
    <Button variant="ghost" size="icon" className="notes-tool" title={t('Joindre un fichier','Attach a file')} aria-label={t('Joindre un fichier','Attach a file')} onClick={()=>send({type:'notes.attach'})}><Icon d="M6 11l5-5a2 2 0 0 0-3-3L3 8a4 4 0 0 0 6 6l5-5"/></Button>
  </div>;
}
