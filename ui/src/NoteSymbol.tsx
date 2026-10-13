import {Lightbulb,ListTodo,Pin,Trash2,ChartNoAxesColumn,LayoutGrid,List} from 'lucide-react';
import AnimatedIcon from './AnimatedIcon';
export default function NoteSymbol({view}:{view:string}){
  if(['all','note'].includes(view))return <AnimatedIcon name="notes"/>;
  if(view==='files')return <AnimatedIcon name="download"/>;
  if(view==='review')return <AnimatedIcon name="history"/>;
  if(view==='archive')return <AnimatedIcon name="archive"/>;
  const Component=({idea:Lightbulb,task:ListTodo,pinned:Pin,trash:Trash2,stats:ChartNoAxesColumn,cards:LayoutGrid,list:List} as Record<string,typeof Pin>)[view]??Pin;
  return <Component size={16} strokeWidth={1.8} aria-hidden="true"/>;
}
