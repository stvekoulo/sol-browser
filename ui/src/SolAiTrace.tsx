import { send } from './bridge';
import { t } from './i18n';
import { AgentActivity } from '@/components/agents/agent-activity';
import type { AgentActivityItem } from '@/components/agents/agent-activity/types';
import { ApprovalCard } from '@/components/agents/approval-card';
import './ai-trace.css';

export type AgentState = { steps: { id: string; kind: string; label: string; detail: string; status: string }[]; approval: { id: string; label: string; detail: string } | null } | null;

// Ce que Sol AI fait sur le site ou dans les notes, en direct, et l'accord demandé avant d'agir sur une page.
export default function SolAiTrace({ agent, busy }: { agent: AgentState; busy: boolean }) {
  if (!agent?.steps.length && !agent?.approval) return null;
  const items: AgentActivityItem[] = agent.steps.map(s => s.kind === 'search'
    ? { id: s.id, type: 'search', query: s.detail ? `${s.label} · « ${s.detail} »` : s.label }
    : { id: s.id, type: 'trace', kind: s.kind, label: s.status === 'denied' ? `${s.label} · ${t('refusé', 'declined')}` : s.status === 'failed' ? `${s.label} · ${t('échec', 'failed')}` : s.label, detail: s.detail || undefined });
  return <div className="ai-trace">
    {!!items.length && <AgentActivity items={items} contentType="mixed" status={busy ? 'working' : 'complete'} collapseOnComplete maxHeight={180}
      activeLabel={t('Sol AI agit dans le navigateur…', 'Sol AI is working in the browser…')} summary={t(`${items.length} étape${items.length > 1 ? 's' : ''}`, `${items.length} step${items.length > 1 ? 's' : ''}`)} />}
    {agent.approval && <div className="ai-approval" key={agent.approval.id}><ApprovalCard title={agent.approval.label} description={agent.approval.detail || t('Sol AI veut agir sur la page.', 'Sol AI wants to act on the page.')}
      approveLabel={t('Autoriser', 'Allow')} rejectLabel={t('Refuser', 'Decline')} pendingLabel={t('Votre accord', 'Your approval')}
      onApprove={() => send({ type: 'ai.approve', id: agent.approval!.id, index: 1 })} onReject={() => send({ type: 'ai.approve', id: agent.approval!.id, index: 0 })} />
      <button className="pill-action ai-approval-always" onClick={() => send({ type: 'ai.approve.always', id: agent.approval!.id })}>{t('Toujours autoriser', 'Always allow')}</button>
      <p className="ai-approval-hint">{t('Pour toutes vos demandes dans ce profil, sur tous les sites. Modifiable dans Paramètres → Sol AI.', 'For all your requests in this profile, across all sites. Change in Settings → Sol AI.')}</p>
    </div>}
  </div>;
}
