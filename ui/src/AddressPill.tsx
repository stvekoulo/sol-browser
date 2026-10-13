import { useEffect,useRef,useState } from 'react';
import { displayAddress,isStartPage,formatDomain,send,type Tab } from './bridge';
import { t } from './i18n';
import { Icon,ShieldIcon } from './Icons';
import { useAddressSuggestions } from './useAddressSuggestions';
// Pilule d'adresse centrale : affiche le domaine centré au repos, et l'URL complète à l'édition.
export default function AddressPill({ tab, focused, grow, shield }: { tab: Tab | undefined; focused: boolean; grow: number; shield: boolean }) {
  const rawUrl = !tab || (tab.url === 'about:blank' || isStartPage(tab.url)) ? '' : displayAddress(tab.url);
  const domain = formatDomain(rawUrl);
  const [text, setText] = useState(rawUrl);
  const [editing, setEditing] = useState(false);
  const pillRef = useRef<HTMLDivElement>(null);
  const inputRef=useRef<HTMLInputElement>(null),suggestions=useAddressSuggestions(tab?.id,setText,inputRef);

  useEffect(() => {
    if (!editing) setText(rawUrl);
  }, [rawUrl, editing]);

  const id = tab?.id;
  return (
    <div ref={pillRef} data-tab-id={id} className={`address-pill${focused ? ' focused' : ''}${editing ? ' editing' : ''}`} style={{ flexGrow: grow }}>
      <span
        className={`address-lead${shield ? '' : ' unshielded'}`}
        title={shield ? t('Publicités et traqueurs bloqués', 'Ads and trackers blocked') : t('Publicités non bloquées (réglage coupé dans Réglages)', 'Ads are not blocked (turned off in Settings)')}
      >
        {tab?.loading ? <span className="spinner" /> : <ShieldIcon />}
      </span>

      <form
        className="address-form"
        onSubmit={e => {
          e.preventDefault();
          if (id && text.trim()) send({ type: 'nav.go', id, input: text });
          (document.activeElement as HTMLElement | null)?.blur();
        }}
      >
        <input
          ref={inputRef}
          role="combobox" aria-autocomplete="list" aria-expanded={editing&&suggestions.count>0}
          aria-label={t('Adresse ou recherche', 'Address or search')}
          placeholder={t('Rechercher ou saisir une adresse', 'Search or enter an address')}
          spellCheck={false}
          value={editing ? text : domain}
          title={rawUrl}
          onChange={e => {
            suggestions.changed(e.target.value);
            const box = (pillRef.current ?? e.target).getBoundingClientRect();
            if (id) send({ type: 'suggest', id, query: e.target.value, x: box.left, y: box.bottom + 6, width: box.width });
          }}
          onFocus={e => {
            setEditing(true);
            setText(rawUrl);
            suggestions.remember(rawUrl);
            requestAnimationFrame(() => e.target.select());
            if (id && !focused) send({ type: 'tab.activate', id });
          }}
          onKeyDown={e => {
            if(suggestions.key(e))return;
            if (e.key === 'Escape') {
              setText(rawUrl);
              e.currentTarget.blur();
            }
          }}
          onBlur={() => {
            setEditing(false);
            suggestions.clear();
            if (id) send({ type: 'suggest', id, query: '', x: 0, y: 0, width: 0 });
          }}
        />
      </form>

      <div className="address-trail">
        {tab?.ai && /^https?:/.test(rawUrl) && !rawUrl.startsWith('https://start.sol/') && <button className="inline-btn" title={t('Résumer cette page avec Sol AI','Summarize this page with Sol AI')} aria-label={t('Résumer cette page avec Sol AI','Summarize this page with Sol AI')} onClick={()=>send({type:'ai.open',id:tab.id})}><Icon d="M8 1l2 5 5 2-5 2-2 5-2-5-5-2 5-2z" size={14}/></button>}
        {tab && /^https?:/.test(rawUrl) && !isStartPage(rawUrl) && rawUrl !== 'https://start.sol/index.html#notes' && (
          <button className={`inline-btn${tab.reader ? ' on' : ''}`} aria-pressed={!!tab.reader}
            aria-label={tab.reader ? t('Quitter le mode lecteur', 'Exit reader mode') : t('Mode lecteur', 'Reader mode')}
            title={tab.reader ? t('Quitter le mode lecteur', 'Exit reader mode') : t('Mode lecteur', 'Reader mode')}
            onClick={() => send({ type: 'reader.toggle', id: tab.id })}>
            <Icon d="M2 3h4a3 3 0 0 1 2 1 3 3 0 0 1 2-1h4v10h-4a3 3 0 0 0-2 1 3 3 0 0 0-2-1H2zM8 4v10" size={14} />
          </button>
        )}
        {tab?.device && (
          <button
            className="zoom-chip"
            aria-label={t(`Vue ${tab.device}, revenir à la vue ordinateur`, `${tab.device} view, back to desktop view`)}
            title={t('Revenir à la vue ordinateur', 'Back to desktop view')}
            onClick={() => send({ type: 'device.set', id: tab.id, query: '' })}
          >
            {tab.device}
          </button>
        )}
        {tab?.device && (
          <button className="inline-btn" aria-label={t("Pivoter l'appareil", 'Rotate device')} title={t("Pivoter l'appareil", 'Rotate device')} onClick={() => send({ type: 'device.rotate', id: tab.id })}>
            <Icon d="M5 2.5h5.5a1 1 0 0 1 1 1V8M4 6.5h3.5a1 1 0 0 1 1 1v5a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1v-5a1 1 0 0 1 1-1zM13.5 6.5l-2 2-2-2" size={14} />
          </button>
        )}
        {rawUrl && (
          <button
            className={`inline-btn${tab?.bookmarked ? ' on' : ''}`}
            aria-label={tab?.bookmarked ? t('Retirer des favoris', 'Remove bookmark') : t('Ajouter aux favoris', 'Add bookmark')}
            title={tab?.bookmarked ? t('Retirer des favoris', 'Remove bookmark') : t('Ajouter aux favoris', 'Add bookmark')}
            onClick={() => tab && send(tab.bookmarked ? { type: 'bookmark.remove', url: tab.url } : { type: 'bookmark.add', url: tab.url, title: tab.title })}
          >
            <Icon d="M8 2.4l1.7 3.5 3.8.5-2.8 2.7.7 3.8L8 11.1l-3.4 1.8.7-3.8-2.8-2.7 3.8-.5z" fill={!!tab?.bookmarked} size={14} />
          </button>
        )}
        <button
          className="inline-btn"
          aria-label={tab?.loading ? t('Arrêter', 'Stop') : t('Recharger', 'Reload')}
          title={tab?.loading ? t('Arrêter', 'Stop') : t('Recharger', 'Reload')}
          disabled={!id}
          onClick={() => id && send({ type: tab?.loading ? 'nav.stop' : 'nav.reload', id })}
        >
          <Icon d={tab?.loading ? 'M4.5 4.5l7 7M11.5 4.5l-7 7' : 'M12.8 8a4.8 4.8 0 1 1-1.4-3.4M12.8 3.2v3h-3'} size={14} />
        </button>
      </div>
    </div>
  );
}
