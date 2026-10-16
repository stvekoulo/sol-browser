import type { MouseEvent, KeyboardEvent } from 'react';
import { send } from './bridge';

export function sidebarMenu(type: string, id: string, event: MouseEvent<HTMLElement>) {
  event.preventDefault(); event.stopPropagation();
  const box = event.currentTarget.getBoundingClientRect();
  send({ type, ...(type === 'shortcut.menu' ? { index: Number(id) } : { id }),
    x: event.type === 'contextmenu' ? event.clientX : box.left,
    y: (event.type === 'contextmenu' ? event.clientY : box.bottom + 4) });
}

export function middleClose(event: MouseEvent, id: string | null) {
  if (event.button !== 1) return;
  event.preventDefault(); event.stopPropagation();
  if (id) send({ type: 'tab.close', id });
}

export function focusTab(event: KeyboardEvent<HTMLElement>) {
  if (!['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key)) return;
  const tabs = [...document.querySelectorAll<HTMLElement>('.sidebar-scroll [role="tab"]')];
  const current = tabs.indexOf(event.currentTarget);
  const index = event.key === 'Home' ? 0 : event.key === 'End' ? tabs.length - 1
    : (current + (event.key === 'ArrowDown' ? 1 : -1) + tabs.length) % tabs.length;
  event.preventDefault(); tabs[index]?.focus();
}
