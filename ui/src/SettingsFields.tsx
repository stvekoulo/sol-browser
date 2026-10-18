import {Button} from '@/components/motion/button/base';
import SolTabs from './SolTabs';
import SettingToggle from './SettingToggle';
import {useState,useRef,useEffect,type RefObject} from 'react';
import {send,formatDomain} from './bridge';
import {t} from './i18n';
import {Icon} from './Icons';
import type {Content,Item,Profile,Setting,Wallpaper} from './PopupTypes';
import solWallpaper from './assets/start-background.png';
export function HomeField({ home }: { home: string }) {
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
export function SiteData({ status }: { status: 'idle' | 'clearing' | 'done' | 'failed' }) {
  const [confirm, setConfirm] = useState(false);
  return <div className="setting-row static">
    <span className="setting-text">
      <span className="row-title">{t('Cookies et données des sites', 'Cookies and site data')}</span>
      <span className="row-sub">{status === 'clearing' ? t('Effacement en cours…', 'Clearing…')
        : status === 'done' ? t('Données effacées. Rechargez les pages ouvertes.', 'Data cleared. Reload open pages.')
        : status === 'failed' ? t('Effacement impossible. Réessayez.', 'Could not clear data. Try again.')
        : t('Déconnecte les comptes de la session de l’onglet actif. Conserve les autres sessions, favoris et mots de passe.', 'Signs out accounts in the active tab’s session. Keeps other sessions, bookmarks and passwords.')}</span>
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

// Fond du nouvel onglet : l'image de Sol, une photo choisie, ou une nouvelle chaque jour. Les vignettes viennent du
// service de photos : elles ne sont demandées que lorsque la grille est ouverte.
export function WallpaperSettings({ photo, daily, photos, custom }: { photo: number; daily: boolean; photos: Wallpaper[]; custom: boolean }) {
  const [choosing, setChoosing] = useState(false);
  const pick = (id: number) => send({ type: 'settings.set', id: 'wallpaper', index: id });
  const current = custom ? undefined : photos.find(p => p.id === photo);
  return (
    <div className="theme-row wallpaper-settings">
      <span className="row-title">{t('Image du nouvel onglet', 'New tab image')}</span>
      <SolTabs value={daily?'daily':'chosen'} label={t('Image du nouvel onglet','New tab image')} change={value=>send({type:'settings.set',id:'wallpaperDaily',index:value==='daily'?1:0})} items={[
        {value:'chosen',label:t('Image choisie','Chosen image')},{value:'daily',label:t('Nouvelle chaque jour','New every day')}
      ]}/>
      <Button variant="ghost" size="sm" className="pill-action" onClick={()=>send({type:'settings.set',id:'wallpaperCustom',index:1})}>{t('Choisir ma photo…','Choose my photo…')}</Button>
      {daily ? (
        <p className="row-hint">{t('Une photo différente chaque jour, parmi la sélection.', 'A different photo every day, from the selection.')}</p>
      ) : (
        <>
          <p className="row-hint">
            {custom ? t('Votre photo personnelle.', 'Your personal photo.') : current ? t(`Photo de ${current.author}.`, `Photo by ${current.author}.`) : t('Image de Sol.', 'Sol’s image.')}
          </p>
          <Button variant="ghost" size="sm" whileHover={undefined} className="pill-action" aria-expanded={choosing} onClick={() => setChoosing(!choosing)}>
            {choosing ? t('Masquer les images', 'Hide images') : t('Choisir une image', 'Choose an image')}
          </Button>
          {choosing && (
            <div className="wallpaper-grid" role="radiogroup" aria-label={t('Images proposées', 'Available images')}>
              <Button variant="ghost" size="sm" whileHover={undefined} role="radio" aria-checked={!custom && photo === 0} className={`wallpaper-tile${!custom && photo === 0 ? ' active' : ''}`} title={t('Image de Sol', 'Sol’s image')} onClick={() => pick(0)}>
                <img src={solWallpaper} alt={t('Image de Sol', 'Sol’s image')} draggable={false} />
              </Button>
              {photos.map(p => (
                <Button variant="ghost" size="sm" whileHover={undefined} key={p.id} role="radio" aria-checked={!custom && photo === p.id} className={`wallpaper-tile${!custom && photo === p.id ? ' active' : ''}`} title={t(`Photo de ${p.author}`, `Photo by ${p.author}`)} onClick={() => pick(p.id)}>
                  <img src={p.thumb} alt={t(`Photo de ${p.author}`, `Photo by ${p.author}`)} loading="lazy" draggable={false} />
                </Button>
              ))}
            </div>
          )}
        </>
      )}
      <p className="row-hint">{t('Photos libres de droits d’Unsplash, servies par Lorem Picsum et téléchargées à la demande.', 'Free-to-use photos from Unsplash, served by Lorem Picsum and downloaded on demand.')}</p>
    </div>
  );
}

export function TintSettings({ items }: { items: Setting[] }) {
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
<SettingToggle key={s.key} setting={s}/>
          ))}
        </div>
      )}
    </>
  );
}
