import type {ReactNode} from 'react';
import {Tabs,TabsList,TabsTrigger} from '@/components/motion/tabs';
export default function SolTabs({value,change,items,label,className=''}:{value:string;change:(value:string)=>void;items:{value:string;label:ReactNode;ariaLabel?:string}[];label:string;className?:string}){
  return <Tabs value={value} onValueChange={change} className={`sol-tabs ${className}`}>
    <TabsList aria-label={label} className="sol-tabs-list" wrapperClassName="sol-tabs-wrap" onKeyDown={e=>{
      if(e.nativeEvent.isComposing||e.altKey||e.ctrlKey||e.metaKey||!['ArrowLeft','ArrowRight','Home','End'].includes(e.key))return;
      const buttons=[...e.currentTarget.querySelectorAll<HTMLButtonElement>('[role="tab"]:not(:disabled)')],at=buttons.indexOf(e.target as HTMLButtonElement);if(at<0)return;
      e.preventDefault();const next=e.key==='Home'?0:e.key==='End'?buttons.length-1:(at+(e.key==='ArrowRight'?1:-1)+buttons.length)%buttons.length;buttons[next]?.focus({preventScroll:true});buttons[next]?.click();
    }}>
      {items.map(item=><TabsTrigger key={item.value} value={item.value} aria-label={item.ariaLabel} tabIndex={item.value===value?0:-1} className="sol-tab" indicatorClassName="sol-tab-indicator">{item.label}</TabsTrigger>)}
    </TabsList>
  </Tabs>;
}
