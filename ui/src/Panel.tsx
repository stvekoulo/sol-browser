import { useEffect, useState } from 'react';
import { onMessage, send } from './bridge';
import { t, useLang } from './i18n';
import { Icon } from './Icons';

type Req = { id: string; method: string; url: string; type: string; status: number; mime: string; size: number; ms: number | null; failed: string | null };
type Detail = {
  id: string; url: string; method: string; status: number; mime: string; failed: string | null;
  requestHeaders: Record<string, string>; responseHeaders: Record<string, string>; postData: string; body: string; note: string;
};
type Filter = 'all' | 'api' | 'errors';

const isApi = (r: Req) => r.type === 'XHR' || r.type === 'Fetch';
const isError = (r: Req) => !!r.failed || r.status >= 400;

const bytes = (n: number) => (n <= 0 ? '—' : n < 1024 ? `${n} ${t('o', 'B')}` : n < 1024 * 1024 ? `${(n / 1024).toFixed(1)} ${t('Ko', 'KB')}` : `${(n / 1024 / 1024).toFixed(1)} ${t('Mo', 'MB')}`);
const time = (ms: number | null) => (ms === null ? '…' : ms < 1000 ? `${Math.round(ms)} ms` : `${(ms / 1000).toFixed(2)} s`);

// « /api/users?page=2 » d'un côté, le site de l'autre : le nom d'une requête se lit d'abord par sa fin.
function parts(url: string): [string, string] {
  try {
    const u = new URL(url);
    return [(u.pathname.split('/').filter(Boolean).pop() ?? '/') + u.search, u.host + u.pathname];
  } catch {
    return [url.slice(0, 80), ''];
  }
}

// Panneau du bas : requêtes réseau de l'onglet affiché. L'hôte envoie la liste ; un clic demande le détail d'une requête.
export default function Panel() {
  useLang();
  const [items, setItems] = useState<Req[]>([]);
  const [filter, setFilter] = useState<Filter>('all');
  const [detail, setDetail] = useState<Detail | null>(null);

  useEffect(() => {
    const off = onMessage(data => {
      const message = data as { panel?: string; items?: Req[] };
      if (message.panel === 'network') setItems(message.items ?? []);
      if (message.panel === 'network-detail') setDetail(data as Detail);
    });
    send({ type: 'network.ready' });
    return off;
  }, []);

  const shown = items.filter(r => (filter === 'api' ? isApi(r) : filter === 'errors' ? isError(r) : true)).reverse(); // la plus récente en haut
  const filters: [Filter, string, number][] = [
    ['all', t('Tout', 'All'), items.length],
    ['api', 'API', items.filter(isApi).length],
    ['errors', t('Erreurs', 'Errors'), items.filter(isError).length],
  ];

  return (
    <div className="net">
      <header className="net-bar">
        <span className="net-title">{t('Réseau', 'Network')}</span>
        <div className="popup-segment net-filters" role="radiogroup" aria-label={t('Filtre', 'Filter')}>
          {filters.map(([key, label, count]) => (
            <button key={key} role="radio" aria-checked={filter === key} className={`popup-seg-btn${filter === key ? ' active' : ''}`} onClick={() => setFilter(key)}>
              <span>{label}</span>
              <span className="popup-seg-count">{count}</span>
            </button>
          ))}
        </div>
        <span className="net-space" />
        <button className="pill-action" onClick={() => (setDetail(null), send({ type: 'network.clear' }))}>{t('Vider', 'Clear')}</button>
        <button className="net-close" aria-label={t('Fermer le panneau', 'Close panel')} title={t('Fermer le panneau', 'Close panel')} onClick={() => send({ type: 'network.toggle' })}>
          <Icon d="M4.5 4.5l7 7M11.5 4.5l-7 7" size={13} />
        </button>
      </header>

      <div className="net-main">
        <div className="net-list" role="table" aria-label={t('Requêtes', 'Requests')}>
          {shown.length === 0 && (
            <p className="net-empty">
              {items.length === 0
                ? t('Aucune requête pour l’instant. Rechargez la page pour voir son chargement complet.', 'No requests yet. Reload the page to see its full load.')
                : t('Aucune requête ne correspond à ce filtre.', 'No request matches this filter.')}
            </p>
          )}
          {shown.map(r => {
            const [name, where] = parts(r.url);
            const state = r.failed ? 'bad' : r.status === 0 ? 'wait' : r.status >= 400 ? 'bad' : r.status >= 300 ? 'dim' : 'ok';
            return (
              <button key={r.id} role="row" className={`net-row${detail?.id === r.id ? ' selected' : ''}`} title={r.url} onClick={() => send({ type: 'network.detail', id: r.id })}>
                <span className={`net-status ${state}`}>{r.failed ? '✕' : r.status || '…'}</span>
                <span className="net-method">{r.method}</span>
                <span className="net-name">
                  <span>{name}</span>
                  <span className="net-where">{where}</span>
                </span>
                <span className="net-type">{r.type}</span>
                <span className="net-num">{bytes(r.size)}</span>
                <span className="net-num">{time(r.ms)}</span>
              </button>
            );
          })}
        </div>
        {detail && <DetailPane detail={detail} onClose={() => setDetail(null)} />}
      </div>
    </div>
  );
}

function DetailPane({ detail, onClose }: { detail: Detail; onClose: () => void }) {
  const note =
    detail.note === 'binary' ? t('Contenu binaire (image, police, fichier) : non affiché.', 'Binary content (image, font, file): not shown.')
    : detail.note === 'truncated' ? t('Réponse longue : seul le début est affiché.', 'Long response: only the beginning is shown.')
    : detail.note === 'unavailable' ? t('Pas de corps, ou réponse que le moteur n’a plus en mémoire.', 'No body, or a response the engine no longer keeps.')
    : '';
  return (
    <aside className="net-detail" aria-label={t('Détail de la requête', 'Request detail')}>
      <div className="net-detail-head">
        <span className="net-method">{detail.method}</span>
        <span className="net-detail-url">{detail.url}</span>
        <button className="net-close" aria-label={t('Fermer le détail', 'Close detail')} onClick={onClose}>
          <Icon d="M4.5 4.5l7 7M11.5 4.5l-7 7" size={12} />
        </button>
      </div>
      <Section title={t('Général', 'General')}>
        <Pairs pairs={{ [t('État', 'Status')]: detail.failed ? `${t('Échec', 'Failed')} (${detail.failed})` : String(detail.status || '…'), [t('Type', 'Type')]: detail.mime || '—' }} />
      </Section>
      <Section title={t('Réponse', 'Response')}>
        {note && <p className="net-note">{note}</p>}
        {detail.body && <pre className="net-body">{pretty(detail.body, detail.mime)}</pre>}
      </Section>
      {detail.postData && (
        <Section title={t('Corps envoyé', 'Request body')}>
          <pre className="net-body">{pretty(detail.postData, 'json')}</pre>
        </Section>
      )}
      <Section title={t('En-têtes de la réponse', 'Response headers')}>
        <Pairs pairs={detail.responseHeaders} />
      </Section>
      <Section title={t('En-têtes de la requête', 'Request headers')}>
        <Pairs pairs={detail.requestHeaders} />
      </Section>
    </aside>
  );
}

// Du JSON est remis en forme pour être lu ; tout le reste est affiché tel quel.
function pretty(text: string, mime: string) {
  if (!mime.includes('json')) return text;
  try {
    return JSON.stringify(JSON.parse(text), null, 2);
  } catch {
    return text;
  }
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="net-section">
      <h3>{title}</h3>
      {children}
    </section>
  );
}

function Pairs({ pairs }: { pairs: Record<string, string> }) {
  const entries = Object.entries(pairs);
  if (entries.length === 0) return <p className="net-note">—</p>;
  return (
    <dl className="net-pairs">
      {entries.map(([name, value]) => (
        <div key={name}>
          <dt>{name}</dt>
          <dd>{value}</dd>
        </div>
      ))}
    </dl>
  );
}
