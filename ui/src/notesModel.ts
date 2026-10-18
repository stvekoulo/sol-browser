export type Note = { id:string;title:string;text:string;url:string;updated:string;created:string;kind:'idea'|'note'|'task';tags:string[];pinned:boolean;archived:boolean;reviewed:boolean;deleted:string|null;due:string|null;files:string[];history:{title:string;text:string;updated:string}[];comments:{id:string;text:string;created:string}[] };
export type NoteAttachment={id:string;name:string;mime:string;size:number;data:string|null};
export type NotesContent={popup:'notes';items:Note[];draft:Note;error:string|null;privateMode:boolean;aiEnabled:boolean;attachments:NoteAttachment[];archiveDays?:number};
export type NoteView='all'|'idea'|'note'|'task'|'pinned'|'archive'|'trash'|'review'|'stats'|'files';
export const titleOf=(n:Note)=>n.title||n.text.split('\n')[0].replace(/^#+\s*/, '').slice(0,80)||'Note';
export const createdOf=(n:Note)=>n.created&&!n.created.startsWith('0001')?n.created:n.updated;
export const tagsFrom=(text:string)=>[...new Set([...text.matchAll(/(?:^|\s)#([\p{L}\p{N}_/-]{1,64})/gu)].map(m=>m[1]))];
export const dirtyNote=(data:NotesContent)=>{const n=data.items.find(n=>n.id===data.draft.id),d=data.draft;return !!(d.id||d.text||d.title||d.files.length)&&(!n||n.title!==d.title||n.text!==d.text||n.kind!==d.kind||JSON.stringify(n.tags)!==JSON.stringify(d.tags)||JSON.stringify(n.files)!==JSON.stringify(d.files)||n.due!==d.due)};
export const inView=(n:Note,view:NoteView)=>view==='trash'?!!n.deleted:!n.deleted&&(view==='archive'?n.archived:!n.archived&&(view==='pinned'?n.pinned:view==='review'?!n.reviewed:view==='files'?n.files.length>0:['idea','note','task'].includes(view)?n.kind===view:true));
export function matchesNote(n:Note,query:string,tag=''){
  if(tag&&!n.tags.some(t=>t===tag||t.startsWith(tag+'/')))return false;
  return (query.match(/"[^"]+"|\S+/g)||[]).every(term=>{
    const value=term.toLocaleLowerCase();
    if(value.startsWith('#'))return n.tags.some(t=>t.toLocaleLowerCase()===value.slice(1)||t.toLocaleLowerCase().startsWith(value.slice(1)+'/'));
    if(value.startsWith('type:'))return n.kind===value.slice(5);
    if(value==='has:file')return n.files.length>0;
    if(value==='is:pinned')return n.pinned;
    if(value==='is:done')return n.reviewed;
    if(value.startsWith('before:')||value.startsWith('after:')){const date=new Date(value.slice(value.indexOf(':')+1)).getTime();return Number.isFinite(date)&&(value.startsWith('before:')?new Date(n.updated).getTime()<date:new Date(n.updated).getTime()>=date)}
    return `${n.title}\n${n.text}\n${n.url}\n${n.tags.join(' ')}`.toLocaleLowerCase().includes(value.replace(/^"|"$/g,''));
  });
}
export const references=(n:Note,all:Note[])=>[...n.text.matchAll(/\[\[([^\]]+)\]\]/g)].map(m=>all.find(x=>!x.deleted&&(x.id===m[1]||titleOf(x).toLocaleLowerCase()===m[1].toLocaleLowerCase()))).filter((n):n is Note=>!!n);
export function toggleTask(text:string,index:number,checked:boolean){
  let current=-1,fence='';return text.split('\n').map(line=>{
    const delimiter=line.match(/^\s{0,3}(`{3,}|~{3,})/);if(delimiter){if(!fence)fence=delimiter[1];else if(delimiter[1][0]===fence[0]&&delimiter[1].length>=fence.length)fence='';return line}
    return fence?line:line.replace(/^(\s*(?:[-*+]|\d+[.)])\s+)\[[ xX]\]/,(full,prefix)=>++current===index?`${prefix}[${checked?'x':' '}]`:full);
  }).join('\n');
}
