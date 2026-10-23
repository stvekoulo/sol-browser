import {useState,type CSSProperties,type KeyboardEvent} from 'react';
import {RadioGroup,RadioGroupItem} from '@/components/motion/radio';
import {Button} from '@/components/motion/button';
import {t} from './i18n';

const families=['Inter Variable','Segoe UI','Inter Variable','Inter Variable','Segoe UI','Segoe UI','Geist Variable','Manrope Variable','DM Sans Variable','Plus Jakarta Sans Variable','Outfit Variable','Figtree Variable','Nunito Variable','Lora Variable'];

export function WelcomeFonts({options,value,onPick}:{options:string[];value:number;onPick:(index:number)=>void}) {
  const [expanded,setExpanded]=useState(value>5);
  const keys=(e:KeyboardEvent<HTMLDivElement>)=>{
    if(e.altKey||e.ctrlKey||e.metaKey||!['ArrowLeft','ArrowRight','ArrowUp','ArrowDown','Home','End'].includes(e.key))return;
    const radios=[...e.currentTarget.querySelectorAll<HTMLButtonElement>('[role=radio]')],at=radios.indexOf(e.target as HTMLButtonElement);
    if(at<0)return;
    e.preventDefault();
    const offset=e.key==='ArrowLeft'?-1:e.key==='ArrowRight'?1:e.key==='ArrowUp'?-2:2;
    const next=e.key==='Home'?0:e.key==='End'?radios.length-1:(at+offset+radios.length)%radios.length;
    radios[next]?.focus();radios[next]?.click();
  };
  return <div className="welcome-fonts" onKeyDown={keys}>
    <div className="welcome-font-heading"><span>{t('Police de l’interface','Interface font')}</span><small>{t('Un style qui vous ressemble.','A style that feels like you.')}</small></div>
    <RadioGroup className="welcome-font-options" value={String(value)} onValueChange={v=>onPick(Number(v))} aria-label={t('Police','Font')}>
      {options.slice(0,expanded?undefined:6).map((option,i)=><div key={option} style={{'--sample-font':`"${families[i]}",sans-serif`,'--sample-weight':i===3||i===5?650:400} as CSSProperties}>
        <RadioGroupItem value={String(i)} label={option} description={t('Aa · L’espace des idées','Aa · Room for ideas')} className="welcome-font-card"/>
      </div>)}
    </RadioGroup>
    <Button variant="ghost" size="sm" pressScale={.96} className="welcome-font-more" aria-expanded={expanded} onClick={()=>setExpanded(!expanded)}>{expanded?t('Réduire la liste','Show fewer'):t(`Voir les ${options.length} polices`,`View all ${options.length} fonts`)}<span aria-hidden>{expanded?'−':'+'}</span></Button>
  </div>;
}
