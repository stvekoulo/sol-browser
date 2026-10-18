import { ChevronDown, Copy, FileText, Languages, Lightbulb, MessageCircleQuestion, NotebookPen, Search } from 'lucide-react';
import type { LucideIcon } from 'lucide-react';
import type { RefObject } from 'react';
import { send } from './bridge';
import { t } from './i18n';
import { EyeTracker } from '@/components/bencho/EyeTracker';
import { Button } from '@/components/motion/button/base';
import './selection-bar.css';

export type SelectionContent = { popup: 'selection'; ai: boolean; more: boolean };

const icon = { size: 15, strokeWidth: 2, 'aria-hidden': true } as const;
function Action({ label, Icon, run }: { label: string; Icon: LucideIcon; run: () => void }) {
  return <Button variant="ghost" size="sm" pressScale={0.96} whileHover={undefined} className="selection-action" title={label} onClick={run}><Icon {...icon} /><span>{label}</span></Button>;
}

// Actions rapides au-dessus d'un texte sélectionné dans une page. La vue ne prend pas le focus : la sélection reste.
export default function SelectionBar({ rootRef, content: c }: { rootRef: RefObject<HTMLDivElement | null>; content: SelectionContent }) {
  return <div ref={rootRef} className="popup selection-bar arriving" data-eyt-frame>
    <div className="selection-row">
      <Action label={t('Copier', 'Copy')} Icon={Copy} run={() => send({ type: 'selection.copy' })} />
      {c.ai && <Button variant="ghost" size="sm" pressScale={0.96} whileHover={undefined} className="selection-action selection-sol" title={t('Ouvrir Sol AI', 'Open Sol AI')} onClick={() => send({ type: 'selection.ai', query: 'open' })}>
        <EyeTracker className="eyt-inline" size={16} eyeScale={1.8} idle /><span>Sol AI</span></Button>}
      {c.ai && <Action label={t('Expliquer', 'Explain')} Icon={Lightbulb} run={() => send({ type: 'selection.ai', query: 'explain' })} />}
      {c.ai && <Action label={t('Traduire', 'Translate')} Icon={Languages} run={() => send({ type: 'selection.ai', query: 'translate' })} />}
      <Button variant="ghost" size="sm" pressScale={0.96} whileHover={undefined} className="selection-action selection-more" aria-expanded={c.more} title={t('Plus d’actions', 'More actions')} onClick={() => send({ type: 'selection.more' })}>
        <span>{t('Plus', 'More')}</span><ChevronDown {...icon} className={c.more ? 'open' : ''} /></Button>
    </div>
    {c.more && <div className="selection-row selection-row-more">
      {c.ai && <Action label={t('Résumer', 'Summarize')} Icon={FileText} run={() => send({ type: 'selection.ai', query: 'summary' })} />}
      {c.ai && <Action label={t('Question', 'Ask')} Icon={MessageCircleQuestion} run={() => send({ type: 'selection.ai', query: 'ask' })} />}
      <Action label={t('Rechercher', 'Search')} Icon={Search} run={() => send({ type: 'selection.search' })} />
      <Action label={t('Note', 'Note')} Icon={NotebookPen} run={() => send({ type: 'selection.note' })} />
    </div>}
  </div>;
}
