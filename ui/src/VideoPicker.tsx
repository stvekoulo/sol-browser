import {useEffect,useRef,useState} from 'react';
import {Button} from '@/components/motion/button/base';
import {send} from './bridge';
import {t} from './i18n';
import AnimatedIcon from './AnimatedIcon';
import {VideoQualities,qualityLabel} from './VideoQualities';
import './video-picker.css';

export type VideoContent = {popup:'videos';id:string;title:string;protectedMedia:boolean;
  phase:string;sourceId:string;qualities:number[];preferred:number;selected:number;
  items:{id:string;label:string;kind:'page'|'file'|'hls'|'dash';host:string}[]};

export default function VideoPicker({content}:{content:VideoContent}) {
  const [quality,setQuality]=useState(content.selected);
  const identity=`${content.id}:${content.title}:${content.sourceId}`,previous=useRef(identity);
  useEffect(()=>{
    const changed=previous.current!==identity;previous.current=identity;
    setQuality(current=>!changed&&content.qualities.includes(current)?current:content.selected);
  },[identity,content.selected,content.qualities]);
  // Never send a stale quality while a live catalogue update is being rendered.
  const selected=content.qualities.includes(quality)?quality:content.selected;
  const ready=content.phase==='ready'&&content.qualities.length>0;
  const failed=content.phase==='failed';
  const busy=!!content.phase&&!ready&&!failed;
  return <section className="video-picker" aria-label={t('Télécharger une vidéo','Download a video')}>
    <header className="video-header">
      <span className="video-heading-icon"><AnimatedIcon name="download" size={22}/></span>
      <div><h2>{t('Télécharger la vidéo','Download video')}</h2><span>{t('Depuis cette page','From this page')}</span></div>
      <Button variant="ghost" size="icon" className="video-close" aria-label={t('Fermer','Close')} onClick={()=>send({type:'popup.close'})}><AnimatedIcon name="close" size={20}/></Button>
    </header>
    <p className="video-page-title">{content.title}</p>
    {content.protectedMedia?<div className="video-status" role="status">
      <AnimatedIcon name="private" size={24}/><strong>{t('Vidéo protégée','Protected video')}</strong>
      <p>{t('Ce lecteur utilise une protection DRM. Cette vidéo ne peut pas être téléchargée.','This player uses DRM protection. This video cannot be downloaded.')}</p>
    </div>:<>
      {content.phase&&content.sourceId!=='catalog'&&<Button variant="ghost" size="sm" className="video-back" onClick={()=>send({type:'video.open',id:content.id})}>
        <AnimatedIcon name="back" size={16}/>{t('Toutes les sources','All sources')}
      </Button>}
      {ready?<>
        <div className="video-quality-heading"><h3>{t('Choisir la qualité','Choose quality')}</h3><span>{content.qualities.length} {t('disponibles','available')}</span></div>
        <VideoQualities values={content.qualities} value={selected} preferred={content.preferred} onChange={setQuality}/>
        {content.preferred>0&&!content.qualities.includes(content.preferred)&&<p role="status" className="video-help">
          {t(`${qualityLabel(content.preferred)} indisponible. Nous proposons ${qualityLabel(content.selected)} ; vous pouvez choisir une autre qualité.`,
            `${qualityLabel(content.preferred)} unavailable. We suggest ${qualityLabel(content.selected)}; you can choose another quality.`)}
        </p>}
        <Button variant="primary" size="lg" className="video-download-btn" onClick={()=>send({type:'video.download',id:content.id,query:content.sourceId,index:selected})}>
          <AnimatedIcon name="download" size={20}/><span>{t('Télécharger','Download')} · {qualityLabel(selected)}</span>
        </Button>
        <p className="video-help">{t('Suivez la progression dans Téléchargements.','Follow progress in Downloads.')}</p>
      </>:content.phase?<div className="video-status" role="status" aria-busy={busy}>
        <span className={busy?'video-loading':''}><AnimatedIcon name={failed?'reload':'search'} size={26}/></span>
        <strong>{failed?t('Qualités introuvables','Qualities unavailable'):t('Recherche des qualités…','Finding qualities…')}</strong>
        <p>{failed?t('Réessayez ou choisissez une autre source.','Try again or choose another source.'):t('Les options s’affichent dès qu’elles sont disponibles.','Options appear as soon as they are available.')}</p>
        {failed&&content.sourceId&&content.sourceId!=='catalog'&&<Button variant="secondary" size="md" onClick={()=>send({type:'video.inspect',id:content.id,query:content.sourceId})}>
          <AnimatedIcon name="reload" size={18}/>{t('Réessayer','Retry')}
        </Button>}
      </div>:<div className="video-sources">
        {content.items.map(source=><Button key={source.id} variant="ghost" size="md" className="video-source" onClick={()=>send({type:'video.inspect',id:content.id,query:source.id})}>
          <AnimatedIcon name="play" size={20}/><span><strong>{source.kind==='page'?t('Vidéo du lecteur','Player video'):source.label}</strong>
            <small>{source.kind==='page'?t('Voir les qualités disponibles','View available qualities'):source.host}</small></span><AnimatedIcon name="forward" size={18}/>
        </Button>)}
        {!content.items.length&&<p className="video-help" role="status">{t('Aucune source disponible pour le moment.','No sources available yet.')}</p>}
      </div>}
      <footer className="video-footer-hints">{t('Les directs et les vidéos protégées ne sont pas pris en charge.','Live and protected videos are not supported.')}</footer>
    </>}
  </section>;
}
