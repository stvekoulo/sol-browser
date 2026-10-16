import type {RefObject} from 'react';
import {Button} from '@/components/motion/button/base';
import {Card} from '@/components/motion/card';
import {Switch} from '@/components/motion/switch';
import {send} from './bridge';
import {t} from './i18n';
import {Icon} from './Icons';
import type {Content} from './PopupTypes';
export default function ExtensionsPopup({content,rootRef}:{content:Extract<Content,{popup:'extensions'}>;rootRef:RefObject<HTMLDivElement|null>}){
  return <div ref={rootRef} className="popup extensions-popup">
    <div className="popup-header"><span className="popup-header-title">{t('Extensions','Extensions')}</span><span className="popup-badge">{content.installed.length}</span></div>
    <p className="row-sub extension-note">{t('Disponibles dans toutes les sessions de ce profil Sol. Cookies et données des extensions restent séparés entre sessions. Rechargez les pages après un changement. Les extensions sont désactivées en navigation privée.','Available in every session of this Sol profile. Extension data and cookies stay separate between sessions. Reload pages after a change. Extensions are disabled in private browsing.')}</p>
    <p className="row-sub extension-note">{t('Si une fonction exige Edge, utilisez « Microsoft Edge… ». Installez-y aussi l’extension et reconnectez-vous : les sessions sont séparées.','If a feature requires Edge, use “Microsoft Edge…”. Install the extension there and sign in again: sessions are separate.')}</p>
    <div className="popup-list">
      {!content.installed.length&&<p className="row-hint">{t('Aucune extension ajoutée pour l’instant.','No extension added yet.')}</p>}
      {content.installed.map(e=><Card key={e.id} size="compact" className="sol-extension-card" data-extension={e.id}>
        <div className="sol-extension-heading"><div className="setting-text"><span className="row-title extension-title">{e.icon&&<img src={e.icon} alt=""/>}{e.name}</span><span className="row-sub">{t(`Version ${e.version}`,`Version ${e.version}`)}{!e.enabled&&t(' · Désactivée',' · Disabled')}</span></div>
          <Switch className="sol-switch" checked={e.enabled} ariaLabel={t(`Activer ${e.name}`,`Enable ${e.name}`)} onCheckedChange={()=>send({type:'extension.toggle',id:e.id})}/></div>
        {e.update&&<div className="sol-extension-update"><span className="row-sub">{t(`Version ${e.update} disponible, avec de nouveaux accès.`,`Version ${e.update} available, with new access.`)}</span><Button variant="ghost" size="sm" className="pill-action primary" onClick={()=>send({type:'extension.update',id:e.id})}>{t('Mettre à jour','Update')}</Button></div>}
        <div className="row-actions">
          <Button variant="ghost" size="sm" className={`pill-action${e.pinned?' on':''}`} aria-pressed={e.pinned} onClick={()=>send({type:'extension.pin',id:e.id})}>{e.pinned?t('Désépingler','Unpin'):t('Épingler','Pin')}</Button>
          {e.page&&e.enabled&&<Button variant="ghost" size="sm" className="pill-action" onClick={()=>send({type:'extension.show',id:e.id})}>{t('Ouvrir','Open')}</Button>}
          {e.options&&e.enabled&&<Button variant="ghost" size="sm" className="pill-action" onClick={()=>send({type:'extension.options',id:e.id})}>{t('Réglages','Settings')}</Button>}
          <Button variant="ghost" size="sm" className="pill-action" onClick={()=>send({type:'extension.edge',id:e.id})}>{t('Microsoft Edge…','Microsoft Edge…')}</Button>
          <Button variant="ghost" size="sm" className="pill-action" onClick={()=>send({type:'extension.reload',id:e.id})}><Icon name="reload" d="" size={13}/>{t('Recharger','Reload')}</Button>
          <Button variant="ghost" size="sm" className="pill-action sol-remove-extension" onClick={()=>send({type:'extension.remove',id:e.id})}>{t('Retirer','Remove')}</Button>
        </div>
      </Card>)}
      {!!content.available.length&&<p className="row-sub extension-note">{t('Trouvées dans vos autres navigateurs :','Found in your other browsers:')}</p>}
      {content.available.map(e=><Card key={e.id} className="sol-extension-card sol-extension-available" size="compact"><span className="setting-text"><span className="row-title">{e.name}</span><span className="row-sub">{e.from}</span></span><Button variant="ghost" size="sm" className="pill-action" onClick={()=>send({type:'extension.add',id:e.id})}>{t('Ajouter','Add')}</Button></Card>)}
    </div>
    <hr/>
    <Button variant="ghost" size="sm" className="row" onClick={()=>send({type:'extension.store'})}><span className="row-title">{t('Installer depuis le Chrome Web Store…','Install from the Chrome Web Store…')}</span></Button>
    {content.installed.some(e=>e.store)&&<Button variant="ghost" size="sm" className="row" disabled={content.updates==='checking'} onClick={()=>send({type:'extensions.update'})}><span className="row-title">{content.updates==='checking'?t('Recherche de mises à jour…','Checking for updates…'):content.updates==='failed'?t('Recherche interrompue · Réessayer','Check interrupted · Retry'):content.updates==='current'||content.updates==='updated'?t('Extensions à jour · Rechercher à nouveau','Extensions up to date · Check again'):t('Rechercher les mises à jour','Check for updates')}</span></Button>}
  </div>;
}
