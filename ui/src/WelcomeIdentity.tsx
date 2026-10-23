import logo from './assets/logo.png';
import { t } from './i18n';

export function WelcomeIdentity({step}: {step:number}) {
  const labels = [t('Votre espace','Your space'),t('Vos repères','Your essentials'),t('Votre style','Your style'),t('Vos habitudes','Your habits'),t('À vous de jouer','Make it yours')];
  return <aside className="welcome-identity" aria-label={t('Étapes de configuration','Setup steps')}>
    <div className="welcome-wordmark"><img src={logo} alt="" draggable={false}/><span>sol</span><small>{t('UN NOUVEAU DÉPART','A FRESH START')}</small></div>
    <div className="welcome-emblem" aria-hidden><img src={logo} alt="" draggable={false}/></div>
    <div className="welcome-manifesto"><span>{t('MOINS DE BRUIT. PLUS D’ESPACE.','LESS NOISE. MORE SPACE.')}</span><h2>{t('Le web,\nà votre rythme.','The web,\nat your pace.')}</h2></div>
    <ol className="welcome-journey">{labels.map((label,i)=><li key={label} aria-current={i===step?'step':undefined} className={i===step?'current':i<step?'complete':''}><span>{i<step?'✓':`0${i+1}`}</span>{label}<i aria-hidden/></li>)}</ol>
    <p className="welcome-signature">{t('Pensé pour Windows. Dessiné pour vous.','Built for Windows. Designed for you.')}</p>
  </aside>;
}
