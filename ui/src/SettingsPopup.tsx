import {Button} from '@/components/motion/button/base';
import SolSelect from './SolSelect';
import SolTabs from './SolTabs';
import SettingToggle from './SettingToggle';
import {useState,useRef,useEffect,type RefObject} from 'react';
import {send,formatDomain} from './bridge';
import {t} from './i18n';
import {Icon} from './Icons';
import type {Content,Item,Profile,Setting,Wallpaper} from './PopupTypes';
import {fontLabels} from './bridge';
import {HomeField,SiteData,WallpaperSettings,TintSettings} from './SettingsFields';
import {RangeSlider} from '@/components/motion/range-slider';
import SolAiAccount from './SolAiAccount';
import './settings-scale.css';
import FanSettings from './FanSettings';
export default function SettingsPopup({content,rootRef,settingsSection,setSettingsSection,opening}:{content:Extract<Content,{popup:'settings'}>;rootRef:RefObject<HTMLDivElement|null>;settingsSection:string;setSettingsSection:(section:string)=>void;opening:number}){
const root=rootRef;
    return (
      <div key={opening} ref={root} className="popup settings-popup arriving">
        <div className="popup-header">
          <span className="popup-header-title">{t('Paramètres', 'Settings')}</span>
<Button variant="ghost" size="sm" pressScale={0.97} className="settings-close" aria-label={t('Fermer','Close')} onClick={()=>send({type:'popup.close'})}><Icon d="M4 4l8 8M12 4l-8 8" size={14}/></Button>
        </div>
<p className="settings-intro">{t('Les modifications sont enregistrées automatiquement.', 'Changes are saved automatically.')}</p>
<SolTabs className="settings-nav" value={settingsSection} change={setSettingsSection} label={t('Sections des paramètres','Settings sections')} items={[["general",t('Général','General')],["appearance",t('Apparence','Appearance')],["privacy",t('Sécurité','Security')],["data",t('Données','Data')],["about","Sol"]].map(([value,label])=>({value,label}))}/>
<div className="theme-row shortcuts-settings" hidden={settingsSection !== 'general'}>
  <div className="shortcuts-heading"><span className="row-title">{t('Commandes et raccourcis','Commands and shortcuts')}</span>
    <Button variant="ghost" size="sm" pressScale={0.97} className="pill-action" onClick={()=>send({type:'palette.open'})}>{t('Ouvrir les commandes','Open commands')}</Button></div>
  <p className="row-hint">{t('Accédez aux actions de Sol depuis le clavier. La palette s’ouvre avec Ctrl+Maj+P.', 'Access Sol actions from the keyboard. Open the palette with Ctrl+Shift+P.')}</p>
  <details><summary>{t('Voir les raccourcis clavier','View keyboard shortcuts')}</summary>
    <div className="shortcuts-list">{content.shortcuts.map(s=><div key={s.action} className="shortcut-setting"><span>{s.title}</span><kbd>{s.keys}</kbd></div>)}</div>
  </details>
</div>
<div className="theme-row" hidden={settingsSection !== 'general'}>
  <span className="row-title">{t('Qualité vidéo par défaut','Default video quality')}</span>
  <SolSelect id="video-quality" label={t('Qualité vidéo par défaut','Default video quality')} value={[0,480,720,1080,1440,2160,4320].indexOf(content.videoQuality)}
    options={[t('Meilleure disponible','Best available'),'480p','720p','1080p','1440p','4K · 2160p','8K · 4320p']}
    onChange={index=>send({type:'settings.set',id:'videoQuality',index:[0,480,720,1080,1440,2160,4320][index]})}/>
  <p className="row-hint">{t('Si elle manque : meilleure qualité inférieure, sinon la plus basse disponible. Vous pouvez changer ce choix avant de télécharger.','If unavailable: best lower quality, otherwise the lowest available. You can change this before downloading.')}</p>
</div>
<div className="theme-row settings-scale" hidden={settingsSection !== 'appearance'}>
<div className="settings-scale-heading"><span className="row-title">{t('Taille de l’interface', 'Interface size')}</span>
<Button variant="ghost" size="sm" pressScale={0.97} className="pill-action" aria-label={t('Réinitialiser la taille de l’interface à 100 %','Reset interface size to 100%')} onClick={()=>send({type:'settings.set',id:'uiScale',index:0})}>{Math.round(content.uiScale*100)} %</Button></div>
<span className="row-sub">{t('Barres, boutons et menus. Le zoom des sites reste indépendant.', 'Bars, buttons and menus. Website zoom stays independent.')}</span>
<RangeSlider aria-label={t('Taille de l’interface','Interface size')} min={80} max={100} step={5} value={Math.round(content.uiScale*100)} formatValueText={value=>`${value} %`} onValueChange={value=>send({type:'settings.set',id:'uiScale',index:[1,.9,.8,.95,.85].indexOf(value/100)})}/>
</div>
        <div className="theme-row settings-scale" hidden={settingsSection !== 'general'}>
          <div className="settings-scale-heading"><span className="row-title">{t('Zoom des pages', 'Page zoom')}</span>
            <Button variant="ghost" size="sm" pressScale={0.97} className="pill-action" aria-label={t('Revenir à 100 %', 'Reset to 100%')} title={t('Revenir à 100 %', 'Reset to 100%')} onClick={() => send({ type: 'zoom.reset' })}>{content.zoom} %</Button>
          </div>
          <span className="setting-text">
            <span className="row-sub">{t('Conservé pour toutes les pages de ce profil. Ctrl +, Ctrl − ou Ctrl avec la molette.', 'Saved for all pages in this profile. Ctrl +, Ctrl − or Ctrl with the mouse wheel.')}</span>
          </span>
          <RangeSlider aria-label={t('Zoom des pages','Page zoom')} min={50} max={200} step={5} value={content.zoom} formatValueText={value=>`${value} %`} onValueChange={value=>send({type:'zoom.set',index:value})}/>
        </div>
        <div className="theme-row" hidden={settingsSection !== 'appearance'}>
          <span className="row-title">{t('Thème', 'Theme')}</span>
          <div className="popup-segment" role="radiogroup" aria-label={t('Thème', 'Theme')}>
            {[t('Par défaut', 'Default'), t('Clair', 'Light'), t('Sombre', 'Dark'), t('Système', 'System')].map((label, index) => (
              <Button variant="ghost" size="sm" pressScale={0.97}
                key={label}
                role="radio"
                aria-checked={content.theme === index}
                className={`popup-seg-btn${content.theme === index ? ' active' : ''}`}
                title={index === 0 ? t("L'interface suit la page visitée", 'The interface follows the visited page') : index === 3 ? t('Suit le réglage clair ou sombre de Windows', 'Follows the Windows light or dark setting') : undefined}
                onClick={() => send({ type: 'settings.set', id: 'theme', index })}
              >
                {label}
              </Button>
            ))}
          </div>
        </div>
        <div className="theme-row" hidden={settingsSection !== 'general'}>
          <span className="row-title">{t('Langue', 'Language')}</span>
          <div className="popup-segment" role="radiogroup" aria-label={t('Langue', 'Language')}>
            {[t('Système', 'System'), 'Français', 'English'].map((label, index) => (
              <Button variant="ghost" size="sm" pressScale={0.97}
                key={label}
                role="radio"
                aria-checked={content.language === index}
                className={`popup-seg-btn${content.language === index ? ' active' : ''}`}
                title={index === 0 ? t('Suit la langue de Windows ; en anglais si elle est absente', 'Follows the Windows language; English if it is not available') : undefined}
                onClick={() => send({ type: 'settings.set', id: 'language', index })}
              >
                {label}
              </Button>
            ))}
          </div>
        </div>
        <div className="theme-row" hidden={settingsSection !== 'general'}>
          <span className="row-title">{t('Orthographe', 'Spelling')}</span>
          <div className="popup-segment" role="radiogroup" aria-label={t('Orthographe', 'Spelling')}>
            {[t('Désactivée', 'Off'), 'Français', 'English', t('Les deux', 'Both')].map((label, index) =>
              <Button variant="ghost" size="sm" pressScale={0.97} key={label} role="radio" aria-checked={content.spelling === index} className={`popup-seg-btn${content.spelling === index ? ' active' : ''}`}
                onClick={() => send({type:'settings.set', id:'spelling', index})}>{label}</Button>)}
          </div>
          <p className="row-hint">{t('Correction locale · dictionnaires Windows : ', 'Local correction · Windows dictionaries: ')}{content.spellingLanguages.map(l => l.startsWith('fr') ? 'Français' : 'English').join(', ') || t('aucun installé', 'none installed')}</p>
          {['fr', 'en'].some(prefix => !content.spellingLanguages.some(l => l.startsWith(prefix))) &&
            <p className="row-hint">{t('Une langue absente ne peut pas être corrigée. Ajoutez son option de saisie dans Windows, puis redémarrez Sol.', 'A missing language cannot be checked. Add its typing feature in Windows, then restart Sol.')}</p>}
          <Button variant="ghost" size="sm" pressScale={0.97} className="pill-action" onClick={() => send({type:'spelling.languages'})}>{t('Dictionnaires Windows', 'Windows dictionaries')} ↗</Button>
        </div>
<div className="theme-row" hidden={settingsSection!=='appearance'}><span className="row-title">{t('Police','Font')}</span><SolSelect id="font" label={t('Police','Font')} value={content.font} options={fontLabels(t('Défaut','Default'),t('Système','System'))} onChange={index=>send({type:'settings.set',id:'font',index})}/></div>
<div className="theme-row" hidden={settingsSection!=='general'}><span className="row-title">{t('Moteur de recherche','Search engine')}</span><SolSelect id="engine" label={t('Moteur de recherche','Search engine')} value={content.engine} options={content.engines} onChange={index=>send({type:'settings.set',id:'engine',index})}/></div>
        {settingsSection === 'general' && <FanSettings enabled={content.fanEnabled} trigger={content.fanTrigger} />}
        {settingsSection === 'general' && <HomeField key={content.home} home={content.home} />}
        {settingsSection === 'appearance' && <TintSettings items={content.tint} />}
        {settingsSection === 'appearance' && content.wallpaperOn && <WallpaperSettings custom={content.wallpaperCustom} photo={content.wallpaper} daily={content.wallpaperDaily} photos={content.wallpapers} />}
        <div className="popup-list">
          {content.items.filter(s => (['startWallpaper','scrollbars','blur'].includes(s.key) ? 'appearance' : ['onlineSuggestions','passwords'].includes(s.key) ? 'privacy' : 'general') === settingsSection).map(s => (
<SettingToggle key={s.key} setting={s}/>
          ))}
          <div className="setting-row static" hidden={settingsSection !== 'data'}>
            <span className="setting-text">
              <span className="row-title">{t('Importer d’un autre navigateur', 'Import from another browser')}</span>
              <span className="row-sub">{t('Favoris, historique et mots de passe de Chrome, Edge, Brave, Comet… ou mots de passe d’un fichier CSV exporté.', 'Bookmarks, history and passwords from Chrome, Edge, Brave, Comet… or passwords from an exported CSV file.')}</span>
            </span>
            <span className="row-actions">
              <Button variant="ghost" size="sm" pressScale={0.97} className="pill-action" onClick={() => send({ type: 'welcome.open' })}>{t('Importer', 'Import')}</Button>
              <Button variant="ghost" size="sm" pressScale={0.97} className="pill-action" onClick={() => send({ type: 'import.csv', id: 'settings' })}>{t('Fichier CSV', 'CSV file')}</Button>
            </span>
          </div>
          <div className="setting-row static" hidden={settingsSection !== 'general'}>
            <span className="setting-text">
              <span className="row-title">{t('Extensions', 'Extensions')}</span>
              <span className="row-sub">{t('Ajouter celles de vos autres navigateurs (Mots de passe iCloud, par exemple) ou un dossier d’extension.', 'Add the ones from your other browsers (iCloud Passwords, for example) or an extension folder.')}</span>
            </span>
            <Button variant="ghost" size="sm" pressScale={0.97} className="pill-action" onClick={() => send({ type: 'extensions.open' })}>{t('Gérer', 'Manage')}</Button>
          </div>
          <div hidden={settingsSection !== 'about'}><SolAiAccount ai={content.ai}/></div>
          <div className="setting-row static" hidden={settingsSection !== 'about'}>
            <span className="setting-text">
              <span className="row-title">Sol {content.version}</span>
              <span className="row-sub">
                {!content.update
                  ? content.check === 'checking' ? t('Recherche d’une mise à jour…', 'Checking for updates…')
                    : content.check === 'current' ? t('Sol est à jour.', 'Sol is up to date.')
                    : content.check === 'failed' ? t('La vérification a échoué. Vérifiez votre connexion et réessayez.', 'The check failed. Check your connection and try again.')
                    : content.check === 'portable' ? t('Cette copie n’est pas installée : elle ne se met pas à jour toute seule.', 'This copy is not installed: it does not update itself.')
                    : content.check === 'store' ? t('Les mises à jour sont gérées par le Microsoft Store.', 'Updates are managed by the Microsoft Store.')
                    : t('Les mises à jour se téléchargent automatiquement.', 'Updates are downloaded automatically.')
                  : content.update.ready
                    ? t(`La version ${content.update.version} est prête. Redémarrez Sol pour l’installer.`, `Version ${content.update.version} is ready. Restart Sol to install it.`)
                    : t(`Téléchargement de la version ${content.update.version} : ${content.update.percent} %`, `Downloading version ${content.update.version}: ${content.update.percent}%`)}
              </span>
            </span>
            {content.update?.ready && <Button variant="ghost" size="sm" pressScale={0.97} className="pill-action primary" onClick={() => send({ type: 'update.restart' })}>{t('Redémarrer', 'Restart')}</Button>}
            {!content.update && content.check !== 'portable' && content.check !== 'store' && (
              <Button variant="ghost" size="sm" pressScale={0.97} className="pill-action" disabled={content.check === 'checking'} onClick={() => send({ type: 'update.check' })}>{t('Vérifier', 'Check')}</Button>
            )}
          </div>
          <div className="setting-row static" hidden={settingsSection !== 'data'}>
            <span className="setting-text">
              <span className="row-title">{t('Sauvegarde', 'Backup')}</span>
              <span className="row-sub">{t('Réglages, onglets, favoris, historique, notes et mots de passe dans un fichier chiffré par une clé que vous choisissez.', 'Settings, tabs, bookmarks, history, notes and passwords in a file encrypted with a key you choose.')}</span>
            </span>
            <span className="row-actions">
              <Button variant="ghost" size="sm" pressScale={0.97} className="pill-action" onClick={() => send({ type: 'backup.export' })}>{t('Exporter', 'Export')}</Button>
              <Button variant="ghost" size="sm" pressScale={0.97} className="pill-action" onClick={() => send({ type: 'backup.import' })}>{t('Restaurer', 'Restore')}</Button>
            </span>
          </div>
          <div className="setting-row static" hidden={settingsSection !== 'privacy'}>
            <span className="setting-text">
              <span className="row-title">{t('Accès accordés aux sites', 'Site permissions')}</span>
              <span className="row-sub">
                {content.permissions === 0
                  ? t('Aucune réponse retenue : chaque site demandera.', 'No saved answer: every site will ask.')
                  : t(
                      `${content.permissions} réponse${content.permissions > 1 ? 's' : ''} retenue${content.permissions > 1 ? 's' : ''} (micro, caméra, position, notifications…)`,
                      `${content.permissions} saved answer${content.permissions > 1 ? 's' : ''} (microphone, camera, location, notifications…)`,
                    )}
              </span>
            </span>
            <Button variant="ghost" size="sm" pressScale={0.97} className="pill-action" disabled={content.permissions === 0} onClick={() => send({ type: 'permissions.clear' })}>{t('Tout oublier', 'Forget all')}</Button>
          </div>
          {settingsSection === 'privacy' && <SiteData status={content.siteData} key={opening} />}
          {settingsSection === 'privacy' && content.sites.length > 0 && (
            <div className="setting-sub">
              {content.sites.map(site => (
                <div key={site.origin} className="setting-row static">
                  <span className="setting-text" title={site.origin}>
                    <span className="row-title">{site.host}</span>
                    <span className="row-sub">{site.summary}</span>
                  </span>
                  <Button variant="ghost" size="sm" pressScale={0.97} className="pill-action" aria-label={t(`Oublier les réponses données à ${site.host}`, `Forget the answers given to ${site.host}`)} onClick={() => send({ type: 'permissions.forget', id: site.origin })}>
                    {t('Oublier', 'Forget')}
                  </Button>
                </div>
              ))}
            </div>
          )}
        </div>
      </div>
    );


}
