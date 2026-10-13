import {useEffect,useRef,type ReactNode} from 'react';
import {MorphingModal} from '@/components/motion/morphing-modal';
import {t} from './i18n';
export default function SolModal({id,title,close,children}:{id:string|null;title:string;close:()=>void;children:ReactNode}){
  const content=useRef<HTMLDivElement>(null),closing=useRef(close);closing.current=close;
  useEffect(()=>{
    if(id===null)return;const previous=document.activeElement as HTMLElement|null;
    const frame=requestAnimationFrame(()=>(content.current?.querySelector<HTMLElement>('input,textarea')??content.current?.querySelector<HTMLElement>('button'))?.focus());
    const key=(e:KeyboardEvent)=>{
      if(e.key==='Escape'){e.preventDefault();e.stopPropagation();closing.current();}
      if(e.key!=='Tab')return;
      const list=[...content.current?.querySelectorAll<HTMLElement>('button:not(:disabled),input:not(:disabled),textarea:not(:disabled),[tabindex="0"]')??[]].filter(el=>el.getClientRects().length);
      const index=list.indexOf(document.activeElement as HTMLElement),next=e.shiftKey?index-1:index+1;
      if(next<0||next>=list.length){e.preventDefault();list[e.shiftKey?list.length-1:0]?.focus();}
    };
    document.addEventListener('keydown',key,true);
    return()=>{cancelAnimationFrame(frame);document.removeEventListener('keydown',key,true);previous?.isConnected&&previous.focus({preventScroll:true});};
  },[id]);
  return <MorphingModal viewId={id} placement="center" onClose={close} className="sol-modal-surface">
    <div ref={content} role="dialog" aria-modal="true" aria-label={title} className="sol-modal-content"><button className="notes-tool sol-modal-close" aria-label={t('Fermer','Close')} onClick={close}>×</button>{children}</div>
  </MorphingModal>;
}
