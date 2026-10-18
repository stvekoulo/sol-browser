import { useLayoutEffect, useRef } from 'react';

// Même ressort discret que le bloc d'origine (bounce=20), échantillonné une seule fois.
// Le navigateur exécute les images clés ; aucun setState ni boucle JS à chaque image.
const samples: number[] = [0];
let value = 0, velocity = 0;
for (let frame = 0; frame < 180; frame++) {
  velocity = (velocity + (1 - value) * .112) * .66; value += velocity;
  if (Math.abs(1 - value) < .0002 && Math.abs(velocity) < .0002) break;
  samples.push(value);
}
samples.push(1);
export function useFanMotion(on: boolean, inline: boolean, spots: { x: number; y: number }[], deep: number, closed: () => void) {
  const element = useRef<HTMLDivElement>(null), first = useRef(true);
  useLayoutEffect(() => {
    const buttons = [...(element.current?.querySelectorAll<HTMLButtonElement>('button') ?? [])];
    if (matchMedia('(prefers-reduced-motion: reduce)').matches) {
      if (!on && !inline) queueMicrotask(closed);
      return;
    }
    const animations = buttons.map((button, index) => {
      const end = inline ? { x: 0, y: 8 } : { x: -deep - spots[index].x, y: deep - spots[index].y };
      const matrix = new DOMMatrix(getComputedStyle(button).transform);
      const start = first.current ? { x: end.x, y: end.y, scale: .82 } : { x: matrix.e, y: matrix.f, scale: matrix.a };
      const target = on ? { x: 0, y: 0, scale: 1 } : { ...end, scale: .82 };
      return button.animate(samples.map(progress => ({ transform: `translate(${start.x + (target.x - start.x) * progress}px, ${start.y + (target.y - start.y) * progress}px) scale(${start.scale + (target.scale - start.scale) * Math.min(progress, 1)})` })), {
        duration: (samples.length - 1) * 16.67, delay: (on ? index : buttons.length - 1 - index) * 49.5, fill: 'both', easing: 'linear',
      });
    });
    first.current = false;
    let cancelled = false;
    Promise.all(animations.map(animation => animation.finished)).then(() => { if (!cancelled && !on) closed(); }).catch(() => {});
    return () => { cancelled = true; buttons.forEach((button, index) => { button.style.transform = getComputedStyle(button).transform; animations[index].cancel(); }); };
  }, [on, inline, spots.length, deep, closed]);
  return element;
}
