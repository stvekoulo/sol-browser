import {useState} from 'react';
import {QRCodeSVG} from 'qrcode.react';
import {Button} from '@/components/motion/button/base';
import {t} from './i18n';
export type LanContent={popup:'lan-preview';items:{name:string;address:string;url:string}[]};
export default function LanPreview({content}:{content:LanContent}){
  const [choice,setChoice]=useState(0),[copied,setCopied]=useState(false);
  const item=content.items[choice];
  return <section className="popup name-form"><h2 className="name-form-title">{t('Tester sur mon téléphone','Preview on my phone')}</h2>
    {item?<>
      <label>{t('Réseau local','Local network')}<select value={choice} onChange={e=>{setChoice(Number(e.target.value));setCopied(false)}}>{content.items.map((a,i)=><option key={a.address} value={i}>{a.name} · {a.address}</option>)}</select></label>
      <div style={{background:'white',padding:16,width:'fit-content',margin:'16px auto',borderRadius:12}}><QRCodeSVG value={item.url} size={208} marginSize={2}/></div>
      <p style={{overflowWrap:'anywhere'}}>{item.url}</p>
      <Button className="pill-action" onClick={async()=>{try{await navigator.clipboard.writeText(item.url);setCopied(true)}catch{setCopied(false)}}}>{copied?t('Copié','Copied'):t('Copier le lien','Copy link')}</Button>
    </>:<p role="status">{t('Aucune adresse réseau locale disponible. Connectez le PC au Wi-Fi ou à Ethernet.','No LAN address available. Connect your PC to Wi-Fi or Ethernet.')}</p>}
    <p className="row-hint">{t('Téléphone et PC sur le même réseau. Le serveur doit écouter sur 0.0.0.0 et son port doit être autorisé par le pare-feu. Sol ne modifie pas votre réseau.','Connect both devices to the same network. Your server must listen on 0.0.0.0 and its port must be allowed by the firewall. Sol does not change your network.')}</p>
  </section>;
}
