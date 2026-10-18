import { t } from './i18n';
export const usagePage = 'https://chatgpt.com/settings/usage';

// Messages communs à la vue Sol AI, au panneau de discussion et aux paramètres.
export const aiError = (code:string) => ({
  'subscription_sharing_usage_limit_exceeded': t('La limite d’utilisation de votre forfait ChatGPT est atteinte.','Your ChatGPT plan usage limit has been reached.'),
  'subscription_sharing_usage_unavailable': t('L’utilisation du forfait ChatGPT n’est pas disponible pour ce compte.','ChatGPT plan usage is unavailable for this account.'),
  'plan-not-granted': t('Connecté, mais l’utilisation du forfait n’a pas été autorisée. Reconnectez-vous et acceptez-la.','Connected, but plan usage was not allowed. Sign in again and allow it.'),
  'signed-out': t('Session ChatGPT expirée. Reconnectez-vous dans Paramètres → Sol.','ChatGPT session expired. Sign in again in Settings → Sol.'),
  'access-denied': t('Connexion refusée dans ChatGPT.','Sign-in was declined in ChatGPT.'),
  'signin-timeout': t('La connexion n’a pas été terminée à temps. Réessayez.','Sign-in was not completed in time. Try again.'),
  'revocation-unconfirmed': t('Déconnecté de Sol. La révocation chez OpenAI n’a pas été confirmée : vous pouvez retirer Sol dans les paramètres ChatGPT.','Signed out of Sol. Revocation at OpenAI was not confirmed: you can remove Sol in ChatGPT settings.'),
  'signout-failed': t('Le fichier de connexion n’a pas pu être effacé. Réessayez de vous déconnecter.', 'The sign-in file could not be removed. Try signing out again.'),
  'invalid-id-token': t('L’identité ChatGPT n’a pas pu être vérifiée. Reconnectez-vous.', 'The ChatGPT identity could not be verified. Sign in again.'),
  'network': t('ChatGPT est injoignable. Vérifiez la connexion puis réessayez.','ChatGPT is unreachable. Check your connection and try again.'),
  'incomplete': t('Réponse incomplète. Essayez un passage plus court.','Incomplete response. Try a shorter passage.'),
  'cancelled': t('Réponse annulée.','Response cancelled.'),
  'no-model': t('Aucun modèle n’est disponible pour ce compte.','No model is available for this account.'),
  'changed-text': t('Le texte a changé. Fermez puis relancez Sol AI.','The text changed. Close and reopen Sol AI.'),
  'note-draft': t('Enregistrez ou abandonnez votre brouillon avant de créer une note.','Save or discard your draft before creating a note.'),
  'invalid-request': t('Ajoutez une consigne ou sélectionnez du texte.','Add instructions or select text.'),
} as Record<string,string>)[code] ?? t(`ChatGPT n’a pas pu terminer (${code}). Réessayez.`,`ChatGPT could not finish (${code}). Try again.`);
