import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { useIconLook } from './Sidebar';
import { formatDomain, onMessage, send } from './bridge';
import { setLang, t, useLang } from './i18n';

import './Start.css';

type Site = { url: string; title: string; last?: number; icon?: string };
type StartData = {
  event: 'start';
  privateMode: boolean;
  wallpaper?: boolean;
  wallpaperPhoto?: boolean;
  lang: string;
  favorites: Site[];
  history: Site[];
  groups: { id: string; name: string; color: string; count: number }[];
};

const empty: StartData = {
  event: 'start',
  privateMode: false,
  wallpaper: false,
  lang: '',
  favorites: [],
  history: [],
  groups: [],
};

// Palette inspirée des tuiles Safari pour les monogrammes sans favicon.
const TILE_COLORS = [
  'linear-gradient(180deg, #fbbf24 0%, #f59e0b 100%)',
  'linear-gradient(180deg, #fb7185 0%, #e11d48 100%)',
  'linear-gradient(180deg, #38bdf8 0%, #0284c7 100%)',
  'linear-gradient(180deg, #fb923c 0%, #ea580c 100%)',
  'linear-gradient(180deg, #94a3b8 0%, #64748b 100%)',
  'linear-gradient(180deg, #6366f1 0%, #4338ca 100%)',
  'linear-gradient(180deg, #34d399 0%, #059669 100%)',
  'linear-gradient(180deg, #a78bfa 0%, #7c3aed 100%)',
];

function tileBackground(seed: string): string {
  let hash = 0;
  for (let i = 0; i < seed.length; i++) hash = (hash * 31 + seed.charCodeAt(i)) | 0;
  return TILE_COLORS[Math.abs(hash) % TILE_COLORS.length];
}

const open = (input: string) => send({ type: 'start.navigate', input });

function SiteTile({ site, enter }: { site: Site; enter?: number }) {
  const domain = formatDomain(site.url);
  const name = site.title || domain || site.url;
  const letter = (domain || name).slice(0, 1).toUpperCase();
  const hasIcon = !!site.icon;
  const look = useIconLook(site.icon ?? null); // icône d'une seule teinte : inversée dans le thème où elle disparaîtrait

  return (
    <button
      className={enter === undefined ? 'start-site' : 'start-site is-entering'}
      style={enter === undefined ? undefined : { animationDelay: `${Math.min(enter * 18, 240)}ms` }}
      title={site.url}
      onClick={() => open(site.url)}
    >
      <span
        className={`start-monogram${hasIcon ? ' has-icon' : ' is-letter'}`}
        style={hasIcon ? undefined : { background: tileBackground(domain || name) }}
        aria-hidden
      >
        {hasIcon ? <img className={look.tone.trim() || undefined} src={site.icon} alt="" draggable={false} /> : letter}
      </span>
      <span className="start-site-label">{name}</span>
    </button>
  );
}

function HistoryIcon({ site }: { site: Site }) {
  const look = useIconLook(site.icon ?? null);
  const name = formatDomain(site.url) || site.title;
  return <span className="start-history-letter" style={site.icon ? undefined : { background: tileBackground(name) }} aria-hidden>
    {site.icon ? <img className={look.tone.trim() || undefined} src={site.icon} alt="" draggable={false} /> : name.slice(0, 1).toUpperCase()}
  </span>;
}

export default function Start() {
  const [data, setData] = useState(empty);
  const [photo, setPhoto] = useState<{ url: string; author?: string; page?: string }>({ url: '' });
  // Favoris : quatre rangées, puis « Voir tout ». Le nombre de colonnes suit la largeur de la page.
  const [allFavorites, setAllFavorites] = useState(false);
  const [columns, setColumns] = useState(9);
  const favoritesGrid = useRef<HTMLDivElement>(null);
  useLayoutEffect(() => {
    const grid = favoritesGrid.current;
    if (!grid) return;
    const measure = () => setColumns(Math.max(1, getComputedStyle(grid).gridTemplateColumns.split(' ').length));
    const observer = new ResizeObserver(measure);
    observer.observe(grid);
    measure();
    return () => observer.disconnect();
  }, []);
  const shownFirst = columns * 4;
  const favorites = allFavorites ? data.favorites : data.favorites.slice(0, shownFirst);

  useLang();
  useEffect(() => {
    const off = onMessage(value => {
      const sent = value as { event?: string; url?: string; tint?: string; author?: string; page?: string };
      if (sent.event === 'start-wallpaper') {
        // La photo choisie pour le fond. Un voile sombre garde le texte lisible sur une image claire.
        setPhoto({ url: sent.url ?? '', author: sent.author, page: sent.page });
        document.body.style.backgroundImage = sent.url ? `linear-gradient(rgba(0, 0, 0, 0.2), rgba(0, 0, 0, 0.34)), url("${sent.url}")` : '';
        document.body.style.backgroundColor = (sent.url && sent.tint) || ''; // la couleur de la photo : celle que prend l'interface autour
        if (!sent.url) delete document.documentElement.dataset.wallpaperPhoto; // photo indisponible : l'image de Sol
        return;
      }
      const next = value as StartData;
      if (next.event !== 'start') return;
      setLang(next.lang);
      setData(next);
      document.documentElement.dataset.private = String(next.privateMode);
      document.documentElement.dataset.wallpaper = String(!!next.wallpaper);
      // En attendant la photo, pas d'image de Sol : la page garde un fond uni, puis la photo arrive.
      if (next.wallpaper && next.wallpaperPhoto) document.documentElement.dataset.wallpaperPhoto = 'true';
      else { delete document.documentElement.dataset.wallpaperPhoto; document.body.style.backgroundImage = ''; document.body.style.backgroundColor = ''; }
    });
    send({ type: 'start.ready' });
    document.title = t('Nouvel onglet', 'New tab');
    return () => {
      off();
      document.body.style.backgroundImage = '';
      document.body.style.backgroundColor = '';
      delete document.documentElement.dataset.wallpaperPhoto;
    };
  }, []);

  return (
    <main className="start-page">
      <div className="start-content">
        {data.privateMode && (
          <header className="start-heading">
            <h1>{t('Navigation privée', 'Private browsing')}</h1>
            <p>{t('Vos visites ne sont pas enregistrées.', 'Your visits are not saved.')}</p>
          </header>
        )}

        <section className="start-section">
          <div className="start-section-head">
            <h2>{t('Favoris', 'Favorites')}</h2>
            {data.favorites.length > shownFirst && (
              <button
                className="start-more"
                aria-expanded={allFavorites}
                onClick={() => {
                  if (!allFavorites) send({ type: 'start.favorites' }); // les icônes des favoris qui n'étaient pas affichés
                  setAllFavorites(!allFavorites);
                }}
              >
                {allFavorites ? t('Voir moins', 'Show less') : t(`Voir tout (${data.favorites.length})`, `Show all (${data.favorites.length})`)}
              </button>
            )}
          </div>
          <div className="start-favorites" ref={favoritesGrid}>
            {favorites.map((site, index) => (
              <SiteTile key={site.url} site={site} enter={allFavorites && index >= shownFirst ? index - shownFirst : undefined} />
            ))}
          </div>
          {!data.favorites.length && (
            <p className="start-empty">
              {t('Ajoutez vos sites préférés avec l’étoile de la barre d’adresse.', 'Save your favorite sites with the star in the address bar.')}
            </p>
          )}
        </section>

        <section className="start-section">
          <h2>{t('Groupes d’onglets', 'Tab groups')}</h2>
          <div className="start-groups">
            {data.groups.map(group => (
              <button key={group.id} onClick={() => send({ type: 'start.group', id: group.id })}>
                <span className="start-group-dot" style={{ background: group.color }} />
                <strong>{group.name}</strong>
                <small>
                  {group.count} {group.count === 1 ? t('onglet', 'tab') : t('onglets', 'tabs')}
                </small>
              </button>
            ))}
          </div>
          {!data.groups.length && (
            <p className="start-empty">
              {t('Vos groupes créés dans le panneau latéral apparaîtront ici.', 'Groups created in the sidebar will appear here.')}
            </p>
          )}
        </section>

        <section className="start-section">
          <h2>{t('Historique de navigation', 'Browsing history')}</h2>
          <div className="start-history">
            {data.history.map(site => {
              const domain = formatDomain(site.url);
              const title = site.title || domain;
              return (
                <button key={site.url} onClick={() => open(site.url)} title={site.url}>
                  <HistoryIcon site={site} />
                  <span>
                    <strong>{title}</strong>
                    <small>{domain}</small>
                  </span>
                  <time>
                    {new Date(site.last!).toLocaleDateString(document.documentElement.lang, { day: 'numeric', month: 'short' })}
                  </time>
                </button>
              );
            })}
          </div>
          {!data.history.length && (
            <p className="start-empty">
              {data.privateMode
                ? t('Aucun historique conservé en navigation privée.', 'No history is saved in private browsing.')
                : t('Vos dernières visites apparaîtront ici.', 'Your recent visits will appear here.')}
            </p>
          )}
        </section>
      </div>
      {data.wallpaper && photo.url && photo.author && (
        <button className="start-credit" title={photo.page} onClick={() => open(photo.page!)}>
          {t(`Photo : ${photo.author} · Unsplash`, `Photo: ${photo.author} · Unsplash`)}
        </button>
      )}
    </main>
  );
}
