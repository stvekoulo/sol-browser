import MarkdownImage from './MarkdownImage';
import Markdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import remarkMath from 'remark-math';
import rehypeRaw from 'rehype-raw';
import rehypeSanitize, { defaultSchema } from 'rehype-sanitize';
import rehypeKatex from 'rehype-katex';
import rehypeHighlight from 'rehype-highlight';
import { send } from './bridge';
import { t } from './i18n';
import 'katex/dist/katex.min.css';
import './markdown.css';

const schema = {...defaultSchema,attributes:{...defaultSchema.attributes,code:[['className',/^language-./,'math-inline','math-display']]}};
export default function SolMarkdown({text,onOpen=(url:string)=>send({type:'tab.open',url})}:{text:string;onOpen?:(url:string)=>void}) {
  return <div className="sol-markdown"><Markdown remarkPlugins={[remarkGfm,remarkMath]}
    remarkRehypeOptions={{footnoteLabel:t('Notes','Notes'),footnoteBackLabel:t('Retour à la référence','Back to reference')}}
    rehypePlugins={[rehypeRaw,[rehypeSanitize,schema],[rehypeKatex,{trust:false,maxExpand:1000,maxSize:10}],[rehypeHighlight,{ignoreMissing:true}]]}
    components={{
      a:({href,children,node,...props})=><a {...props} href={href} onClick={e=>{e.preventDefault();if(href?.startsWith('#')){try{const id=decodeURIComponent(href.slice(1));(document.getElementById(id)||document.getElementById('user-content-'+id))?.scrollIntoView({block:'nearest'});}catch{}}else if(href&&/^https?:\/\//i.test(href))onOpen(href);}}>{children}</a>,
      img:({src,alt})=><MarkdownImage src={src} alt={alt}/>,
      table:({children})=><div className="markdown-table" tabIndex={0}><table>{children}</table></div>,
    }}>{text}</Markdown></div>;
}
