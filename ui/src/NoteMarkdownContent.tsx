import Markdown from 'react-markdown';
import {createContext,useContext,type ComponentPropsWithoutRef} from 'react';
import type {ExtraProps} from 'react-markdown';
import type {Root,RootContent} from 'hast';
import remarkGfm from 'remark-gfm';
import remarkMath from 'remark-math';
import rehypeRaw from 'rehype-raw';
import rehypeSanitize from 'rehype-sanitize';
import rehypeKatex from 'rehype-katex';
import rehypeHighlight from 'rehype-highlight';
import { send } from './bridge';
import MarkdownImage from './MarkdownImage';
import {Checkbox} from '@/components/motion/checkbox';
import { t } from './i18n';
import { references,titleOf,toggleTask,type Note } from './notesModel';
import './markdown.css';
import 'katex/dist/katex.min.css';
const TaskContext=createContext<{note:Note;change?: (text:string)=>void}|null>(null);
function indexTasks(){return (tree:Root)=>{
  let index=0;
  const visit=(node:Root|RootContent)=>{if(node.type==='element'&&node.tagName==='input'&&node.properties.type==='checkbox')node.properties['data-task-index']=index++;if('children'in node)node.children.forEach(visit)};
  visit(tree);
};}
// A stable renderer keeps keyboard focus and the check animation through autosave echoes.
function TaskCheckbox({checked,node}:ComponentPropsWithoutRef<'input'>&ExtraProps){
  const {note,change}=useContext(TaskContext)!,index=Number(node?.properties['data-task-index']);
  return <Checkbox className="sol-checkbox note-task-checkbox" checked={!!checked} disabled={!change} aria-label={t(`Tâche ${index+1}`,`Task ${index+1}`)} onCheckedChange={checked=>change?.(toggleTask(note.text,index,checked))}/>;
}
export default function NoteMarkdown({note,all,change,edit}:{note:Note;all:Note[];change?:(text:string)=>void;edit:(id:string)=>void}){
  const links=references(note,all),text=note.text.replace(/\[\[([^\]]+)\]\]/g,(full,name)=>{const target=links.find(n=>n.id===name||titleOf(n).toLocaleLowerCase()===name.toLocaleLowerCase());return target?`[${name.replace(/[\[\]\\]/g,'\\$&')}](#sol-note-${target.id})`:full});
  return <TaskContext value={{note,change}}><div className="sol-markdown"><Markdown remarkPlugins={[remarkGfm,remarkMath]} remarkRehypeOptions={{footnoteLabel:t('Notes','Notes'),footnoteBackLabel:t('Retour à la référence','Back to reference')}} rehypePlugins={[rehypeRaw,rehypeSanitize,[rehypeKatex,{trust:false,maxExpand:1000,maxSize:10}],[rehypeHighlight,{ignoreMissing:true}],indexTasks]}
    components={{a:({href,children})=><a href={href} onClick={e=>{e.preventDefault();if(href?.startsWith('#sol-note-'))edit(href.slice(10));else if(href&&/^https?:\/\//.test(href))send({type:'notes.link',url:href});else if(href?.startsWith('#')){try{document.getElementById(decodeURIComponent(href.slice(1)))?.scrollIntoView({block:'nearest'})}catch{}}}}>{children}</a>,
    input:TaskCheckbox,
    img:({src,alt})=><MarkdownImage src={src} alt={alt}/>,
    table:({children})=><div className="markdown-table" tabIndex={0}><table>{children}</table></div>}}>{text}</Markdown></div></TaskContext>;
}
