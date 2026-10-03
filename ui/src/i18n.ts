import { useSyncExternalStore } from 'react';

// Langue de l'interface : français ou anglais. L'hôte l'annonce avec le thème ; en attendant, celle du moteur
// (que l'hôte lance dans la même langue), et l'anglais pour toute autre langue.
let lang: 'fr' | 'en' = navigator.language.toLowerCase().startsWith('fr') ? 'fr' : 'en';
const listeners = new Set<() => void>();
document.documentElement.lang = lang;

export function setLang(next: string | undefined) {
  if ((next !== 'fr' && next !== 'en') || next === lang) return;
  lang = next;
  document.documentElement.lang = lang;
  listeners.forEach(notify => notify());
}

/** À appeler dans chaque vue racine : elle se redessine quand la langue change. */
export const useLang = () =>
  useSyncExternalStore(
    notify => {
      listeners.add(notify);
      return () => void listeners.delete(notify);
    },
    () => lang,
  );

// ponytail: deux langues, textes côte à côte ; passer à des tables par clé à la troisième langue.
export const t = (fr: string, en: string) => (lang === 'fr' ? fr : en);
