import {Card} from '@/components/motion/card';
import {Button} from '@/components/motion/button/base';
import {useState,type RefObject} from 'react';
import {SharedLayoutBg} from '@/components/motion/shared-layout-bg';
import {Input} from '@/components/motion/input';
import {send,formatDomain} from './bridge';
import {t} from './i18n';
import {Icon} from './Icons';
import type {Content,Item,Profile,Setting,Wallpaper} from './PopupTypes';
export function ProfileList({ rootRef, items }: { rootRef: RefObject<HTMLDivElement | null>; items: Profile[] }) {
  const [confirming, setConfirming] = useState<string | null>(null);
  return (
    <div ref={rootRef} className="popup arriving profiles-popup">
      <div className="popup-header">
        <Icon name="user" size={18}/><span className="popup-header-title">{t('Profils', 'Profiles')}</span>
      </div>
      <SharedLayoutBg className="popup-list sol-account-list" inset={0} pillClassName="sol-account-hover">
        {items.map(p => (
          <Card size="compact" selected={p.current} key={p.id} className={`sol-profile-card profile-row${p.current ? ' current' : ''}`}>
            <Button variant="ghost" size="sm" pressScale={0.98} whileHover={undefined}
              className="profile-pick"
              disabled={p.current}
              aria-current={p.current?"true":undefined}
              title={p.current ? t('Profil de cette fenêtre', "This window's profile") : t(`Passer au profil ${p.name}`, `Switch to profile ${p.name}`)}
              onClick={() => send({ type: 'profile.switch', id: p.id })}
            >
              <span className="row-badge profile-avatar" aria-hidden>{p.name[0]?.toUpperCase()}</span>
              <span className="row-title">{p.name}</span>
              {p.current && <span className="row-pill">{t('Actif', 'Active')}</span>}
            </Button>
            {p.renamable && (
              <Button variant="ghost" size="sm" pressScale={0.98} whileHover={undefined} className="profile-edit" aria-label={t(`Renommer le profil ${p.name}`, `Rename profile ${p.name}`)} title={t('Renommer', 'Rename')} onClick={() => send({ type: 'profile.edit', id: p.id })}>
                <Icon d="M10.5 3.5l2 2-6.5 6.5-2.5.5.5-2.5z" size={12} />
              </Button>
            )}
            {p.deletable &&
              (confirming === p.id ? (
                <Button variant="ghost" size="sm" pressScale={0.98} whileHover={undefined} className="pill-action danger" onClick={() => send({ type: 'profile.delete', id: p.id })}>
                  {t('Supprimer', 'Delete')}
                </Button>

              ) : (
                <Button variant="ghost" size="sm" pressScale={0.98} whileHover={undefined} className="profile-delete" aria-label={t(`Supprimer le profil ${p.name}`, `Delete profile ${p.name}`)} title={t('Supprimer ce profil et ses données', 'Delete this profile and its data')} onClick={() => setConfirming(p.id)}>
                  <Icon name="delete" size={14} />
                </Button>
              ))}
          </Card>
        ))}
      </SharedLayoutBg>
      <hr />
      <Button variant="ghost" size="sm" pressScale={0.98} whileHover={undefined} className="row" onClick={() => send({ type: 'profile.new' })}>
        <span className="row-badge" aria-hidden>
          <Icon name="userAdd" size={16} />
        </span>
        <span className="row-title">{t('Nouveau profil', 'New profile')}</span>
      </Button>
      <Button variant="ghost" size="sm" pressScale={0.98} whileHover={undefined} className="row" title={t('Ni historique ni session ; cookies, cache et connexions sont effacés à la fermeture de la fenêtre. Sans les connexions des autres profils.', 'No history or session; cookies, cache and sign-ins are erased when the window closes. Without the other profiles’ sign-ins.')} onClick={() => send({ type: 'window.private' })}>
        <span className="row-badge" aria-hidden>
          <Icon name="private" size={16} />
        </span>
        <span className="row-title">{t('Nouvelle fenêtre privée', 'New private window')}</span>
        <span className="row-pill">{t('Ctrl+Maj+N', 'Ctrl+Shift+N')}</span>
      </Button>
    </div>
  );
}

// Saisie d'un nom (profil, groupe d'onglets). La réponse part sous la forme de la commande indiquée par l'hôte.
export function SessionPicker({rootRef,content}:{rootRef:RefObject<HTMLDivElement|null>;content:Extract<Content,{popup:'sessions'}>}) {
  const [confirm,setConfirm]=useState(''),[edit,setEdit]=useState(''),[name,setName]=useState('');
  const busy=!!content.deleting;
  return <div ref={rootRef} className="popup arriving sessions-popup" role="dialog" aria-labelledby="session-picker-title">
    <div className="popup-header"><span className="session-picker-heading"><Icon name="session" size={17}/><span id="session-picker-title" className="popup-header-title">{t('Session de cet onglet','Tab session')}</span></span><Button variant="ghost" size="icon" pressScale={0.98} whileHover={undefined} className="session-picker-close" aria-label={t('Fermer','Close')} onClick={()=>send({type:'popup.close'})}><Icon name="close" size={14}/></Button></div>
    <p className="row-hint">{t('Les nouveaux onglets et liens reprennent cette session. Les autres onglets gardent la leur. Changer de session recharge cet onglet et peut effacer une saisie en cours.','New tabs and links inherit this session. Other tabs keep their own. Switching sessions reloads this tab and may discard unsaved input.')}</p>
    <SharedLayoutBg className="popup-list sol-account-list" inset={0} pillClassName="sol-account-hover">
      {[{id:'',name:t('Session actuelle','Current session'),count:0},...content.items].map(item=><div key={item.id} className={`session-entry${content.current===item.id?' current':''}`}>
        <div className="profile-row">
          <Button variant="ghost" size="sm" pressScale={0.98} whileHover={undefined} className="row" disabled={busy} aria-pressed={content.current===item.id} onClick={()=>send({type:'session.set',id:content.id,query:item.id})}>
            <span className="row-badge session-avatar" aria-hidden><Icon name="session" size={15}/></span><span className="row-title">{item.name}</span>
            {(content.current===item.id||item.count>0)&&<span className="row-pill session-current">{content.current===item.id?<><Icon name="check" size={12}/>{t('Actuelle','Current')}</>:t(`${item.count} onglet(s)`,`${item.count} tab(s)`)}</span>}
          </Button>
          {item.id&&<><Button variant="ghost" size="sm" pressScale={0.98} whileHover={undefined} className="profile-edit" disabled={busy} aria-label={t(`Renommer ${item.name}`,`Rename ${item.name}`)} onClick={()=>{setEdit(item.id);setName(item.name);setConfirm('')}}><Icon name="edit" size={14}/></Button>
            <Button variant="ghost" size="sm" pressScale={0.98} whileHover={undefined} className="profile-delete" disabled={busy||item.count>0} title={item.count?t('Fermez les onglets de cette session pour la supprimer.','Close this session’s tabs to delete it.'):t('Supprimer cette session et ses données','Delete this session and its data')} aria-label={t(`Supprimer ${item.name}`,`Delete ${item.name}`)} onClick={()=>{setConfirm(item.id);setEdit('')}}><Icon name="delete" size={14}/></Button></>}
        </div>
        {edit===item.id&&item.id&&<form className="session-edit" onSubmit={e=>{e.preventDefault();if(name.trim())send({type:'session.rename',id:item.id,title:name})}}><Input className="sol-popup-input" classNames={{field:"sol-popup-input-field"}} autoFocus aria-label={t('Nom de la session','Session name')} value={name} maxLength={30} onChange={setName}/><Button variant="ghost" size="sm" pressScale={0.98} whileHover={undefined} type="submit" className="pill-action" disabled={!name.trim()}>{t('Renommer','Rename')}</Button><Button variant="ghost" size="sm" pressScale={0.98} whileHover={undefined} type="button" className="pill-action" onClick={()=>setEdit('')}>{t('Annuler','Cancel')}</Button></form>}
        {confirm===item.id&&item.id&&<div className="session-confirm"><p className="row-hint">{t('Supprimer définitivement les cookies, connexions, stockage et extensions de cette session ?','Permanently delete this session’s cookies, sign-ins, storage and extensions?')}</p><Button variant="ghost" size="sm" pressScale={0.98} whileHover={undefined} className="pill-action danger" disabled={busy} onClick={()=>send({type:'session.delete',id:item.id})}>{busy?t('Suppression…','Deleting…'):t('Supprimer','Delete')}</Button><Button variant="ghost" size="sm" pressScale={0.98} whileHover={undefined} className="pill-action" disabled={busy} onClick={()=>setConfirm('')}>{t('Annuler','Cancel')}</Button></div>}
      </div>)}
    </SharedLayoutBg>
    <Button variant="ghost" size="sm" pressScale={0.98} whileHover={undefined} className="row session-create" disabled={busy||content.items.length>=30} onClick={()=>send({type:'session.new',id:content.id})}><span className="row-badge" aria-hidden><Icon name="plus" size={16}/></span><span className="row-title">{t('Nouvelle session…','New session…')}</span></Button>
    <p className="row-hint sessions-footer">{t('Historique, favoris et notes restent partagés dans ce profil Sol. Les sessions d’une fenêtre privée sont effacées à sa fermeture.','History, bookmarks and notes stay shared in this Sol profile. Private-window sessions are erased when the window closes.')}</p>
  </div>;
}
