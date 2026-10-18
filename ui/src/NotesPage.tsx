import { useEffect,useState } from 'react';
import { formatDomain,send } from './bridge';
import { t, useLang } from './i18n';
import { Icon } from './Icons';
import NoteEditor from './NoteEditor';
import NotesStats from './NotesStats';
import NotesControls from './NotesControls';
import NotesTags from './NotesTags';
import { useNotes } from './useNotes';
import { inView,matchesNote,titleOf,type NoteView } from './notesModel';
import './notes.css';
import {Button} from '@/components/motion/button/base';
import {Input} from '@/components/motion/input';
import {Card} from '@/components/motion/card';
import {Checkbox} from '@/components/motion/checkbox';
import {SharedLayoutBg} from '@/components/motion/shared-layout-bg';
import NotesCollectionTools from './NotesCollectionTools';
import SolModal from './SolModal';
import NoteSymbol from './NoteSymbol';

export default function NotesPage() {
  useLang();
  const {data,change,meta,edit,flush}=useNotes(),[query,setQuery]=useState(''),[view,setView]=useState<NoteView>('all'),[tag,setTag]=useState('');
  const [layout,setLayout]=useState(()=>localStorage.getItem('sol.notes.layout')||'cards'),[sort,setSort]=useState(()=>localStorage.getItem('sol.notes.sort')||'recent');
  const [showEditor,setShowEditor]=useState(false),[editorKey,setEditorKey]=useState(0),[focus,setFocus]=useState(false),[rail,setRail]=useState(false),[selection,setSelection]=useState<string[]>([]),[selecting,setSelecting]=useState(false),[confirmPurge,setConfirmPurge]=useState(false),[tagEdit,setTagEdit]=useState<string|null>(null),[tagName,setTagName]=useState(''),[hydrated,setHydrated]=useState(false);
  useEffect(()=>{document.title=t('Notes — Sol','Notes — Sol')},[]);
  useEffect(()=>{if(data&&!hydrated){setShowEditor(!!(data.draft.id||data.draft.text||data.draft.files.length));setHydrated(true)}},[data,hydrated]);
  useEffect(()=>localStorage.setItem('sol.notes.layout',layout),[layout]);useEffect(()=>localStorage.setItem('sol.notes.sort',sort),[sort]);
  const [compact,setCompact]=useState(()=>matchMedia('(max-width:800px)').matches),[medium,setMedium]=useState(()=>matchMedia('(max-width:1100px)').matches);
  useEffect(()=>{const small=matchMedia('(max-width:800px)'),middle=matchMedia('(max-width:1100px)'),update=()=>{setCompact(small.matches);setMedium(middle.matches)};small.addEventListener('change',update);middle.addEventListener('change',update);return()=>{small.removeEventListener('change',update);middle.removeEventListener('change',update)}},[]);
  if (!data) return null;
  const navigationInModal=compact||showEditor&&(medium||focus);
  const choose=(id:string)=>{edit(id);setEditorKey(key=>key+1);setShowEditor(true);setFocus(false);setRail(false)};
  const navigate=(next:NoteView)=>{flush();setView(next);setTag('');setSelection([]);setSelecting(false);setShowEditor(false);setRail(false)};
  const items=data.items.filter(n=>inView(n,view)&&matchesNote(n,query,tag));
  if(sort!=='manual')items.sort((a,b)=>Number(b.pinned)-Number(a.pinned)||(sort==='title'?titleOf(a).localeCompare(titleOf(b)):sort==='oldest'?new Date(a.updated).getTime()-new Date(b.updated).getTime():new Date(b.updated).getTime()-new Date(a.updated).getTime()));
  const views:[NoteView,string,string][]=[['all',t('Tout','All'),'◫'],['idea',t('Idées','Ideas'),'✦'],['note',t('Notes','Notes'),'▤'],['task',t('Tâches','Tasks'),'☑'],['pinned',t('Épinglés','Pinned'),'⌖'],['files',t('Fichiers','Files'),'↧'],['review',t('À revoir','Review'),'↻'],['archive',t('Archives','Archive'),'▱'],['trash',t('Corbeille','Trash'),'⌫'],['stats',t('Activité','Activity'),'▦']];
  const action=(query:string,ids=selection)=>{flush();send({type:'notes.action',query,input:JSON.stringify(ids)});setSelection([]);setSelecting(false);setConfirmPurge(false)};
  const move=(from:string,to:string)=>{
    if(!from||from===to)return;const ids=data.items.map(n=>n.id),index=ids.indexOf(from);if(index<0)return;ids.splice(index,1);ids.splice(ids.indexOf(to),0,from);send({type:'notes.order',input:JSON.stringify(ids)});setSort('manual');
  };
  const navigation=<aside className="notes-browser" aria-label={t('Organisation des notes','Note organization')}><header className="notes-browser-heading"><h1>Notes</h1>{data.aiEnabled&&<Button variant="ghost" size="sm" whileHover={undefined} pressScale={0.98} className="pill-action notes-ai" title={t('Demander à Sol AI de chercher, résumer ou créer des notes','Ask Sol AI to search, summarize or create notes')} onClick={()=>send({type:'chat.toggle',id:'notes'})}>Sol AI</Button>}<Button variant="ghost" size="sm" whileHover={undefined} pressScale={0.98} className="notes-tool" aria-label={t('Nouvelle note','New note')} title={t('Nouvelle note','New note')} onClick={()=>choose('')}><Icon d="M10 3l3 3M4 10l6-7 3 3-6 7-4 1zM11 12v2H2V5h3"/></Button></header>
      {data.privateMode&&<p className="notes-private">{t('Privé · mémoire uniquement','Private · memory only')}</p>}
      <nav className="notes-navigation"><SharedLayoutBg inset={0} className="notes-nav-motion" pillClassName="notes-nav-hover">{views.map(([key,label,icon])=><Button variant="ghost" size="sm" whileHover={undefined} pressScale={0.98} key={key} aria-current={view===key&&!tag?'page':undefined} className={view===key&&!tag?'active':''} onClick={()=>navigate(key)}><span aria-hidden><NoteSymbol view={key}/></span><span>{label}</span>{key!=='stats'&&<small>{data.items.filter(n=>inView(n,key)).length}</small>}</Button>)}</SharedLayoutBg></nav>
      <NotesTags items={data.items} selected={tag} choose={name=>{navigate('all');setTag(name)}} rename={name=>{setTagEdit(name);setTagName(name)}} flush={flush}/>
      <NotesControls days={data.archiveDays??0} ids={items.map(n=>n.id)} flush={flush}/>
    </aside>;
  return <main className={`notes-workspace${showEditor?' has-editor':''}${focus?' note-focus':''}${rail?' rail-open':''}`}>
    {!navigationInModal&&navigation}
    <SolModal id={navigationInModal&&rail?"note-navigation":null} title={t("Organisation des notes","Note organization")} close={()=>setRail(false)}>{navigation}</SolModal>
    <section className="notes-collection"><header className="notes-collection-header"><div><Button variant="ghost" size="sm" whileHover={undefined} pressScale={0.98} className="notes-tool notes-rail-toggle" aria-haspopup="dialog" aria-expanded={rail} aria-label={t('Afficher les filtres','Show filters')} onClick={()=>setRail(!rail)}>☷</Button><h2>{tag?'#'+tag:views.find(v=>v[0]===view)?.[1]}</h2><span>{items.length}</span></div><Button variant="ghost" size="sm" className="pill-action" onClick={()=>choose('')}>{t('Nouvelle note','New note')} +</Button></header>
      <NotesCollectionTools {...{query,setQuery,layout,setLayout,sort,setSort,selecting}} toggleSelection={()=>{setSelecting(!selecting);setSelection([])}}/>
      {data.error&&<p className="notes-error" role="alert">{data.error}</p>}
      {selecting&&<div className="notes-batch"><Button variant="ghost" size="sm" className="pill-action" onClick={()=>setSelection(items.map(n=>n.id))}>{t('Tout sélectionner','Select all')}</Button><span>{selection.length}</span>{(view==='trash'?[['restore',t('Restaurer','Restore')]]:[['pin',t('Épingler','Pin')],['archive',t('Archiver','Archive')],['trash',t('Corbeille','Trash')],['kind:idea',t('Idées','Ideas')],['kind:note',t('Notes','Notes')],['kind:task',t('Tâches','Tasks')]]).map(([key,label])=><Button variant="ghost" size="sm" className="pill-action" key={key} disabled={!selection.length} onClick={()=>action(key)}>{label}</Button>)}{view==='trash'&&<Button variant="ghost" size="sm" className="pill-action" disabled={!selection.length} onClick={()=>confirmPurge?action('purge'):setConfirmPurge(true)}>{confirmPurge?t('Confirmer la suppression définitive','Confirm permanent deletion'):t('Supprimer définitivement','Delete permanently')}</Button>}</div>}
      {view==='stats'?<NotesStats items={data.items}/>:<div className={`notes-browser-list note-${showEditor?'list':layout}`}>
        {view==='review'&&items.length>0&&<Button variant="ghost" size="sm" whileHover={undefined} pressScale={0.98} className="pill-action note-random" onClick={()=>choose(items[Math.floor(Math.random()*items.length)].id)}>{t('Redécouvrir une note au hasard','Rediscover a random note')} ↻</Button>}
        {!items.length&&<div className="notes-welcome"><Icon d="M4 2h8v12H4zM6 5h4M6 8h4" size={32}/><h2>{query?t('Aucun résultat','No results'):t('Un espace pour vos idées','A space for your ideas')}</h2><p>{t('Notes, tâches et passages enregistrés, ensemble dans Sol.','Notes, tasks and saved passages, together in Sol.')}</p>{view!=='trash'&&<Button variant="ghost" size="sm" className="pill-action" onClick={()=>choose('')}>{t('Créer une note','Create a note')}</Button>}</div>}
        {items.map(n=><Card interactive selected={showEditor&&data.draft.id===n.id} role="article" size="compact" className={`note-card${showEditor&&data.draft.id===n.id?' selected':''}`} key={n.id} draggable={!selecting&&view!=='trash'} onDragStart={e=>e.dataTransfer.setData('application/sol-note',n.id)} onDragOver={e=>e.preventDefault()} onDrop={e=>{e.preventDefault();move(e.dataTransfer.getData('application/sol-note'),n.id)}} onContextMenu={e=>{e.preventDefault();setSelecting(true);setSelection([n.id])}}>
          {selecting&&<Checkbox className="sol-checkbox note-card-check" aria-label={t(`Sélectionner ${titleOf(n)}`,`Select ${titleOf(n)}`)} checked={selection.includes(n.id)} onCheckedChange={checked=>setSelection(checked?[...selection,n.id]:selection.filter(id=>id!==n.id))}/>}
          <Button variant="ghost" size="sm" whileHover={undefined} pressScale={0.98} className="notes-preview" aria-label={titleOf(n)} onClick={()=>view==='trash'?setSelection([n.id]):selecting?setSelection(selection.includes(n.id)?selection.filter(id=>id!==n.id):[...selection,n.id]):choose(n.id)}><div className="note-card-top"><span><NoteSymbol view={n.kind}/></span>{n.pinned&&<span title={t('Épinglé','Pinned')}>⌖</span>}<time>{new Date(n.updated).toLocaleDateString()}</time></div><strong>{titleOf(n)}</strong><span className="note-card-excerpt">{n.text.replace(/[#*`\[\]]/g,'').slice(0,280)}</span><div className="note-card-tags">{n.tags.map(tag=><small key={tag}>#{tag}</small>)}</div><div className="note-card-bottom">{n.url&&<small>{formatDomain(n.url)}</small>}{n.files.length>0&&<small>↧ {n.files.length}</small>}{n.kind==='task'&&<small className={n.due&&new Date(n.due).getTime()<Date.now()&&!n.reviewed?'overdue':''}>{n.reviewed?t('Terminée','Done'):n.due?new Date(n.due).toLocaleString():t('À faire','To do')}</small>}</div></Button>
          {view==='trash'&&<div className="note-trash-actions"><Button variant="ghost" size="sm" className="pill-action" onClick={()=>action('restore',[n.id])}>{t('Restaurer','Restore')}</Button><Button variant="ghost" size="sm" className="pill-action" onClick={()=>{setSelecting(true);setSelection([n.id]);setConfirmPurge(true)}}>{t('Supprimer','Delete')}</Button></div>}
        </Card>)}
      </div>}
    </section>
    {showEditor&&view!=='trash'&&<NoteEditor key={editorKey} data={data} change={change} meta={meta} edit={choose} close={()=>{setShowEditor(false);setFocus(false)}} focus={focus} setFocus={setFocus} flush={flush} openFilters={()=>setRail(true)}/>}
    <SolModal id={tagEdit===null?null:"tag-rename"} title={t("Modifier le tag","Edit tag")} close={()=>setTagEdit(null)}><form className="note-modal" onSubmit={e=>{e.preventDefault();flush();send({type:'notes.tag',id:tagEdit??'',title:tagName.replace(/^#/,'')});setTagEdit(null)}}><h2>{t('Modifier le tag','Edit tag')}</h2><Input aria-label={t('Nom du tag','Tag name')} value={tagName} maxLength={64} onChange={setTagName}/><div><Button variant="ghost" size="sm" whileHover={undefined} pressScale={0.98} type="button" className="pill-action" onClick={()=>setTagEdit(null)}>{t('Annuler','Cancel')}</Button><Button variant="ghost" size="sm" className="pill-action" type="submit">{tagName?t('Enregistrer','Save'):t('Supprimer le tag','Delete tag')}</Button></div></form></SolModal>
  </main>;
}
