import type {Download} from './Downloads';
import type {NotesContent} from './QuickNotes';
import type {SolAiState} from './SolAiAccount';
import type {AiContent} from './SolAiAction';
export type Item = { url: string; title: string; kind?: 'search' | 'go' | 'history' | 'site' };
export type PaletteItem = { id: string; title: string; hint: string; shortcut: string; group: string; keywords: string };
export type Wallpaper = { id: number; thumb: string; author: string };
import type {VideoContent} from './VideoPicker';
export type Content =
  | VideoContent
  | { popup: 'palette'; items: PaletteItem[]; listHeight: number }
  | AiContent
  | NotesContent
  | { popup: 'notes-select'; items: {id:string; title:string}[] }
  | { popup: 'translation'; id: string; target: 'fr' | 'en'; available: boolean; original: boolean }
  | { popup: 'suggestions'; items: Item[] }
  | { popup: 'library'; tab?: 'history' | 'bookmarks' | null; query: string; page: number; pageSize: number; total: number; bookmarks: Item[]; history: Item[] }
  | { popup: 'menu'; items: string[] }
  | { popup: 'split-layout'; ratio: number }
  | { popup: 'downloads'; items: Download[] }
  | { popup: 'settings'; section?: string; ai: SolAiState; version: string; update: { version: string; percent: number; ready: boolean } | null; check: 'idle' | 'checking' | 'current' | 'failed' | 'portable'; zoom: number; uiScale: number; videoQuality:number; fanEnabled: boolean; fanTrigger: number; wallpaperCustom: boolean; shortcuts: {action:string; title:string; keys:string}[]; theme: number; language: number; spelling: number; spellingLanguages: string[]; home: string; wallpaperOn: boolean; wallpaper: number; wallpaperDaily: boolean; wallpapers: Wallpaper[]; font: number;  engine: number; engines: string[]; sites: { origin: string; host: string; summary: string }[]; permissions: number; siteData: 'idle' | 'clearing' | 'done' | 'failed'; tint: Setting[]; items: Setting[] }
  | { popup: 'profiles'; items: Profile[] }
  | { popup: 'sessions'; id: string; current: string; deleting: string | null; items: {id:string;name:string;count:number}[] }
  | { popup: 'group-edit'; create: boolean; id: string; name: string; color: string; icon: string; presets: string[]; count?: number; ai?: boolean; suggesting?: boolean; suggestion?: { name: string; color: string; icon: string } | null }
  | { popup: 'shortcut-edit'; id: string; name: string; url: string }
  | { popup: 'compare-pick'; id: string; min: number; max: number; devices: { name: string; label: string; size: string; on: boolean }[] }
  | { popup: 'permission'; site: string; what: string; secure: boolean }
  | { popup: 'prompt'; title: string; hint: string; placeholder: string; submit: string; value: string; command: string; id: string | null; secret?: boolean }
  | { popup: 'notice'; title: string; hint: string }
  | { popup: 'site-reset'; site: string; session: string }
  | { popup: 'extension-confirm'; id: string; name: string; version: string; icon: string | null; update: boolean; access: string[] }
  | { popup: 'extensions'; session: string; updates: '' | 'checking' | 'current' | 'updated' | 'failed'; installed: { id: string; name: string; version: string; enabled: boolean; page: boolean; options: boolean; pinned: boolean; icon: string | null; store: boolean; update: string | null }[]; available: { id: string; name: string; version: string; from: string }[] };

export type Profile = { id: string; name: string; current: boolean; renamable: boolean; deletable: boolean };

export type Setting = { key: string; label: string; hint: string; on: boolean };
