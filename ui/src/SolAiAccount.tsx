import { EyeTracker } from '@/components/bencho/EyeTracker';
import { send } from './bridge';
import { t } from './i18n';
import SolSelect from './SolSelect';
import { aiError, usagePage } from './solAiMessages';
import { ThinkingShimmer } from '@/components/agents/loading-states/thinking-shimmer';

export type SolAiState = { connected:boolean; ready:boolean; email:string; signingIn:boolean; error:string|null; isPrivate:boolean; tone:number; length:number; categories:boolean; categorizing:boolean; categoriesError:string|null; categoriesUpdated:number|null; categoriesClassified:number; categoriesPending:number; categoriesBudget:number; models:{slug:string;name:string}[]; model:string; autoApproveActions?:boolean };

// Compte ChatGPT de Sol AI, suivant les règles d’interface d’OpenAI : bouton « Continuer avec ChatGPT », badge « Connecté »,
// mention du forfait utilisé et lien « Gérer l’usage ».
export default function SolAiAccount({ ai }: { ai: SolAiState }) {
  return <div className="ai-settings" data-eyt-frame>
    <div className="setting-row static">
      <span className="setting-text">
        <span className="row-title ai-title"><EyeTracker className="eyt-inline" size={24} eyeScale={1.5} />Sol AI {ai.connected && <span className="ai-badge">{t('Connecté','Connected')}</span>}</span>
        <span className="row-sub">{ai.isPrivate ? t('Indisponible en navigation privée.','Unavailable in private browsing.')
          : ai.connected ? `${ai.email}${ai.ready ? ' · ' + t('Utilise votre forfait ChatGPT','Using ChatGPT plan') : ''}`
          : t('Résumer, expliquer, interroger, traduire ou rédiger avec votre abonnement ChatGPT, sans clé API.','Summarize, explain, ask, translate or write with your ChatGPT subscription, no API key.')}</span>
      </span>
      {!ai.isPrivate && <span className="row-actions">
        {ai.signingIn ? <><ThinkingShimmer>{t('Connexion dans le navigateur…','Signing in through the browser…')}</ThinkingShimmer><button className="pill-action" onClick={()=>send({type:'ai.connect.cancel'})}>{t('Annuler','Cancel')}</button></>
          : !ai.ready ? <button className="pill-action primary" onClick={()=>send({type:'ai.connect'})}>{t('Continuer avec ChatGPT','Continue with ChatGPT')}</button> : null}
        {ai.connected && !ai.signingIn && <><button className="pill-action" onClick={()=>send({type:'tab.open',url:usagePage})}>{t('Gérer l’usage','Manage usage')}</button><button className="pill-action" onClick={()=>send({type:'ai.disconnect'})}>{t('Se déconnecter','Sign out')}</button></>}
      </span>}
    </div>
    {ai.error && <p className="row-hint" role="alert">{aiError(ai.error)}</p>}
    {!ai.isPrivate && <p className="row-hint">{t('Le texte choisi part chez OpenAI uniquement quand vous lancez une action. Sol n’envoie ni mots de passe ni, sauf classement activé ci-dessous, historique et favoris ; il garde les jetons chiffrés sur cet ordinateur. Avec Plus, la limite de 5 heures est partagée avec vos autres applications ChatGPT.','The chosen text goes to OpenAI only when you start an action. Sol never sends passwords, nor history and bookmarks unless sorting is turned on below, and keeps tokens encrypted on this computer. With Plus, the 5-hour limit is shared with your other ChatGPT apps.')}</p>}
    {!ai.isPrivate && <button className="setting-row" role="switch" aria-checked={!!ai.autoApproveActions} onClick={()=>send({type:'settings.set',id:'aiAutoApproveActions',index:ai.autoApproveActions?0:1})}>
        <span className="setting-text"><span className="row-title">{t('Toujours autoriser les actions de Sol AI','Always allow Sol AI actions')}</span>
          <span className="row-sub">{t('Sol exécute vos demandes sur tous les sites sans redemander votre accord. Inclut les diagnostics demandés : erreurs console et détails réseau envoyés à ChatGPT, avec masquage des secrets connus. Des données privées peuvent subsister. Ce choix reste dans ce profil et peut être désactivé ici.','Sol carries out your requests across sites without asking again. Includes requested diagnostics: console errors and network details sent to ChatGPT with known secrets masked. Private data may remain. Saved in this profile; turn it off here.')}</span></span>
        <span className={`switch${ai.autoApproveActions?' on':''}`} aria-hidden="true"/>
      </button>}
    {ai.ready && <>
      <button className="setting-row" role="switch" aria-checked={ai.categories} onClick={()=>send({type:'settings.set',id:'aiCategories',index:ai.categories?0:1})}>
        <span className="setting-text"><span className="row-title">{t('Classer mes favoris et mon historique','Sort my bookmarks and history')}</span>
          <span className="row-sub">{t('Sol AI range vos sites par catégorie sur la page d’accueil. Une grande bibliothèque est classée par lots sur quelques jours, puis seuls les nouveaux sites sont envoyés. Seuls le domaine et un titre partent chez OpenAI, jamais les adresses complètes.','Sol AI sorts your sites into categories on the start page. A large library is sorted in batches over a few days, then only new sites are sent. Only the domain and a title go to OpenAI, never full addresses.')}</span></span>
        <span className={`switch${ai.categories?' on':''}`} aria-hidden="true"/>
      </button>
      {ai.categories && <div className="ai-toolbar"><span className="row-hint" role="status">{ai.categorizing ? <ThinkingShimmer>{t(`Classement en cours… ${ai.categoriesClassified.toLocaleString('fr')} / ${(ai.categoriesClassified+ai.categoriesPending).toLocaleString('fr')} sites`,`Sorting… ${ai.categoriesClassified.toLocaleString('en')} / ${(ai.categoriesClassified+ai.categoriesPending).toLocaleString('en')} sites`)}</ThinkingShimmer>
          : ai.categoriesError ? (ai.categoriesError==='categories-empty' ? t('Aucune catégorie n’a pu être établie.','No categories could be made.') : aiError(ai.categoriesError))
          : ai.categoriesPending > 0 ? t(`${ai.categoriesClassified.toLocaleString('fr')} sites classés, ${ai.categoriesPending.toLocaleString('fr')} restants. ${ai.categoriesBudget > 0 ? 'La suite se fait dans l’heure.' : 'La suite reprend demain.'}`,
              `${ai.categoriesClassified.toLocaleString('en')} sites sorted, ${ai.categoriesPending.toLocaleString('en')} left. ${ai.categoriesBudget > 0 ? 'More within the hour.' : 'Resumes tomorrow.'}`)
          : ai.categoriesUpdated ? t(`${ai.categoriesClassified.toLocaleString('fr')} sites classés. Dernier lot : ${new Date(ai.categoriesUpdated).toLocaleString('fr')}`,`${ai.categoriesClassified.toLocaleString('en')} sites sorted. Last batch: ${new Date(ai.categoriesUpdated).toLocaleString('en')}`) : t('Premier classement en préparation.','First sort pending.')}</span>
        <button className="pill-action" disabled={ai.categorizing} onClick={()=>send({type:'ai.categorize'})}>{t('Classer maintenant','Sort now')}</button></div>}
      <div className="theme-row"><span className="row-title">{t('Modèle','Model')}</span>
        {ai.models.length ? <SolSelect id="ai-model" label={t('Modèle ChatGPT de Sol AI','Sol AI ChatGPT model')} value={Math.max(0, ai.models.findIndex(m=>m.slug===ai.model)+1)}
            options={[t('Automatique (recommandé)','Automatic (recommended)'), ...ai.models.map(m=>m.name)]} onChange={index=>send({type:'settings.set',id:'aiModel',index})}/>
          : <span className="row-hint">{t('Chargement des modèles de votre compte…','Loading your account’s models…')}</span>}
        <p className="row-hint">{t('Les modèles proposés dépendent de votre abonnement ChatGPT. « Automatique » utilise le modèle recommandé par OpenAI pour votre compte.','Available models depend on your ChatGPT subscription. “Automatic” uses the model OpenAI recommends for your account.')}</p></div>
      <div className="theme-row"><span className="row-title">{t('Ton','Tone')}</span><SolSelect id="ai-tone" label={t('Ton de Sol AI','Sol AI tone')} value={ai.tone} options={[t('Naturel','Natural'),t('Amical','Friendly'),t('Professionnel','Professional')]} onChange={index=>send({type:'settings.set',id:'aiTone',index})}/></div>
      <div className="theme-row"><span className="row-title">{t('Réponses','Responses')}</span><SolSelect id="ai-length" label={t('Longueur des réponses','Response length')} value={ai.length} options={[t('Équilibrées','Balanced'),t('Courtes','Short'),t('Détaillées','Detailed')]} onChange={index=>send({type:'settings.set',id:'aiLength',index})}/></div>
    </>}
  </div>;
}
