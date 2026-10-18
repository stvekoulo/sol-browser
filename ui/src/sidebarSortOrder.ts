import type {Command} from './bridge';
export type SidebarOrder={id:string;kind:'tab'|'group';first:number;last:number};
export function sidebarSortOrder(before:SidebarOrder[],after:SidebarOrder[]):Command|null{
  if(before.length!==after.length||new Set(after.map(b=>b.id)).size!==before.length||after.some(b=>!before.some(a=>a.id===b.id)))return null;
  for(let from=0;from<before.length;from++){
    const source=before[from],to=after.findIndex(b=>b.id===source.id);if(to===from)continue;
    const attempt=[...before];attempt.splice(from,1);attempt.splice(to,0,source);
    if(attempt.some((b,i)=>b.id!==after[i].id))continue;
    const target=before[to],index=to>from?target.last+(source.kind==='group'?1:0):target.first;
    return {type:source.kind==='group'?'group.move':'tab.move',id:source.id,index};
  }
  return null;
}
