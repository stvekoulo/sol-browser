import { useEffect, useLayoutEffect, useRef, useState, type RefObject } from 'react';
import { formatDomain, onMessage, send } from './bridge';
import { DownloadCard, type Download } from './Downloads';
import GroupEditor from './GroupEditor';
import { t, useLang } from './i18n';
import { Icon } from './Icons';

type Item = { url: string; title: string; kind?: 'search' | 'go' | 'history' | 'site' };
type Content =
  | { popup: 'suggestions'; items: Item[] }
  | { popup: 'library'; bookmarks: Item[]; history: Item[] }
  | { popup: 'menu'; items: string[] }
  | { popup: 'downloads'; items: Download[] }
  | { popup: 'settings'; zoom: number; theme: number; language: number; home: string; permissions: number; tint: Setting[]; items: Setting[] }
  | { popup: 'profiles'; items: Profile[] }
  | { popup: 'group-edit'; create: boolean; id: string; name: string; color: string; icon: string; presets: string[] }
  | { popup: 'permission'; site: string; what: string; secure: boolean }
  | { popup: 'prompt'; title: string; hint: string; placeholder: string; submit: string; value: string; command: string; id: string | null };

type Profile = { id: string; name: string; current: boolean; deletable: boolean };

type Setting = { key: string; label: string; hint: string; on: boolean };

// Associe une icône vectorielle contextuelle à chaque libellé de menu.
function menuIconPath(label: string): string {
  const l = label.toLowerCase();
  const has = (...words: string[]) => words.some(w => l.includes(w));
  if (has('épingler', 'détacher', 'pin')) return 'M5.5 2.5h5l.5 4 2 2v1h-9v-1l2-2zM8 9.5v4.5';
  if (isClosing(label)) return 'M4.5 4.5l7 7M11.5 4.5l-7 7';
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
  if (has('inspecter', 'source', 'inspect')) return 'M5.5 5L2.5 8l3 3M10.5 5l3 3-3 3';
  return 'M4 8h8M8 4l4 4-4 4';
}

const isClosing = (label: string) => /fermer|close/i.test(label);

export default function Popup() {
  const [content, setContent] = useState<Content | null>(null);
  const [selected, setSelected] = useState(-1);
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
        setLibTab(next.bookmarks.length > 0 ? 'bookmarks' : 'history');
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
    const report = () => send({ type: 'popup.size', height: el.offsetHeight });
    report();
    const observer = new ResizeObserver(report);
    observer.observe(el);
    return () => observer.disconnect();
  }, [content, libTab]);

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
      <div key={opening} ref={root} className="popup arriving">
        <div className="popup-header">
          <span className="popup-header-title">{t('Réglages', 'Settings')}</span>
        </div>
        <div className="setting-row static">
          <span className="setting-text">
            <span className="row-title">{t('Zoom de la page', 'Page zoom')}</span>
            <span className="row-sub">{t('Ctrl + et Ctrl −, ou Ctrl avec la molette', 'Ctrl + and Ctrl −, or Ctrl with the mouse wheel')}</span>
          </span>
          <span className="stepper" role="group" aria-label={t('Zoom de la page', 'Page zoom')}>
            <button aria-label={t('Réduire', 'Zoom out')} onClick={() => send({ type: 'zoom.out' })}>−</button>
            <button className="stepper-value" aria-label={t('Revenir à 100 %', 'Reset to 100%')} title={t('Revenir à 100 %', 'Reset to 100%')} onClick={() => send({ type: 'zoom.reset' })}>
              {content.zoom} %
            </button>
            <button aria-label={t('Agrandir', 'Zoom in')} onClick={() => send({ type: 'zoom.in' })}>+</button>
          </span>
        </div>
        <div className="theme-row">
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
        <div className="theme-row">
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
        <HomeField key={content.home} home={content.home} />
        <TintSettings items={content.tint} />
        <div className="popup-list">
          {content.items.map(s => (
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
          <div className="setting-row static">
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
        </div>
      </div>
    );

  if (content.popup === 'profiles')
    return <ProfileList key={opening} rootRef={root} items={content.items} />;

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

  if (content.popup === 'prompt')
    return <NameForm key={opening} rootRef={root} ask={content} />;

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
        <span className="row-sub">{t('Ouverte dans chaque nouvel onglet. Vide : Google.', 'Opened in every new tab. Empty: Google.')}</span>
      </label>
      <input id="home-input" type="text" inputMode="url" spellCheck={false} placeholder="https://www.google.com" value={text} onChange={e => setText(e.target.value)} onBlur={save} />
    </form>
  );
}

// Sous-menu des réglages : où la couleur du site visité teinte l'interface.
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
          if (name.trim()) send({ type: ask.command, id: ask.id ?? undefined, title: name.trim() });
        }}
      >
        <label htmlFor="name-input" className="name-form-title">{ask.title}</label>
        {ask.hint && <p className="row-sub">{ask.hint}</p>}
        <input id="name-input" autoFocus maxLength={40} placeholder={ask.placeholder} spellCheck={false} value={name} onChange={e => setName(e.target.value)} onFocus={e => e.target.select()} />
        <div className="name-form-actions">
          <button type="button" className="pill-action" onClick={() => send({ type: 'popup.close' })}>{t('Annuler', 'Cancel')}</button>
          <button type="submit" className="pill-action primary" disabled={!name.trim()}>{ask.submit}</button>
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
