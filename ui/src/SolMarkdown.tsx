import { lazy, Suspense } from 'react';
const MarkdownContent = lazy(()=>import('./MarkdownContent'));
// Le parseur et les formules ne sont chargés que lorsqu'un résultat ou un aperçu en a besoin.
export default function SolMarkdown(props:{text:string;onOpen?:(url:string)=>void}) {
  return <Suspense fallback={<div style={{whiteSpace:'pre-wrap'}}>{props.text}</div>}><MarkdownContent {...props}/></Suspense>;
}
