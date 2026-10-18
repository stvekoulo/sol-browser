import {Switch} from '@/components/motion/switch';
import {send} from './bridge';
import type {Setting} from './PopupTypes';
export default function SettingToggle({setting}:{setting:Setting}){
  return <label className="setting-row sol-setting" data-setting={setting.key}>
    <span className="setting-text"><span className="row-title">{setting.label}</span>{setting.hint&&<span className="row-sub">{setting.hint}</span>}</span>
    <Switch className="sol-switch" checked={setting.on} ariaLabel={setting.label} onCheckedChange={on=>send({type:'settings.set',id:setting.key,index:on?1:0})}/>
  </label>;
}
