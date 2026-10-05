import { useEffect, useLayoutEffect, useRef, useState, type RefObject } from 'react';
import { formatDomain, interfaceScale, onMessage, send } from './bridge';
import { DownloadCard, type Download } from './Downloads';
import GroupEditor from './GroupEditor';
import { t, useLang } from './i18n';
import { Icon } from './Icons';

type Item = { url: string; title: string; kind?: 'search' | 'go' | 'history' | 'site' };
type Content =
  | { popup: 'suggestions'; items: Item[] }
  | { popup: 'library'; tab?: 'history' | 'bookmarks' | null; bookmarks: Item[]; history: Item[] }
  | { popup: 'menu'; items: string[] }
  | { popup: 'split-layout'; ratio: number }
  | { popup: 'downloads'; items: Download[] }
  | { popup: 'settings'; version: string; update: { version: string; percent: number; ready: boolean } | null; check: 'idle' | 'checking' | 'current' | 'failed' | 'portable'; zoom: number; uiScale: number; theme: number; language: number; home: string; font: number; ublock: string | null; engine: number; engines: string[]; sites: { origin: string; host: string; summary: string }[]; permissions: number; siteData: 'idle' | 'clearing' | 'done' | 'failed'; tint: Setting[]; items: Setting[] }
  | { popup: 'profiles'; items: Profile[] }
  | { popup: 'group-edit'; create: boolean; id: string; name: string; color: string; icon: string; presets: string[] }
  | { popup: 'shortcut-edit'; id: string; name: string; url: string }
  | { popup: 'compare-pick'; id: string; min: number; max: number; devices: { name: string; label: string; size: string; on: boolean }[] }
  | { popup: 'permission'; site: string; what: string; secure: boolean }
  | { popup: 'prompt'; title: string; hint: string; placeholder: string; submit: string; value: string; command: string; id: string | null; secret?: boolean }
  | { popup: 'notice'; title: string; hint: string }
  | { popup: 'extension-confirm'; id: string; name: string; version: string; icon: string | null; update: boolean; access: string[] }
  | { popup: 'extensions'; updates: '' | 'checking' | 'current' | 'updated' | 'failed'; installed: { id: string; name: string; version: string; enabled: boolean; page: boolean; options: boolean; pinned: boolean; icon: string | null; store: boolean; update: string | null }[]; available: { id: string; name: string; version: string; from: string }[] };

type Profile = { id: string; name: string; current: boolean; renamable: boolean; deletable: boolean };

type Setting = { key: string; label: string; hint: string; on: boolean };

// Associe une icône vectorielle contextuelle à chaque libellé de menu.
function menuIconPath(label: string): string {
  const l = label.toLowerCase();
  const has = (...words: string[]) => words.some(w => l.includes(w));
  if (has('épingler', 'détacher', 'pin')) return 'M5.5 2.5h5l.5 4 2 2v1h-9v-1l2-2zM8 9.5v4.5';
  if (isClosing(label)) return 'M4.5 4.5l7 7M11.5 4.5l-7 7';
  if (has('pivoter', 'rotate')) return 'M12.8 8a4.8 4.8 0 1 1-1.4-3.4M12.8 3.2v3h-3';
  if (has('comparer', 'compare', 'écrans', 'screens')) return 'M1.5 4.5h3.5v7H1.5zM6.5 3.5h3v9h-3zM11 5.5h3.5v5H11z';
  if (has('macbook', 'imac')) return 'M3.5 3.5h9v6.5h-9zM1.5 12.5h13';
  if (has('mobile', 'iphone', 'ipad', 'appareil', 'device')) return 'M5 2.5h6v11H5zM7.3 11.5h1.4';
  if (has('ordinateur', 'desktop')) return 'M2.5 3.5h11v7h-11zM6 13h4M8 10.5V13';
  if (has('zone', 'area')) return 'M2.5 5.5v-3h3M10.5 2.5h3v3M13.5 10.5v3h-3M5.5 13.5h-3v-3';
  if (has('visible', 'entière', 'capturer', 'capture'))
    return 'M2.5 5.5h2l1-1.5h5l1 1.5h2v7h-11zM8 11a2 2 0 1 0 0-4 2 2 0 0 0 0 4';
  if (has('scindée', 'split')) return 'M2 3h12v10H2zM8 3v10';
  if (has('nouvel onglet', 'new tab')) return 'M8 3.5v9M3.5 8h9';
  if (has('copier', 'copy')) return 'M5.5 3.5h7v7h-7zM3.5 6.5v6h6';
  if (has('coller', 'paste')) return 'M5 3h6v2H5zM3.5 4.5h9v9h-9z';
  if (has('recharger', 'actualiser', 'reload', 'refresh')) return 'M12.8 8a4.8 4.8 0 1 1-1.4-3.4M12.8 3.2v3h-3';
  if (has('précédent', 'retour', 'back')) return 'M9.5 4L5.5 8l4 4';
  if (has('suivant', 'avancer', 'forward')) return 'M6.5 4l4 4-4 4';
  if (has('réseau', 'network')) return 'M2.5 12.5h2v-3h-2zM7 12.5h2v-6H7zM11.5 12.5h2v-9h-2z';
  if (has('inspecter', 'source', 'inspect')) return 'M5.5 5L2.5 8l3 3M10.5 5l3 3-3 3';
  return 'M4 8h8M8 4l4 4-4 4';
}

const isClosing = (label: string) => /fermer|close/i.test(label);

export default function Popup() {
  const [content, setContent] = useState<Content | null>(null);
  const [selected, setSelected] = useState(-1);
  const [settingsSection, setSettingsSection] = useState('general');
  const [libTab, setLibTab] = useState<'bookmarks' | 'history'>('bookmarks');
  const root = useRef<HTMLDivElement>(null);
  const fresh = useRef(false);
  useLang();
  const [opening, setOpening] = useState(0); // change à chaque ouverture : rejoue l'apparition

  useEffect(() => {
    const off = onMessage(data => {
      const select = (data as { select?: unknown }).select;
      if (typeof select === 'number') return setSelected(select);
      // « fresh » précède le contenu d'une nouvelle ouverture : celui-ci jouera son apparition.
      if ((data as { fresh?: boolean }).fresh) return void (fresh.current = true);
      if (fresh.current) setOpening(n => n + 1);
      fresh.current = false;
      const next = data as Content;
      setSelected(-1);
      if (next.popup === 'library')
        setLibTab(next.tab ?? (next.bookmarks.length > 0 ? 'bookmarks' : 'history'));
      setContent(next);
    });
    const close = () => send({ type: 'popup.close' });
    // Perdre le curseur referme la vue, mais pas tout de suite : un clic dans la vue lui fait perdre puis
    // reprendre le curseur, et une fermeture immédiate avalerait ce clic.
    let leaving = 0;
    const blurred = () => {
      window.clearTimeout(leaving);
      leaving = window.setTimeout(() => !document.hasFocus() && close(), 150);
    };
    window.addEventListener('blur', blurred);
    const key = (e: KeyboardEvent) => e.key === 'Escape' && close();
    window.addEventListener('keydown', key);
    return () => {
      off();
      window.clearTimeout(leaving);
      window.removeEventListener('blur', blurred);
      window.removeEventListener('keydown', key);
    };
  }, []);

  // La vue annonce sa hauteur à chaque changement, y compris quand l'hôte vient de changer sa largeur :
  // le contenu se remet alors en page et sa hauteur n'est plus celle du premier rendu.
  useLayoutEffect(() => {
    const el = root.current;
    if (!content || !el) return;
    const report = () => send({ type: 'popup.size', height: el.offsetHeight * interfaceScale() });
    window.addEventListener('ui-scale', report);
    report();
    const observer = new ResizeObserver(report);
    observer.observe(el);
    return () => { observer.disconnect(); window.removeEventListener('ui-scale', report); };
  }, [content, libTab, settingsSection]);

  if (!content) return null;

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

  if (content.popup === 'menu')
    return (
      <div key={opening} ref={root} className="popup arriving" role="menu">
        {content.items.map((label, index) => {
          if (label === '') return <hr key={index} />;
          const danger = isClosing(label);
          return (
            <button
              key={index}
              className={`row${danger ? ' danger' : ''}`}
              role="menuitem"
              onMouseDown={e => e.button === 0 && send({ type: 'menu.pick', index })}
            >
              <span className="row-badge" aria-hidden>
                <Icon d={menuIconPath(label)} size={13} />
              </span>
              <span className="row-title">{label}</span>
            </button>
          );
        })}
      </div>
    );

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

  if (content.popup === 'settings')
    return (
      <div key={opening} ref={root} className="popup settings-popup arriving">
        <div className="popup-header">
          <span className="popup-header-title">{t('Paramètres', 'Settings')}</span>
<button className="settings-close" aria-label={t('Fermer','Close')} onClick={()=>send({type:'popup.close'})}><Icon d="M4 4l8 8M12 4l-8 8" size={14}/></button>
        </div>
<p className="settings-intro">{t('Les modifications sont enregistrées automatiquement.', 'Changes are saved automatically.')}</p>
<nav className="popup-segment settings-nav" aria-label={t('Sections des paramètres', 'Settings sections')}>
{[['general',t('Général','General')],['appearance',t('Apparence','Appearance')],['privacy',t('Sécurité','Security')],['data',t('Données','Data')],['about','Sol']].map(([key,label])=><button key={key} aria-pressed={settingsSection===key} className={'popup-seg-btn'+(settingsSection===key?' active':'')} onClick={()=>setSettingsSection(key)}>{label}</button>)}
</nav>
<div className="theme-row" hidden={settingsSection !== 'appearance'}>
<span className="row-title">{t('Taille de l’interface', 'Interface size')}</span>
<span className="row-sub">{t('Barres, boutons et menus. Le zoom des sites reste indépendant.', 'Bars, buttons and menus. Website zoom stays independent.')}</span>
<div className="popup-segment" role="radiogroup" aria-label={t('Taille de l’interface','Interface size')}>
{[1,.9,.8].map((scale,index)=><button key={scale} role="radio" aria-checked={content.uiScale===scale} className={'popup-seg-btn'+(content.uiScale===scale?' active':'')} onClick={()=>send({type:'settings.set',id:'uiScale',index})}>{Math.round(scale*100)} %</button>)}
</div></div>
        <div className="setting-row static" hidden={settingsSection !== 'general'}>
          <span className="setting-text">
            <span className="row-title">{t('Zoom des pages', 'Page zoom')}</span>
            <span className="row-sub">{t('Conservé pour toutes les pages de ce profil. Ctrl +, Ctrl − ou Ctrl avec la molette.', 'Saved for all pages in this profile. Ctrl +, Ctrl − or Ctrl with the mouse wheel.')}</span>
          </span>
          <span className="stepper" role="group" aria-label={t('Zoom des pages', 'Page zoom')}>
            <button aria-label={t('Réduire', 'Zoom out')} onClick={() => send({ type: 'zoom.out' })}>−</button>
            <button className="stepper-value" aria-label={t('Revenir à 100 %', 'Reset to 100%')} title={t('Revenir à 100 %', 'Reset to 100%')} onClick={() => send({ type: 'zoom.reset' })}>
              {content.zoom} %
            </button>
            <button aria-label={t('Agrandir', 'Zoom in')} onClick={() => send({ type: 'zoom.in' })}>+</button>
          </span>
        </div>
        <div className="theme-row" hidden={settingsSection !== 'appearance'}>
          <span className="row-title">{t('Thème', 'Theme')}</span>
          <div className="popup-segment" role="radiogroup" aria-label={t('Thème', 'Theme')}>
            {[t('Par défaut', 'Default'), t('Clair', 'Light'), t('Sombre', 'Dark'), t('Système', 'System')].map((label, index) => (
              <button
                key={label}
                role="radio"
                aria-checked={content.theme === index}
                className={`popup-seg-btn${content.theme === index ? ' active' : ''}`}
                title={index === 0 ? t("L'interface suit la page visitée", 'The interface follows the visited page') : index === 3 ? t('Suit le réglage clair ou sombre de Windows', 'Follows the Windows light or dark setting') : undefined}
                onClick={() => send({ type: 'settings.set', id: 'theme', index })}
              >
                {label}
              </button>
            ))}
          </div>
        </div>
        <div className="theme-row" hidden={settingsSection !== 'general'}>
          <span className="row-title">{t('Langue', 'Language')}</span>
          <div className="popup-segment" role="radiogroup" aria-label={t('Langue', 'Language')}>
            {[t('Système', 'System'), 'Français', 'English'].map((label, index) => (
              <button
                key={label}
                role="radio"
                aria-checked={content.language === index}
                className={`popup-seg-btn${content.language === index ? ' active' : ''}`}
                title={index === 0 ? t('Suit la langue de Windows ; en anglais si elle est absente', 'Follows the Windows language; English if it is not available') : undefined}
                onClick={() => send({ type: 'settings.set', id: 'language', index })}
              >
                {label}
              </button>
            ))}
          </div>
        </div>
        <div className="theme-row" hidden={settingsSection !== 'appearance'}>
          <span className="row-title">{t('Police', 'Font')}</span>
          <div className="popup-segment grid" role="radiogroup" aria-label={t('Police', 'Font')}>
            {[t('Défaut', 'Default'), t('Système', 'System'), 'Inter', 'Inter Bold', 'SF Pro', 'SF Pro Bold'].map((label, index) => (
              <button
                key={label}
                role="radio"
                aria-checked={content.font === index}
                className={`popup-seg-btn${content.font === index ? ' active' : ''}`}
                onClick={() => send({ type: 'settings.set', id: 'font', index })}
              >
                {label}
              </button>
            ))}
          </div>
        </div>
        <div className="theme-row" hidden={settingsSection !== 'general'}>
          <span className="row-title">{t('Moteur de recherche', 'Search engine')}</span>
          <div className="popup-segment" role="radiogroup" aria-label={t('Moteur de recherche', 'Search engine')}>
            {content.engines.map((label, index) => (
              <button
                key={label}
                role="radio"
                aria-checked={content.engine === index}
                className={`popup-seg-btn${content.engine === index ? ' active' : ''}`}
                onClick={() => send({ type: 'settings.set', id: 'engine', index })}
              >
                {label}
              </button>
            ))}
          </div>
        </div>
        {settingsSection === 'general' && <HomeField key={content.home} home={content.home} />}
        {settingsSection === 'appearance' && <TintSettings items={content.tint} />}
        <div className="popup-list">
          {content.items.filter(s => (['startWallpaper','scrollbars','blur'].includes(s.key) ? 'appearance' : ['blocker','onlineSuggestions','passwords'].includes(s.key) ? 'privacy' : 'general') === settingsSection).map(s => (
            <button
              key={s.key}
              className="setting-row"
              role="switch"
              aria-checked={s.on}
              onClick={() => send({ type: 'settings.set', id: s.key, index: s.on ? 0 : 1 })}
            >
              <span className="setting-text">
                <span className="row-title">{s.label}</span>
                {s.hint && <span className="row-sub">{s.hint}</span>}
              </span>
              <span className={`switch${s.on ? ' on' : ''}`} aria-hidden />
            </button>
          ))}
          {settingsSection === 'privacy' && content.ublock && (
            <div className="setting-row static">
              <span className="setting-text">
                <span className="row-title">uBlock Origin Lite {content.ublock}</span>
                <span className="row-sub">{t('Listes de filtres et sites sans filtrage. Logiciel libre (GPLv3), mis à jour automatiquement.', 'Filter lists and sites without filtering. Free software (GPLv3), updated automatically.')}</span>
              </span>
              <button className="pill-action" onClick={() => send({ type: 'blocker.options' })}>{t('Ouvrir', 'Open')}</button>
            </div>
          )}
          <div className="setting-row static" hidden={settingsSection !== 'data'}>
            <span className="setting-text">
              <span className="row-title">{t('Importer d’un autre navigateur', 'Import from another browser')}</span>
              <span className="row-sub">{t('Favoris, historique et mots de passe de Chrome, Edge, Brave, Comet… ou mots de passe d’un fichier CSV exporté.', 'Bookmarks, history and passwords from Chrome, Edge, Brave, Comet… or passwords from an exported CSV file.')}</span>
            </span>
            <span className="row-actions">
              <button className="pill-action" onClick={() => send({ type: 'welcome.open' })}>{t('Importer', 'Import')}</button>
              <button className="pill-action" onClick={() => send({ type: 'import.csv', id: 'settings' })}>{t('Fichier CSV', 'CSV file')}</button>
            </span>
          </div>
          <div className="setting-row static" hidden={settingsSection !== 'general'}>
            <span className="setting-text">
              <span className="row-title">{t('Extensions', 'Extensions')}</span>
              <span className="row-sub">{t('Ajouter celles de vos autres navigateurs (Mots de passe iCloud, par exemple) ou un dossier d’extension.', 'Add the ones from your other browsers (iCloud Passwords, for example) or an extension folder.')}</span>
            </span>
            <button className="pill-action" onClick={() => send({ type: 'extensions.open' })}>{t('Gérer', 'Manage')}</button>
          </div>
          <div className="setting-row static" hidden={settingsSection !== 'about'}>
            <span className="setting-text">
              <span className="row-title">Sol {content.version}</span>
              <span className="row-sub">
                {!content.update
                  ? content.check === 'checking' ? t('Recherche d’une mise à jour…', 'Checking for updates…')
                    : content.check === 'current' ? t('Sol est à jour.', 'Sol is up to date.')
                    : content.check === 'failed' ? t('La vérification a échoué. Vérifiez votre connexion et réessayez.', 'The check failed. Check your connection and try again.')
                    : content.check === 'portable' ? t('Cette copie n’est pas installée : elle ne se met pas à jour toute seule.', 'This copy is not installed: it does not update itself.')
                    : t('Les mises à jour se téléchargent automatiquement.', 'Updates are downloaded automatically.')
                  : content.update.ready
                    ? t(`La version ${content.update.version} est prête. Redémarrez Sol pour l’installer.`, `Version ${content.update.version} is ready. Restart Sol to install it.`)
                    : t(`Téléchargement de la version ${content.update.version} : ${content.update.percent} %`, `Downloading version ${content.update.version}: ${content.update.percent}%`)}
              </span>
            </span>
            {content.update?.ready && <button className="pill-action primary" onClick={() => send({ type: 'update.restart' })}>{t('Redémarrer', 'Restart')}</button>}
            {!content.update && content.check !== 'portable' && (
              <button className="pill-action" disabled={content.check === 'checking'} onClick={() => send({ type: 'update.check' })}>{t('Vérifier', 'Check')}</button>
            )}
          </div>
          <div className="setting-row static" hidden={settingsSection !== 'data'}>
            <span className="setting-text">
              <span className="row-title">{t('Sauvegarde', 'Backup')}</span>
              <span className="row-sub">{t('Réglages, onglets, favoris, historique et mots de passe dans un fichier chiffré par une clé que vous choisissez.', 'Settings, tabs, bookmarks, history and passwords in a file encrypted with a key you choose.')}</span>
            </span>
            <span className="row-actions">
              <button className="pill-action" onClick={() => send({ type: 'backup.export' })}>{t('Exporter', 'Export')}</button>
              <button className="pill-action" onClick={() => send({ type: 'backup.import' })}>{t('Restaurer', 'Restore')}</button>
            </span>
          </div>
          <div className="setting-row static" hidden={settingsSection !== 'privacy'}>
            <span className="setting-text">
              <span className="row-title">{t('Accès accordés aux sites', 'Site permissions')}</span>
              <span className="row-sub">
                {content.permissions === 0
                  ? t('Aucune réponse retenue : chaque site demandera.', 'No saved answer: every site will ask.')
                  : t(
                      `${content.permissions} réponse${content.permissions > 1 ? 's' : ''} retenue${content.permissions > 1 ? 's' : ''} (micro, caméra, position, notifications…)`,
                      `${content.permissions} saved answer${content.permissions > 1 ? 's' : ''} (microphone, camera, location, notifications…)`,
                    )}
              </span>
            </span>
            <button className="pill-action" disabled={content.permissions === 0} onClick={() => send({ type: 'permissions.clear' })}>{t('Tout oublier', 'Forget all')}</button>
          </div>
          {settingsSection === 'privacy' && <SiteData status={content.siteData} key={opening} />}
          {settingsSection === 'privacy' && content.sites.length > 0 && (
            <div className="setting-sub">
              {content.sites.map(site => (
                <div key={site.origin} className="setting-row static">
                  <span className="setting-text" title={site.origin}>
                    <span className="row-title">{site.host}</span>
                    <span className="row-sub">{site.summary}</span>
                  </span>
                  <button className="pill-action" aria-label={t(`Oublier les réponses données à ${site.host}`, `Forget the answers given to ${site.host}`)} onClick={() => send({ type: 'permissions.forget', id: site.origin })}>
                    {t('Oublier', 'Forget')}
                  </button>
                </div>
              ))}
            </div>
          )}
        </div>
      </div>
    );

  if (content.popup === 'profiles')
    return <ProfileList key={opening} rootRef={root} items={content.items} />;

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
          <p className="row-sub extension-note">{t('Les extensions activées s’appliquent aux pages de ce profil. Rechargez la page après un changement. Elles restent désactivées en navigation privée.', 'Enabled extensions apply to pages in this profile. Reload the page after a change. They remain disabled in private browsing.')}</p>
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

  const open = (item: Item) => send({ type: 'tab.open', url: item.url });
  const items = libTab === 'bookmarks' ? content.bookmarks : content.history;

  return (
    <div key={opening} ref={root} className="popup arriving">
      <div className="popup-segment" role="tablist">
        <button
          role="tab"
          aria-selected={libTab === 'bookmarks'}
          className={`popup-seg-btn${libTab === 'bookmarks' ? ' active' : ''}`}
          onMouseDown={e => {
            e.preventDefault();
            setLibTab('bookmarks');
          }}
        >
          <Icon d="M8 2.4l1.7 3.5 3.8.5-2.8 2.7.7 3.8L8 11.1l-3.4 1.8.7-3.8-2.8-2.7 3.8-.5z" size={13} />
          <span>{t('Favoris', 'Bookmarks')}</span>
          <span className="popup-seg-count">{content.bookmarks.length}</span>
        </button>
        <button
          role="tab"
          aria-selected={libTab === 'history'}
          className={`popup-seg-btn${libTab === 'history' ? ' active' : ''}`}
          onMouseDown={e => {
            e.preventDefault();
            setLibTab('history');
          }}
        >
          <Icon d="M8 3.5v4.5l3 1.8M13.5 8a5.5 5.5 0 1 1-11 0 5.5 5.5 0 0 1 11 0z" size={13} />
          <span>{t('Historique', 'History')}</span>
          <span className="popup-seg-count">{content.history.length}</span>
        </button>
      </div>

      <div className="popup-list">
        {items.length === 0 ? (
          <div className="empty-pill">
            {libTab === 'bookmarks'
              ? t("Aucun favori enregistré pour l'instant.", 'No bookmarks yet.')
              : t('Aucune page visitée récemment.', 'No recently visited pages.')}
          </div>
        ) : (
          items.map((item, idx) => (
            <Row
              key={`${item.url}-${idx}`}
              item={{ ...item, kind: libTab === 'bookmarks' ? 'site' : 'history' }}
              onPick={() => open(item)}
            />
          ))
        )}
      </div>
    </div>
  );
}

// Page ouverte dans un nouvel onglet. Validée par Entrée ou en quittant le champ ; l'hôte renvoie l'adresse retenue.
function HomeField({ home }: { home: string }) {
  const [text, setText] = useState(home);
  const save = () => text.trim() !== home && send({ type: 'settings.text', id: 'home', input: text });
  return (
    <form
      className="theme-row home-row"
      onSubmit={e => {
        e.preventDefault();
        save();
      }}
    >
      <label htmlFor="home-input" className="setting-text">
        <span className="row-title">{t("Page d'accueil", 'Home page')}</span>
        <span className="row-sub">{t('Ouverte dans chaque nouvel onglet. Vide : la page d’accueil de Sol.', 'Opened in every new tab. Empty: the Sol start page.')}</span>
      </label>
      <input id="home-input" type="text" inputMode="url" spellCheck={false} placeholder={t('Page d’accueil Sol', 'Sol start page')} value={text} onChange={e => setText(e.target.value)} onBlur={save} />
    </form>
  );
}

// Sous-menu des réglages : où la couleur du site visité teinte l'interface.
function SiteData({ status }: { status: 'idle' | 'clearing' | 'done' | 'failed' }) {
  const [confirm, setConfirm] = useState(false);
  return <div className="setting-row static">
    <span className="setting-text">
      <span className="row-title">{t('Cookies et données des sites', 'Cookies and site data')}</span>
      <span className="row-sub">{status === 'clearing' ? t('Effacement en cours…', 'Clearing…')
        : status === 'done' ? t('Données effacées. Rechargez les pages ouvertes.', 'Data cleared. Reload open pages.')
        : status === 'failed' ? t('Effacement impossible. Réessayez.', 'Could not clear data. Try again.')
        : t('Déconnecte les comptes de ce profil. Conserve les favoris et mots de passe.', 'Signs out accounts in this profile. Keeps bookmarks and passwords.')}</span>
    </span>
    <span className="row-actions">
      {confirm && <button className="pill-action" onClick={() => setConfirm(false)}>{t('Annuler', 'Cancel')}</button>}
      <button className="pill-action" disabled={status === 'clearing'} onClick={() => {
        if (!confirm) { setConfirm(true); return; }
        setConfirm(false); send({ type: 'site-data.clear' });
      }}>{confirm ? t('Confirmer', 'Confirm') : t('Effacer', 'Clear')}</button>
    </span>
  </div>;
}

function TintSettings({ items }: { items: Setting[] }) {
  const [open, setOpen] = useState(false);
  const active = items.filter(s => s.on).length;
  return (
    <>
      <button className="setting-row" aria-expanded={open} onClick={() => setOpen(o => !o)}>
        <span className="setting-text">
          <span className="row-title">{t('Couleurs du site', 'Site colors')}</span>
          <span className="row-sub">{active === 0 ? t("Désactivées : l'interface reste neutre", 'Off: the interface stays neutral') : t(`Actives sur ${active} zone${active > 1 ? 's' : ''} sur ${items.length}`, `On for ${active} of ${items.length} areas`)}</span>
        </span>
        <span className={`chevron${open ? ' open' : ''}`} aria-hidden>
          <Icon d="M6 4l4 4-4 4" size={13} />
        </span>
      </button>
      {open && (
        <div className="setting-sub">
          {items.map(s => (
            <button key={s.key} className="setting-row" role="switch" aria-checked={s.on} onClick={() => send({ type: 'settings.set', id: s.key, index: s.on ? 0 : 1 })}>
              <span className="setting-text">
                <span className="row-title">{s.label}</span>
                {s.hint && <span className="row-sub">{s.hint}</span>}
              </span>
              <span className={`switch${s.on ? ' on' : ''}`} aria-hidden />
            </button>
          ))}
        </div>
      )}
    </>
  );
}

// Panneau des profils : en choisir un, en créer un, en supprimer un (en deux clics, la suppression est définitive).
function ProfileList({ rootRef, items }: { rootRef: RefObject<HTMLDivElement | null>; items: Profile[] }) {
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
function NameForm({ rootRef, ask }: { rootRef: RefObject<HTMLDivElement | null>; ask: Extract<Content, { popup: 'prompt' }> }) {
  const [name, setName] = useState(ask.value);
  return (
    <div ref={rootRef} className="popup arriving">
      <form
        className="name-form"
        onSubmit={e => {
          e.preventDefault();
          if (name.trim()) send({ type: ask.command, id: ask.id ?? undefined, title: ask.secret ? name : name.trim() });
        }}
      >
        <label htmlFor="name-input" className="name-form-title">{ask.title}</label>
        {ask.hint && <p className="row-sub">{ask.hint}</p>}
        <input id="name-input" type={ask.secret ? 'password' : 'text'} autoFocus maxLength={ask.secret ? 128 : 40} placeholder={ask.placeholder} spellCheck={false} value={name} onChange={e => setName(e.target.value)} onFocus={e => e.target.select()} />
        <div className="name-form-actions">
          <button type="button" className="pill-action" onClick={() => send({ type: 'popup.close' })}>{t('Annuler', 'Cancel')}</button>
          <button type="submit" className="pill-action primary" disabled={!name.trim()}>{ask.submit}</button>
        </div>
      </form>
    </div>
  );
}

// Modification d'un raccourci de la carte latérale : son nom (vide = celui du site) et son adresse.
function ShortcutForm({ rootRef, ask }: { rootRef: RefObject<HTMLDivElement | null>; ask: Extract<Content, { popup: 'shortcut-edit' }> }) {
  const [name, setName] = useState(ask.name);
  const [url, setUrl] = useState(ask.url);
  return (
    <div ref={rootRef} className="popup arriving">
      <form
        className="name-form"
        onSubmit={e => {
          e.preventDefault();
          if (url.trim()) send({ type: 'shortcut.update', id: ask.id, title: name.trim(), input: url.trim() });
        }}
      >
        <span className="name-form-title">{t('Modifier le raccourci', 'Edit shortcut')}</span>
        <label htmlFor="name-input" className="row-sub">{t('Nom (vide : celui du site)', "Name (empty: the site's own)")}</label>
        <input id="name-input" autoFocus maxLength={24} spellCheck={false} value={name} onChange={e => setName(e.target.value)} onFocus={e => e.target.select()} />
        <label htmlFor="url-input" className="row-sub">{t('Adresse', 'Address')}</label>
        <input id="url-input" type="text" inputMode="url" maxLength={2000} placeholder="https://…" spellCheck={false} value={url} onChange={e => setUrl(e.target.value)} />
        <div className="name-form-actions">
          <button type="button" className="pill-action" onClick={() => send({ type: 'popup.close' })}>{t('Annuler', 'Cancel')}</button>
          <button type="submit" className="pill-action primary" disabled={!url.trim()}>{t('Enregistrer', 'Save')}</button>
        </div>
      </form>
    </div>
  );
}

// Choix des écrans de la comparaison : de « min » à « max » appareils, affichés côte à côte.
function ComparePicker({ rootRef, ask }: { rootRef: RefObject<HTMLDivElement | null>; ask: Extract<Content, { popup: 'compare-pick' }> }) {
  const [chosen, setChosen] = useState(() => ask.devices.filter(d => d.on).map(d => d.name));
  const valid = chosen.length >= ask.min && chosen.length <= ask.max;
  return (
    <div ref={rootRef} className="popup arriving">
      <form
        className="name-form"
        onSubmit={e => {
          e.preventDefault();
          if (valid) send({ type: 'device.compare', id: ask.id, query: chosen.join('|') });
        }}
      >
        <span className="name-form-title">{t('Écrans à comparer', 'Screens to compare')}</span>
        <p className="row-sub">{t(`De ${ask.min} à ${ask.max} écrans, affichés côte à côte.`, `${ask.min} to ${ask.max} screens, shown side by side.`)}</p>
        <div className="popup-list">
          {ask.devices.map(d => {
            const on = chosen.includes(d.name);
            return (
              <button
                key={d.name}
                type="button"
                className="setting-row"
                role="switch"
                aria-checked={on}
                disabled={!on && chosen.length >= ask.max}
                onClick={() => setChosen(on ? chosen.filter(n => n !== d.name) : [...chosen, d.name])}
              >
                <span className="setting-text">
                  <span className="row-title">{d.label}</span>
                  <span className="row-sub">{d.size}</span>
                </span>
                <span className={`switch${on ? ' on' : ''}`} aria-hidden />
              </button>
            );
          })}
        </div>
        <div className="name-form-actions">
          <button type="button" className="pill-action" onClick={() => send({ type: 'popup.close' })}>{t('Annuler', 'Cancel')}</button>
          <button type="submit" className="pill-action primary" disabled={!valid}>{t('Comparer', 'Compare')}</button>
        </div>
      </form>
    </div>
  );
}

function Row({ item, selected = false, onPick }: { item: Item; selected?: boolean; onPick: () => void }) {
  const domain = formatDomain(item.url);
  const iconD =
    item.kind === 'search'
      ? 'M10.5 10.5L14 14M11.5 7a4.5 4.5 0 1 1-9 0 4.5 4.5 0 0 1 9 0z'
      : item.kind === 'go'
        ? 'M3.5 8h9M9 4.5L12.5 8 9 11.5'
        : item.kind === 'site'
          ? 'M8 2.4l1.7 3.5 3.8.5-2.8 2.7.7 3.8L8 11.1l-3.4 1.8.7-3.8-2.8-2.7 3.8-.5z'
          : 'M8 3.5v4.5l3 1.8M13.5 8a5.5 5.5 0 1 1-11 0 5.5 5.5 0 0 1 11 0z';

  return (
    <button
      className={`row${selected ? ' selected' : ''}`}
      role="option"
      aria-selected={selected}
      onMouseDown={e => e.button === 0 && onPick()}
    >
      <span className="row-badge" aria-hidden>
        <Icon d={iconD} size={13} />
      </span>
      {item.kind === 'search' ? (
        <>
          <span className="row-title">{item.title}</span>
          <span className="row-pill">{t('Recherche', 'Search')}</span>
        </>
      ) : item.kind === 'go' ? (
        <>
          <span className="row-title">{t('Ouvrir', 'Open')} {item.title}</span>
          <span className="row-pill">{t('Aller', 'Go')}</span>
        </>
      ) : (
        <>
          <span className="row-title">{item.title || domain || item.url}</span>
          {domain && <span className="row-pill">{domain}</span>}
        </>
      )}
    </button>
  );
}
