import {Checkbox} from '@/components/motion/checkbox';
import {useState,useRef,useEffect,type RefObject} from 'react';
import {send,formatDomain} from './bridge';
import {t} from './i18n';
import {Icon} from './Icons';
import type {Content,Item,Profile,Setting,Wallpaper} from './PopupTypes';
export function NameForm({ rootRef, ask }: { rootRef: RefObject<HTMLDivElement | null>; ask: Extract<Content, { popup: 'prompt' }> }) {
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
export function ShortcutForm({ rootRef, ask }: { rootRef: RefObject<HTMLDivElement | null>; ask: Extract<Content, { popup: 'shortcut-edit' }> }) {
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
export function ComparePicker({ rootRef, ask }: { rootRef: RefObject<HTMLDivElement | null>; ask: Extract<Content, { popup: 'compare-pick' }> }) {
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
<label key={d.name} className="setting-row sol-setting"><span className="setting-text"><span className="row-title">{d.label}</span><span className="row-sub">{d.size}</span></span><Checkbox className="sol-checkbox" aria-label={d.label} checked={on} disabled={!on&&chosen.length>=ask.max} onCheckedChange={checked=>setChosen(checked?[...chosen,d.name]:chosen.filter(n=>n!==d.name))}/></label>
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
