import { useEffect, useLayoutEffect, useRef } from 'react';
import { send } from './bridge';

// État du glisser vertical d'onglet dans la carte latérale.
let drag: { id: string; grab: number; start: number; y: number; pending: number; moved: boolean } | null = null;
const SLIDE = 'transform 160ms cubic-bezier(0.22, 1, 0.36, 1)';
const slotElements = () => [...document.querySelectorAll<HTMLElement>('[data-slot-id]')];

function slideHome(el: HTMLElement) {
  el.style.transition = SLIDE;
  el.style.transform = '';
  el.addEventListener('transitionend', () => (el.style.transition = ''), { once: true });
}

function followMouseY() {
  const mine = drag && slotElements().find(el => el.dataset.slotId === drag!.id);
  const list = mine?.offsetParent as HTMLElement | null;
  if (!drag || !mine || !list) return null;
  const top = Math.max(0, Math.min(drag.y - drag.grab - list.getBoundingClientRect().top, list.clientHeight - mine.offsetHeight));
  mine.style.transition = 'none';
  mine.style.transform = `translateY(${top - mine.offsetTop}px)`;
  mine.classList.add('dragging');
  return { mine, top };
}

export function startSlotDrag(el: HTMLElement | null, clientY: number) {
  if (!el) return;
  drag = {
    id: el.dataset.slotId!,
    grab: clientY - el.getBoundingClientRect().top,
    start: clientY,
    y: clientY,
    pending: -1,
    moved: false,
  };
}

// Hook gérant le réordonnancement vertical fluide des onglets et l'animation FLIP.
export function useVerticalDrag() {
  useEffect(() => {
    const stop = () => {
      const mine = drag && slotElements().find(el => el.dataset.slotId === drag!.id);
      drag = null;
      if (!mine) return;
      slideHome(mine);
      mine.classList.remove('dragging');
    };
    const move = (e: globalThis.MouseEvent) => {
      if (!drag) return;
      if (!(e.buttons & 1)) return stop();
      if (!drag.moved && Math.abs(e.clientY - drag.start) < 5) return;
      drag.moved = true;
      drag.y = e.clientY;
      const placed = followMouseY();
      if (!placed) return void (drag = null);

      const { mine, top } = placed;
      const from = Number(mine.dataset.slotFirst);
      if (drag.pending !== -1) return;
      const center = top + mine.offsetHeight / 2;
      let target = from;
      for (const el of slotElements()) {
        if (el === mine) continue;
        const first = Number(el.dataset.slotFirst), last = Number(el.dataset.slotLast);
        const middle = el.offsetTop + el.offsetHeight / 2;
        if (first > from && center > middle) target = Math.max(target, last);
        if (first < from && center < middle) target = Math.min(target, first);
      }
      if (target !== from) {
        drag.pending = target;
        send({ type: 'tab.move', id: drag.id, index: target });
      }
    };
    window.addEventListener('mousemove', move);
    window.addEventListener('mouseup', stop);
    document.documentElement.addEventListener('mouseleave', stop);
    window.addEventListener('blur', stop);
    return () => {
      window.removeEventListener('mousemove', move);
      window.removeEventListener('mouseup', stop);
      document.documentElement.removeEventListener('mouseleave', stop);
      window.removeEventListener('blur', stop);
    };
  }, []);

  const tops = useRef(new Map<string, number>());
  useLayoutEffect(() => {
    if (drag) drag.pending = -1;
    const still = matchMedia('(prefers-reduced-motion: reduce)').matches;
    for (const el of slotElements()) {
      const id = el.dataset.slotId!;
      const before = tops.current.get(id);
      const now = el.offsetTop;
      tops.current.set(id, now);
      if (drag?.moved && id === drag.id) followMouseY();
      else if (drag?.moved && !still && before !== undefined && before !== now) {
        el.style.transition = 'none';
        el.style.transform = `translateY(${before - now}px)`;
        el.getBoundingClientRect();
        slideHome(el);
      }
    }
  });
}
