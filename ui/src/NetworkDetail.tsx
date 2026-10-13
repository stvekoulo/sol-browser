import { useEffect, useState } from 'react';
import { t } from './i18n';
import { Icon } from './Icons';
import { toCurl } from './networkFormat';

export type Detail = {
  tabId: string;
  id: string; url: string; method: string; status: number; mime: string; failed: string | null;
  requestHeaders: Record<string, string>; responseHeaders: Record<string, string>; postData: string; body: string; note: string;
};

export function DetailPane({ detail, onClose, onEdit }: { detail: Detail; onClose: () => void; onEdit:()=>void }) {
  const [tab,setTab]=useState<'response'|'headers'|'request'>('response');
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
      <div className="net-detail-actions">
        <button className="pill-action net-edit" onClick={onEdit}>{t('Modifier et renvoyer','Edit and resend')}</button>
        <CopyAction label={t('Copier l’URL','Copy URL')} text={detail.url}/>
        <CopyAction label="cURL (Bash)" text={toCurl(detail)}/>
      </div>
      <Section title={t('Général', 'General')}>
        <Pairs pairs={{ [t('État', 'Status')]: detail.failed ? `${t('Échec', 'Failed')} (${detail.failed})` : String(detail.status || '…'), [t('Type', 'Type')]: detail.mime || '—' }} />
      </Section>
      <div className="popup-segment net-filters net-detail-tabs">{(['response','headers','request'] as const).map(key=><button key={key} className={'popup-seg-btn'+(tab===key?' active':'')} aria-pressed={tab===key} onClick={()=>setTab(key)}>{key==='response'?t('Réponse','Response'):key==='headers'?t('En-têtes','Headers'):t('Corps envoyé','Request body')}</button>)}</div>
      {tab==='response'&&<Section title={t('Réponse', 'Response')}>
        {note && <p className="net-note">{note}</p>}
        {detail.body&&<CopyAction label={t('Copier la réponse','Copy response')} text={pretty(detail.body,detail.mime)}/>}
        {detail.body && <pre className="net-body">{pretty(detail.body, detail.mime)}</pre>}
        {!detail.body&&!note&&<p className="net-note">{t('Aucun corps de réponse.','No response body.')}</p>}
      </Section>}
      {tab==='request' && (
        <Section title={t('Corps envoyé', 'Request body')}>
          {detail.postData&&<CopyAction label={t('Copier le corps','Copy body')} text={pretty(detail.postData,'json')}/>}
          <pre className="net-body">{pretty(detail.postData, 'json')}</pre>
          {!detail.postData&&<p className="net-note">{t('Aucun corps envoyé.','No request body.')}</p>}
        </Section>
      )}
      {tab==='headers'&&<><Section title={t('En-têtes de la réponse', 'Response headers')}>
        <Pairs pairs={detail.responseHeaders} />
      </Section>
      <Section title={t('En-têtes de la requête', 'Request headers')}>
        <Pairs pairs={detail.requestHeaders} />
      </Section></>}
    </aside>
  );
}

export function CopyAction({text,label}:{text:string;label:string}) {
  const [copied,setCopied]=useState(false),[failed,setFailed]=useState(false);
  useEffect(()=>{setCopied(false);setFailed(false)},[text]);
  return <button className="pill-action net-copy" disabled={!text} title={label} onClick={async()=>{
    try {await navigator.clipboard.writeText(text);setCopied(true);setFailed(false)}
    catch {setFailed(true)}
  }}>{failed?t('Copie indisponible','Copy unavailable'):copied?t('Copié','Copied'):label}</button>;
}

// Du JSON est remis en forme pour être lu ; tout le reste est affiché tel quel.
export function pretty(text: string, mime: string) {
  if (!mime.includes('json')) return text;
  try {
    return JSON.stringify(JSON.parse(text), null, 2);
  } catch {
    return text;
  }
}

export function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="net-section">
      <h3>{title}</h3>
      {children}
    </section>
  );
}

export function Pairs({ pairs }: { pairs: Record<string, string> }) {
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
