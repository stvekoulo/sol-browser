import type {ReactNode} from 'react';
import {Collapsible,CollapsibleTrigger,CollapsibleContent,CollapsibleIndicator} from '@/components/motion/collapsible';
export default function NoteFold({title,children}:{title:ReactNode;children:ReactNode}){
  return <Collapsible className="note-fold"><CollapsibleTrigger className="note-fold-trigger">{title}<CollapsibleIndicator/></CollapsibleTrigger><CollapsibleContent contentClassName="note-fold-body">{children}</CollapsibleContent></Collapsible>;
}
