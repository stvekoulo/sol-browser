import { t } from './i18n';
import { createdOf,type Note } from './notesModel';
export default function NotesStats({items}:{items:Note[]}){
  const active=items.filter(n=>!n.deleted),days=Array.from({length:84},(_,i)=>{const date=new Date();date.setDate(date.getDate()-83+i);return date});
  const count=(date:Date)=>active.filter(n=>new Date(createdOf(n)).toDateString()===date.toDateString()).length;
  const tags=[...new Set(active.flatMap(n=>n.tags))].map(tag=>({tag,count:active.filter(n=>n.tags.includes(tag)).length})).sort((a,b)=>b.count-a.count);
  return <section className="notes-stats"><h2>{t('Votre carnet','Your notebook')}</h2><div className="note-stat-numbers">{[[t('Notes et idées','Notes and ideas'),active.length],[t('Tâches terminées','Completed tasks'),active.filter(n=>n.kind==='task'&&n.reviewed).length],['Tags',tags.length]].map(([label,total])=><div key={label}><strong>{total}</strong><span>{label}</span></div>)}</div>
    <h3>{t('Activité des 12 dernières semaines','Activity over the last 12 weeks')}</h3><div className="note-heatmap">{days.map(date=><span key={date.toISOString()} title={`${date.toLocaleDateString()} · ${count(date)} notes`} style={{opacity:.15+Math.min(count(date),5)*.17}}/>)}</div>
    <h3>{t('Tags les plus utilisés','Most used tags')}</h3>{tags.slice(0,15).map(({tag,count})=><div className="note-tag-stat" key={tag}><span>#{tag}</span><progress value={count} max={active.length}/><span>{count}</span></div>)}
  </section>;
}
