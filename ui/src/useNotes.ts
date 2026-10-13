import { useEffect,useRef,useState } from 'react';
import { onMessage,send } from './bridge';
import { dirtyNote,tagsFrom,type Note,type NotesContent } from './notesModel';
export function useNotes(initial:NotesContent|null=null,autosave=true){
  const [data,setData]=useState<NotesContent|null>(initial),local=useRef<Note|null>(null),current=useRef(data);current.current=data;
  useEffect(()=>{
    const off=onMessage(message=>{
      const incoming=message as NotesContent;if(incoming.popup!=='notes')return;
      let draft=incoming.draft;const pending=local.current;
      if(pending&&(pending.id===draft.id||!pending.id&&draft.id&&pending.url===draft.url)){
        if(!incoming.error&&(pending.title!==draft.title||pending.text!==draft.text||pending.kind!==draft.kind||JSON.stringify(pending.tags)!==JSON.stringify(draft.tags)||pending.due!==draft.due)){
          draft={...draft,title:pending.title,text:pending.text,kind:pending.kind,tags:pending.tags,due:pending.due};local.current=draft;
          send({type:'notes.draft',id:draft.id,title:draft.title,input:draft.text});send({type:'notes.meta',id:draft.id,input:JSON.stringify({kind:draft.kind,tags:draft.tags,due:draft.due})});
        }
        else local.current=null;
      }
      current.current={...incoming,draft};setData(current.current);
    });
    send({type:'notes.ready'});return off;
  },[]);
  useEffect(()=>{if(!autosave||!data||data.error||!dirtyNote(data)||!data.draft.id&&!data.draft.text.trim()&&!data.draft.files.length)return;
    const timer=setTimeout(()=>{const d=current.current?.draft;if(d)send({type:'notes.autosave',id:d.id,title:d.title,input:d.text})},750);return()=>clearTimeout(timer);
  },[data,autosave]);
  const change=(field:'title'|'text',value:string)=>{
    const old=current.current;if(!old)return;const draft={...old.draft,[field]:value};
    if(field==='text'){const oldInline=tagsFrom(old.draft.text);draft.tags=[...new Set([...draft.tags.filter(t=>!oldInline.includes(t)),...tagsFrom(value)])].slice(0,40);}
    local.current=draft;setData({...old,draft});current.current={...old,draft};
    send({type:'notes.draft',id:draft.id,title:draft.title,input:draft.text});
    if(field==='text')send({type:'notes.meta',id:draft.id,input:JSON.stringify({kind:draft.kind,tags:draft.tags,due:draft.due})});
  };
  const meta=(patch:Partial<Pick<Note,'kind'|'tags'|'due'>>)=>{
    const old=current.current;if(!old)return;const draft={...old.draft,...patch};local.current=draft;setData({...old,draft});current.current={...old,draft};
    send({type:'notes.meta',id:draft.id,input:JSON.stringify({kind:draft.kind,tags:draft.tags,due:draft.due})});
  };
  const flush=()=>{const d=current.current?.draft;if(d&&(d.id||d.text.trim()||d.files.length))send({type:'notes.autosave',id:d.id,title:d.title,input:d.text})};
  const edit=(id:string)=>{if(autosave)flush();local.current=null;send({type:'notes.edit',id})};
  return {data,change,meta,edit,flush};
}
