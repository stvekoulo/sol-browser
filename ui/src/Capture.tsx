import { useEffect, useLayoutEffect, useRef, useState, type PointerEvent } from 'react';
import { Icon } from './Icons';
import { onMessage, send } from './bridge';
import { t, useLang } from './i18n';

type Area = { x:number; y:number; width:number; height:number };
export default function Capture() {
  const [phase,setPhase]=useState<'idle'|'ready'|'saving'|'selecting'>('idle');
  const [mode,setMode]=useState('visible');
  const [includeBrowser,setIncludeBrowser]=useState(false);
  const [error,setError]=useState('');
  const [preview,setPreview]=useState('');
  const [area,setArea]=useState<Area|null>(null);
  const root=useRef<HTMLDivElement>(null);
  const image=useRef<HTMLImageElement>(null);
  const anchor=useRef<{x:number;y:number}|null>(null);
  useLang();
  const close=()=>{send({type:'capture.selection',index:0});send({type:'capture.close'});setPhase('idle');setPreview('');};
  useEffect(()=>{
    const off=onMessage(data=>{
      const message=data as {event?:string;kind?:string;mode?:string;data?:string};
      if(message.event==='capture-open'){
        setPhase('ready');setMode(message.mode!);setError('');
        requestAnimationFrame(()=>{if(root.current)send({type:'capture.size',height:root.current.getBoundingClientRect().height});});
      }
      if(message.event==='capture-preview'){setPreview(message.data!);setArea(null);setPhase('selecting');send({type:'capture.selection',index:1});}
      if(message.event==='capture-state'){
        if(message.kind==='done')close();
        else if(message.kind==='error'){setError(message.data??t('Capture impossible.','Capture failed.'));setPhase('ready');send({type:'capture.selection',index:0});send({type:'capture.visibility',index:1});}
        else if(message.kind==='saving'){setPhase('saving');send({type:'capture.selection',index:0});send({type:'capture.visibility',index:0});}
      }
    });
    const escape=(event:KeyboardEvent)=>{if(event.key==='Escape')close();};
    window.addEventListener('keydown',escape);return()=>{off();window.removeEventListener('keydown',escape);};
  },[]);
  useLayoutEffect(()=>{
    if(!root.current||phase==='selecting'||phase==='idle')return;
    const measure=()=>send({type:'capture.size',height:root.current!.getBoundingClientRect().height});
    const observer=new ResizeObserver(measure);observer.observe(root.current);measure();return()=>observer.disconnect();
  },[phase]);
  const select=(event:PointerEvent<HTMLDivElement>)=>{
    if(!image.current)return;
    const box=image.current.getBoundingClientRect();
    const point={x:Math.max(0,Math.min(1,(event.clientX-box.left)/box.width)),y:Math.max(0,Math.min(1,(event.clientY-box.top)/box.height))};
    if(event.type==='pointerdown'){anchor.current=point;setArea(null);event.currentTarget.setPointerCapture(event.pointerId);return;}
    if(!anchor.current)return;
    const next={x:Math.min(point.x,anchor.current.x),y:Math.min(point.y,anchor.current.y),width:Math.abs(point.x-anchor.current.x),height:Math.abs(point.y-anchor.current.y)};
    setArea(next);
    if(event.type==='pointerup'){
      anchor.current=null;
      const width=Math.floor(next.width*image.current.naturalWidth),height=Math.floor(next.height*image.current.naturalHeight);
      if(width<5||height<5)return;
      send({type:'capture.region',x:Math.floor(next.x*image.current.naturalWidth),y:Math.floor(next.y*image.current.naturalHeight),width,height});
      setPhase('saving');send({type:'capture.selection',index:0});send({type:'capture.visibility',index:0});
    }
  };
  if(phase==='selecting')return <div className="capture-selection" onPointerDown={select} onPointerMove={select} onPointerUp={select}>
    <img ref={image} src={preview} alt={t('Aperçu du navigateur à sélectionner','Browser preview to select')} draggable={false}/>
    <p>{t('Trace une zone · Échap pour annuler','Drag an area · Escape to cancel')}</p>
    {area&&<div className="capture-area" style={{left:`${area.x*100}%`,top:`${area.y*100}%`,width:`${area.width*100}%`,height:`${area.height*100}%`}}/>}
  </div>;
  return <div ref={root} className="capture-pill" role="region" aria-label={t('Options de capture','Capture options')}>
    {phase==='ready'?<>
      <div className="capture-options">
        <span className="capture-symbol"><Icon d="M2.5 5h2l1-1.5h5l1 1.5h2v8h-11zM8 11a2 2 0 1 0 0-4 2 2 0 0 0 0 4"/></span>
        <div className="capture-modes" role="group" aria-label={t('Zone de capture','Capture area')}>
          {[["region",t('Zone','Area')],["visible",includeBrowser?t('Fenêtre entière','Entire window'):t('Partie visible','Visible part')],...(!includeBrowser?[["full",t('Page entière','Full page')]]:[])].map(([value,label])=><button key={value} aria-pressed={mode===value} onClick={()=>setMode(value)}>{label}</button>)}
        </div>
        <label title={t('Inclut la barre du haut, la sidebar et les onglets affichés.','Includes the top bar, sidebar and displayed tabs.')}><input className="capture-browser" type="checkbox" checked={includeBrowser} onChange={e=>{setIncludeBrowser(e.target.checked);if(mode==='full')setMode('visible');}}/>{t('Inclure le navigateur','Include browser')}</label>
        <button className="capture-action" onClick={()=>{setError('');setPhase('saving');send({type:'capture.run',query:mode,index:includeBrowser?1:0});}}>{t('Capturer','Capture')}</button>
        <button className="capture-close" aria-label={t('Fermer','Close')} onClick={close}>×</button>
      </div>
      <p className={error?'capture-error':'capture-hint'}>{error||(includeBrowser?t('Barre du haut, sidebar et onglets affichés inclus. Les contrôles de capture sont exclus.','Top bar, sidebar and displayed tabs included. Capture controls are excluded.'):t('Seul le contenu de la page est capturé.','Only page content is captured.'))}</p>
    </>:phase==='saving'?<p className="capture-hint">{t('Préparation de la capture…','Preparing capture…')}</p>:null}
  </div>;
}
