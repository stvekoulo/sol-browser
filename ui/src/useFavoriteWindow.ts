import { useLayoutEffect, useRef, useState, type KeyboardEvent, type RefObject } from 'react';

// Quatre rangées au repos ; « Voir tout » garde la hauteur complète sans monter les centaines de cartes hors écran.
export function useFavoriteWindow(grid: RefObject<HTMLDivElement | null>, count: number, columns: number, expanded: boolean) {
  const [range, setRange] = useState({ first: 0, last: 8 });
  const rows = Math.ceil(count / columns);
  const pendingFocus = useRef<number | null>(null);
  const first = expanded ? Math.min(range.first, Math.max(0, rows - 1)) * columns : 0;
  const last = Math.min(count, expanded ? range.last * columns : columns * 4);
  useLayoutEffect(() => {
    const element = grid.current;
    if (!expanded || !element) return;
    let frame = 0;
    const update = () => {
      frame = 0;
      const box = element.getBoundingClientRect(), style = getComputedStyle(element);
      const stride = 102 + (parseFloat(style.rowGap) || 0), top = box.top + (parseFloat(style.paddingTop) || 0);
      const first = Math.max(0, Math.min(rows - 1, Math.floor(-top / stride) - 1));
      const last = Math.min(rows, Math.max(first + 1, Math.ceil((innerHeight - top) / stride) + 1));
      setRange(previous => previous.first === first && previous.last === last ? previous : { first, last });
    };
    const schedule = () => { if (!frame) frame = requestAnimationFrame(update); };
    update();
    addEventListener('scroll', schedule, { capture: true, passive: true });
    addEventListener('resize', schedule, { passive: true });
    return () => { removeEventListener('scroll', schedule, true); removeEventListener('resize', schedule); cancelAnimationFrame(frame); };
  }, [expanded, count, columns, rows, grid]);
  useLayoutEffect(() => {
    if (pendingFocus.current === null) return;
    const tile = grid.current?.querySelectorAll<HTMLButtonElement>('.start-site')[pendingFocus.current - first];
    if (!tile) return;
    pendingFocus.current = null; tile.focus({ preventScroll: true }); tile.scrollIntoView({ block: 'nearest' });
  }, [first, last, grid]);
  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (!expanded || event.key !== 'Tab') return;
    const buttons = [...(grid.current?.querySelectorAll<HTMLButtonElement>('button:not([disabled])') ?? [])];
    const next = event.shiftKey && event.target === buttons[0] && first > 0 ? first - 1
      : !event.shiftKey && event.target === buttons.at(-1) && last < count ? last : null;
    if (next === null) return;
    event.preventDefault(); pendingFocus.current = next;
    const row = Math.floor(next / columns); setRange({ first: row, last: Math.min(rows, row + 8) });
  };
  return { first, last, rows, onKeyDown };
}
