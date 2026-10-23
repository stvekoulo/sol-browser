import {useEffect,useState} from 'react';
import {Button} from '@/components/motion/button/base';
import {Checkbox} from '@/components/motion/checkbox';
import AnimatedIcon from './AnimatedIcon';
import {VideoQualities,qualityLabel} from './VideoQualities';
import type {VideoContent} from './VideoPicker';
import {send} from './bridge';
import {t} from './i18n';

export type VideoCollectionContent = {token:string;mode:'playlist'|'page';start:number;more:boolean;
  entries:{key:string;title:string;position:number;host:string}[]};

export function VideoCollectionPicker({content}:{content:VideoContent}) {
  const collection=content.collection!;
  const [checked,setChecked]=useState<Set<string>>(()=>new Set());
  const [quality,setQuality]=useState(content.preferred);
  useEffect(()=>setChecked(new Set()),[collection.token]);
  const selected=collection.entries.filter(entry=>checked.has(entry.key));
  const busy=content.phase!=='ready'&&content.phase!=='failed';
  const all=selected.length>0&&selected.length===collection.entries.length;
  const load=(start:number)=>send({type:'video.collection',id:content.id,query:collection.mode,index:start});
  return <section className="video-picker" aria-label={t('Télécharger plusieurs vidéos','Download multiple videos')}>
    <header className="video-header">
      <span className="video-heading-icon"><AnimatedIcon name="download" size={22}/></span>
      <div><h2>{collection.mode==='playlist'?t('Playlist','Playlist'):t('Vidéos de la page','Videos on this page')}</h2>
        <span>{t('Sélectionnez les vidéos à télécharger','Select the videos to download')}</span></div>
      <Button variant="ghost" size="icon" className="video-close" aria-label={t('Fermer','Close')} onClick={()=>send({type:'popup.close'})}><AnimatedIcon name="close" size={20}/></Button>
    </header>
    <Button variant="ghost" size="sm" className="video-back" onClick={()=>send({type:'video.open',id:content.id})}>
      <AnimatedIcon name="back" size={16}/>{t('Une seule vidéo','Single video')}
    </Button>
    {content.protectedMedia?<p role="status" className="video-help">{t('Ce lecteur utilise une protection DRM.','This player uses DRM protection.')}</p>
      :busy?<div className="video-status" role="status" aria-busy="true"><span className="video-loading"><AnimatedIcon name="search" size={24}/></span>
        <strong>{t('Recherche des vidéos…','Finding videos…')}</strong><p>{t('Aucun téléchargement ne démarre pendant la recherche.','No downloads start while searching.')}</p></div>
      :content.phase==='failed'?<div className="video-status" role="status"><strong>{t('Liste indisponible','List unavailable')}</strong>
        <p>{t('Ce site ne fournit pas de playlist accessible. Essayez les vidéos détectées dans la page.','This site does not provide an accessible playlist. Try the videos detected on this page.')}</p>
        <Button variant="secondary" size="sm" onClick={()=>load(collection.start)}>{t('Réessayer','Retry')}</Button></div>
      :<>
        {collection.entries.length>0?<>
          <Checkbox checked={all} indeterminate={selected.length>0&&!all} label={t('Tout sélectionner sur cette page','Select all on this page')}
            onCheckedChange={value=>setChecked(new Set(value?collection.entries.map(entry=>entry.key):[]))}/>
          <div className="video-collection-list" aria-label={t('Vidéos disponibles','Available videos')}>
            {collection.entries.map(entry=><div key={entry.key} className="video-collection-entry">
              <Checkbox checked={checked.has(entry.key)} label={`${entry.position}. ${entry.title}`} description={entry.host}
                onCheckedChange={value=>setChecked(current=>{const next=new Set(current);if(value)next.add(entry.key);else next.delete(entry.key);return next;})}/>
            </div>)}
          </div>
        </>:<p className="video-help" role="status">{t('Aucune vidéo accessible dans cette liste.','No accessible videos in this list.')}</p>}
        {collection.mode==='playlist'&&<nav className="video-collection-pages" aria-label={t('Pages de la playlist','Playlist pages')}>
          <Button variant="ghost" size="sm" disabled={collection.start===1} onClick={()=>load(Math.max(1,collection.start-100))}>{t('Précédentes','Previous')}</Button>
          <span>{t(`À partir de ${collection.start}`,`From ${collection.start}`)}</span>
          <Button variant="ghost" size="sm" disabled={!collection.more} onClick={()=>load(collection.start+100)}>{t('Suivantes','Next')}</Button>
        </nav>}
        {collection.entries.length>0&&<>
          <h3 className="video-batch-quality">{t('Qualité souhaitée pour chaque vidéo','Preferred quality for each video')}</h3>
          <VideoQualities values={[0,480,720,1080,1440,2160,4320]} value={quality} preferred={content.preferred} onChange={setQuality}/>
          <p className="video-help">{t('Chaque vidéo utilise la meilleure qualité disponible jusqu’à votre choix. Si aucune qualité inférieure n’existe, la plus proche au-dessus est utilisée. Deux téléchargements à la fois.',
            'Each video uses the best available quality up to your choice. If no lower quality exists, the nearest higher one is used. Two downloads at a time.')}</p>
          <Button variant="primary" size="lg" className="video-download-btn" disabled={!selected.length} onClick={()=>send({
            type:'video.batch',id:content.id,query:collection.token,input:JSON.stringify(selected.map(entry=>entry.key)),index:quality,
          })}><AnimatedIcon name="download" size={20}/><span>{t(`Télécharger ${selected.length} vidéo${selected.length>1?'s':''}`,`Download ${selected.length} video${selected.length>1?'s':''}`)} · {qualityLabel(quality)}</span></Button>
        </>}
        <p className="video-help">{t('Seuls les éléments cochés sont ajoutés à Téléchargements. Les vidéos privées, supprimées, en direct ou protégées peuvent être indisponibles.','Only checked items are added to Downloads. Private, deleted, live or protected videos may be unavailable.')}</p>
      </>}
  </section>;
}
