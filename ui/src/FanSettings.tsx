import SettingToggle from './SettingToggle';
import SolSelect from './SolSelect';
import {send} from './bridge';
import {t} from './i18n';

export default function FanSettings({enabled, trigger}: {enabled: boolean; trigger: number}) {
  return <div className="theme-row">
    <SettingToggle setting={{key:'fanEnabled', on:enabled, label:t('Éventail d’actions','Action fan'),
      hint:t('Raccourcis dans les pages web, les notes et le chat.','Shortcuts on web pages, notes and chat.')}}/>
    {enabled && <SolSelect id="fan-trigger" label={t('Afficher l’éventail','Show action fan')} value={trigger}
      options={[t('Au survol immédiatement','Immediately on hover'),t('Après 5 secondes de survol','After hovering for 5 seconds'),t('Au clic droit','On right click')]}
      onChange={index=>send({type:'settings.set',id:'fanTrigger',index})}/>}
  </div>;
}
