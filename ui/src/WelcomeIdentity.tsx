import logo from './assets/logo.png';
import { t } from './i18n';

type Props = { step: number; first: boolean; cloud: boolean; name: string; theme: number; engine: string };
type Mood = 'space' | 'data' | 'marks' | 'style' | 'habits' | 'ready';

// Panneau gauche de l'accueil, à la manière d'Arc : une phrase par étape, et une fenêtre Sol miniature dont le
// contenu montre ce que l'étape change.
export function WelcomeIdentity({ step, first, cloud, name, theme, engine }: Props) {
  const all: { mood: Mood; label: string; line: string; sub: string }[] = [
    { mood: 'space', label: t('Votre espace', 'Your space'), line: t('Un navigateur qui vous connaît.', 'A browser that knows you.'), sub: t('Votre nom et votre langue, dès la première page.', 'Your name and language, from the very first page.') },
    ...(cloud ? [{ mood: 'data' as const, label: t('Vos données', 'Your data'), line: t('Vos données vous suivent.', 'Your data follows you.'), sub: t('Favoris, mots de passe, extensions : chiffrés avant de quitter ce PC.', 'Bookmarks, passwords, extensions: encrypted before they leave this PC.') }] : []),
    { mood: 'marks', label: t('Vos repères', 'Your essentials'), line: t('Reprenez là où vous en étiez.', 'Pick up where you left off.'), sub: t('Favoris et historique arrivent en un clic.', 'Bookmarks and history arrive in one click.') },
    { mood: 'style', label: t('Votre style', 'Your style'), line: t('Un espace à votre image.', 'A space that looks like you.'), sub: t('Clair, sombre ou comme Windows.', 'Light, dark or like Windows.') },
    { mood: 'habits', label: t('Vos habitudes', 'Your habits'), line: t('Cherchez comme vous aimez.', 'Search the way you like.'), sub: t('Votre moteur, et la vidéo qui vous suit d’onglet en onglet.', 'Your engine, and video that follows you from tab to tab.') },
    { mood: 'ready', label: t('À vous de jouer', 'Make it yours'), line: t('Tout est prêt.', 'All set.'), sub: t('Bienvenue dans Sol.', 'Welcome to Sol.') },
  ];
  // Rouvert pour l'import, l'accueil n'a qu'une étape : celle des repères.
  const steps = first ? all : all.filter(s => s.mood === 'marks');
  const at = Math.min(step, steps.length - 1), current = steps[at];
  const bright = theme === 1 || (theme === 3 && !matchMedia('(prefers-color-scheme: dark)').matches);
  const hello = name ? t(`Bonjour ${name}`, `Hello ${name}`) : t('Bonjour', 'Hello');
  return <aside className="welcome-identity" aria-label={t('Étapes de configuration', 'Setup steps')}>
    <div className="welcome-wordmark"><img src={logo} alt="" draggable={false} /><span>sol</span></div>
    <div key={current.mood} className="welcome-scene">
      <h2>{current.line}</h2>
      <p>{current.sub}</p>
      {current.mood === 'ready' ? <figure className="welcome-pass" aria-hidden>
        <div className="welcome-pass-art"><i /><i /></div>
        <strong>{name || 'Sol'}</strong>
        <small>{t('Membre depuis le ', 'Member since ')}{new Date().toLocaleDateString(t('fr-FR', 'en-GB'), { day: 'numeric', month: 'long', year: 'numeric' })}</small>
        <footer><span>SOL</span><span>WINDOWS</span></footer>
      </figure> : <figure className={`welcome-preview${bright ? ' bright' : ''}`} aria-hidden>
        <div className="welcome-preview-bar"><i /><i /><i /><span>{current.mood === 'habits' && engine ? t(`Rechercher avec ${engine}`, `Search with ${engine}`) : t('Rechercher ou saisir une adresse', 'Search or enter an address')}</span></div>
        <Scene mood={current.mood} hello={hello} bright={bright} engine={engine} />
      </figure>}
    </div>
    <div className="welcome-progress">
      <span>{first ? `0${at + 1} / 0${steps.length}` : t('IMPORTATION', 'IMPORT')}</span>
      <strong>{current.label}</strong>
      <ol aria-label={t('Progression', 'Progress')}>{steps.map((s, i) => <li key={s.mood} aria-current={i === at ? 'step' : undefined} className={i <= at ? 'on' : ''} title={s.label} />)}</ol>
    </div>
  </aside>;
}

// Contenu de la fenêtre miniature, propre à chaque étape.
function Scene({ mood, hello, bright, engine }: { mood: Mood; hello: string; bright: boolean; engine: string }) {
  if (mood === 'data') return <ul className="welcome-preview-list">
    {[t('Favoris', 'Bookmarks'), t('Historique', 'History'), t('Mots de passe', 'Passwords'), t('Extensions', 'Extensions')].map(item => <li key={item}><b>✓</b>{item}</li>)}
    <li className="welcome-preview-lock">{t('Chiffré sur ce PC · AES-256', 'Encrypted on this PC · AES-256')}</li>
  </ul>;
  if (mood === 'marks') return <>
    <div className="welcome-preview-marks"><i /><i /><i /><i /></div>
    <ul className="welcome-preview-list rows">{[t('Favoris importés', 'Imported bookmarks'), t('Historique de navigation', 'Browsing history'), t('Mots de passe enregistrés', 'Saved passwords')].map(item => <li key={item}><i />{item}</li>)}</ul>
  </>;
  if (mood === 'style') return <div className="welcome-preview-page">
    <strong className="welcome-preview-aa">Aa</strong>
    <small>{bright ? t('Thème clair', 'Light theme') : t('Thème sombre', 'Dark theme')}</small>
  </div>;
  if (mood === 'habits') return <div className="welcome-preview-page">
    <span className="welcome-preview-search">{engine || 'Google'}</span>
    <span className="welcome-preview-pip"><i /></span>
  </div>;
  return <div className="welcome-preview-page">
    <img src={logo} alt="" draggable={false} />
    <strong>{hello}</strong>
    <small>{new Date().toLocaleDateString(t('fr-FR', 'en-GB'), { weekday: 'long', day: 'numeric', month: 'long' })}</small>
  </div>;
}
