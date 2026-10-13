import { useEffect, useRef, useState } from 'react';
import { onMessage, send } from './bridge';
import { t, useLang } from './i18n';
import { Icon } from './Icons';
import { CopyAction, DetailPane, Pairs, pretty, type Detail } from './NetworkDetail';
import { matchesRequest } from './networkFormat';

type Req = { id: string; method: string; url: string; type: string; status: number; mime: string; size: number; ms: number | null; failed: string | null };

type Filter = 'all' | 'api' | 'errors';
type Draft = { Url:string; Method:string; Headers:Record<string,string>; Body:string; Status:number; Delay:number; Mode:string };
type Result = { error?:string; reason?:string; status?:number; body?:string; headers?:Record<string,string>; truncated?:boolean };
type Tools = { tabId:string; console:{level:string;text:string}[]; mock:Draft|null; result:Result|null; busy:boolean; mockBusy?:boolean; paused?:boolean; preserve?:boolean };
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
  const [query,setQuery]=useState(''), [sort,setSort]=useState<'recent'|'duration'|'size'>('recent');
  const activeTab=useRef(''), selectedRequest=useRef<string|null>(null);

  useEffect(() => {
    const off = onMessage(data => {
      const message = data as { panel?: string; items?: Req[] };
      if (message.panel === 'network') {
        setItems(message.items ?? []);
        const next=data as Tools;
        if(activeTab.current && activeTab.current!==next.tabId){setDetail(null);selectedRequest.current=null;setMode('network');setDraft(emptyDraft());setDraftTab('');setQuery('')}
        activeTab.current=next.tabId;setTools(next);
        if(selectedRequest.current && !message.items?.some(item=>item.id===selectedRequest.current)){setDetail(null);selectedRequest.current=''}
      }
      if (message.panel === 'network-detail') {const next=data as Detail;if(next.tabId===activeTab.current&&(selectedRequest.current===null||next.id===selectedRequest.current)){selectedRequest.current=next.id;setDetail(next)}}
    });
    send({ type: 'network.ready' });
    return off;
  }, []);

  const shown = items.filter(r => matchesRequest(r,query) && (filter === 'api' ? isApi(r) : filter === 'errors' ? isError(r) : true)).reverse();
  if(sort!=='recent')shown.sort((a,b)=>sort==='duration'?(b.ms??0)-(a.ms??0):b.size-a.size);
  const lines=tools.console.filter(line=>line.text.toLowerCase().includes(query.toLowerCase())&&(filter!=='errors'||line.level==='error'||line.level==='warning'));
  const filters: [Filter, string, number][] = [
    ['all', t('Tout', 'All'), items.length],
    ['api', 'API', items.filter(isApi).length],
    ['errors', t('Erreurs', 'Errors'), items.filter(isError).length],
  ];

  return (
    <div className="net">
      <header className="net-bar">
        <span className="net-title">{t('Outils dev', 'Dev tools')}</span>
        <div className="popup-segment net-filters" aria-label={t('Outils réseau','Network tools')}>
          {(['network','console','compose'] as const).map(key=><button key={key} className={'popup-seg-btn'+(mode===key?' active':'')} aria-pressed={mode===key} onClick={()=>{setMode(key);if(key==='compose'&&!draftTab)setDraftTab(tools.tabId)}}>{key==='network'?t('Requêtes','Requests'):key==='console'?'Console':t('Rejeu / Simulation','Replay / Mock')}</button>)}
        </div>
        <span className="net-space" />
        <button className={'net-icon'+(tools.paused?' on':'')} data-action="pause" aria-pressed={!!tools.paused} title={tools.paused?t('Reprendre la capture','Resume capture'):t('Mettre la capture en pause','Pause capture')} onClick={()=>send({type:'network.pause',id:tools.tabId,index:tools.paused?0:1})}><Icon d={tools.paused?'M5 3l8 5-8 5z':'M5 3v10M11 3v10'} size={16}/></button>
        <button className="net-icon" data-action="reload" title={t('Recharger la page','Reload page')} onClick={()=>send({type:'nav.reload',id:tools.tabId})}><Icon d="M12 5a5 5 0 1 0 1 6M12 2v4H8" size={16}/></button>
        <button className="net-icon" data-action="export" title={t('Exporter le journal JSON · requêtes, en-têtes et console','Export JSON log · requests, headers and console')} disabled={!items.length&&!tools.console.length} onClick={()=>send({type:'network.export',id:tools.tabId})}><Icon d="M8 2v8M4 7l4 4 4-4M3 12v2h10v-2" size={16}/></button>
        <button className="net-icon" data-action="devtools" title={t('DevTools natifs · F12','Native DevTools · F12')} onClick={()=>send({type:'devtools.open'})}><Icon d="M5 3L1 8l4 5M11 3l4 5-4 5M9 2L7 14" size={16}/></button>
        <button className="net-icon" data-action="clear" title={t('Vider le journal','Clear log')} onClick={() => {setDetail(null);selectedRequest.current='';send({ type: 'network.clear' })}}><Icon d="M3 4h10M6 2h4M4 4l1 10h6l1-10M7 7v4M9 7v4" size={16}/></button>
        <button className="net-close" aria-label={t('Fermer le panneau', 'Close panel')} title={t('Fermer le panneau', 'Close panel')} onClick={() => send({ type: 'network.toggle' })}>
          <Icon d="M4.5 4.5l7 7M11.5 4.5l-7 7" size={13} />
        </button>
      </header>
      {mode!=='compose'&&<div className="net-controls">
        <label className="net-search"><Icon d="M7 12a5 5 0 1 0 0-10 5 5 0 0 0 0 10M11 11l3 3" size={14}/><input aria-label={t('Rechercher dans le journal','Search log')} placeholder={mode==='network'?t('URL, méthode, type ou statut…','URL, method, type or status…'):t('Rechercher un message…','Find a message…')} value={query} onChange={e=>setQuery(e.target.value)}/></label>
        {mode==='network' ? <div className="popup-segment net-filters" role="radiogroup" aria-label={t('Filtre', 'Filter')}>
          {filters.map(([key, label, count]) => (
            <button key={key} role="radio" aria-checked={filter === key} className={`popup-seg-btn${filter === key ? ' active' : ''}`} onClick={() => setFilter(key)}>
              <span>{label}</span>
              <span className="popup-seg-count">{count}</span>
            </button>
          ))}
        </div>:<button className={'pill-action'+(filter==='errors'?' on':'')} aria-pressed={filter==='errors'} onClick={()=>setFilter(filter==='errors'?'all':'errors')}>{t('Erreurs et alertes','Errors and warnings')}</button>}
        {mode==='network'&&<div className="popup-segment net-filters" aria-label={t('Trier les requêtes','Sort requests')}>{(['recent','duration','size'] as const).map(key=><button key={key} className={'popup-seg-btn'+(sort===key?' active':'')} aria-pressed={sort===key} onClick={()=>setSort(key)}>{key==='recent'?t('Récentes','Recent'):key==='duration'?t('Durée','Duration'):t('Taille','Size')}</button>)}</div>}
        <label className="net-preserve"><input type="checkbox" checked={tools.preserve??true} onChange={e=>send({type:'network.preserve',id:tools.tabId,index:e.target.checked?1:0})}/>{t('Conserver au rechargement','Keep across reloads')}</label>
      </div>}
      <div className="net-summary"><span>{tools.paused?t('Capture en pause','Capture paused'):t('Capture active','Capturing')}</span><span>{shown.length} / {items.length} {t('requêtes','requests')}</span><span>{bytes(items.reduce((sum,item)=>sum+item.size,0))}</span>{mode==='console'&&<CopyAction label={t('Copier les messages','Copy messages')} text={lines.map(line=>`${line.level} · ${line.text}`).join('\n')}/>}</div>

      {tools.mock && <div className="net-mock-banner"><span>{t('Simulation active sur cet onglet :','Active mock in this tab:')} {tools.mock.Method} {tools.mock.Url}</span><button className="pill-action" disabled={tools.mockBusy} onClick={()=>send({type:'network.unmock',id:tools.tabId})}>{t('Désactiver','Disable')}</button></div>}
      {mode==='console' ? <div className="net-console" aria-label="Console">{!lines.length&&<p className="net-note">{tools.console.length?t('Aucun message ne correspond au filtre.','No matching messages.'):t('Les messages apparaissent après l’ouverture du panneau.','Messages are captured after opening this panel.')}</p>}{lines.map((line,i)=><pre className={'net-body console-'+line.level} key={i}>{line.level} · {line.text}</pre>)}</div> : mode==='compose' ? <Composer draft={draft} setDraft={setDraft} tools={tools} tabId={draftTab} /> : <div className="net-main">
        <div className="net-list" role="table" aria-label={t('Requêtes', 'Requests')}>
          <div className="net-columns" role="row">{[t('État','Status'),t('Méthode','Method'),t('Requête','Request'),t('Type','Type'),t('Taille','Size'),t('Durée','Duration')].map(label=><span role="columnheader" key={label}>{label}</span>)}</div>
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
              <button key={r.id} role="row" className={`net-row${selectedRequest.current === r.id ? ' selected' : ''}`} title={r.url} onClick={() => {selectedRequest.current=r.id;send({ type: 'network.detail', id: r.id })}}>
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
        {detail && <DetailPane detail={detail} onClose={() => {setDetail(null);selectedRequest.current=''}} onEdit={()=>{setDraft({...emptyDraft(),Url:detail.url,Method:detail.method,Headers:Object.fromEntries(Object.entries(detail.requestHeaders).filter(([key,value])=>!/(cookie|authorization|token|secret|api[-_]?key|host|content-length|connection|sec-)/i.test(key)&&!value.includes('[masqué]'))),Body:detail.postData.includes('[masqué]')?'':detail.postData});setDraftTab(detail.tabId);setMode('compose')}} />}
      </div>}
    </div>
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
