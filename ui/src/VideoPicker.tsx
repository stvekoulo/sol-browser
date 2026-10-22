import {Button} from '@/components/motion/button/base';
import {send} from './bridge';
import {t} from './i18n';
import {Icon} from './Icons';
import './video-picker.css';
import {useEffect,useRef,useState} from 'react';

export type VideoContent = {popup:'videos';id:string;title:string;protectedMedia:boolean;
  phase:string;sourceId:string;qualities:number[];preferred:number;selected:number;
  items:{id:string;label:string;kind:'page'|'file'|'hls'|'dash';host:string}[]};

export default function VideoPicker({content}:{content:VideoContent}) {
  const [quality,setQuality]=useState(content.selected);
  const identity=`${content.id}:${content.title}:${content.sourceId}`,previous=useRef(identity);
  useEffect(()=>{const changed=previous.current!==identity;previous.current=identity;setQuality(current=>!changed&&content.qualities.includes(current)?current:content.selected)},[identity,content.selected,content.qualities]);
  const label=(height:number)=>height===0?t('Qualité d’origine','Original quality'):height===2160?'4K · 2160p':height===4320?'8K · 4320p':`${height}p`;
  return <section className="video-picker" aria-label={t('Télécharger une vidéo','Download a video')}>
    <div className="popup-header"><span className="popup-header-title">{t('Vidéos de cette page','Videos on this page')}</span>
      <Button variant="ghost" size="sm" className="pill-action" aria-label={t('Fermer','Close')} onClick={()=>send({type:'popup.close'})}>×</Button></div>
    <p className="video-page-title">{content.title}</p>
    {content.protectedMedia ? <p role="status">{t('Ce lecteur utilise une protection DRM. Sol ne peut pas télécharger cette vidéo.','This player uses DRM protection. Sol cannot download this video.')}</p> : <>
      {content.phase && content.sourceId!=='catalog' && <Button variant="ghost" size="sm" className="pill-action" onClick={()=>send({type:'video.open',id:content.id})}>← {t('Sources','Sources')}</Button>}
      {content.phase==='ready'?<>
        <div className="video-sources" role="radiogroup" aria-label={t('Qualité de la vidéo','Video quality')} onKeyDown={e=>{if(!['ArrowDown','ArrowUp','Home','End'].includes(e.key))return;e.preventDefault();const values=content.qualities,index=values.indexOf(quality);const next=e.key==='Home'?0:e.key==='End'?values.length-1:(index+(e.key==='ArrowDown'?1:-1)+values.length)%values.length;setQuality(values[next]);(e.currentTarget.querySelectorAll('button')[next] as HTMLButtonElement)?.focus()}}>
          {content.qualities.map(height=><Button key={height} variant="ghost" size="sm" className="video-source" role="radio" tabIndex={quality===height?0:-1} aria-checked={quality===height} onClick={()=>setQuality(height)}>
            <span>{label(height)}</span><span aria-hidden>{quality===height?'✓':''}</span>
          </Button>)}
        </div>
        {content.preferred>0&&content.selected!==content.preferred&&<p role="status" className="video-help">{t(`Votre préférence (${label(content.preferred)}) n’est pas disponible. Qualité proposée : ${label(content.selected)}.`,`Your preference (${label(content.preferred)}) is unavailable. Suggested quality: ${label(content.selected)}.`)}</p>}
        <Button variant="ghost" size="sm" className="pill-action primary" onClick={()=>send({type:'video.download',id:content.id,query:content.sourceId,index:quality})}>{t(`Télécharger · ${label(quality)}`,`Download · ${label(quality)}`)}</Button>
      </>:content.phase?<p role="status">{content.phase==='failed'?t('Impossible de lire les qualités. Réessayez ou sélectionnez une autre source. Les directs et contenus protégés ne sont pas pris en charge.','Unable to read qualities. Retry or select another source. Live and protected streams are not supported.'):content.phase==='engine.downloader'?t('Préparation du moteur vidéo…','Preparing video engine…'):content.phase==='engine.ffmpeg'?t('Préparation de l’assemblage vidéo…','Preparing video merging…'):t('Recherche des qualités disponibles…','Finding available qualities…')}</p>:<div className="video-sources">{content.items.map(source=><Button key={source.id} variant="ghost" size="sm" className="video-source"
        onClick={()=>send({type:'video.inspect',id:content.id,query:source.id})}>
        <Icon d="M8 2v8M5 7l3 3 3-3M3 13h10" size={16}/>
        <span><strong>{source.kind==='page'?t('Vidéo du lecteur','Video from the player'):source.label}</strong>
          <small>{source.kind==='page'?t('Retrouver la vidéo et ses pistes sur le site','Find the video and its tracks on this site'):source.host}</small></span>
        <span aria-hidden>→</span>
      </Button>)}</div>}
      <p className="video-help">{t('Choisissez la qualité, puis téléchargez. Retrouvez la vidéo dans Téléchargements, avec pause, reprise et annulation.','Choose a quality, then download. Find the video in Downloads with pause, resume and cancel.')}</p>
      <p className="video-help">{t('La compatibilité dépend du site et de son lecteur. Les contenus protégés ne sont pas pris en charge.','Compatibility depends on the site and its player. Protected content is not supported.')}</p>
    </>}
  </section>;
}
