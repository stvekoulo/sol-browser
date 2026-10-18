import {send} from './bridge';
import {t} from './i18n';
import {Button} from '@/components/motion/button/base';
export type Update = {version:string;percent:number;ready:boolean};
export default function DockUpdate({update}:{update:Update}){return (
    <div className="dock-update">
      <span className="row-title">{update.ready ? t(`Sol ${update.version} est prêt`, `Sol ${update.version} is ready`) : t(`Téléchargement de Sol ${update.version}`, `Downloading Sol ${update.version}`)}</span>
      {update.ready ? (
        <>
          <span className="row-sub">{t('Redémarrez Sol pour installer la mise à jour. Vos onglets seront rouverts.', 'Restart Sol to install the update. Your tabs will reopen.')}</span>
          <div className="dock-update-actions">
            <Button variant="ghost" size="sm" className="pill-action" onClick={() => send({ type: 'update.later' })}>{t('Plus tard', 'Later')}</Button>
            <Button variant="ghost" size="sm" className="pill-action primary" onClick={() => send({ type: 'update.restart' })}>{t('Redémarrer', 'Restart')}</Button>
          </div>
        </>
      ) : (
        <>
          <span className="row-sub">{update.percent} %</span>
          <span className="dock-update-bar"><span style={{ width: `${update.percent}%` }} /></span>
        </>
      )}
    </div>
);}
