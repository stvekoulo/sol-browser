import {useState,useRef,useEffect,type RefObject} from 'react';
import {send,formatDomain} from './bridge';
import {t} from './i18n';
import {Icon} from './Icons';
import type {Content,Item,Profile,Setting,Wallpaper} from './PopupTypes';
export function ProfileList({ rootRef, items }: { rootRef: RefObject<HTMLDivElement | null>; items: Profile[] }) {
  const [confirming, setConfirming] = useState<string | null>(null);
  return (
    <div ref={rootRef} className="popup arriving">
      <div className="popup-header">
        <span className="popup-header-title">{t('Profils', 'Profiles')}</span>
      </div>
      <div className="popup-list">
        {items.map(p => (
          <div key={p.id} className={`profile-row${p.current ? ' current' : ''}`}>
            <button
              className="profile-pick"
              disabled={p.current}
              title={p.current ? t('Profil de cette fenêtre', "This window's profile") : t(`Passer au profil ${p.name}`, `Switch to profile ${p.name}`)}
              onClick={() => send({ type: 'profile.switch', id: p.id })}
            >
              <span className="row-badge" aria-hidden>{p.name[0]?.toUpperCase()}</span>
              <span className="row-title">{p.name}</span>
              {p.current && <span className="row-pill">{t('Actif', 'Active')}</span>}
            </button>
            {p.renamable && (
              <button className="profile-edit" aria-label={t(`Renommer le profil ${p.name}`, `Rename profile ${p.name}`)} title={t('Renommer', 'Rename')} onClick={() => send({ type: 'profile.edit', id: p.id })}>
                <Icon d="M10.5 3.5l2 2-6.5 6.5-2.5.5.5-2.5z" size={12} />
              </button>
            )}
            {p.deletable &&
              (confirming === p.id ? (
                <button className="pill-action danger" onClick={() => send({ type: 'profile.delete', id: p.id })}>
                  {t('Supprimer', 'Delete')}
                </button>
              ) : (
                <button className="profile-delete" aria-label={t(`Supprimer le profil ${p.name}`, `Delete profile ${p.name}`)} title={t('Supprimer ce profil et ses données', 'Delete this profile and its data')} onClick={() => setConfirming(p.id)}>
                  <Icon d="M4.5 4.5l7 7M11.5 4.5l-7 7" size={12} />
                </button>
              ))}
          </div>
        ))}
      </div>
      <hr />
      <button className="row" onClick={() => send({ type: 'profile.new' })}>
        <span className="row-badge" aria-hidden>
          <Icon d="M8 3.5v9M3.5 8h9" size={13} />
        </span>
        <span className="row-title">{t('Nouveau profil', 'New profile')}</span>
      </button>
      <button className="row" title={t('Ni historique ni session ; cookies, cache et connexions sont effacés à la fermeture de la fenêtre. Sans les connexions des autres profils.', 'No history or session; cookies, cache and sign-ins are erased when the window closes. Without the other profiles’ sign-ins.')} onClick={() => send({ type: 'window.private' })}>
        <span className="row-badge" aria-hidden>
          <Icon d="M8 1.8L3 3.9v4.2c0 3.2 2.1 5.6 5 6.4 2.9-.8 5-3.2 5-6.4V3.9L8 1.8z" size={13} />
        </span>
        <span className="row-title">{t('Nouvelle fenêtre privée', 'New private window')}</span>
        <span className="row-pill">{t('Ctrl+Maj+N', 'Ctrl+Shift+N')}</span>
      </button>
    </div>
  );
}

// Saisie d'un nom (profil, groupe d'onglets). La réponse part sous la forme de la commande indiquée par l'hôte.
export function SessionPicker({rootRef,content}:{rootRef:RefObject<HTMLDivElement|null>;content:Extract<Content,{popup:'sessions'}>}) {
  const [confirm,setConfirm]=useState(''),[edit,setEdit]=useState(''),[name,setName]=useState('');
  const busy=!!content.deleting;
  return <div ref={rootRef} className="popup arriving sessions-popup">
    <div className="popup-header"><span className="popup-header-title">{t('Session de ce site','Site session')}</span></div>
    <p className="row-hint">{t('Votre choix est mémorisé pour ce site. Les autres sites utilisent la session actuelle par défaut. Changer de session recharge la page ; le contenu saisi peut être perdu.','Your choice is remembered for this site. Other sites use the current session by default. Switching reloads the page; unsaved input may be lost.')}</p>
    <div className="popup-list">
      {[{id:'',name:t('Session actuelle','Current session'),count:0},...content.items].map(item=><div key={item.id} className="session-entry">
        <div className="profile-row">
          <button className="row" disabled={busy} aria-pressed={content.current===item.id} onClick={()=>send({type:'session.set',id:content.id,query:item.id})}>
            <span className="row-badge" aria-hidden>{item.id?'◉':'○'}</span><span className="row-title">{item.name}</span>
            {(content.current===item.id||item.count>0)&&<span className="row-pill">{content.current===item.id?t('Actuelle','Current'):t(`${item.count} onglet(s)`,`${item.count} tab(s)`)}</span>}
          </button>
          {item.id&&<><button className="profile-edit" disabled={busy} aria-label={t(`Renommer ${item.name}`,`Rename ${item.name}`)} onClick={()=>{setEdit(item.id);setName(item.name);setConfirm('')}}><Icon d="M10.5 3.5l2 2-6.5 6.5-2.5.5.5-2.5z" size={12}/></button>
            <button className="profile-delete" disabled={busy||item.count>0} title={item.count?t('Fermez les onglets de cette session pour la supprimer.','Close this session’s tabs to delete it.'):t('Supprimer cette session et ses données','Delete this session and its data')} aria-label={t(`Supprimer ${item.name}`,`Delete ${item.name}`)} onClick={()=>{setConfirm(item.id);setEdit('')}}><Icon d="M4.5 4.5l7 7M11.5 4.5l-7 7" size={12}/></button></>}
        </div>
        {edit===item.id&&item.id&&<form className="session-edit" onSubmit={e=>{e.preventDefault();if(name.trim())send({type:'session.rename',id:item.id,title:name})}}><input autoFocus aria-label={t('Nom de la session','Session name')} value={name} maxLength={30} onChange={e=>setName(e.target.value)}/><button className="pill-action" disabled={!name.trim()}>{t('Renommer','Rename')}</button><button type="button" className="pill-action" onClick={()=>setEdit('')}>{t('Annuler','Cancel')}</button></form>}
        {confirm===item.id&&item.id&&<div className="session-confirm"><p className="row-hint">{t('Supprimer définitivement les cookies, connexions, stockage et extensions de cette session ?','Permanently delete this session’s cookies, sign-ins, storage and extensions?')}</p><button className="pill-action danger" disabled={busy} onClick={()=>send({type:'session.delete',id:item.id})}>{busy?t('Suppression…','Deleting…'):t('Supprimer','Delete')}</button><button className="pill-action" disabled={busy} onClick={()=>setConfirm('')}>{t('Annuler','Cancel')}</button></div>}
      </div>)}
    </div>
    <button className="row" disabled={busy||content.items.length>=30} onClick={()=>send({type:'session.new',id:content.id})}><span className="row-badge" aria-hidden>+</span><span className="row-title">{t('Nouvelle session…','New session…')}</span></button>
    <p className="row-hint">{t('Historique, favoris et notes restent partagés dans ce profil Sol. Les sessions d’une fenêtre privée sont effacées à sa fermeture.','History, bookmarks and notes stay shared in this Sol profile. Private-window sessions are erased when the window closes.')}</p>
  </div>;
}
