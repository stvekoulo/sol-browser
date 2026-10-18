import { lazy,Suspense } from 'react';
import type { Note } from './notesModel';
const Content=lazy(()=>import('./NoteMarkdownContent'));
export default function NoteMarkdown(props:{note:Note;all:Note[];change?:(text:string)=>void;edit:(id:string)=>void}){
  return <Suspense fallback={<div className="note-editor-loading" aria-busy="true">…</div>}><Content {...props}/></Suspense>;
}
