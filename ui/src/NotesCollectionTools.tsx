import {Input} from '@/components/motion/input';
import {Button} from '@/components/motion/button/base';
import SolTabs from './SolTabs';
import SolSelect from './SolSelect';
import {Icon} from './Icons';
import {t} from './i18n';
import NoteSymbol from './NoteSymbol';
export default function NotesCollectionTools({query,setQuery,layout,setLayout,sort,setSort,selecting,toggleSelection}:{query:string;setQuery:(v:string)=>void;layout:string;setLayout:(v:string)=>void;sort:string;setSort:(v:string)=>void;selecting:boolean;toggleSelection:()=>void}){
  const sorts=[['recent',t('Récentes','Recent')],['oldest',t('Anciennes','Oldest')],['title',t('Titre','Title')],['manual',t('Ordre manuel','Manual order')]];
  return <div className="notes-collection-tools">
    <Input type="search" className="notes-search sol-note-search" classNames={{field:'sol-note-search-field'}} aria-label={t('Rechercher dans les notes','Search notes')} title="#tag · type:task · has:file · before:2026-12-01" placeholder={t('Rechercher…','Search…')} value={query} onChange={setQuery} leftIcon={<Icon d="M7 11a4 4 0 1 1 0-8 4 4 0 0 1 0 8M10 10l3 3"/>}/>
    <div className="notes-view-tools"><SolTabs value={layout} change={setLayout} label={t('Disposition des notes','Notes layout')} items={[{value:'cards',label:<NoteSymbol view="cards"/>,ariaLabel:t('Vue cartes','Card view')},{value:'list',label:<NoteSymbol view="list"/>,ariaLabel:t('Vue liste','List view')}]}/>
      <SolSelect id="notes-sort" label={t('Trier','Sort')} value={Math.max(0,sorts.findIndex(s=>s[0]===sort))} options={sorts.map(s=>s[1])} onChange={i=>setSort(sorts[i][0])}/>
      <Button variant="ghost" size="icon" className="notes-tool" aria-label={t('Sélection multiple','Select multiple')} aria-pressed={selecting} onClick={toggleSelection}>☑</Button>
    </div>
  </div>;
}
