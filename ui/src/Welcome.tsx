import { useEffect, useRef, useState, type ReactNode } from 'react';
import { onMessage, send } from './bridge';
import { t, useLang } from './i18n';
import { Icon } from './Icons';
import logo from './assets/logo.png';
import browserLight from './assets/welcome/browser-light.webp';
import browserDark from './assets/welcome/browser-dark.webp';
import libraryLight from './assets/welcome/library-light.webp';
import libraryDark from './assets/welcome/library-dark.webp';
import playerLight from './assets/welcome/player-light.webp';
import playerDark from './assets/welcome/player-dark.webp';
import compareLight from './assets/welcome/compare-light.webp';
import compareDark from './assets/welcome/compare-dark.webp';
import { drawSol } from './solScene';

type Browser = { id: string; name: string; profiles: { folder: string; name: string }[] };
type Setup = {
  first: boolean; name: string; theme: number; font: number; language: number; engine: number; engines: string[];
  blocker: boolean; pip: boolean; browsers: Browser[];
};
type Imported = Record<string, { bookmarks: number; history: number; passwords: number; locked: number } | 'running'>;

const key = (browser: string, folder: string) => `${browser}|${folder}`;

// Accueil de première ouverture, sur toute la fenêtre : se présenter, reprendre ses favoris et son historique d'un
// autre navigateur, choisir l'apparence et les réglages essentiels. Chaque choix est appliqué tout de suite par les
// commandes ordinaires des réglages ; rien n'attend la dernière étape. Rouvert plus tard, il va droit à l'import.
export default function Welcome() {
  useLang();
  const [setup, setSetup] = useState<Setup | null>(null);
  const [step, setStep] = useState(0);
  const [imported, setImported] = useState<Imported>({});
  const [name, setName] = useState('');
  const [csv, setCsv] = useState<number | null>(null);

  useEffect(() => {
    const off = onMessage(data => {
      const message = data as { welcome?: string; id?: string; folder?: string; bookmarks?: number; history?: number; passwords?: number; locked?: number };
      if (message.welcome === 'reset') send({ type: 'welcome.ready' });
      if (message.welcome === 'csv') setCsv(message.passwords ?? 0);
      if (message.welcome === 'state') {
        const next = data as unknown as Setup;
        setSetup(next);
        setName(next.name);
        setImported({});
        setStep(next.first ? 0 : 1); // déjà accueilli : on vient pour l'import
      }
      if (message.welcome === 'imported' && message.id !== undefined)
        setImported(all => ({ ...all, [key(message.id!, message.folder ?? '')]: { bookmarks: message.bookmarks ?? 0, history: message.history ?? 0, passwords: message.passwords ?? 0, locked: message.locked ?? 0 } }));
    });
    send({ type: 'welcome.ready' });
    return off;
  }, []);

  if (!setup) return <main className="welcome" />;

  const set = (id: string, index: number, patch: Partial<Setup>) => {
    setSetup({ ...setup, ...patch });
    send({ type: 'settings.set', id, index });
  };
  const last = setup.first ? 4 : 1;
  const next = () => {
    if (step === 0) send({ type: 'welcome.name', title: name.trim() });
    if (step >= last) send({ type: 'welcome.done' });
    else setStep(step + 1);
  };
  const run = (browser: string, folder: string) => {
    setImported(all => ({ ...all, [key(browser, folder)]: 'running' }));
    send({ type: 'import.run', id: browser, query: folder });
  };

  const steps: ReactNode[] = [
    <>
      <img className="welcome-logo" src={logo} alt="" draggable={false} />
      <h1>{t('Bienvenue dans Sol', 'Welcome to Sol')}</h1>
      <p className="welcome-lead">{t('Configurez Sol en quelques étapes.', 'Set up Sol in a few steps.')}</p>
      <label className="welcome-field">
        <span>{t('Votre nom', 'Your name')}</span>
        <input autoFocus maxLength={40} spellCheck={false} placeholder={t('Facultatif', 'Optional')} value={name} onChange={e => setName(e.target.value)} onKeyDown={e => e.key === 'Enter' && next()} />
      </label>
      <Choice label={t('Langue', 'Language')} options={[t('Système', 'System'), 'Français', 'English']} value={setup.language} onPick={i => set('language', i, { language: i })} />
    </>,

    <>
      <h1>{t('Importer vos données', 'Import your data')}</h1>
      <p className="welcome-lead">{t('Importez les favoris, l’historique et les mots de passe de vos autres navigateurs. Les mots de passe seront disponibles au prochain démarrage de Sol.', 'Import bookmarks, history and passwords from your other browsers. Passwords will be available the next time Sol starts.')}</p>
      <div className="welcome-list">
        {setup.browsers.length === 0 && <p className="welcome-empty">{t('Aucun autre navigateur n’a été trouvé sur cet ordinateur.', 'No other browser was found on this computer.')}</p>}
        {setup.browsers.flatMap(browser =>
          browser.profiles.map(profile => {
            const state = imported[key(browser.id, profile.folder)];
            const many = browser.profiles.length > 1;
            return (
              <div key={key(browser.id, profile.folder)} className="welcome-row">
                <span className="welcome-badge" aria-hidden>{browser.name[0]}</span>
                <span className="welcome-row-text">
                  <span className="welcome-row-title">{browser.name}{many && ` · ${profile.name}`}</span>
                  <span className="welcome-row-sub" aria-live="polite">
                    {state === 'running' ? t('Importation en cours…', 'Importing…')
                      : state ? t(`${state.bookmarks} favori${state.bookmarks > 1 ? 's' : ''}, ${state.history} page${state.history > 1 ? 's' : ''} d’historique, ${state.passwords} mot${state.passwords > 1 ? 's' : ''} de passe`, `${state.bookmarks} bookmark${state.bookmarks === 1 ? '' : 's'}, ${state.history} history page${state.history === 1 ? '' : 's'}, ${state.passwords} password${state.passwords === 1 ? '' : 's'}`)
                        + (state.locked ? t(` (${state.locked} protégé${state.locked > 1 ? 's' : ''} par ce navigateur, non importé${state.locked > 1 ? 's' : ''})`, ` (${state.locked} protected by that browser, not imported)`) : '')
                      : t('Favoris, historique et mots de passe', 'Bookmarks, history and passwords')}
                  </span>
                </span>
                {state && state !== 'running' ? (
                  <span className="welcome-done" aria-hidden><Icon d="M3.5 8.5l3 3 6-6.5" size={14} /></span>
                ) : (
                  <button className="pill-action" disabled={state === 'running'} onClick={() => run(browser.id, profile.folder)}>{t('Importer', 'Import')}</button>
                )}
              </div>
            );
          }),
        )}
      </div>
      <p className="welcome-csv">
        {csv === null
          ? t('Mots de passe protégés par votre navigateur ? Exportez-les depuis ses réglages, puis', 'Passwords protected by your browser? Export them from its settings, then')
          : csv > 0
            ? t(`${csv} mot${csv > 1 ? 's' : ''} de passe importé${csv > 1 ? 's' : ''} du fichier.`, `${csv} password${csv === 1 ? '' : 's'} imported from the file.`)
            : t('Aucun mot de passe trouvé dans ce fichier.', 'No password found in this file.')}{' '}
        <button onClick={() => send({ type: 'import.csv' })}>{csv === null ? t('importez le fichier CSV', 'import the CSV file') : t('Importer un autre fichier', 'Import another file')}</button>
      </p>
    </>,

    <>
      <h1>{t('Apparence', 'Appearance')}</h1>
      <p className="welcome-lead">{t('Choisissez le thème et la police de l’interface.', 'Choose the theme and the interface font.')}</p>
      <Choice label={t('Thème', 'Theme')} options={[t('Par défaut', 'Default'), t('Clair', 'Light'), t('Sombre', 'Dark'), t('Système', 'System')]} value={setup.theme} onPick={i => set('theme', i, { theme: i })} />
      <Choice label={t('Police', 'Font')} grid options={[t('Défaut', 'Default'), t('Système', 'System'), 'Inter', 'Inter Bold', 'SF Pro', 'SF Pro Bold']} value={setup.font} onPick={i => set('font', i, { font: i })} />
    </>,

    <>
      <h1>{t('Réglages', 'Settings')}</h1>
      <p className="welcome-lead">{t('Vous pourrez modifier ces réglages à tout moment.', 'You can change these settings at any time.')}</p>
      <Choice label={t('Moteur de recherche', 'Search engine')} options={setup.engines} value={setup.engine} onPick={i => set('engine', i, { engine: i })} />
      <Toggle title={t('Bloquer les publicités et les traqueurs', 'Block ads and trackers')} sub={t('Avec uBlock Origin Lite', 'With uBlock Origin Lite')} on={setup.blocker} onPick={on => set('blocker', on ? 1 : 0, { blocker: on })} />
      <Toggle title={t('Lecteur flottant', 'Floating player')} sub={t('Garde la vidéo à l’écran lorsque vous changez d’onglet', 'Keeps the video on screen when you switch tabs')} on={setup.pip} onPick={on => set('pip', on ? 1 : 0, { pip: on })} />
    </>,

    <>
      <img className="welcome-logo small" src={logo} alt="" draggable={false} />
      <h1>{t('Sol est prêt', 'Sol is ready')}</h1>
      <p className="welcome-lead">{t('La configuration est terminée. Vous pouvez commencer à naviguer.', 'Setup is complete. You can start browsing.')}</p>
    </>,
  ];

  return (
    <main className="welcome">
      <section className="welcome-card" aria-label={t('Accueil de Sol', 'Sol welcome')}>
        {/* La clé rejoue l'apparition du contenu à chaque étape. */}
        <div key={step} className="welcome-step">{steps[step]}</div>
        <footer className="welcome-foot">
          {setup.first && (
            <div className="welcome-dots" aria-hidden>
              {steps.map((_, i) => <span key={i} className={i === step ? 'on' : i < step ? 'past' : ''} />)}
            </div>
          )}
          <span className="welcome-space" />
          {setup.first && step > 0 && <button className="pill-action" onClick={() => setStep(step - 1)}>{t('Retour', 'Back')}</button>}
          <button className="pill-action primary welcome-next" onClick={next}>
            {step >= last ? (setup.first ? t('Commencer', 'Get started') : t('Terminer', 'Done')) : t('Continuer', 'Continue')}
          </button>
        </footer>
      </section>
      <Preview step={step} />
    </main>
  );
}

// À droite de la carte : l'animation de Sol pour l'accueil, puis une vraie capture du navigateur à chaque étape,
// celle de la fonction dont l'étape parle. Chaque capture existe en clair et en sombre : la feuille de style montre
// celle du thème en cours, donc l'aperçu suit le choix du thème.
const SHOTS: ([string, string, string] | null)[] = [
  null, // étape 0 : l'animation
  [libraryLight, libraryDark, 'library'],   // reprise des favoris et de l'historique
  [browserLight, browserDark, 'browser'],   // apparence
  [playerLight, playerDark, 'player'],      // l'essentiel : le lecteur flottant
  [compareLight, compareDark, 'compare'],   // prêt : les outils pour développeurs
];

const CAPTIONS: Record<string, () => string> = {
  library: () => t('Favoris et historique', 'Bookmarks and history'),
  browser: () => t('Onglets, groupes et raccourcis dans la barre latérale', 'Tabs, groups and shortcuts in the sidebar'),
  player: () => t('Lecteur flottant', 'Floating player'),
  compare: () => t('Aperçu sur téléphone, tablette et ordinateur', 'Preview on phone, tablet and desktop'),
};

function Preview({ step }: { step: number }) {
  return (
    <aside className="preview" aria-hidden>
      <div className="preview-window">
        <SolScene shown={step === 0} />
        {SHOTS.map((shot, index) =>
          shot && (
            <figure key={shot[2]} className={`preview-shot${index === step ? ' on' : ''}`}>
              <img className="light" src={shot[0]} alt="" draggable={false} />
              <img className="dark" src={shot[1]} alt="" draggable={false} />
              <figcaption>{CAPTIONS[shot[2]]()}</figcaption>
            </figure>
          ),
        )}
      </div>
    </aside>
  );
}

// L'animation de Sol, dessinée en direct. Elle ne tourne que tant qu'elle est montrée ; si Windows est réglé sans
// animations, une seule image est dessinée.
function SolScene({ shown }: { shown: boolean }) {
  const canvas = useRef<HTMLCanvasElement>(null);
  useEffect(() => {
    const g = canvas.current?.getContext('2d');
    if (!g || !shown) return;
    const still = matchMedia('(prefers-reduced-motion: reduce)').matches;
    let frame = 0;
    const start = performance.now();
    const tick = (now: number) => {
      drawSol(g, g.canvas.width, g.canvas.height, (now - start) / 1000);
      if (!still) frame = requestAnimationFrame(tick);
    };
    frame = requestAnimationFrame(tick);
    return () => cancelAnimationFrame(frame);
  }, [shown]);
  return <canvas ref={canvas} className={`preview-scene${shown ? ' on' : ''}`} width={1200} height={675} />;
}

function Choice({ label, options, value, onPick, grid = false }: { label: string; options: string[]; value: number; onPick: (index: number) => void; grid?: boolean }) {
  return (
    <div className="welcome-choice">
      <span className="welcome-label">{label}</span>
      <div className={`popup-segment${grid ? ' grid' : ''}`} role="radiogroup" aria-label={label}>
        {options.map((option, index) => (
          <button key={option} role="radio" aria-checked={value === index} className={`popup-seg-btn${value === index ? ' active' : ''}`} onClick={() => onPick(index)}>
            {option}
          </button>
        ))}
      </div>
    </div>
  );
}

function Toggle({ title, sub, on, onPick }: { title: string; sub: string; on: boolean; onPick: (on: boolean) => void }) {
  return (
    <button className="setting-row welcome-toggle" role="switch" aria-checked={on} onClick={() => onPick(!on)}>
      <span className="setting-text">
        <span className="row-title">{title}</span>
        <span className="row-sub">{sub}</span>
      </span>
      <span className={`switch${on ? ' on' : ''}`} aria-hidden />
    </button>
  );
}
