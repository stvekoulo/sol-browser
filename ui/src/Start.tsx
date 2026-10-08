import { useEffect, useState } from 'react';
import { useIconLook } from './Sidebar';
import { formatDomain, onMessage, send } from './bridge';
import { setLang, t, useLang } from './i18n';

import './Start.css';

type Site = { url: string; title: string; last?: number; icon?: string; preview?: string; previewFailed?: boolean };
type StartData = {
  event: 'start';
  privateMode: boolean;
  wallpaper?: boolean;
  lang: string;
  favorites: Site[];
  frequent: Site[];
  history: Site[];
  groups: { id: string; name: string; color: string; count: number }[];
};

const empty: StartData = {
  event: 'start',
  privateMode: false,
  wallpaper: false,
  lang: '',
  favorites: [],
  frequent: [],
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

function SiteTile({ site }: { site: Site }) {
  const domain = formatDomain(site.url);
  const name = site.title || domain || site.url;
  const letter = (domain || name).slice(0, 1).toUpperCase();
  const hasIcon = !!site.icon;
  const look = useIconLook(site.icon ?? null); // icône d'une seule teinte : inversée dans le thème où elle disparaîtrait

  return (
    <button className="start-site" title={site.url} onClick={() => open(site.url)}>
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

  useLang();
  useEffect(() => {
    const off = onMessage(value => {
      const next = value as StartData;
      if (next.event !== 'start') return;
      setLang(next.lang);
      setData(next);
      document.documentElement.dataset.private = String(next.privateMode);
      document.documentElement.dataset.wallpaper = String(!!next.wallpaper);
    });
    send({ type: 'start.ready' });
    document.title = t('Nouvel onglet', 'New tab');
    return off;
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
          <h2>{t('Favoris', 'Favorites')}</h2>
          <div className="start-favorites">
            {data.favorites.map(site => (
              <SiteTile key={site.url} site={site} />
            ))}
          </div>
          {!data.favorites.length && (
            <p className="start-empty">
              {t('Ajoutez vos sites préférés avec l’étoile de la barre d’adresse.', 'Save your favorite sites with the star in the address bar.')}
            </p>
          )}
        </section>

        <section className="start-section">
          <h2>{t('Fréquemment consultés', 'Frequently visited')}</h2>
          <div className="start-frequent">
            {data.frequent.map(site => {
              const domain = formatDomain(site.url);
              const letter = (domain || site.title || '?').slice(0, 1).toUpperCase();
              return (
                <button
                  className="start-preview"
                  key={site.url}
                  title={site.title || site.url}
                  onClick={() => open(site.url)}
                >
                  <span className="start-preview-image">
                    {site.preview ? (
                      <img src={site.preview} alt="" draggable={false} />
                    ) : (
                      <span className="start-preview-placeholder">
                        <span className="start-preview-pedestal" aria-hidden>
                          <span className="start-preview-badge" style={{ background: tileBackground(domain) }}>
                            {letter}
                          </span>
                        </span>
                        <span className="start-preview-status">
                          {site.previewFailed ? t('Aperçu indisponible', 'Preview unavailable') : t('Aperçu en cours…', 'Loading preview…')}
                        </span>
                      </span>
                    )}
                  </span>
                  <span className="start-preview-domain">{domain}</span>
                </button>
              );
            })}
          </div>
          {!data.frequent.length && (
            <p className="start-empty">
              {data.privateMode
                ? t('Indisponible en navigation privée.', 'Unavailable in private browsing.')
                : t('Vos sites les plus consultés apparaîtront ici.', 'Your most visited sites will appear here.')}
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
    </main>
  );
}
