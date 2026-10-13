import Markdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import remarkMath from 'remark-math';
import rehypeRaw from 'rehype-raw';
import rehypeSanitize from 'rehype-sanitize';
import rehypeKatex from 'rehype-katex';
import rehypeHighlight from 'rehype-highlight';
import { send } from './bridge';
import MarkdownImage from './MarkdownImage';
import { t } from './i18n';
import { references,titleOf,toggleTask,type Note } from './notesModel';
import './markdown.css';
import 'katex/dist/katex.min.css';
export default function NoteMarkdown({note,all,change,edit}:{note:Note;all:Note[];change?:(text:string)=>void;edit:(id:string)=>void}){
  const links=references(note,all),text=note.text.replace(/\[\[([^\]]+)\]\]/g,(full,name)=>{const target=links.find(n=>n.id===name||titleOf(n).toLocaleLowerCase()===name.toLocaleLowerCase());return target?`[${name.replace(/[\[\]\\]/g,'\\$&')}](#sol-note-${target.id})`:full});
  let task=0;
  return <div className="sol-markdown"><Markdown remarkPlugins={[remarkGfm,remarkMath]} remarkRehypeOptions={{footnoteLabel:t('Notes','Notes'),footnoteBackLabel:t('Retour à la référence','Back to reference')}} rehypePlugins={[rehypeRaw,rehypeSanitize,[rehypeKatex,{trust:false,maxExpand:1000,maxSize:10}],[rehypeHighlight,{ignoreMissing:true}]]}
    components={{a:({href,children})=><a href={href} onClick={e=>{e.preventDefault();if(href?.startsWith('#sol-note-'))edit(href.slice(10));else if(href&&/^https?:\/\//.test(href))send({type:'notes.link',url:href});else if(href?.startsWith('#')){try{document.getElementById(decodeURIComponent(href.slice(1)))?.scrollIntoView({block:'nearest'})}catch{}}}}>{children}</a>,
    input:({checked,type})=>{const index=task++;return <input type={type} checked={!!checked} disabled={!change} aria-label={`Tâche ${index+1}`} onChange={e=>change?.(toggleTask(note.text,index,e.target.checked))}/>},
    img:({src,alt})=><MarkdownImage src={src} alt={alt}/>,
    table:({children})=><div className="markdown-table" tabIndex={0}><table>{children}</table></div>}}>{text}</Markdown></div>;
}
