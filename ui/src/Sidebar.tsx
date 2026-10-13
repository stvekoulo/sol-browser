import { useEffect, useRef, useState } from 'react';
import { displayAddress, interfaceScale, isStartPage, onState, send, type State } from './bridge';
import { t, useLang } from './i18n';
import { CopyIcon, Icon, LockIcon } from './Icons';
import { useSidebarDrag } from './useSidebarDrag';
import { SidebarTabs } from './SidebarTabs';
import { SidebarPinned } from './SidebarPinned';
import { useAddressSuggestions } from './useAddressSuggestions';
import logo from './assets/logo.png';
export { useIconLook } from './useIconLook';

type Icons = Record<string, string | null | undefined>;

// Décalage de la vue Sidebar dans MainWindow.xaml (colonne gauche sous la barre de 52px).
const OFFSET_X = 0;
const OFFSET_Y = () => 52 * interfaceScale();

export default function Sidebar() {
  const [state, setState] = useState<State | null>(null);
  const [icons, setIcons] = useState<Icons>({});
  const [copied, setCopied] = useState(false);
  const [status, setStatus] = useState(''); // adresse du lien survolé dans la page
  const [motion, setMotion] = useState<{ way: '' | 'entering' | 'leaving'; run: number }>({ way: '', run: 0 });
  const [urlText, setUrlText] = useState('');
  const [editingUrl, setEditingUrl] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);
  useLang();

  useEffect(() => {
    const off = onState(setState, e => {
      if (e.event === 'favicon' && e.id) setIcons(all => ({ ...all, [e.id!]: e.data }));
      if (e.event === 'status') setStatus(e.text ?? '');
      // L'hôte fait entrer ou sortir la carte : le mouvement est joué ici, par son contenu.
      // Flottante (au survol, ou le temps d'une animation) : la vue se réduit à la carte, sans marge autour.
      if (e.event === 'sidebar-enter' || e.event === 'sidebar-leave') document.body.classList.add('floating');
      if (e.event === 'sidebar-docked') document.body.classList.remove('floating');
      if (e.event === 'sidebar-enter') setMotion(m => ({ way: 'entering', run: m.run + 1 }));
      if (e.event === 'sidebar-leave') setMotion(m => ({ way: 'leaving', run: m.run + 1 }));
    });
    send({ type: 'ui.ready' });
    return off;
  }, []);

  const activeTab = state?.tabs.find(t => t.id === state.activeId);
  const suggestions=useAddressSuggestions(activeTab?.id,setUrlText,inputRef);
  const activeUrl = !activeTab || (activeTab.url === 'about:blank' || isStartPage(activeTab.url)) ? '' : displayAddress(activeTab.url);

  useEffect(() => {
    if (!editingUrl) setUrlText(activeUrl);
  }, [activeUrl, editingUrl]);

  useSidebarDrag();

  if (!state) return null;
  const { tabs, split, activeId } = state;

  const copyUrl = () => {
    if (!activeUrl) return;
    navigator.clipboard?.writeText(activeUrl).catch(() => {});
    setCopied(true);
    setTimeout(() => setCopied(false), 1200);
  };

  return (
    <aside className={`sidebar-card ${motion.way}`} onAnimationEnd={() => setMotion(m => ({ ...m, way: '' }))} aria-label={t('Panneau latéral des onglets', 'Tabs sidebar')}>
      <div className="sidebar-heading">
        <button className="sidebar-profile" title={t(`Profil : ${state.profile}`, `Profile: ${state.profile}`)} onClick={e => {
          const box = e.currentTarget.getBoundingClientRect();
          send({ type: 'profile.menu', x: box.right + OFFSET_X, y: box.bottom + OFFSET_Y() + 6 });
        }}>
          <Icon d="M8 8a2.6 2.6 0 1 0 0-5.2A2.6 2.6 0 0 0 8 8M3 13.2c.6-2.2 2.6-3.4 5-3.4s4.4 1.2 5 3.4" size={14} />
          <span>{state.privateMode ? t('Privé', 'Private') : state.profile}</span>
        </button>
        <span className="sidebar-brand"><img src={logo} alt="" /><strong>Sol</strong></span>
      </div>
      {activeTab && <button data-action="session.menu" className="sidebar-session" title={t('Choisir une session · Ctrl+Maj+B', 'Choose a session · Ctrl+Shift+B')} onClick={e => {
        const box = e.currentTarget.getBoundingClientRect();
        send({type:'session.menu',id:activeTab.id,x:box.left + OFFSET_X,y:box.bottom + OFFSET_Y() + 6});
      }}><Icon d="M3 4h10v8H3zM5 2v2m6-2v2M3 7h10" size={13}/><span>{activeTab.sessionId ? `${t('Session','Session')} · ${activeTab.sessionName}` : activeTab.sessionName}</span><Icon d="M5 6l3 3 3-3" size={12}/></button>}
      <div className="sidebar-url-pill" onClick={e => {
        if (!(e.target as Element).closest('button')) inputRef.current?.focus();
      }}>
        <LockIcon />
        <form
          className="sidebar-url-form"
          onSubmit={e => {
            e.preventDefault();
            if (activeId && urlText.trim()) send({ type: 'nav.go', id: activeId, input: urlText });
            inputRef.current?.blur();
          }}
        >
          <input
            ref={inputRef}
            aria-label={t('Adresse ou recherche', 'Address or search')}
            placeholder={t('Rechercher ou saisir une adresse', 'Search or enter an address')}
            spellCheck={false}
            role="combobox" aria-autocomplete="list" aria-expanded={editingUrl&&suggestions.count>0}
            value={urlText}
            onChange={e => {suggestions.changed(e.target.value);const box=e.currentTarget.closest('.sidebar-url-pill')!.getBoundingClientRect();if(activeId)send({type:'suggest',id:activeId,query:e.target.value,x:box.left+OFFSET_X,y:box.bottom+OFFSET_Y()+6,width:box.width})}}
            onFocus={e => {
              setEditingUrl(true);
              suggestions.remember(urlText);
              e.target.select();
            }}
            onKeyDown={e => {
              if(suggestions.key(e))return;
              if (e.key === 'Escape') {
                setUrlText(activeUrl);
                e.currentTarget.blur();
              }
            }}
            onBlur={() => {setEditingUrl(false);suggestions.clear();if(activeId)send({type:'suggest',id:activeId,query:'',x:0,y:0,width:0})}}
          />
        </form>
        <button
          className={`sidebar-url-btn${copied ? ' copied' : ''}`}
          aria-label={t("Copier l'adresse", 'Copy address')}
          title={copied ? t('Adresse copiée', 'Address copied') : t("Copier l'adresse", 'Copy address')}
          onClick={copyUrl}
        >
          {copied ? <Icon d="M3.5 8.5l3 3 6-6.5" size={14} /> : <CopyIcon />}
        </button>
        <button
          className="sidebar-url-btn"
          aria-label={t('Effacer ou saisir une adresse', 'Clear or enter an address')}
          title={t('Saisir une nouvelle adresse', 'Enter a new address')}
          onClick={() => {
            setEditingUrl(true);
            setUrlText('');
            inputRef.current?.focus();
          }}
        >
          <Icon d="M4.5 4.5l7 7M11.5 4.5l-7 7" size={13} />
        </button>
      </div>

      <div className="sidebar-scroll" data-tab-count={tabs.length}>
        <SidebarPinned state={state} icons={icons} />
        <SidebarTabs state={state} icons={icons} />
      </div>
      <div className="sidebar-drag-hint" aria-live="polite" />

      <div className="sidebar-quick-actions" role="group" aria-label={t('Actions rapides', 'Quick actions')}>
        {[
          { type: 'window.private', label: t('Navigation privée', 'Private browsing'), path: 'M6.5 8a2.5 2.5 0 1 1-5 0 2.5 2.5 0 0 1 5 0M14.5 8a2.5 2.5 0 1 1-5 0 2.5 2.5 0 0 1 5 0M6.5 8h3' },
          { type: 'notes.manage', label: t('Notes', 'Notes'), path: 'M4 2.5h8v11H4zM6 5h4M6 8h4M6 11h2' },
          { type: split.visible ? 'split.close' : 'split.open', label: split.visible ? t('Fermer la vue scindée', 'Close split view') : t('Afficher deux pages côte à côte', 'Show two pages side by side'), path: 'M2 3h12v10H2zM8 3v10' },
          ...(split.visible ? [{ type: 'split.menu', label: t('Disposition des pages', 'Page layout'), path: 'M2 3h12v10H2zM6 3v10M4 6v4M10 6v4' }] : []),
          { type: 'history.open', label: t('Historique', 'History'), path: 'M2 7a6 6 0 1 1 1.5 5M2 3v4h4M8 4.5V8l2.5 1.5' },
          { type: 'downloads.open', label: t('Téléchargements', 'Downloads'), path: 'M8 2.5v8M4.5 7.5L8 11l3.5-3.5M3 13.5h10' },
        ].map(action => <button key={action.type} data-action={action.type} className={`sidebar-quick-btn${action.type === 'window.private' ? ' private-action' : ''}${action.type === 'split.menu' ? ' split-proportions' : ''}${action.type === 'split.close' || action.type==='notes.manage'&&activeTab?.url==='https://start.sol/index.html#notes' ? ' on' : ''}`} aria-label={action.label} title={action.label} aria-pressed={action.type === 'split.open' || action.type === 'split.close' ? split.visible : undefined} onContextMenu={e=>{
          if(action.type!=='notes.manage')return;e.preventDefault();const box=e.currentTarget.getBoundingClientRect();send({type:'notes.menu',id:activeId??undefined,x:box.right+OFFSET_X,y:box.top+OFFSET_Y()-6});
        }} onClick={e => {
          const box = e.currentTarget.getBoundingClientRect();
          // Le panneau s'ouvre au-dessus du bouton : dessous, il n'y a plus de place et il recouvrirait cette rangée.
          send({ type: action.type, x: box.right + OFFSET_X, y: box.top + OFFSET_Y() - 6 });
        }}><Icon d={action.path} size={16} /></button>)}
      </div>

      {/* Où mène le lien survolé : repère contre l'hameçonnage, à la place de la bulle noire du moteur. */}
      <div className={`sidebar-status${status ? ' shown' : ''}`} aria-live="off" title={status}>
        <Icon d="M6.5 9.5l3-3M7 4.5l1-1a2.5 2.5 0 0 1 3.5 3.5l-1 1M9 11.5l-1 1A2.5 2.5 0 0 1 4.5 9l1-1" size={12} />
        <span>{status.replace(/^https?:\/\/(www\.)?/, '')}</span>
      </div>
    </aside>
  );
}
