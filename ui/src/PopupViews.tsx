import SolMenu from './SolMenu';
import {useState,useRef,useEffect,type RefObject} from 'react';
import {send,formatDomain} from './bridge';
import {t} from './i18n';
import {Icon} from './Icons';
import type {Content,Item,Profile,Setting,Wallpaper} from './PopupTypes';
import GroupEditor from './GroupEditor';
import QuickNotes from './QuickNotes';
import SolAiAction from './SolAiAction';
import {DownloadCard} from './Downloads';
import CommandPalette from './CommandPalette';
import Row from './PopupRow';
import {menuIconPath,isClosing} from './PopupIcons';
import {ProfileList,SessionPicker} from './PopupProfiles';
import {NameForm,ShortcutForm,ComparePicker} from './PopupForms';
import SettingsPopup from './SettingsPopup';
import LibraryPopup from './LibraryPopup';
export default function PopupViews({content,rootRef,opening,selected,settingsSection,setSettingsSection}:{content:Content;rootRef:RefObject<HTMLDivElement|null>;opening:number;selected:number;settingsSection:string;setSettingsSection:(section:string)=>void}){
const root=rootRef;


  if (content.popup === 'ai-action') return <div key={opening} ref={root} className="popup arriving"><SolAiAction content={content}/></div>;

  if (content.popup === 'palette')
    return <div key={opening} ref={root} className="popup arriving palette-popup"><CommandPalette items={content.items} listHeight={content.listHeight} /></div>;

  if (content.popup === 'notes-select')
    return <div ref={root} className="popup arriving"><div className="popup-header"><span className="popup-header-title">{t('Ajouter le passage à…', 'Add passage to…')}</span></div>
      <button className="row" onClick={()=>send({type:'notes.select',id:''})}>{t('Une nouvelle note', 'A new note')}</button>
      <div className="popup-list">{content.items.map(n=><button className="row" key={n.id} onClick={()=>send({type:'notes.select',id:n.id})}>{n.title}</button>)}</div></div>;

  if (content.popup === 'notes')
    return <div key={opening} ref={root} className="popup arriving notes-popup"><QuickNotes key={`${opening}-${content.draft.id}-${content.draft.updated}`} content={content} /></div>;

  if (content.popup === 'translation')
    return <div key={opening} ref={root} className="popup arriving translation-popup" aria-label={t('Traduire la page', 'Translate page')}>
      <div className="translation-heading"><span className="row-title">{t('Traduire la page', 'Translate page')}</span>
        <button className="pill-action" aria-label={t('Fermer', 'Close')} onClick={() => send({type:'popup.close'})}>×</button></div>
      <div className="popup-segment" role="group" aria-label={t('Langue de traduction', 'Translation language')}>
        <button className="popup-seg-btn" disabled={!content.available} onClick={() => send({type:'translation.run', id:content.id, query:content.target})}>
          {content.target === 'fr' ? t('Traduire en français', 'Translate to French') : t('Traduire en anglais', 'Translate to English')}<span aria-hidden> ↗</span>
        </button>
      </div>
      {content.original && <button className="pill-action" onClick={() => send({type:'translation.original', id:content.id})}>{t('Voir l’original', 'View original')}</button>}
      <p className="row-hint">{content.available ? t('Google Traduction affiche la page dans cet onglet. Son adresse est transmise à Google.', 'Google Translate displays the page in this tab. Its address is sent to Google.') : t('Cette adresse locale ne peut pas être traduite par Google.', 'This local address cannot be translated by Google.')}</p>
    </div>;

  if (content.popup === 'suggestions')
    return (
      <div ref={root} className="popup" role="listbox" aria-label="Suggestions">
        {content.items.map((item, index) => (
          <Row
            key={index}
            item={item}
            selected={index === selected}
            onPick={() => send({ type: 'suggest.pick', index })}
          />
        ))}
      </div>
    );

  if (content.popup === 'split-layout')
    return <div key={opening} ref={root} className="popup arriving split-layout-popup" role="menu" aria-label={t('Disposition des pages', 'Page layout')}>
      <div className="split-layout-heading">{t('Disposition des pages', 'Page layout')}</div>
      {[{ratio:1/3,label:t('Plus de place à droite','More room on the right')},{ratio:0.5,label:t('Deux pages égales','Equal pages')},{ratio:2/3,label:t('Plus de place à gauche','More room on the left')}].map(p =>
        <button key={p.ratio} className="row split-layout-choice" role="menuitemradio" aria-checked={Math.abs(content.ratio-p.ratio)<0.01}
          onClick={() => send({type:'split.resize',ratio:p.ratio})}>
          <span className="split-layout-preview" aria-hidden><span style={{flex:p.ratio}}/><span style={{flex:1-p.ratio}}/></span>
          <span className="row-title">{p.label}</span>
          {Math.abs(content.ratio-p.ratio)<0.01 && <Icon d="M3 8l3 3 7-7" size={13}/>}
        </button>)}
      <div className="split-layout-hint">{t('Glissez le séparateur pour ajuster librement.', 'Drag the divider to adjust freely.')}</div>
    </div>;

  if(content.popup==='menu')return <SolMenu key={opening} items={content.items} rootRef={root}/>;

  if (content.popup === 'downloads')
    return (
      <div key={opening} ref={root} className="popup arriving">
        <div className="popup-header">
          <span className="popup-header-title">{t('Téléchargements', 'Downloads')}</span>
          <span className="popup-badge">{content.items.length}</span>
        </div>
        <div className="popup-list">
          {content.items.map(d => (
            <DownloadCard key={d.id} d={d} />
          ))}
        </div>
      </div>
    );

  if (content.popup === 'sessions') return <SessionPicker key={opening} rootRef={root} content={content} />;
  if (content.popup === 'profiles')
    return <ProfileList key={opening} rootRef={root} items={content.items} />;

  if (content.popup === 'site-reset') return <div ref={root} className="popup arriving">
    <div className="popup-header"><span className="popup-header-title">{t('Vider la session de ce site ?', 'Clear this site’s session?')}</span></div>
    <p className="row-sub extension-note"><strong>{content.site}</strong> · {content.session}</p>
    <p className="row-sub extension-note">{t('Cookies, données locales, cache du site et autorisations seront effacés. Vous serez déconnecté et la page sera rechargée.', 'Cookies, local data, site cache and permissions will be cleared. You will be signed out and the page will reload.')}</p>
    <p className="row-sub extension-note">{t('Les cookies partagés avec d’autres sous-domaines seront aussi supprimés, ainsi que le cache HTTP de cette session. Les autres sessions, vos mots de passe, favoris et historique sont conservés.', 'Cookies shared with other subdomains and this session’s HTTP cache will also be cleared. Other sessions, your passwords, bookmarks and history are kept.')}</p>
    <div className="extension-confirm-actions"><button className="pill-action" onClick={() => send({type:'popup.close'})}>{t('Annuler','Cancel')}</button><button className="pill-action danger" onClick={() => send({type:'site.reset'})}>{t('Vider et recharger','Clear and reload')}</button></div>
  </div>;

  if (content.popup === 'extension-confirm')
    return <div ref={root} className="popup arriving">
      <div className="popup-header"><span className="popup-header-title extension-title">{content.icon && <img src={content.icon} alt="" />}{content.update ? t(`Mettre à jour « ${content.name} » ?`, `Update “${content.name}”?`) : t(`Ajouter « ${content.name} » ?`, `Add “${content.name}”?`)}</span></div>
      <p className="row-sub extension-note">{t(`Version ${content.version}, paquet signé du Chrome Web Store.`, `Version ${content.version}, signed Chrome Web Store package.`)} {content.access.length > 0 ? t('Elle pourra :', 'It can:') : t('Elle ne demande aucun accès particulier.', 'It asks for no special access.')}</p>
      {content.access.length > 0 && <ul className="extension-access">{content.access.map(line => <li key={line}>{line}</li>)}</ul>}
      <div className="extension-confirm-actions">
        <button className="pill-action" onClick={() => send({type:'popup.close'})}>{t('Annuler', 'Cancel')}</button>
        <button className="pill-action primary" onClick={() => send({type:'extension.confirm', id:content.id})}>{content.update ? t('Mettre à jour', 'Update') : t('Ajouter l’extension', 'Add extension')}</button>
      </div>
    </div>;

  if (content.popup === 'extensions')
    return (
      <div ref={root} className="popup arriving">
        <div className="popup-header">
          <span className="popup-header-title">{t('Extensions', 'Extensions')}</span>
        </div>
        <div className="popup-list">
          <p className="row-sub extension-note">{t('Session : ', 'Session: ')}<strong>{content.session}</strong>. {t('Les extensions activées s’appliquent aux pages de cette session. Rechargez la page après un changement. Elles restent désactivées en navigation privée.', 'Enabled extensions apply to pages in this session. Reload the page after a change. They remain disabled in private browsing.')}</p>
          {content.installed.length === 0 && <p className="row-sub extension-note">{t('Aucune extension ajoutée pour l’instant.', 'No extension added yet.')}</p>}
          {content.installed.map(e => (
            <div key={e.id} className="setting-row static extension-row">
              <span className="setting-text">
                <span className="row-title extension-title">{e.icon && <img src={e.icon} alt="" />}{e.name}</span>
                <span className="row-sub">{e.enabled ? t(`Version ${e.version}`, `Version ${e.version}`) : t('Désactivée', 'Disabled')}{e.update && t(` · version ${e.update} disponible, avec de nouveaux accès`, ` · version ${e.update} available, with new access`)}</span>
              </span>
              <span className="row-actions">
                {e.update && <button className="pill-action primary" onClick={() => send({ type: 'extension.update', id: e.id })}>{t('Mettre à jour', 'Update')}</button>}
                <button className={`pill-action${e.pinned ? ' on' : ''}`} aria-pressed={e.pinned} onClick={() => send({ type: 'extension.pin', id: e.id })}>{e.pinned ? t('Désépingler', 'Unpin') : t('Épingler', 'Pin')}</button>
                {e.page && e.enabled && <button className="pill-action" onClick={() => send({ type: 'extension.show', id: e.id })}>{t('Ouvrir', 'Open')}</button>}
                {e.options && e.enabled && <button className="pill-action" onClick={() => send({ type: 'extension.options', id: e.id })}>{t('Réglages', 'Settings')}</button>}
                <button className="pill-action" onClick={() => send({ type: 'extension.reload', id: e.id })}>{t('Recharger', 'Reload')}</button>
                <button className="pill-action" onClick={() => send({ type: 'extension.toggle', id: e.id })}>{e.enabled ? t('Désactiver', 'Disable') : t('Activer', 'Enable')}</button>
                <button className="pill-action" onClick={() => send({ type: 'extension.remove', id: e.id })}>{t('Retirer', 'Remove')}</button>
              </span>
            </div>
          ))}
          {content.available.length > 0 && <p className="row-sub extension-note">{t('Trouvées dans vos autres navigateurs :', 'Found in your other browsers:')}</p>}
          {content.available.map(e => (
            <div key={e.id} className="setting-row static">
              <span className="setting-text">
                <span className="row-title">{e.name}</span>
                <span className="row-sub">{e.from}</span>
              </span>
              <button className="pill-action" onClick={() => send({ type: 'extension.add', id: e.id })}>{t('Ajouter', 'Add')}</button>
            </div>
          ))}
        </div>
        <hr />
        <button className="row" onClick={() => send({ type: 'extension.store' })}>
          <span className="row-title">{t('Installer depuis le Chrome Web Store…', 'Install from the Chrome Web Store…')}</span>
        </button>
        {content.installed.some(e => e.store) &&
          <button className="row" disabled={content.updates === 'checking'} onClick={() => send({ type: 'extensions.update' })}>
            <span className="row-title">{content.updates === 'checking' ? t('Recherche de mises à jour…', 'Checking for updates…')
              : content.updates === 'current' ? t('Extensions à jour · Rechercher à nouveau', 'Extensions up to date · Check again')
              : content.updates === 'updated' ? t('Extensions mises à jour · Rechercher à nouveau', 'Extensions updated · Check again')
              : content.updates === 'failed' ? t('Recherche impossible · Réessayer', 'Check failed · Try again')
              : t('Rechercher des mises à jour', 'Check for updates')}</span>
          </button>}
        <button className="row" onClick={() => send({ type: 'extension.folder' })}>
          <span className="row-title">{t('Ajouter depuis un dossier…', 'Add from a folder…')}</span>
        </button>
      </div>
    );

  if (content.popup === 'group-edit')
    return <GroupEditor key={opening} rootRef={root} group={content} />;

  if (content.popup === 'permission')
    return (
      <div key={opening} ref={root} className="popup arriving" role="alertdialog" aria-label={t("Demande d'accès", 'Permission request')}>
        <div className="name-form">
          <span className="name-form-title">{content.site} {t('souhaite', 'wants to')} {content.what}</span>
          <p className="row-sub">
            {content.secure ? t('Votre réponse sera retenue pour ce site.', 'Your answer will be remembered for this site.') : t("Attention : la connexion à ce site n'est pas chiffrée. Votre réponse sera retenue pour ce site.", 'Warning: the connection to this site is not encrypted. Your answer will be remembered for this site.')}
          </p>
          <div className="name-form-actions">
            <button className="pill-action" autoFocus onClick={() => send({ type: 'permission.answer', index: 0 })}>{t('Refuser', 'Deny')}</button>
            <button className="pill-action primary" onClick={() => send({ type: 'permission.answer', index: 1 })}>{t('Autoriser', 'Allow')}</button>
          </div>
        </div>
      </div>
    );

  if (content.popup === 'shortcut-edit')
    return <ShortcutForm key={opening} rootRef={root} ask={content} />;

  if (content.popup === 'compare-pick')
    return <ComparePicker key={opening} rootRef={root} ask={content} />;

  if (content.popup === 'prompt')
    return <NameForm key={opening} rootRef={root} ask={content} />;

  if (content.popup === 'notice')
    return (
      <div ref={root} className="popup arriving">
        <div className="name-form">
          <span className="name-form-title">{content.title}</span>
          <p className="row-sub">{content.hint}</p>
          <div className="name-form-actions">
            <button autoFocus className="pill-action primary" onClick={() => send({ type: 'popup.close' })}>OK</button>
          </div>
        </div>
      </div>
    );


if(content.popup==='settings')return <SettingsPopup content={content} rootRef={root} opening={opening} settingsSection={settingsSection} setSettingsSection={setSettingsSection}/>;
if(content.popup==='library')return <LibraryPopup key={opening} content={content} rootRef={root} opening={opening}/>;
return null;
}
