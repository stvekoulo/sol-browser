import { useEffect, useState } from 'react';
import { EyeTracker } from '@/components/bencho/EyeTracker';
import { onMessage, send } from './bridge';
import { t, useLang } from './i18n';
import SolMarkdown from './SolMarkdown';
import { aiError } from './solAiMessages';
import { Message, MessageBubble, MessageBubbleContent, MessageContent, MessageScroller, MessageTyping } from '@/components/agents/message';
import { PromptInput } from '@/components/agents/prompt-input';
import { CornerActions } from '@/components/bencho/CornerActions';
import { Copy, NotebookPen } from 'lucide-react';
import SolAiTrace, { type AgentState } from './SolAiTrace';
import './chat.css';

type ChatState = { chat: true; ready: boolean; signingIn?: boolean; account: string; busy: boolean; error: string | null; messages: { from: 'user' | 'assistant'; text: string }[]; agent?: AgentState; focus?: string };

const suggestions = (focus?: string) => focus === 'notes'
  ? [t('Résume mes notes récentes', 'Summarize my recent notes'), t('Cherche dans mes notes : ', 'Search my notes for: '), t('Crée une note avec les points clés de cette page', 'Create a note with the key points of this page')]
  : [t('Que peut-on faire sur ce site ?', 'What can I do on this site?'), t('Résume cette page', 'Summarize this page'), t('Trouve dans la documentation : ', 'Find in the documentation: ')];

// Panneau de droite : questions libres à Sol AI, avec le forfait ChatGPT de l'utilisateur.
export default function SolChat() {
  useLang();
  const [state, setState] = useState<ChatState>({ chat: true, ready: false, account: '', busy: false, error: null, messages: [] });
  const [draft, setDraft] = useState('');
  useEffect(() => {
    const off = onMessage(data => { if ((data as ChatState)?.chat) setState(data as ChatState); });
    send({ type: 'chat.ready' });
    return off;
  }, []);
  return <div className="sol-chat" data-eyt-frame>
    <header className="sol-chat-head">
      {!!state.messages.length && <EyeTracker className="eyt-inline" size={24} eyeScale={1.5} />}
      <strong>Sol AI</strong>
      <span className="sol-chat-plan">{state.ready ? t('Forfait ChatGPT', 'ChatGPT plan') : ''}</span>
      <button className="pill-action" disabled={state.busy || !state.messages.length} onClick={() => send({ type: 'chat.clear' })}>{t('Nouvelle discussion', 'New chat')}</button>
      <button className="pill-action" aria-label={t('Fermer', 'Close')} title={t('Fermer', 'Close')} onClick={() => send({ type: 'chat.toggle' })}>×</button>
    </header>
    <MessageScroller className="sol-chat-log" label={t('Discussion', 'Conversation')} busy={state.busy}>
      {!state.messages.length && <div className="sol-chat-empty">
        <EyeTracker className="eyt-inline sol-chat-mascot" size={72} />
        <p>{state.focus === 'notes' ? t('Sol AI peut chercher, résumer et créer vos notes.', 'Sol AI can search, summarize and create your notes.')
          : t('Posez une question. Sol AI peut lire et parcourir l’onglet ouvert, et utiliser vos notes.', 'Ask anything. Sol AI can read and browse the open tab, and use your notes.')}</p>
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
