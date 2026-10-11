import { useEffect, type PointerEvent } from 'react';
import { send, type Command } from './bridge';
import { t } from './i18n';

type Drop = { el: HTMLElement; style: string; commands: Command[]; label: string };
type Drag = { el: HTMLElement; pointer: number; x: number; y: number; startX: number; startY: number; origin?: DOMRect; ghost?: HTMLElement; drop?: Drop };
let drag: Drag | null = null;
const slots = (kind: string) => [...document.querySelectorAll<HTMLElement>(`.sidebar-scroll [data-kind="${kind}"]`)];
const position = (el: HTMLElement, last = false) => Number(last ? el.dataset.last : el.dataset.first);

export function startSlotDrag(el: HTMLElement | null, event: PointerEvent) {
  if (!el || event.button !== 0 || event.target instanceof HTMLInputElement) return;
  drag = { el, pointer: event.pointerId, x: event.clientX, y: event.clientY, startX: event.clientX, startY: event.clientY };
  el.setPointerCapture(event.pointerId);
}

function targetAt(d: Drag): Drop | undefined {
  const hit = document.elementFromPoint(d.x, d.y)?.closest<HTMLElement>('.sidebar-scroll');
  if (!hit || !d.el.isConnected) return;
  const kind = d.el.dataset.kind!, id = d.el.dataset.id!, from = position(d.el);
  const element = document.elementFromPoint(d.x, d.y) as HTMLElement | null;
  if (kind === 'shortcut') {
    const target = element?.closest<HTMLElement>('[data-kind="shortcut"]');
    if (!target || target === d.el) return;
    return { el: target, style: 'drop-into', commands: [{type: 'shortcut.move', id, index: position(target)}], label: t('Ranger l’accès rapide', 'Reorder shortcut') };
  }
  const pinZone = element?.closest<HTMLElement>('[data-pin-zone]');
  if (pinZone && kind === 'tab') {
    const target = element?.closest<HTMLElement>('[data-kind="tab"]');
    const commands: Command[] = d.el.dataset.pinned ? [] : [{type: 'tab.pin', id, index: 1}];
    if (target && target !== d.el) commands.push({type: 'tab.move', id, index: position(target)});
    if (!commands.length) return;
    return {el: target ?? pinZone, style: 'drop-into', commands, label: t('Épingler et ranger l’onglet', 'Pin and reorder tab')};
  }
  const header = element?.closest<HTMLElement>('.tab-group-heading');
  if (header && kind === 'tab') {
    const group = header.parentElement!;
    if (d.el.closest('[data-kind="group"]') === group) return;
    return {el: header, style: 'drop-into', commands: [
      ...(d.el.dataset.pinned ? [{type: 'tab.pin', id, index: 0}] : []),
      {type: 'group.add', id, query: group.dataset.id}], label: t('Ajouter au groupe', 'Add to group')};
  }
  const end = element?.closest<HTMLElement>('.sidebar-new-tab');
  if (end && kind === 'tab') return {el: end, style: 'drop-into', commands: [
    ...(d.el.dataset.pinned ? [{type: 'tab.pin', id, index: 0}] : []),
    {type: 'tab.move', id, index: Number(hit.dataset.tabCount) - 1}, {type: 'group.remove', id}],
    label: t('Ranger hors des groupes', 'Move outside groups')};
  const candidates = kind === 'group'
    ? [...hit.querySelectorAll<HTMLElement>('.sidebar-tabs > [data-kind]')]
    : slots('tab').filter(el => !el.dataset.pinned && !d.el.contains(el));
  if (kind === 'tab') candidates.push(...hit.querySelectorAll<HTMLElement>('.tab-group-block.collapsed'));
  const valid = candidates.filter(el => el !== d.el && !d.el.contains(el));
  if (!valid.length && kind === 'tab' && d.el.dataset.pinned) {
    return {el: hit.querySelector<HTMLElement>('.sidebar-tabs')!, style: 'drop-into', commands: [{type: 'tab.pin', id, index: 0}], label: t('Détacher l’onglet', 'Unpin tab')};
  }
  const distance = (el: HTMLElement) => { const r = el.getBoundingClientRect(); return Math.abs(d.y - (r.top + r.bottom) / 2); };
  const target = valid.sort((a, b) => distance(a) - distance(b))[0];
  if (!target) return;
  const rect = target.getBoundingClientRect(), after = d.y > (rect.top + rect.bottom) / 2;
  let index = after ? position(target, true) + 1 : position(target);
  const commands: Command[] = [];
  if (kind === 'group') commands.push({type: 'group.move', id, index});
  else {
    if (d.el.dataset.pinned) {
      commands.push({type: 'tab.pin', id, index: 0});
      // Unpin inserts at the boundary; the following move uses the updated tab indexes.
      index = Math.max(slots('tab').filter(el => el.dataset.pinned).length - 1, index - 1);
      commands.push({type: 'tab.move', id, index});
    } else {
      if (index > from) index--;
      if (index === from) return;
      commands.push({type: 'tab.move', id, index});
    }
  }
  return {el: target, style: after ? 'drop-after' : 'drop-before', commands, label: kind === 'group'
    ? t('Déplacer le groupe', 'Move group') : d.el.dataset.pinned ? t('Détacher et déplacer l’onglet', 'Unpin and move tab') : t('Déplacer l’onglet', 'Move tab')};
}

export function useSidebarDrag() {
  useEffect(() => {
    let frame = 0, suppressClick = false, reset = 0;
    const hint = () => document.querySelector<HTMLElement>('.sidebar-drag-hint');
    const clearDrop = () => {
      if (drag?.drop) drag.drop.el.classList.remove(drag.drop.style);
      if (hint()) hint()!.textContent = '';
    };
    const stop = (commit = false) => {
      cancelAnimationFrame(frame); clearDrop();
      if (drag?.ghost) {
        suppressClick = true; window.clearTimeout(reset);
        if (commit) drag.drop?.commands.forEach(send);
      }
      drag?.ghost?.remove(); drag?.el.classList.remove('drag-source');
      if (drag?.el.hasPointerCapture(drag.pointer)) drag.el.releasePointerCapture(drag.pointer);
      document.documentElement.classList.remove('sidebar-dragging'); drag = null;
    };
    const update = () => {
      if (!drag?.ghost) return;
      const d = drag;
      const origin = d.origin!, card = document.querySelector('.sidebar-card')!.getBoundingClientRect();
      const left = origin.left + (d.el.classList.contains('pinned-pill') ? d.x - d.startX : 0);
      d.ghost!.style.left = `${Math.max(card.left, Math.min(left, card.right - origin.width))}px`;
      d.ghost!.style.top = `${origin.top + d.y - d.startY}px`;
      clearDrop(); d.drop = targetAt(d);
      if (d.drop) { d.drop.el.classList.add(d.drop.style); if (hint()) hint()!.textContent = `${d.drop.label} · ${t('Échap pour annuler', 'Esc to cancel')}`; }
      const list = document.querySelector<HTMLElement>('.sidebar-scroll')!;
      const box = list.getBoundingClientRect();
      const speed = d.y < box.top + 28 ? -9 : d.y > box.bottom - 28 ? 9 : 0;
      if (speed && d.y >= box.top - 20 && d.y <= box.bottom + 20) list.scrollTop += speed;
      frame = requestAnimationFrame(update);
    };
    const move = (event: globalThis.PointerEvent) => {
      if (!drag) return;
      if (!(event.buttons & 1)) return stop();
      drag.x = event.clientX; drag.y = event.clientY;
      if (drag.ghost || Math.hypot(drag.x - drag.startX, drag.y - drag.startY) < 5) return;
      const source = drag.el.dataset.kind === 'group' ? drag.el.querySelector<HTMLElement>('.tab-group-heading')! : drag.el;
      const box = source.getBoundingClientRect(), style = getComputedStyle(source);
      drag.origin = box;
      const scale = Number(getComputedStyle(document.body).zoom) || 1;
      const ghost = source.cloneNode(true) as HTMLElement;
      ghost.classList.add('sidebar-drag-ghost'); ghost.removeAttribute('id');
      const background = style.backgroundColor === 'rgba(0, 0, 0, 0)' ? getComputedStyle(document.querySelector('.sidebar-card')!).backgroundColor : style.backgroundColor;
      Object.assign(ghost.style, {width: `${box.width / scale}px`, height: `${box.height / scale}px`, transform: `scale(${scale})`, transformOrigin: 'top left', background, color: style.color, font: style.font});
      ghost.style.setProperty('--group', style.getPropertyValue('--group'));
      ghost.querySelectorAll('[data-kind]').forEach(el => el.removeAttribute('data-kind'));
      document.documentElement.appendChild(ghost); drag.ghost = ghost;
      drag.el.classList.add('drag-source'); document.documentElement.classList.add('sidebar-dragging');
      event.preventDefault(); update();
    };
    const click = (event: globalThis.MouseEvent) => { if (suppressClick && event.detail) { event.preventDefault(); event.stopImmediatePropagation(); suppressClick = false; } };
    const up = () => { stop(true); if (suppressClick) reset = window.setTimeout(() => { suppressClick = false; }, 80); };
    const down = () => { suppressClick = false; }, cancel = () => stop();
    const key = (event: KeyboardEvent) => { if (event.key === 'Escape' && drag) { event.preventDefault(); stop(); } };
    window.addEventListener('pointermove', move); window.addEventListener('pointerup', up); window.addEventListener('pointercancel', cancel);
    window.addEventListener('pointerdown', down, true);
    window.addEventListener('blur', cancel); window.addEventListener('keydown', key);
    document.addEventListener('click', click, true);
    return () => {
      stop(); window.clearTimeout(reset);
      window.removeEventListener('pointermove', move); window.removeEventListener('pointerup', up); window.removeEventListener('pointercancel', cancel);
      window.removeEventListener('pointerdown', down, true);
      window.removeEventListener('blur', cancel); window.removeEventListener('keydown', key); document.removeEventListener('click', click, true);
    };
  }, []);
}
