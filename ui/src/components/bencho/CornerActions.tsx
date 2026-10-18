// Bencho block "Action node": Copyright (c) 2026 Lorenzo Cabra, MIT; see ui/licenses/bencho-MIT.txt.
// Géométrie, ressort et décalage d'origine ; seules les commandes utilisées sont montées.
import { useCallback, useEffect, useState, type CSSProperties, type ReactNode } from "react";
import type { LucideIcon } from "lucide-react";
import "./bencho.css";
import { useFanMotion } from './fanMotion';

export type CornerAction = { id: string; label: string; Icon: LucideIcon; run: () => void };
type Props = { actions: CornerAction[]; children: ReactNode; reach?: number; corner?: number; className?: string; style?: CSSProperties; layout?: 'corner' | 'inline' };
const DOT = 36, R = DOT / 2, SPACING = DOT + 12, BLEED = 6;
const clamp = (v: number, lo: number, hi: number) => Math.min(hi, Math.max(lo, v));

// Contour du coin décalé : bord supérieur, arc, puis bord droit.
function ride(s: number, corner: number, off: number) {
  const arc = Math.max(0.001, corner + off), length = arc * Math.PI / 2;
  if (s <= 0) return { x: -corner + s, y: -off };
  if (s >= length) return { x: off, y: corner + s - length };
  const angle = Math.PI / 2 - s / arc;
  return { x: -corner + arc * Math.cos(angle), y: corner - arc * Math.sin(angle) };
}

function Fan({ actions, corner, reach, inline, on, closed }: { actions: CornerAction[]; corner: number; reach: number; inline: boolean; on: boolean; closed: () => void }) {
  const c = clamp(corner, 0, 32), off = 6 + clamp(reach, 20, 40) * .24 + R;
  const mid = (c + off) * Math.PI / 4;
  const spots = actions.map((_, i) => ride(mid + (i - (actions.length - 1) / 2) * SPACING, c, off));
  const deep = (c >= R + 2 ? c * Math.SQRT2 : Math.max(R * Math.SQRT2 + 2, c * Math.SQRT2 + c + 2)) / Math.SQRT2;
  const ref = useFanMotion(on, inline, spots, deep, closed);
  const box = spots.reduce((b, s) => ({
    l: Math.min(b.l, s.x - R - BLEED), r: Math.max(b.r, s.x + R + BLEED),
    t: Math.min(b.t, s.y - R - BLEED), d: Math.max(b.d, s.y + R + BLEED),
  }), { l: 0, r: 0, t: 0, d: 0 });
  const w = box.r - box.l, h = box.d - box.t;
  const biteX = Math.max(0, -box.l - c), biteY = Math.min(h, -box.t + c);
  return <div ref={ref} className="nod-fan" aria-hidden={!on || undefined} style={inline ? undefined : { top: 0, right: 0 }}>
    {/* Le pont transparent permet d'atteindre les commandes sans quitter leur carte. */}
    {!inline && <i className="nod-reach" aria-hidden="true" data-on={on || undefined} style={{ left: box.l, top: box.t, width: w, height: h,
      clipPath: `polygon(0 0, ${w}px 0, ${w}px ${h}px, ${biteX}px ${h}px, ${biteX}px ${biteY}px, 0 ${biteY}px)` }} />}
    {actions.map((action, i) => <button key={action.id} className="nod-act" type="button"
      tabIndex={on ? 0 : -1} disabled={!on}
      aria-label={action.label} title={action.label}
      onClick={e => { e.stopPropagation(); action.run(); }}
      style={inline ? undefined : { width: DOT, height: DOT, marginTop: -R, marginLeft: -R, translate: `${spots[i].x}px ${spots[i].y}px` }}>
      <action.Icon size={16} strokeWidth={2} />
    </button>)}
  </div>;
}

export function CornerActions({ actions, children, reach = 25, corner = 20, className = "", style, layout = 'corner' }: Props) {
  const [on, setOn] = useState(false);
  const [mounted, setMounted] = useState(false);
  const closed = useCallback(() => setMounted(false), []);
  const open = () => { setMounted(true); setOn(true); };
  useEffect(() => {
    if (!on) return;
    const escape = (event: KeyboardEvent) => { if (event.key === 'Escape') setOn(false); };
    document.addEventListener('keydown', escape, true);
    return () => document.removeEventListener('keydown', escape, true);
  }, [on]);
  if (!actions.length) return <>{children}</>;
  return <div className={`nod ${className}`} data-layout={layout} data-mounted={mounted || undefined} style={{ ...style, "--nod-r": `${clamp(corner, 0, 32)}px` } as CSSProperties}
    onPointerEnter={open}
    onPointerOut={e => { if (!(e.relatedTarget instanceof Node) || !e.currentTarget.contains(e.relatedTarget)) setOn(false); }}
    onPointerCancel={() => setOn(false)} onFocus={open}
    onBlur={e => { if (!(e.relatedTarget instanceof Node) || !e.currentTarget.contains(e.relatedTarget)) setOn(false); }}
    onKeyDown={e => { if (e.key === 'Escape') { setOn(false); e.stopPropagation(); } }}>
    <div className="nod-card" data-on={layout === 'corner' && on || undefined}>{children}</div>
    {(mounted || layout === 'inline') && <Fan actions={actions.slice(0, 4)} corner={corner} reach={reach} inline={layout === 'inline'} on={on || layout === 'inline'} closed={closed} />}
  </div>;
}
