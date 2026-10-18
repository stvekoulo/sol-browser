import {useEffect,useRef,type MouseEvent} from 'react';
import {send} from './bridge';
import {t} from './i18n';

export function WindowControls({maximized}:{maximized:boolean}){
  return <div className="lights" aria-label={t('Fenêtre','Window')}>
    <button className="light close" aria-label={t('Fermer','Close')} onClick={()=>send({type:'window.close'})}/>
    <button className="light minimize" aria-label={t('Réduire','Minimize')} onClick={()=>send({type:'window.minimize'})}/>
    <button className="light maximize" aria-label={maximized?t('Restaurer','Restore'):t('Agrandir','Maximize')} onClick={()=>send({type:'window.maximize'})}/>
  </div>;
}

export function useWindowDrag(){
  const armed=useRef<{x:number;y:number}|null>(null);
  useEffect(()=>{
    const disarm=()=>{armed.current=null};
    const move=(e:globalThis.MouseEvent)=>{
      if(!armed.current)return;
      if(!(e.buttons&1))return disarm();
      if(Math.abs(e.screenX-armed.current.x)+Math.abs(e.screenY-armed.current.y)<4)return;
      disarm();send({type:'window.drag'});
    };
    window.addEventListener('mousemove',move);window.addEventListener('mouseup',disarm);
    window.addEventListener('mousedown',disarm,true);window.addEventListener('blur',disarm);window.addEventListener('mouseleave',disarm);
    return()=>{window.removeEventListener('mousemove',move);window.removeEventListener('mouseup',disarm);window.removeEventListener('mousedown',disarm,true);window.removeEventListener('blur',disarm);window.removeEventListener('mouseleave',disarm)};
  },[]);
  return {
    onMouseDown:(e:MouseEvent)=>{if(e.button===0&&e.target===e.currentTarget)armed.current={x:e.screenX,y:e.screenY}},
    onDoubleClick:(e:MouseEvent)=>{if(e.target===e.currentTarget)send({type:'window.maximize'})}
  };
}
