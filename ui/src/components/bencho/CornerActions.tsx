// Bencho block "Action node": Copyright (c) 2026 Lorenzo Cabra, MIT; see ui/licenses/bencho-MIT.txt.
// Adapté pour Sol : le bloc d'origine dessinait sa propre carte de démonstration (repère, nom, visages) ; ici la carte
// est celle de Sol, passée en enfant, et l'éventail reçoit ses actions. La géométrie, les ressorts et l'échelonnement
// sont ceux du bloc. Sans photos : le stub AVATARS d'origine n'a plus d'usage et a été retiré.
import { useEffect, useRef, useState, type ReactNode } from "react";
import type { LucideIcon } from "lucide-react";
import "./bencho.css";

/* ── one spring, for everything that settles ───────────────
   Frames, not milliseconds. `dt` is expressed in sixtieths of
   a second and the damping is RAISED to it rather than
   multiplied by it, so a dropped frame decays the same amount
   of energy as the two frames it replaced. Multiplying is the
   version that makes a spring behave differently on a busy
   page, which is the hardest kind of bug to see.

   The loop parks itself the moment the value has settled. */

/* The pair is chosen by DAMPING RATIO and then written back
   as stiffness and decay, because the ratio is the thing a
   person is actually setting and the two numbers on their own
   do not say what they add up to.

     zeta = -ln(d) / (2 * sqrt(k))

     0   → zeta ~0.85, heavy, arrives without a ring
     50  → zeta ~0.41
     100 → zeta ~0.20, lively, two visible rebounds

   Both ends shippable, which is the constraint that fixed the
   numbers rather than taste. */
const springOf = (tune: number) => ({
  /* stiffness: how hard it is pulled toward the target */
  k: 0.08 + (tune / 100) * 0.16,
  /* decay, per frame: how much of the velocity survives */
  d: 0.62 + (tune / 100) * 0.2,
});

/* Units matter. The snap threshold is absolute, so a caller
   works in pixels or in 0..100 — a spring driven over 0..1
   would be "settled" before it had visibly moved. */
function useSpring(target: number, tune = 50, instant = false) {
  const [at, setAt] = useState(target);
  const cur = useRef(target);
  const vel = useRef(0);
  const raf = useRef(0);

  useEffect(() => {
    if (instant) {
      cur.current = target;
      vel.current = 0;
      setAt(target);
      return;
    }
    const { k, d } = springOf(tune);
    let prev = 0;
    const tick = (t: number) => {
      const dt = prev ? clamp((t - prev) / 16.67, 0, 2.5) : 1;
      prev = t;
      vel.current += (target - cur.current) * k * dt;
      vel.current *= Math.pow(d, dt);
      cur.current += vel.current * dt;
      if (Math.abs(target - cur.current) < 0.02 && Math.abs(vel.current) < 0.02) {
        cur.current = target;
        vel.current = 0;
        setAt(target);
        raf.current = 0;
        return;
      }
      setAt(cur.current);
      raf.current = requestAnimationFrame(tick);
    };
    raf.current = requestAnimationFrame(tick);
    return () => {
      cancelAnimationFrame(raf.current);
      raf.current = 0;
    };
    /* `tune` sits here beside `target`: the loop closes over it,
       so without it a knob turned mid-flight would do nothing
       until something else restarted the effect. Restarting
       picks up from the refs, so it continues rather than
       snapping. */
  }, [target, tune, instant]);

  return at;
}

/* Read once. A preference, not a live input. */
const stillness = () =>
  typeof window !== "undefined" &&
  !!window.matchMedia?.("(prefers-reduced-motion: reduce)").matches;

/* ══ Action node ══════════════════════════════════════════
   A card with its controls folded into the corner until you
   want them.

   THE CONTROLS ARE THE COMPONENT. What this block is about is
   where a card's actions live when you are not using them, and
   the answer it argues for is: inside the corner they belong to.

   ── THEY COME OUT OF THE CORNER, THEY DO NOT ARRIVE AT IT ─
   The reflex is to fade a toolbar in above the card, and it
   is wrong in a way that is easy to miss: a control that
   fades in has no relationship to the thing it controls. It
   could belong to anything. These are drawn on an arc struck
   from the card's own top-right corner, they start AT that
   corner with no radius and no size, and they travel out
   along it — so the card is visibly where they came from and
   the corner is visibly what they are attached to.

   ── ONE SPRING EACH, AND THE STAGGER IS A DELAY ON THE
      TARGET, NOT AN EASE ─────────────────────────────────
   Four springs, always constructed, because hooks cannot be
   conditional — the count decides how many are drawn, not how
   many exist. Each one is told to open a beat after the one
   before it, and the spring does the rest.

   Closing runs the other way, last out first back, so the
   fan folds into the corner rather than collapsing from the
   inside. */

const clamp = (v: number, lo: number, hi: number) => Math.min(hi, Math.max(lo, v));

/* the button and half of it. Everything else that has a size
   is measured off this one — the spacing, the hiding place —
   so the buttons grow by changing this number and nothing else. */
const DOT = 36;
const R = DOT / 2;

/* centre to centre along the path. A real distance rather
   than an angle: twelve pixels of air between two buttons is
   twelve pixels whatever the corner is doing. */
const SPACING = DOT + 12;

/* ── they ride the card's OWN corner, offset ───────────────
   The path is the card's outline pushed out by a constant
   distance: straight along the top edge, round the corner on
   an arc concentric with it, straight down the right edge.
   Buttons are placed by how far along that path they are.

   An offset path is the card's own shape: every point on it is
   exactly `gap` from the nearest card surface, corner or edge,
   at every radius — a circle struck from one point only looked
   right at one corner radius and put a wide fan's end buttons
   through the card's top edge.

   `s` is arc length measured from where the corner arc meets
   the top edge. Returns the point and the direction out of the
   card there. */
function ride(s: number, corner: number, off: number) {
  /* the offset arc is concentric with the card's, so its
     radius is the card's plus the offset — floored just above
     zero for a square card */
  const arc = Math.max(0.001, corner + off);
  const len = (arc * Math.PI) / 2;
  if (s <= 0) {
    /* along the top edge, going left */
    return { x: -corner + s, y: -off, nx: 0, ny: -1 };
  }
  if (s >= len) {
    /* down the right edge */
    return { x: off, y: corner + (s - len), nx: 1, ny: 0 };
  }
  const th = Math.PI / 2 - s / arc;
  return {
    x: -corner + arc * Math.cos(th),
    y: corner - arc * Math.sin(th),
    nx: Math.cos(th),
    ny: -Math.sin(th),
  };
}

/* ── where they hide, and it has to be PROVABLY inside ─────
   A point on the card's diagonal, far enough in that a whole
   button fits behind the card with nothing showing past the
   corner. Two cases, because a rounded corner is bitten out of
   the very material a square one has:

     · corner >= R + 2, and the corner's own centre is the
       answer — at least R inside everything, with two pixels
       to spare (at exactly R the button draws a hairline).

     · below that, go past the corner's disc entirely and stop
       where both edges are clear. */
const hideAt = (corner: number) =>
  corner >= R + 2
    ? corner * Math.SQRT2
    : Math.max(R * Math.SQRT2 + 2, corner * Math.SQRT2 + corner + 2);

/* ── Reach is the air between the card and the fan ─────────
   20 to 40 on the original scale, which is 11px of gap to 16:
   tighter and the buttons foul the corner, wider and they stop
   reading as attached to the card. */
const gapPx = (reach: number) => 6 + (clamp(reach, 20, 40) / 100) * 24;

/* the beat between one button and the next, at the top of the
   range. 90ms is about as long as a cascade can be before the
   last one reads as late rather than as following. */
const BEAT = 90;

export type CornerAction = { id: string; label: string; Icon: LucideIcon; run: () => void };

/* Deliberately no delete in a fan: the one action that cannot
   be undone does not belong in a row that appears under your
   cursor. Callers keep it that way. */
export function CornerActions({
  actions,
  children,
  /* the air between the card and the buttons, 20..40 */
  reach = 25,
  /* the spring, 0..100 — see springOf */
  bounce = 20,
  /* how far apart the buttons open, 0..100 */
  stagger = 55,
  /* the card's own corner, in px. The buttons keep theirs. */
  corner = 20,
  className = "",
}: {
  actions: CornerAction[];
  children: ReactNode;
  reach?: number;
  bounce?: number;
  stagger?: number;
  corner?: number;
  className?: string;
}) {
  const n = clamp(actions.length, 0, 4);
  const still = stillness();

  /* ── open, and then open PER BUTTON ─────────────────────
     `on` is the pointer (or keyboard focus, in Sol). `lit` is
     what each spring is aiming at, which is the same thing a
     moment later — and the moment is the stagger. */
  const [on, setOn] = useState(false);
  const [lit, setLit] = useState([false, false, false, false]);
  const timers = useRef<number[]>([]);

  useEffect(() => {
    timers.current.forEach(clearTimeout);
    timers.current = [];
    const gap = still ? 0 : (clamp(stagger, 0, 100) / 100) * BEAT;
    for (let i = 0; i < 4; i += 1) {
      /* opening runs first to last; closing runs last to
         first, so the fan folds back into the corner in the
         order it came out of it */
      const place = on ? i : n - 1 - i;
      const at = gap * place;
      const set = () =>
        setLit((prev) => {
          if (prev[i] === on) return prev;
          const next = [...prev];
          next[i] = on;
          return next;
        });
      if (at <= 0) set();
      else timers.current.push(window.setTimeout(set, at));
    }
    return () => {
      timers.current.forEach(clearTimeout);
      timers.current = [];
    };
  }, [on, n, stagger, still]);

  /* Four, always: a hook cannot be drawn conditionally and a
     spring nobody reads costs a number that never moves. */
  const p0 = useSpring(lit[0] ? 100 : 0, bounce, still);
  const p1 = useSpring(lit[1] ? 100 : 0, bounce, still);
  const p2 = useSpring(lit[2] ? 100 : 0, bounce, still);
  const p3 = useSpring(lit[3] ? 100 : 0, bounce, still);
  const at = [p0, p1, p2, p3];

  /* ── where each button ends up ──────────────────────────
     The path is offset from the card by the gap PLUS a
     button's own half, so the gap is measured from the card
     to the button's edge. Centred on the corner's midpoint and
     spread by arc length, so the fan is symmetrical about the
     diagonal however many are on it. */
  const c = clamp(corner, 0, 32);
  const off = gapPx(reach) + R;
  const mid = ((c + off) * Math.PI) / 4;
  const spots = Array.from({ length: n }, (_, i) =>
    ride(mid + (i - (n - 1) / 2) * SPACING, c, off),
  );

  /* the one place they all fold back to */
  const deep = hideAt(c) / Math.SQRT2;
  const hid = { x: -deep, y: deep };

  /* ── the air between the card and the fan is a HOLE ─────
     Move the pointer from the card toward a button and it
     leaves the card on the way across — the fan folds itself
     away from under the cursor that was reaching for it. So the
     fan carries a hit area: the box its own buttons occupy,
     bled a little, live only while the fan is open. */
  const BLEED = 6;
  const box = spots.reduce(
    (b, s) => ({
      l: Math.min(b.l, s.x - R - BLEED),
      r: Math.max(b.r, s.x + R + BLEED),
      t: Math.min(b.t, s.y - R - BLEED),
      d: Math.max(b.d, s.y + R + BLEED),
    }),
    { l: 0, r: 0, t: 0, d: 0 },
  );
  const pw = box.r - box.l;
  const ph = box.d - box.t;
  const px = -box.l;
  const py = -box.t;
  /* the bite taken out of the pad is INSIDE the card: x < -c
     with y > c is card at every radius */
  const bx = Math.max(0, px - c);
  const by = Math.min(ph, py + c);

  if (n === 0) return <>{children}</>;
  return (
    <div
      className={`nod ${className}`}
      style={{ "--nod-r": `${c}px` } as React.CSSProperties}
      onPointerEnter={() => setOn(true)}
      /* `out` with a containment test, not `leave`: a pointer
         that leaves with no related target still folds the fan. */
      onPointerOut={(e) => {
        const to = e.relatedTarget as Node | null;
        if (!to || !e.currentTarget.contains(to)) setOn(false);
      }}
      onPointerCancel={() => setOn(false)}
      /* Sol : le clavier ouvre l'éventail aussi, quand la carte ou un de ses boutons a le focus */
      onFocus={() => setOn(true)}
      onBlur={(e) => {
        const to = e.relatedTarget as Node | null;
        if (!to || !e.currentTarget.contains(to)) setOn(false);
      }}
    >
      <div className="nod-card" data-on={on || undefined}>{children}</div>

      {/* ── the fan, anchored ON the card's top-right corner ──
          Every button's travel is measured from the one point
          they all came out of. The buttons overflow the card. */}
      <div className="nod-fan" style={{ top: 0, right: 0 }}>
        <i
          className="nod-reach"
          aria-hidden="true"
          data-on={on || undefined}
          style={{
            left: box.l,
            top: box.t,
            width: pw,
            height: ph,
            clipPath: `polygon(0 0, ${pw}px 0, ${pw}px ${ph}px, ${bx}px ${ph}px, ${bx}px ${by}px, 0 ${by}px)`,
          }}
        />
        {actions.slice(0, n).map((a, i) => {
          const u = clamp(at[i] / 100, 0, 1.4);
          const spot = spots[i];
          /* ── out of the corner, in a straight line ───────
             From the hidden point to its place on the path.
             `u` is not clamped at 1, so the spring's overshoot
             carries each button a little past its place and back. */
          const put = (from: number, to: number) => from + (to - from) * u;
          return (
            <button
              key={a.id}
              className="nod-act"
              type="button"
              aria-label={a.label}
              title={a.label}
              /* hidden from the keyboard while it is folded away */
              tabIndex={on ? 0 : -1}
              onClick={(e) => { e.stopPropagation(); a.run(); }}
              style={{
                width: DOT,
                height: DOT,
                marginTop: -R,
                marginLeft: -R,
                translate: `${put(hid.x, spot.x)}px ${put(hid.y, spot.y)}px`,
                /* barely any scale and no fade: a button hidden
                   UNDER the card does not need to be transparent
                   to be hidden. A variable, not the scale, so the
                   hover can multiply it — see .nod-act. */
                "--pop": 0.82 + 0.18 * Math.min(u, 1),
                /* Sol : beaucoup de cartes de Sol sont translucides, un bouton rangé dessous se verrait au travers.
                   Masqué seulement une fois rentré, donc sans fondu pendant la course. */
                visibility: at[i] < 0.5 && !lit[i] ? "hidden" : undefined,
              } as React.CSSProperties}
            >
              <a.Icon size={16} strokeWidth={2} />
            </button>
          );
        })}
      </div>
    </div>
  );
}
