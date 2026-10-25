import {useLayoutEffect, useRef} from 'react';
import {RadioGroup, RadioGroupItem} from '@/components/motion/radio';
import {t} from './i18n';

export const qualityLabel = (height:number) => height===0 ? t('Qualité d’origine','Original quality')
  : height===2160 ? '4K · 2160p' : height===4320 ? '8K · 4320p' : `${height}p`;

export function VideoQualities({values, value, preferred, onChange}:{
  values:number[]; value:number; preferred:number; onChange:(value:number)=>void;
}) {
  const root=useRef<HTMLDivElement>(null);
  // Compose installed beUI radios with roving focus, including a wrapping two-column layout.
  useLayoutEffect(()=>{
    const group=root.current?.querySelector('[role="radiogroup"]');
    group?.setAttribute('aria-label',t('Qualité de la vidéo','Video quality'));
    group?.querySelectorAll<HTMLButtonElement>('[role="radio"]').forEach((button,index)=>{
      button.tabIndex=values[index]===value?0:-1;
    });
  },[value,values]);
  return <div ref={root} onKeyDown={event=>{
    const direction=['ArrowRight','ArrowDown'].includes(event.key)?1:['ArrowLeft','ArrowUp'].includes(event.key)?-1:0;
    if(!values.length||(!direction&&!['Home','End'].includes(event.key)))return;
    event.preventDefault();
    const index=event.key==='Home'?0:event.key==='End'?values.length-1:(values.indexOf(value)+direction+values.length)%values.length;
    onChange(values[index]);root.current?.querySelectorAll<HTMLButtonElement>('[role="radio"]')[index]?.focus();
  }}>
    <RadioGroup value={String(value)} onValueChange={next=>onChange(Number(next))} className="video-qualities">
      {values.map(height=><RadioGroupItem key={height} value={String(height)} label={qualityLabel(height)}
        description={height===preferred&&preferred>0?t('Votre préférence','Your preference'):height===values[0]?t('Meilleure disponible','Best available'):undefined}
        className={`video-quality-item ${height===value?'is-selected':''}`}/>)}
    </RadioGroup>
  </div>;
}
