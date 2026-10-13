import {useEffect,useRef,useState,type KeyboardEvent} from 'react';
import {Select,SelectTrigger,SelectValue,SelectContent,SelectItem} from '@/components/motion/select';

export default function SolSelect({id,label,value,options,onChange}:{id:string;label:string;value:number;options:string[];onChange:(index:number)=>void}){
  const [open,setOpen]=useState(false),root=useRef<HTMLDivElement>(null);
  const choices=()=>Array.from(root.current?.querySelectorAll<HTMLButtonElement>('[role="option"]:not(:disabled)')??[]);
  const focus=(index:number)=>{const list=choices(),option=list[Math.max(0,Math.min(index,list.length-1))];option?.focus({preventScroll:true});option?.scrollIntoView({block:'nearest'});};
  const trigger=()=>root.current?.querySelector<HTMLButtonElement>('[aria-haspopup="listbox"]');
  useEffect(()=>{if(open){const frame=requestAnimationFrame(()=>focus(value));return()=>cancelAnimationFrame(frame);}},[open]);
  const key=(e:KeyboardEvent<HTMLDivElement>)=>{
    if(e.nativeEvent.isComposing||e.altKey||e.ctrlKey||e.metaKey)return;
    if(e.key==='Escape'&&open){e.preventDefault();e.stopPropagation();setOpen(false);trigger()?.focus();return;}
    if(e.key==='Tab'){setOpen(false);return;}
    if(!['ArrowDown','ArrowUp','Home','End'].includes(e.key))return;
    e.preventDefault();const list=choices(),current=list.indexOf(document.activeElement as HTMLButtonElement);
    if(!open){setOpen(true);return;}
    focus(e.key==='Home'?0:e.key==='End'?list.length-1:(current+(e.key==='ArrowDown'?1:-1)+list.length)%list.length);
  };
  return <div ref={root} className={`sol-select${open?' open':''}`} data-sol-select={id} onKeyDown={key}>
    <Select value={String(value)} open={open} onOpenChange={setOpen} onValueChange={next=>{onChange(Number(next));requestAnimationFrame(()=>trigger()?.focus());}}>
      <SelectTrigger ariaLabel={`${label} : ${options[value]??''}`} className="sol-select-trigger"><SelectValue placeholder={options[value]??label}/></SelectTrigger>
      <SelectContent className="sol-select-content">{options.map((option,index)=><SelectItem key={index} value={String(index)} className="sol-select-option">{option}</SelectItem>)}</SelectContent>
    </Select>
  </div>;
}
