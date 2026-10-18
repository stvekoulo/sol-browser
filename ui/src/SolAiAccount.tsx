import { send } from './bridge';
import { t } from './i18n';
import SolSelect from './SolSelect';
import { aiError, usagePage } from './solAiMessages';
import { ThinkingShimmer } from '@/components/agents/loading-states/thinking-shimmer';

export type SolAiState = { connected:boolean; ready:boolean; email:string; signingIn:boolean; error:string|null; isPrivate:boolean; tone:number; length:number; categories:boolean; categorizing:boolean; categoriesError:string|null; categoriesUpdated:number|null };

// Compte ChatGPT de Sol AI, suivant les règles d’interface d’OpenAI : bouton « Continuer avec ChatGPT », badge « Connecté »,
// mention du forfait utilisé et lien « Gérer l’usage ».
export default function SolAiAccount({ ai }: { ai: SolAiState }) {
  return <div className="ai-settings">
    <div className="setting-row static">
      <span className="setting-text">
        <span className="row-title">Sol AI {ai.connected && <span className="ai-badge">{t('Connecté','Connected')}</span>}</span>
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
    {ai.ready && <>
      <button className="setting-row" role="switch" aria-checked={ai.categories} onClick={()=>send({type:'settings.set',id:'aiCategories',index:ai.categories?0:1})}>
        <span className="setting-text"><span className="row-title">{t('Classer mes favoris et mon historique','Sort my bookmarks and history')}</span>
          <span className="row-sub">{t('Une fois par jour, Sol AI range vos sites par catégorie sur la page d’accueil. Le titre et le domaine de vos favoris et pages visitées sont alors envoyés à OpenAI, jamais les adresses complètes.','Once a day, Sol AI sorts your sites into categories on the start page. The title and domain of your bookmarks and visited pages are sent to OpenAI, never full addresses.')}</span></span>
        <span className={`switch${ai.categories?' on':''}`} aria-hidden="true"/>
      </button>
      {ai.categories && <div className="ai-toolbar"><span className="row-hint" role="status">{ai.categorizing ? <ThinkingShimmer>{t('Classement en cours…','Sorting…')}</ThinkingShimmer>
          : ai.categoriesError ? (ai.categoriesError==='categories-empty' ? t('Aucune catégorie n’a pu être établie.','No categories could be made.') : aiError(ai.categoriesError))
          : ai.categoriesUpdated ? t(`Dernier classement : ${new Date(ai.categoriesUpdated).toLocaleString('fr')}`,`Last sorted: ${new Date(ai.categoriesUpdated).toLocaleString('en')}`) : t('Premier classement en préparation.','First sort pending.')}</span>
        <button className="pill-action" disabled={ai.categorizing} onClick={()=>send({type:'ai.categorize'})}>{t('Classer maintenant','Sort now')}</button></div>}
      <div className="theme-row"><span className="row-title">{t('Ton','Tone')}</span><SolSelect id="ai-tone" label={t('Ton de Sol AI','Sol AI tone')} value={ai.tone} options={[t('Naturel','Natural'),t('Amical','Friendly'),t('Professionnel','Professional')]} onChange={index=>send({type:'settings.set',id:'aiTone',index})}/></div>
      <div className="theme-row"><span className="row-title">{t('Réponses','Responses')}</span><SolSelect id="ai-length" label={t('Longueur des réponses','Response length')} value={ai.length} options={[t('Équilibrées','Balanced'),t('Courtes','Short'),t('Détaillées','Detailed')]} onChange={index=>send({type:'settings.set',id:'aiLength',index})}/></div>
    </>}
  </div>;
}
