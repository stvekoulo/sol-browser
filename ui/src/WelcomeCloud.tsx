import { useEffect, useState, type FormEvent } from 'react';
import { StatefulButton, type ButtonState } from '@/components/motion/button';
import { Input } from '@/components/motion/input';
import { send } from './bridge';
import { t } from './i18n';
import SolTabs from './SolTabs';
import type { CloudState } from './SettingsCloud';

const errorText = (code: string) => ({
  credentials: t('Adresse ou mot de passe incorrect', 'Wrong email or password'),
  exists: t('Un compte existe déjà avec cette adresse', 'An account already exists for this email'),
  weak: t('Le mot de passe doit contenir au moins 8 caractères', 'The password needs at least 8 characters'),
  network: t('Connexion au service impossible', 'Could not reach the service'),
  key: t('Cette clé n’ouvre pas la sauvegarde', 'This key does not open the backup'),
  'key-rule': t('Au moins 12 caractères, différente du mot de passe du compte', 'At least 12 characters, different from the account password'),
} as Record<string, string>)[code] ?? t('Le service a refusé la demande, réessayez', 'The service rejected the request, try again');

// Étape de l'accueil : compte Sol. Une sauvegarde trouvée est restaurée en entier, sinon la sauvegarde automatique est proposée.
export function WelcomeCloud({ cloud }: { cloud: CloudState }) {
  const [mode, setMode] = useState<'in' | 'up'>('in');
  const [email, setEmail] = useState(''), [password, setPassword] = useState(''), [key, setKey] = useState('');
  useEffect(() => { if (cloud.email) setPassword(''); }, [cloud.email]);
  const latest = cloud.backups[0];
  // Le bouton raconte l'action en cours : envoi, réussite (compte prêt, restauration lancée) ou échec.
  const state: ButtonState = cloud.busy ? 'loading' : cloud.error ? 'error' : cloud.notice === 'restored' ? 'success' : 'idle';
  const submit = (e: FormEvent) => {
    e.preventDefault();
    if (cloud.busy) return;
    if (!cloud.email) { if (email.trim() && password) send({ type: 'cloud.sign', id: mode, query: email, input: password }); }
    else if (key) send(latest ? { type: 'cloud.restore.key', id: latest.id, title: key } : { type: 'cloud.auto.key', title: key });
  };
  const status = cloud.notice === 'restored' ? t('Restauration terminée : Sol redémarre avec toutes vos données.', 'Restore complete: Sol restarts with all your data.') : null;
  return <>
    <h1>{t('Retrouvez vos données', 'Get your data back')}</h1>
    <p className="welcome-lead">{cloud.email
      ? latest ? t(`Sauvegarde du ${latest.date} trouvée (${latest.device}). Entrez sa clé : Sol restaure tout et redémarre.`, `Backup from ${latest.date} found (${latest.device}). Enter its key: Sol restores everything and restarts.`)
        : cloud.auto ? t('Sauvegarde automatique activée. Continuez la configuration.', 'Automatic backup is on. Continue setting up.')
        : t('Choisissez une clé de sauvegarde : Sol sauvegardera tout, chiffré sur ce PC, toutes les 24 h.', 'Choose a backup key: Sol will back up everything, encrypted on this PC, every 24 h.')
      : t('Connectez-vous pour restaurer une sauvegarde, ou créez un compte pour sauvegarder ce PC. Vous pouvez aussi passer cette étape.', 'Sign in to restore a backup, or create an account to back up this PC. You can also skip this step.')}</p>
    {!(cloud.email && cloud.auto && !latest) && <form className="welcome-cloud" onSubmit={submit}>
      {!cloud.email && <SolTabs className="welcome-options" label={t('Compte', 'Account')} value={mode} change={v => setMode(v as 'in' | 'up')} items={[
        { value: 'in', label: t('Se connecter', 'Sign in'), ariaLabel: t('Se connecter', 'Sign in') },
        { value: 'up', label: t('Créer un compte', 'Create account'), ariaLabel: t('Créer un compte', 'Create account') },
      ]} />}
      {!cloud.email && <Input className="welcome-field" classNames={{ field: 'welcome-input-shell' }} label={t('Adresse e-mail', 'Email')} type="email" autoComplete="email" spellCheck={false} value={email} onChange={setEmail} />}
      {!cloud.email && <Input className="welcome-field" classNames={{ field: 'welcome-input-shell' }} label={t('Mot de passe du compte', 'Account password')} type="password" autoComplete={mode === 'up' ? 'new-password' : 'current-password'} value={password} onChange={setPassword} />}
      {cloud.email && <Input className="welcome-field" classNames={{ field: 'welcome-input-shell' }} label={t('Clé de sauvegarde', 'Backup key')} type="password" autoComplete="off" value={key} onChange={setKey} />}
      <StatefulButton type="submit" variant="ghost" size="sm" pressScale={0.96} className="pill-action primary welcome-cloud-submit" state={state}
        loadingText={!cloud.email ? t('Connexion…', 'Signing in…') : latest ? t('Restauration…', 'Restoring…') : t('Envoi de la sauvegarde…', 'Sending backup…')}
        successText={t('Sol redémarre…', 'Sol is restarting…')} errorText={t('Réessayer', 'Try again')}>
        {!cloud.email ? (mode === 'up' ? t('Créer le compte', 'Create the account') : t('Se connecter', 'Sign in'))
          : latest ? t('Restaurer et redémarrer', 'Restore and restart') : t('Activer la sauvegarde automatique', 'Turn on automatic backup')}
      </StatefulButton>
    </form>}
    {status && <p className="welcome-lead" role="status">{status}</p>}
    {cloud.error && <p className="welcome-lead" role="alert">{errorText(cloud.error)}</p>}
  </>;
}
