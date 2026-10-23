import { Mic, Square, X } from 'lucide-react';
import { useEffect, useState } from 'react';
import { VoiceOrb } from '@/components/agents/voice-orb';
import { onMessage, send } from './bridge';
import { t } from './i18n';
import './voice.css';

export type VoiceState = { open: boolean; provider: 'windows'; mode: 'idle' | 'listening' | 'thinking' | 'speaking' | 'approval' | 'error'; level: number; text: string; error: string | null };

function voiceError(code: string) {
  switch (code) {
    case 'language-missing': return t('Installez la reconnaissance vocale française dans les paramètres Windows.', 'Install English speech recognition in Windows settings.');
    case 'voice-missing': return t('Aucune voix française disponible pour lire la réponse. Ajoutez une voix dans Windows.', 'No English voice is available to read the answer. Add a voice in Windows.');
    case 'microphone': return t('Microphone indisponible. Vérifiez le périphérique et l’accès des applications de bureau au micro dans Windows.', 'Microphone unavailable. Check your device and Windows microphone access for desktop apps.');
    case 'no-speech': return t('Aucune parole détectée. Réessayez en parlant près du micro.', 'No speech detected. Try speaking closer to your microphone.');
    case 'unclear': return t('La reconnaissance est incertaine. Vous pouvez corriger le texte avant de l’envoyer.', 'Recognition is uncertain. You can edit the text before sending it.');
    case 'speech': return t('La lecture vocale est indisponible. La réponse reste affichée dans la discussion.', 'Speech playback is unavailable. The answer is still visible in the conversation.');
    default: return t('La reconnaissance vocale a échoué. Vérifiez les paramètres vocaux de Windows et réessayez.', 'Speech recognition failed. Check Windows speech settings and try again.');
  }
}

export default function SolVoice({ state: snapshot, busy, approval, onUseText }: { state: VoiceState; busy: boolean; approval: boolean; onUseText: (text: string) => void }) {
  const [state, setState] = useState(snapshot);
  useEffect(() => setState(snapshot), [snapshot]);
  useEffect(() => onMessage(data => { if ((data as { voice?: VoiceState })?.voice) setState((data as { voice: VoiceState }).voice); }), []);
  const listening = state.mode === 'listening';
  const speaking = state.mode === 'speaking';
  const label = listening ? t('Je vous écoute…', 'Listening…') : speaking ? t('Sol AI vous répond', 'Sol AI is speaking')
    : approval ? t('Votre accord est nécessaire', 'Your approval is needed') : busy ? t('Sol AI agit…', 'Sol AI is working…') : t('À vous de parler', 'Your turn to speak');
  return <section className="sol-voice" aria-label={t('Mode vocal Sol AI', 'Sol AI voice mode')}>
    <button className="pill-action sol-voice-close" aria-label={t('Quitter le mode vocal', 'Exit voice mode')} onClick={() => send({ type: 'voice.close' })}><X size={16} /></button>
    <VoiceOrb activity={state.level} active={state.open && state.mode !== 'error'} className="sol-voice-orb" aria-label={t('Orbe vocal Sol AI', 'Sol AI voice orb')} />
    <p className="sol-voice-label" role="status">{label}</p>
    {!!state.text && <p className="sol-voice-transcript">{state.text}</p>}
    {state.error && <p className="sol-voice-error" role="alert">{voiceError(state.error)}</p>}
    <div className="sol-voice-actions">
      {listening || speaking
        ? <button className="pill-action" onClick={() => send({ type: 'voice.stop' })}><Square size={14} />{t('Arrêter', 'Stop')}</button>
        : busy && !approval
          ? <button className="pill-action" onClick={() => send({ type: 'chat.cancel' })}><Square size={14} />{t('Arrêter la tâche', 'Stop task')}</button>
          : <button className="pill-action primary" onClick={() => send({ type: 'voice.listen' })}><Mic size={14} />{approval ? t('Répondre oui ou non', 'Answer yes or no') : t('Parler', 'Speak')}</button>}
      {!busy && state.text && <button className="pill-action" onClick={() => { send({ type: 'voice.stop' }); onUseText(state.text); }}>{t('Modifier le texte', 'Edit text')}</button>}
      {state.error && state.error !== 'unclear' && state.error !== 'no-speech' && <button className="pill-action" onClick={() => send({ type: 'voice.settings' })}>{t('Paramètres Windows', 'Windows settings')}</button>}
    </div>
    <p className="sol-voice-help">{approval
      ? t('Consultez l’action ci-dessous, puis dites « oui » pour autoriser ou « non » pour refuser.', 'Review the action below, then say “yes” to approve or “no” to reject.')
      : t('Voix Windows · Votre demande est envoyée après une pause. Le micro s’arrête ensuite.', 'Windows voice · Your request is sent after a pause. The microphone then stops.')}</p>
  </section>;
}
