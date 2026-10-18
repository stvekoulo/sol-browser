import {useSyncExternalStore} from 'react';

type FanSettings = {enabled: boolean; trigger: number};
let current: FanSettings = {enabled: true, trigger: 2};
const listeners = new Set<() => void>();
const subscribe = (listener: () => void) => { listeners.add(listener); return () => { listeners.delete(listener); }; };
export function setFanSettings(value: FanSettings) {
  const next = {enabled: value.enabled !== false, trigger: [0, 1, 2].includes(value.trigger) ? value.trigger : 2};
  if (next.enabled === current.enabled && next.trigger === current.trigger) return;
  current = next;
  listeners.forEach(listener => listener());
}
export const useFanSettings = () => useSyncExternalStore(subscribe, () => current);
