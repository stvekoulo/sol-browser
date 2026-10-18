import { useState } from 'react';
import { BookOpen, Copy, FileText, Languages, Lightbulb, MessageCircleQuestion, NotebookPen, PenLine, RefreshCw, Replace, SpellCheck, TextCursorInput, Wand2, X } from 'lucide-react';
import { EyeTracker } from '@/components/bencho/EyeTracker';
import { send } from './bridge';
import { t } from './i18n';
import SolMarkdown from './SolMarkdown';
import SolSelect from './SolSelect';
import SolTabs from './SolTabs';
import { Button } from '@/components/motion/button/base';
import { Alert, AlertAction, AlertContent, AlertDescription, AlertIcon } from '@/components/motion/alert';
import { StreamingResponse } from '@/components/agents/streaming-response';
import { AgentProgress } from '@/components/agents/loading-states/agent-progress';
import { PromptInput } from '@/components/agents/prompt-input';
import SolAiTrace, { type AgentState } from './SolAiTrace';

export type AiContent = { popup:'ai-action'; action:string; text:string; source:string; scope:'page'|'selection'|'field'; editable:boolean; truncated:boolean; language:string; target:string; ready:boolean; account:string; busy:boolean; error:string|null; result:string|null; agent:AgentState; request:{action:string;language:string;input:string}|null };
import { aiError, usagePage } from './solAiMessages';
export { aiError, usagePage } from './solAiMessages';

const languages = ['fr','en','es','de','it','pt'], languageNames = ['Français','English','Español','Deutsch','Italiano','Português'];

const icon = { className:'size-3.5', 'aria-hidden':true } as const;

export default function SolAiAction({content:c}:{content:AiContent}) {
  const [action,setAction]=useState(c.action), [instruction,setInstruction]=useState(''), [language,setLanguage]=useState(Math.max(0,languages.indexOf(c.target)));
  const actions:Record<string,{label:string;run:string;busy:string;icon:typeof FileText}>={
    summary:{label:t('Résumer','Summarize'),run:t(c.scope==='page'?'Résumer la page':'Résumer le passage',c.scope==='page'?'Summarize the page':'Summarize the passage'),busy:t('Résumé','Summarizing'),icon:FileText},
    explain:{label:t('Expliquer','Explain'),run:t('Expliquer simplement','Explain simply'),busy:t('Explication','Explaining'),icon:Lightbulb},
    ask:{label:t('Question','Ask'),run:'',busy:t('Lecture du site','Reading the site'),icon:MessageCircleQuestion},
    translate:{label:t('Traduire','Translate'),run:t('Traduire','Translate'),busy:t('Traduction','Translating'),icon:Languages},
    write:{label:t('Rédiger','Write'),run:'',busy:t('Rédaction','Writing'),icon:PenLine},
    rephrase:{label:t('Reformuler','Rephrase'),run:t('Reformuler le texte','Rephrase the text'),busy:t('Reformulation','Rephrasing'),icon:Wand2},
    improve:{label:t('Corriger','Fix'),run:t('Corriger et clarifier','Fix and clarify'),busy:t('Correction','Fixing'),icon:SpellCheck},
  };
  const available=Object.keys(actions).filter(key=>c.editable||!['write','rephrase','improve'].includes(key));
  const composer=action==='write'||action==='ask';
  const run=(value=instruction)=>{send({type:'ai.run',query:action,title:action==='translate'?languages[language]:c.language,input:composer?value:''});};
  // La réponse affichée correspond toujours à la demande envoyée (depuis la vue ou le menu contextuel), pas à une option changée depuis.
  const sent=c.request;
  const shown=c.result!==null && !!sent && sent.action===action && (action!=='translate'||languages[language]===sent.language) && (!composer||instruction.trim()===sent.input.trim());
  const done=shown && !c.busy && !c.error && !!c.result;
  const words=c.text.trim()?c.text.trim().split(/\s+/).length:0;
  const scope={page:t('Page entière','Whole page'),selection:t('Sélection','Selection'),field:t('Champ de saisie','Text field')}[c.scope];
  const ScopeIcon=c.scope==='page'?BookOpen:c.scope==='selection'?TextCursorInput:PenLine;
  const error=c.error && c.error!=='cancelled' ? c.error : null;
  return <div className="ai-action" data-eyt-frame>
    <header className="ai-head">
      <EyeTracker className="eyt-inline" size={26} eyeScale={1.5} />
      <strong>Sol AI</strong>
      <span className="ai-badge">ChatGPT</span>
      <Button variant="ghost" size="sm" className="ai-icon-btn" aria-label={t('Fermer','Close')} title={t('Fermer','Close')} onClick={()=>send({type:'popup.close'})}><X {...icon}/></Button>
    </header>

    {/* Ce qui sera envoyé, en une ligne : la source et sa taille, le texte complet au survol. */}
    <div className="ai-context" title={c.text.slice(0,600)}>
      <span className="ai-context-icon"><ScopeIcon {...icon}/></span>
      <span className="ai-context-text"><strong>{c.source||t('Votre texte','Your text')}</strong>
        <small>{scope}{words?` · ${words.toLocaleString()} ${t(words>1?'mots':'mot',words>1?'words':'word')}`:''}{c.truncated?` · ${t('extrait','excerpt')}`:''}</small></span>
    </div>

    <SolTabs className="ai-tabs" value={action} change={value=>!c.busy&&setAction(value)} label={t('Action Sol AI','Sol AI action')}
      items={available.map(key=>{const A=actions[key].icon;return {value:key,ariaLabel:actions[key].label,label:<span className="ai-tab" title={actions[key].label}><A {...icon}/>{key===action&&actions[key].label}</span>};})}/>

    {!c.ready ? <Alert variant="info" open><AlertIcon/><AlertContent><AlertDescription>{t('Connectez votre compte ChatGPT pour utiliser Sol AI.','Connect your ChatGPT account to use Sol AI.')}</AlertDescription>
        <AlertAction><Button size="sm" className="pill-action primary" onClick={()=>send({type:'ai.connect'})}>{t('Continuer avec ChatGPT','Continue with ChatGPT')}</Button></AlertAction></AlertContent></Alert>
      : composer ? <PromptInput className="ai-composer" value={instruction} onValueChange={setInstruction} minRows={1} maxRows={5} maxLength={50000} loading={c.busy} onStop={()=>send({type:'ai.cancel'})}
          aria-label={action==='ask'?t('Question sur ce site','Question about this site'):t('Consigne de rédaction','Writing instructions')}
          placeholder={action==='ask'?t('Que voulez-vous savoir sur ce site ?','What do you want to know about this site?'):t('Que souhaitez-vous écrire ?','What would you like to write?')}
          onSubmit={value=>{if(value.trim()&&!c.busy)run(value);}}/>
      : !c.busy && !done && <div className="ai-run">
          {action==='translate' && <SolSelect id="ai-language" label={t('Langue','Language')} value={language} options={languageNames} onChange={setLanguage}/>}
          <Button size="sm" className="pill-action primary ai-run-btn" disabled={!c.text.trim()} onClick={()=>run()}>{(()=>{const A=actions[action].icon;return <A {...icon}/>;})()}{actions[action].run}</Button>
        </div>}

    {c.busy && !composer && <div className="ai-progress" role="status" aria-live="polite"><AgentProgress label={actions[action].busy}/><Button variant="ghost" size="sm" className="pill-action" onClick={()=>send({type:'ai.cancel'})}>{t('Arrêter','Stop')}</Button></div>}
    {c.busy && action==='ask' && !c.agent?.steps.length && <div className="ai-progress" role="status" aria-live="polite"><AgentProgress label={actions.ask.busy}/></div>}
    {c.busy && action==='write' && <div className="ai-progress" role="status" aria-live="polite"><AgentProgress label={actions[action].busy}/></div>}

    {action==='ask' && sent?.action==='ask' && <SolAiTrace agent={c.agent} busy={c.busy}/>}

    {shown && !!c.result && <div className="ai-result" tabIndex={0}>
      <StreamingResponse status={c.busy?'streaming':c.error?'error':'complete'} showActions={false}><SolMarkdown text={c.result}/></StreamingResponse>
    </div>}

    {done && <div className="ai-actions">
      <Button variant="ghost" size="sm" className="ai-action-btn" onClick={()=>send({type:'ai.copy'})}><Copy {...icon}/>{t('Copier','Copy')}</Button>
      <Button variant="ghost" size="sm" className="ai-action-btn" onClick={()=>send({type:'ai.note'})}><NotebookPen {...icon}/>{t('Note','Note')}</Button>
      {c.editable && ['write','rephrase','improve','translate'].includes(action) && <Button variant="ghost" size="sm" className="ai-action-btn" disabled={c.truncated} onClick={()=>send({type:'ai.apply'})}><Replace {...icon}/>{t('Remplacer','Replace')}</Button>}
      <Button variant="ghost" size="sm" className="ai-action-btn ai-action-end" aria-label={t('Régénérer','Regenerate')} title={t('Régénérer','Regenerate')} onClick={()=>run(sent?.input??instruction)}><RefreshCw {...icon}/></Button>
    </div>}

    {error && <Alert variant={error==='subscription_sharing_usage_limit_exceeded'?'warning':'destructive'} open><AlertIcon/><AlertContent><AlertDescription>{aiError(error)}</AlertDescription>
      {error==='subscription_sharing_usage_limit_exceeded' && <AlertAction><Button size="sm" className="pill-action primary" onClick={()=>send({type:'tab.open',url:usagePage})}>{t('Gérer l’usage','Manage usage')}</Button></AlertAction>}</AlertContent></Alert>}

    {c.ready && <p className="ai-foot">{done?t('Vérifiez la réponse avant de l’utiliser. ','Review the response before using it. '):''}{t('Envoyé à OpenAI avec votre forfait ChatGPT','Sent to OpenAI with your ChatGPT plan')}{c.account?` · ${c.account}`:''}</p>}
  </div>;
}
