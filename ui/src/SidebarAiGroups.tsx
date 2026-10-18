import { send } from './bridge';
import { t } from './i18n';
import { aiError } from './solAiMessages';
import { EyeTracker } from '@/components/bencho/EyeTracker';
import { ThinkingShimmer } from '@/components/agents/loading-states/thinking-shimmer';
import './sidebar-ai.css';

export type AiTabsState = { ungrouped: number; busy: boolean; error: string | null; groups: { name: string; color: string; icon: string; tabs: string[] }[] } | null | undefined;

// Carte de la barre latérale : à partir de six onglets hors groupe, Sol AI propose de les ranger.
// La proposition s'affiche d'abord ; rien ne bouge avant « Appliquer ».
export function SidebarAiGroups({ ai }: { ai: AiTabsState }) {
  if (!ai) return null;
  return <div className="sidebar-ai" data-eyt-frame>
    <div className="sidebar-ai-head">
      <EyeTracker className="eyt-inline" size={22} eyeScale={1.6} />
      <span>{ai.busy ? <ThinkingShimmer>{t('Sol AI range vos onglets…', 'Sol AI is sorting your tabs…')}</ThinkingShimmer>
        : ai.groups.length ? t('Groupes proposés', 'Suggested groups')
        : t(`${ai.ungrouped} onglets hors groupe`, `${ai.ungrouped} ungrouped tabs`)}</span>
    </div>
    {ai.error && <p className="sidebar-ai-note" role="alert">{ai.error === 'groups-empty' ? t('Aucun regroupement évident.', 'No obvious grouping.') : aiError(ai.error)}</p>}
    {ai.groups.map(g => <div key={g.name} className="sidebar-ai-group" title={g.tabs.join('\n')}>
      <span className="sidebar-ai-dot" style={{ background: g.color || 'var(--accent)' }} />
      <strong>{g.icon} {g.name}</strong><small>{g.tabs.length}</small>
    </div>)}
    {!ai.busy && <div className="sidebar-ai-actions">
      {ai.groups.length
        ? <button className="pill-action primary" onClick={() => send({ type: 'tabs.ai.apply' })}>{t('Appliquer', 'Apply')}</button>
        : <button className="pill-action primary" onClick={() => send({ type: 'tabs.ai' })}>{t('Ranger avec Sol AI', 'Sort with Sol AI')}</button>}
      <button className="pill-action" onClick={() => send({ type: 'tabs.ai.dismiss' })}>{t('Ignorer', 'Dismiss')}</button>
    </div>}
  </div>;
}
