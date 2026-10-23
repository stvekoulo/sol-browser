import { lazy, Suspense, useEffect, useState } from 'react';
import { EyeTracker } from '@/components/bencho/EyeTracker';
import { onMessage, send } from './bridge';
import { t, useLang } from './i18n';
import SolMarkdown from './SolMarkdown';
import { aiError } from './solAiMessages';
import { Message, MessageBubble, MessageBubbleContent, MessageContent, MessageScroller, MessageTyping } from '@/components/agents/message';
import { PromptInput } from '@/components/agents/prompt-input';
import { CornerActions } from '@/components/bencho/CornerActions';
import { Copy, Mic, NotebookPen } from 'lucide-react';
import SolAiTrace, { type AgentState } from './SolAiTrace';
import type { VoiceState } from './SolVoice';
import './chat.css';

const SolVoice = lazy(() => import('./SolVoice'));
type ChatState = { chat: true; ready: boolean; signingIn?: boolean; account: string; busy: boolean; error: string | null; messages: { from: 'user' | 'assistant'; text: string }[]; agent?: AgentState; focus?: string; voice?: VoiceState; autoApproveActions?: boolean };

const suggestions = (focus?: string) => focus === 'developer'
  ? [t('Ouvre l’inspecteur réseau de cet onglet', 'Open the network inspector for this tab'), t('Explique cette erreur et propose une correction : ', 'Explain this error and suggest a fix: '), t('Quels appareils puis-je simuler pour tester le responsive ?', 'Which devices can I simulate to test responsive layouts?')]
  : focus === 'notes'
  ? [t('Résume mes notes récentes', 'Summarize my recent notes'), t('Cherche dans mes notes : ', 'Search my notes for: '), t('Crée une note avec les points clés de cette page', 'Create a note with the key points of this page')]
  : [t('Prépare un espace de travail avec ', 'Prepare a workspace with '), t('Aide-moi à remplir ce formulaire', 'Help me fill this form'), t('Résume cette page', 'Summarize this page')];

// Panneau de droite : questions libres à Sol AI, avec le forfait ChatGPT de l'utilisateur.
export default function SolChat() {
  useLang();
  const [state, setState] = useState<ChatState>({ chat: true, ready: false, account: '', busy: false, error: null, messages: [] });
  const [draft, setDraft] = useState('');
  useEffect(() => {
    const off = onMessage(data => {
      const next = data as ChatState;
      if (next?.chat) setState(next);
      // Le niveau audio est suivi dans SolVoice pour ne pas recalculer tout l'historique à chaque syllabe.
      else if (next?.voice) setState(current => current.voice?.open === next.voice!.open ? current : { ...current, voice: next.voice });
    });
    send({ type: 'chat.ready' });
    return off;
  }, []);
  return <div className="sol-chat" data-eyt-frame>
    <header className="sol-chat-head">
      {!!state.messages.length && <EyeTracker className="eyt-inline" size={24} eyeScale={1.5} />}
      <strong>Sol AI</strong>
      <span className="sol-chat-plan">{state.ready ? t('Forfait ChatGPT', 'ChatGPT plan') : ''}</span>
      {state.ready && state.voice?.available === true && <button className="pill-action" aria-pressed={!!state.voice?.open} aria-label={t('Mode vocal', 'Voice mode')} title={t('Mode vocal', 'Voice mode')} onClick={() => send({ type: state.voice?.open ? 'voice.close' : 'voice.open' })}><Mic size={16} /></button>}
      <button className="pill-action" disabled={state.busy || !state.messages.length} onClick={() => send({ type: 'chat.clear' })}>{t('Nouvelle discussion', 'New chat')}</button>
      <button className="pill-action" aria-label={t('Fermer', 'Close')} title={t('Fermer', 'Close')} onClick={() => send({ type: 'chat.toggle' })}>×</button>
    </header>
    <MessageScroller className="sol-chat-log" label={t('Discussion', 'Conversation')} busy={state.busy}>
      {!state.messages.length && <div className="sol-chat-empty">
        <EyeTracker className="eyt-inline sol-chat-mascot" size={72} />
        <p>{state.focus === 'developer' ? t('Demandez un diagnostic, une correction de code, une simulation réseau ou un test responsive. Pour l’analyse demandée, les erreurs et détails utiles sont envoyés à ChatGPT avec masquage des secrets connus.', 'Ask for a diagnosis, code fix, network simulation or responsive test. Requested analysis sends relevant errors and details to ChatGPT with known secrets masked.')
          : state.focus === 'notes' ? t('Sol AI peut chercher, résumer et créer vos notes.', 'Sol AI can search, summarize and create your notes.')
          : t('Demandez à Sol AI de parcourir un site, remplir un formulaire, organiser vos onglets ou préparer un espace de travail avec vos notes.', 'Ask Sol AI to browse a site, fill a form, organize your tabs or prepare a workspace with your notes.')}</p>
        {state.ready && <div className="sol-chat-suggestions">{suggestions(state.focus).map(s => <button key={s} className="pill-action" onClick={() => setDraft(s)}>{s}</button>)}</div>}
      </div>}
      {state.messages.map((m, i) => <Message key={i} from={m.from} animateIn={i === state.messages.length - 1}>
        <MessageContent>
          {m.from === 'assistant' && !m.text && state.busy && i === state.messages.length - 1
            ? <MessageTyping label={t('Sol AI répond', 'Sol AI is answering')} />
            : m.from === 'assistant' && m.text && !(state.busy && i === state.messages.length - 1)
              // Commandes dans le flux : aucune sortie du panneau, même en largeur réduite.
              ? <CornerActions layout="inline" actions={[
                  { id: 'copy', label: t('Copier la réponse', 'Copy the answer'), Icon: Copy, run: () => send({ type: 'chat.copy', id: String(i) }) },
                  { id: 'note', label: t('En faire une note', 'Make it a note'), Icon: NotebookPen, run: () => send({ type: 'chat.note', id: String(i) }) },
                ]}><MessageBubble variant="ghost"><MessageBubbleContent><SolMarkdown text={m.text} /></MessageBubbleContent></MessageBubble></CornerActions>
              : <MessageBubble variant={m.from === 'user' ? 'solid' : 'ghost'}><MessageBubbleContent>{m.from === 'user' ? m.text : <SolMarkdown text={m.text} />}</MessageBubbleContent></MessageBubble>}
        </MessageContent>
      </Message>)}
      <SolAiTrace agent={state.agent ?? null} busy={state.busy} />
      {state.error && state.error !== 'cancelled' && <p role="alert" className="sol-chat-error">{aiError(state.error)}</p>}
    </MessageScroller>
    {state.ready && state.autoApproveActions && <p className="sol-chat-permission" role="status">{t('Actions toujours autorisées · modifiable dans Paramètres → Sol AI', 'Actions always allowed · change in Settings → Sol AI')}</p>}
    {state.ready && state.voice?.available === true && state.voice.open && <Suspense fallback={null}><SolVoice state={state.voice} busy={state.busy} approval={!!state.agent?.approval} onUseText={setDraft} /></Suspense>}
    {state.ready
      ? <PromptInput className="sol-chat-input" value={draft} onValueChange={setDraft} minRows={1} maxRows={6} maxLength={20000}
          placeholder={t('Votre question…', 'Your question…')} aria-label={t('Question pour Sol AI', 'Question for Sol AI')}
          loading={state.busy} onStop={() => send({ type: 'chat.cancel' })}
          onSubmit={value => { if (!value.trim() || state.busy) return; send({ type: 'chat.send', input: value }); setDraft(''); }} />
      : <div className="sol-chat-connect"><p>{state.error ? aiError(state.error) : t('Connectez votre compte ChatGPT pour discuter avec Sol AI.', 'Connect your ChatGPT account to chat with Sol AI.')}</p>
          {state.signingIn
            ? <><p role="status">{t('Terminez la connexion dans le navigateur…', 'Finish signing in through the browser…')}</p><button className="pill-action" onClick={() => send({ type: 'ai.connect.cancel' })}>{t('Annuler', 'Cancel')}</button></>
            : <button className="pill-action primary" onClick={() => send({ type: 'ai.connect' })}>{t('Continuer avec ChatGPT', 'Continue with ChatGPT')}</button>}</div>}
  </div>;
}
