import { useState } from 'react';
import { send } from './bridge';
import { t } from './i18n';

export type SolAiState = {
  available: boolean; enabled: boolean; selected: string; busy: boolean; warm: boolean; phase: string;
  currentId: string | null; percent: number; error: string | null; freeMb: number; cacheDirectory: string;
  recommended: string | null; machine: {ramMb:number; availableMb:number; threads:number};
  models: { id: string; name: string; sizeMb: number | null; license: string; cached: boolean; device: string; usable:boolean }[];
  check: { id: string; text: string; loadMs: number; elapsedMs: number; memoryMb: number; device: string } | null;
  optimization: {id:string;cpuMs:number;gpuMs:number;status:string} | null;
};
const size = (mb: number) => mb >= 1024 ? `${(mb / 1024).toFixed(1)} ${t('Go', 'GB')}` : `${mb} ${t('Mo', 'MB')}`;

export type AiPreferences = {tone:number;length:number;thinking:boolean;keepMinutes:number;adaptation:boolean;context:string;isPrivate:boolean};
export default function SolAiSettings({ ai, preferences }: { ai: SolAiState; preferences: AiPreferences }) {
  const [customDuration, setCustomDuration] = useState(false);
  const [confirm, setConfirm] = useState('');
  const [accepted, setAccepted] = useState(false);
  const displayedModel=ai.models.find(m=>m.id===ai.selected)??ai.models.find(m=>m.cached)??ai.models[0];
  const [optimize, setOptimize] = useState(false);
  const phase = ({starting:t('Démarrage du module…','Starting module…'),downloading:t('Téléchargement du modèle','Downloading model'),
    removing:t('Suppression…','Removing…'),loading:t('Chargement en mémoire…','Loading into memory…'),checking:t('Vérification locale…','Running local check…'),thinking:t('Sol AI réfléchit…','Sol AI is thinking…'),generating:t('Rédaction de la réponse…','Writing the answer…'),
    unloading:t('Libération de la mémoire…','Releasing memory…'),optimizing:t('Optimisation de Sol AI…','Optimizing Sol AI…'),cancelling:t('Annulation…','Cancelling…')} as Record<string,string>)[ai.phase];
  const error = ai.error === 'disk-space' ? t('Espace disque insuffisant. Libérez de la place puis réessayez.','Not enough disk space. Free some space and retry.')
    : ai.error === 'module-busy' ? t('Sol AI est utilisé dans une autre fenêtre. Fermez cette autre fenêtre ou désactivez-y Sol AI avant de réessayer.','Sol AI is being used in another window. Close that window or turn Sol AI off there before retrying.')
    : ai.error === 'memory-limit' ? t('Le moteur manque de mémoire. Fermez des applications puis réessayez.','The engine ran out of memory. Close applications and retry.')
    : ai.error === 'machine-capacity' ? t('La mémoire ou l’espace disponible ne suffit pas actuellement. Fermez des applications puis réessayez.','Available memory or disk space is currently insufficient. Close applications and retry.')
    : ai.error === 'catalog-failed' ? t('Le catalogue est inaccessible. Les modèles déjà installés restent disponibles ; réessayez avec une connexion.','The catalog is unreachable. Installed models remain available; retry with a connection.')
    : ai.error === 'not-installed' ? t('Ce modèle doit être téléchargé avant la vérification.','Download this model before checking it.')
    : ai.error === 'catalog-save-failed' ? t('Impossible de conserver le catalogue. Vérifiez l’accès au dossier et l’espace disque.','Could not save the catalog. Check folder access and disk space.')
    : t('Le module n’a pas terminé l’opération. Réessayez, après avoir libéré de la mémoire. Sol reste disponible.','The module did not complete this operation. Retry after freeing memory. Sol remains available.');
  return <div className="ai-settings">
    <button className="setting-row" role="switch" aria-checked={ai.enabled}
      onClick={() => send({type:'ai.enable',index:ai.enabled?0:1})}>
      <span className="setting-text"><span className="row-title">Sol AI</span>
        <span className="row-sub">{t('Un moteur local facultatif pour les outils Sol. Désactivé au départ.','An optional local engine for Sol tools. Off by default.')}</span></span>
      <span className={`switch${ai.enabled?' on':''}`} aria-hidden="true" />
    </button>
    <p className="row-hint">{t('Aucun modèle n’est téléchargé automatiquement. Une fois installé et activé, Sol AI se prépare à l’ouverture et reste disponible selon la durée choisie. Désactiver Sol AI libère sa mémoire et conserve son installation.','No model is downloaded automatically. Once installed and enabled, Sol AI prepares when Sol opens and stays available for your chosen duration. Turning it off releases memory and keeps the installation.')}</p>
    <p className="row-hint">{t('Le catalogue et les téléchargements utilisent les serveurs Microsoft. L’inférence reste locale ; la télémétrie facultative du moteur est désactivée.','The catalog and downloads use Microsoft servers. Inference stays local; optional engine telemetry is disabled.')}</p>
    {ai.enabled && <>
      <button className="setting-row" role="switch" aria-checked={preferences.thinking} onClick={()=>send({type:'settings.set',id:'aiThinking',index:preferences.thinking?0:1})}><span className="setting-text"><span className="row-title">Think</span><span className="row-sub">{t('Réfléchir avant de répondre. Activé par défaut ; désactivez-le pour une réponse plus rapide. S’applique à la prochaine action.','Think before answering. On by default; turn off for a faster response. Applies to the next action.')}</span></span><span className={`switch${preferences.thinking?' on':''}`} aria-hidden="true"/></button>
      <div className="theme-row">
        <span className="row-title">{t('Disponibilité','Availability')}</span>
        <label className="ai-toolbar">{t('Garder Sol AI chargé','Keep Sol AI loaded')} <select aria-label={t('Durée de disponibilité de Sol AI','Sol AI availability duration')} value={customDuration || ![0,45].includes(preferences.keepMinutes) ? 'custom' : String(preferences.keepMinutes)} onChange={e=>{setCustomDuration(e.target.value==='custom');if(e.target.value!=='custom')send({type:'settings.set',id:'aiKeepMinutes',index:Number(e.target.value)})}}><option value="45">{t('45 minutes','45 minutes')}</option><option value="0">{t('Jusqu’à la fermeture','Until Sol closes')}</option><option value="custom">{t('Durée personnalisée','Custom duration')}</option></select></label>
        {(customDuration || ![0,45].includes(preferences.keepMinutes)) && <label className="ai-toolbar">{t('Minutes après la dernière action','Minutes after the last action')} <input className="ai-duration" type="number" min="1" max="10080" step="1" aria-label={t('Durée en minutes','Duration in minutes')} key={preferences.keepMinutes} defaultValue={preferences.keepMinutes||45} onBlur={e=>{if(e.currentTarget.checkValidity())send({type:'settings.set',id:'aiKeepMinutes',index:Number(e.currentTarget.value)})}} onKeyDown={e=>{if(e.key==='Enter')e.currentTarget.blur()}}/></label>}
        <p className="row-hint">{ai.warm ? t('Chargé en mémoire : les prochaines actions évitent ce chargement.','Loaded in memory: subsequent actions skip loading.') : ai.phase==='loading'||ai.phase==='starting' ? t('Préparation de Sol AI…','Preparing Sol AI…') : t('Sol AI se chargera à la prochaine action.','Sol AI will load on the next action.')} {t('Le délai repart après chaque action. La mémoire peut être libérée si l’ordinateur en manque.','The timer restarts after each action. Memory may be released if your computer runs low.')}</p>
      </div>
      <div className="theme-row">
        <span className="row-title">{t('Vos préférences','Your preferences')}</span>
        <p className="row-hint">{t('Conservées dans ce profil. Elles adaptent la rédaction et les explications, sans modifier les faits à résumer ou traduire.','Stored in this profile. They customize writing and explanations without changing facts to summarize or translate.')}</p>
        <label className="ai-toolbar">{t('Ton','Tone')} <select aria-label={t('Ton de Sol AI','Sol AI tone')} value={preferences.tone} onChange={e=>send({type:'settings.set',id:'aiTone',index:Number(e.target.value)})}>{[t('Naturel','Natural'),t('Amical','Friendly'),t('Professionnel','Professional')].map((label,i)=><option key={i} value={i}>{label}</option>)}</select></label>
        <label className="ai-toolbar">{t('Réponses','Responses')} <select aria-label={t('Longueur des réponses','Response length')} value={preferences.length} onChange={e=>send({type:'settings.set',id:'aiLength',index:Number(e.target.value)})}>{[t('Équilibrées','Balanced'),t('Courtes','Short'),t('Détaillées','Detailed')].map((label,i)=><option key={i} value={i}>{label}</option>)}</select></label>
        <button className="setting-row" role="switch" aria-checked={preferences.adaptation} disabled={preferences.isPrivate} onClick={()=>send({type:'settings.set',id:'aiAdaptation',index:preferences.adaptation?0:1})}><span className="setting-text"><span className="row-title">{t('S’adapter à ma navigation','Adapt to my browsing')}</span><span className="row-sub">{t('Utiliser localement les titres et domaines de mes favoris et pages récentes ou fréquentes. Aucune navigation privée.','Locally use titles and domains from bookmarks and recent or frequent pages. No private browsing.')}</span></span><span className={`switch${preferences.adaptation?' on':''}`} aria-hidden="true"/></button>
        {preferences.adaptation && <details><summary>{t('Informations utilisées','Information used')}</summary><div className="ai-source">{preferences.context||t('Aucune information disponible dans ce profil.','No information available in this profile.')}</div><p className="row-hint">{t('Actualisées avec votre navigation. Effacer l’historique retire les visites de ce contexte ; retirer les favoris retire leurs titres. Le modèle n’est pas réentraîné.','Updated as you browse. Clearing history removes visits from this context; removing bookmarks removes their titles. The model is not retrained.')}</p></details>}
        <button className="pill-action" onClick={()=>send({type:'settings.set',id:'aiReset',index:0})}>{t('Réinitialiser la personnalisation','Reset personalization')}</button>
      </div>
      <div className="ai-toolbar"><span className="row-title">{t('Installer Sol AI','Install Sol AI')}</span>
        <button className="pill-action" disabled={ai.busy} onClick={() => send({type:'ai.catalog'})}>{t('Actualiser','Refresh')}</button></div>
      <p className="row-hint">{size(ai.freeMb)} {t('disponibles sur le disque.','available on disk.')}</p>
      {displayedModel?.cached && <div className="theme-row"><span className="row-title">{t('Performances','Performance')}</span><p className="row-hint">{t('Comparer le GPU au processeur et retenir le plus rapide. Cette préparation peut télécharger une version accélérée de Sol AI ; les réponses restent sur cet appareil.','Compare the GPU with the processor and keep the faster option. Preparation may download an accelerated Sol AI variant; responses stay on this device.')}</p><button className="pill-action" disabled={ai.busy} onClick={()=>setOptimize(!optimize)}>{t('Optimiser Sol AI','Optimize Sol AI')}</button>{optimize&&<div className="ai-confirm"><p className="row-hint">{t('Installer les composants officiels et la variante GPU compatible, puis mesurer leur vitesse ? Aucun contenu de navigation ne sera utilisé.','Install official components and a compatible GPU variant, then measure their speed? No browsing content will be used.')}</p><button className="pill-action" disabled={ai.busy} onClick={()=>{setOptimize(false);send({type:'ai.optimize',id:displayedModel.id})}}>{t('Optimiser','Optimize')}</button></div>}{ai.optimization&&<p className="row-hint" role="status">{ai.optimization.status==='gpu'?t('Accélération GPU retenue pour cet ordinateur.','GPU acceleration selected for this computer.'):ai.optimization.status==='cpu-faster'?t('Le GPU n’apporte pas un gain suffisant ; Sol conserve le processeur.','The GPU does not provide a sufficient gain; Sol keeps using the processor.'):t('Accélération GPU indisponible pour ce modèle ; Sol conserve le processeur.','GPU acceleration is unavailable for this model; Sol keeps using the processor.')}</p>}</div>}
      <p className="row-hint">{t('Configuration de cet ordinateur :','Computer specifications:')} {size(ai.machine.ramMb)} {t('de mémoire,','memory,')} {ai.machine.threads} {t('processeurs logiques. La vitesse est mesurée après installation.','logical processors. Speed is measured after installation.')}</p>
      {ai.busy && <div className="ai-operation" role="status" aria-live="polite">
        <div className="ai-toolbar"><span>{phase}</span><button className="pill-action" disabled={ai.phase==='cancelling'} onClick={() => send({type:'ai.cancel'})}>{t('Annuler','Cancel')}</button></div>
        {ai.currentId && <span className="row-sub">{ai.models.find(m=>m.id===ai.currentId)?.name}</span>}
        {ai.phase==='downloading' && <><progress value={ai.percent} max={100} aria-label={t('Téléchargement','Download')} /><span className="row-sub">{ai.percent} %</span></>}
      </div>}
      {ai.error && <p className="row-hint" role="alert">{error}</p>}
      {!ai.busy && ai.phase==='cancelled' && <p className="row-hint" role="status">{t('Opération annulée. Vous pouvez réessayer quand vous le souhaitez.','Operation cancelled. Retry whenever you wish.')}</p>}
      {!ai.busy && !ai.error && ai.models.length===0 && <p className="row-hint">{t('Sol AI ne peut pas être installé pour le moment. Libérez de la mémoire ou de l’espace disque, puis actualisez.','Sol AI cannot be installed right now. Free memory or disk space, then refresh.')}</p>}
      <div className="ai-models">{ai.models.filter(m=>m.id===displayedModel?.id).map(model => <div className="ai-model" key={model.id}>
        <div className="ai-toolbar"><span className="row-title">{model.name}</span>
          <span className="row-sub">{model.sizeMb ? size(model.sizeMb) : t('Taille inconnue','Unknown size')}</span></div>
        <span className="row-sub">{model.license} · {model.device} · {model.cached ? t('Installé','Installed') : t('À télécharger','Not installed')}</span>

        {!model.usable && <span className="row-sub">{t('Mémoire ou espace momentanément insuffisant.','Temporarily insufficient memory or disk space.')}</span>}
        <div className="row-actions">
          {model.cached ? <>
            <button className="pill-action" disabled={ai.busy || !model.usable} onClick={() => send({type:'ai.check',id:model.id})}>{t('Vérifier','Check')}</button>
            <button className="pill-action" disabled={ai.busy} onClick={() => {setConfirm(confirm===`remove:${model.id}`?'':`remove:${model.id}`);setAccepted(false);}}>{t('Supprimer','Remove')}</button>
          </> : <button className="pill-action" disabled={ai.busy || !model.sizeMb} onClick={() => {setConfirm(confirm===model.id?'':model.id);setAccepted(false);}}>{t('Télécharger','Download')}</button>}
        </div>
        {confirm===model.id && !model.cached && <div className="ai-confirm">
          <p className="row-hint">{t('Licence','License')} : {model.license}. {t('Usage et modification autorisés selon ses conditions ; aucun modèle n’est inclus dans Sol.','Use and modification permitted under its terms; no model is included with Sol.')}</p>
          {/apache/i.test(model.license) && <button className="pill-action" onClick={()=>send({type:'tab.open',url:'https://www.apache.org/licenses/LICENSE-2.0'})}>{t('Lire la licence','Read the license')}</button>}
          <p className="row-hint">{t('Espace temporaire estimé :','Estimated temporary space:')} {size((model.sizeMb??0)*2+512)}. {t('La mémoire nécessaire dépend du modèle et de la tâche.','Memory needed depends on the model and task.')}</p>
          <label><input type="checkbox" checked={accepted} onChange={e=>setAccepted(e.target.checked)} />{t('J’accepte la licence et ce téléchargement.','I accept the license and this download.')}</label>
          <button className="pill-action" disabled={ai.busy || !accepted || ai.freeMb<(model.sizeMb??0)*2+512}
            onClick={() => {send({type:'ai.download',id:model.id});setConfirm('');setAccepted(false);}}>{t('Installer sur cet ordinateur','Install on this computer')}</button>
        </div>}
        {confirm===`remove:${model.id}` && model.cached && <div className="ai-confirm">
          <p className="row-hint">{t('Supprimer les fichiers locaux de ce modèle ? Un nouveau téléchargement sera nécessaire.','Remove this model’s local files? Using it again will require another download.')}</p>
          <div className="row-actions"><button className="pill-action" disabled={ai.busy} onClick={()=>{send({type:'ai.remove',id:model.id});setConfirm('');}}>{t('Confirmer la suppression','Confirm removal')}</button><button className="pill-action" onClick={()=>setConfirm('')}>{t('Garder','Keep')}</button></div>
        </div>}
      </div>)}</div>
      {ai.check && <div className="ai-check" role="status">
        <span className="row-title">{t('Vérification locale réussie','Local check passed')}</span>
        <p>{ai.check.text}</p>
        <span className="row-sub">{ai.check.device} · {(ai.check.elapsedMs/1000).toFixed(1)} s · {size(ai.check.memoryMb)} {t('au pic du processus','process peak')}</span>
        <p className="row-hint">{t('Essai fixe « 2 + 2 », sans page ni données personnelles.','Fixed “2 + 2” check, with no page or personal data.')} {!ai.busy && t('Le module est arrêté et le modèle déchargé.','The module has stopped and the model is unloaded.')}</p>
      </div>}
      <details className="ai-location"><summary>{t('Emplacement des modèles','Model location')}</summary><span className="row-sub">{ai.cacheDirectory}</span></details>
    </>}
  </div>;
}
