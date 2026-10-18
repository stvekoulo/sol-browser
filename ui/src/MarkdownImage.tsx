import { useState } from 'react';
import { t } from './i18n';
export default function MarkdownImage({src,alt}:{src?:string;alt?:string}){
  const [show,setShow]=useState(false);
  if(!src||!/^https?:\/\//i.test(src))return <span>{alt}</span>;
  return show?<img src={src} alt={alt||''} loading="lazy" referrerPolicy="no-referrer"/>:<button className="pill-action" onClick={()=>setShow(true)}>{t('Afficher l’image','Show image')}{alt?` · ${alt}`:''}</button>;
}
