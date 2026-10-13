import { lazy,Suspense } from 'react';
const Editor=lazy(()=>import('./RichNoteContent'));
export default function RichNoteEditor(props:{value:string;mode:'wysiwyg'|'ir';noteId:string;change:(text:string)=>void;save:()=>void}){
  return <Suspense fallback={<p>{props.value||'…'}</p>}><Editor {...props}/></Suspense>;
}
