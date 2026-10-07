import { useEffect, useState } from 'react';
import { onMessage, send } from './bridge';
import { t, useLang } from './i18n';
import { Icon } from './Icons';

type Req = { id: string; method: string; url: string; type: string; status: number; mime: string; size: number; ms: number | null; failed: string | null };
type Detail = {
  tabId: string;
  id: string; url: string; method: string; status: number; mime: string; failed: string | null;
  requestHeaders: Record<string, string>; responseHeaders: Record<string, string>; postData: string; body: string; note: string;
};
type Filter = 'all' | 'api' | 'errors';
type Draft = { Url:string; Method:string; Headers:Record<string,string>; Body:string; Status:number; Delay:number; Mode:string };
type Result = { error?:string; reason?:string; status?:number; body?:string; headers?:Record<string,string>; truncated?:boolean };
type Tools = { tabId:string; console:{level:string;text:string}[]; mock:Draft|null; result:Result|null; busy:boolean; mockBusy?:boolean };
const emptyDraft = ():Draft=>({Url:'',Method:'GET',Headers:{'Content-Type':'application/json'},Body:'',Status:200,Delay:0,Mode:'response'});

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
  const [tools,setTools]=useState<Tools>({tabId:'',console:[],mock:null,result:null,busy:false});
  const [mode,setMode]=useState<'network'|'console'|'compose'>('network');
  const [draft,setDraft]=useState<Draft>(emptyDraft);
  const [draftTab,setDraftTab]=useState('');

  useEffect(() => {
    const off = onMessage(data => {
      const message = data as { panel?: string; items?: Req[] };
      if (message.panel === 'network') {
        setItems(message.items ?? []);
        const next=data as Tools;
        setTools(old=>{if(old.tabId && old.tabId!==next.tabId){setDetail(null);setMode('network');setDraft(emptyDraft());setDraftTab('')}return next});
      }
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
        <div className="popup-segment net-filters" aria-label={t('Outils réseau','Network tools')}>
          {(['network','console','compose'] as const).map(key=><button key={key} className={'popup-seg-btn'+(mode===key?' active':'')} aria-pressed={mode===key} onClick={()=>{setMode(key);if(key==='compose'&&!draftTab)setDraftTab(tools.tabId)}}>{key==='network'?t('Requêtes','Requests'):key==='console'?'Console':t('Rejeu / Simulation','Replay / Mock')}</button>)}
        </div>
        {mode==='network' && <div className="popup-segment net-filters" role="radiogroup" aria-label={t('Filtre', 'Filter')}>
          {filters.map(([key, label, count]) => (
            <button key={key} role="radio" aria-checked={filter === key} className={`popup-seg-btn${filter === key ? ' active' : ''}`} onClick={() => setFilter(key)}>
              <span>{label}</span>
              <span className="popup-seg-count">{count}</span>
            </button>
          ))}
        </div>}
        <span className="net-space" />
        <button className="pill-action" onClick={() => (setDetail(null), send({ type: 'network.clear' }))}>{t('Vider', 'Clear')}</button>
        <button className="net-close" aria-label={t('Fermer le panneau', 'Close panel')} title={t('Fermer le panneau', 'Close panel')} onClick={() => send({ type: 'network.toggle' })}>
          <Icon d="M4.5 4.5l7 7M11.5 4.5l-7 7" size={13} />
        </button>
      </header>

      {tools.mock && <div className="net-mock-banner"><span>{t('Simulation active sur cet onglet :','Active mock in this tab:')} {tools.mock.Method} {tools.mock.Url}</span><button className="pill-action" disabled={tools.mockBusy} onClick={()=>send({type:'network.unmock',id:tools.tabId})}>{t('Désactiver','Disable')}</button></div>}
      {mode==='console' ? <div className="net-console" aria-label="Console">{!tools.console.length&&<p className="net-note">{t('Les messages apparaissent après l’ouverture du panneau.','Messages are captured after opening this panel.')}</p>}{tools.console.map((line,i)=><pre className={'net-body console-'+line.level} key={i}>{line.level} · {line.text}</pre>)}</div> : mode==='compose' ? <Composer draft={draft} setDraft={setDraft} tools={tools} tabId={draftTab} /> : <div className="net-main">
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
        {detail && <DetailPane detail={detail} onClose={() => setDetail(null)} onEdit={()=>{setDraft({...emptyDraft(),Url:detail.url,Method:detail.method,Headers:Object.fromEntries(Object.entries(detail.requestHeaders).filter(([key,value])=>!/(cookie|authorization|token|secret|api[-_]?key|host|content-length|connection|sec-)/i.test(key)&&!value.includes('[masqué]'))),Body:detail.postData.includes('[masqué]')?'':detail.postData});setDraftTab(detail.tabId);setMode('compose')}} />}
      </div>}
    </div>
  );
}

function DetailPane({ detail, onClose, onEdit }: { detail: Detail; onClose: () => void; onEdit:()=>void }) {
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
      <button className="pill-action net-edit" onClick={onEdit}>{t('Modifier et renvoyer','Edit and resend')}</button>
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

function Composer({draft,setDraft,tools,tabId}:{draft:Draft;setDraft:(draft:Draft)=>void;tools:Tools;tabId:string}) {
  const [headers,setHeaders]=useState(JSON.stringify(draft.Headers,null,2)),[error,setError]=useState('');
  useEffect(()=>{setHeaders(JSON.stringify(draft.Headers,null,2));setError('')},[draft.Headers]);
  const run=(type:'network.replay'|'network.mock')=>{
    try { const parsed=JSON.parse(headers);if(!parsed||Array.isArray(parsed)||typeof parsed!=='object'||Object.values(parsed).some(v=>typeof v!=='string'))throw new Error();setError('');send({type,id:tabId,input:JSON.stringify({...draft,Headers:parsed})}); }
    catch {setError(t('Les en-têtes doivent être un objet JSON contenant des valeurs texte.','Headers must be a JSON object with string values.'))}
  };
  const disabled=tools.busy||tools.mockBusy||tabId!==tools.tabId||!draft.Url;
  const failure = tools.result?.error;
  const resultError = failure==='invalid'?t('Vérifiez l’URL HTTP/HTTPS, les en-têtes, les limites et les valeurs masquées.','Check the HTTP/HTTPS URL, headers, limits and masked values.') : failure==='canceled'?t('Envoi annulé.','Request canceled.') : failure==='timeout'?t('La requête a dépassé le délai de 20 secondes.','The request exceeded the 20-second timeout.') : failure==='mock-unavailable'?t('La simulation n’a pas pu être activée sur cette page.','The mock could not be enabled in this page.') : failure?t('La requête n’a pas abouti.','The request failed.')+' '+(tools.result?.reason??''):'';
  return <div className="net-composer">
    <p className="net-note">{t('Envoi manuel depuis Sol, sans cookies ni identifiants repris du site. Les redirections ne sont pas suivies. Les secrets reconnus sont masqués ; remplacez les valeurs masquées avant d’envoyer.','Manual request from Sol, without site cookies or credentials. Redirects are not followed. Recognized secrets are masked; replace masked values before sending.')}</p>
    <div className="net-compose-row"><label>{t('Méthode','Method')}<select value={draft.Method} onChange={e=>setDraft({...draft,Method:e.target.value})}>{['GET','POST','PUT','PATCH','DELETE','HEAD','OPTIONS'].map(m=><option key={m}>{m}</option>)}</select></label><label className="net-compose-url">URL<input type="url" placeholder="https://…" value={draft.Url} onChange={e=>setDraft({...draft,Url:e.target.value})}/></label></div>
    <div className="net-compose-row"><label>{t('En-têtes (JSON)','Headers (JSON)')}<textarea value={headers} onChange={e=>setHeaders(e.target.value)}/></label><label>{t('Corps','Body')}<textarea value={draft.Body} onChange={e=>setDraft({...draft,Body:e.target.value})} maxLength={20000}/></label></div>
    <div className="net-compose-actions"><button className="pill-action" disabled={disabled} onClick={()=>run('network.replay')}>{tools.busy?t('Envoi…','Sending…'):t('Envoyer la requête','Send request')}</button>{tools.busy&&<button className="pill-action" onClick={()=>send({type:'network.cancel',id:tabId})}>{t('Annuler','Cancel')}</button>}</div>
    <details className="net-mock-options"><summary>{t('Simuler cette URL dans la page','Mock this URL in the page')}</summary><p className="net-note">{t('Une règle par onglet, URL et méthode exactes, uniquement Fetch/XHR. Rien n’est enregistré. Les réponses de service workers peuvent éviter l’interception.','One exact URL and method rule per tab, for Fetch/XHR only. Nothing is saved. Service worker responses may bypass interception.')}</p><div className="net-compose-row">
      <label>{t('Réponse','Response')}<select value={draft.Mode} onChange={e=>setDraft({...draft,Mode:e.target.value})}>{[['response',t('Corps personnalisé','Custom body')],['invalid',t('JSON invalide','Invalid JSON')],['empty',t('Réponse vide','Empty response')],['timeout',t('Expiration','Timeout')]].map(([value,label])=><option key={value} value={value}>{label}</option>)}</select></label>
      <label>{t('Statut','Status')}<input type="number" min={200} max={599} value={draft.Status} onChange={e=>setDraft({...draft,Status:Number(e.target.value)})}/></label><label>{t('Délai (ms)','Delay (ms)')}<input type="number" min={0} max={10000} value={draft.Delay} onChange={e=>setDraft({...draft,Delay:Number(e.target.value)})}/></label></div>
      <button className="pill-action" disabled={disabled} onClick={()=>run('network.mock')}>{t('Activer la simulation','Enable mock')}</button>
    </details>
    {error&&<p role="alert" className="net-note">{error}</p>}
    {!!tools.result&&<section className="net-section" aria-label={t('Résultat','Result')}><h3>{t('Résultat du rejeu','Replay result')} {tools.result.status&&`· ${tools.result.status}`}</h3>{resultError?<p role="alert" className="net-note">{resultError}</p>:<><pre className="net-body">{pretty(tools.result.body??'',Object.entries(tools.result.headers??{}).find(([key])=>key.toLowerCase()==='content-type')?.[1]??'')}</pre>{tools.result.truncated&&<p className="net-note">{t('Seul le début de la réponse est affiché.','Only the beginning of the response is shown.')}</p>}<details><summary>{t('En-têtes de la réponse','Response headers')}</summary><Pairs pairs={tools.result.headers??{}}/></details></>}</section>}
  </div>;
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
