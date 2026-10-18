"use client";
// beUI source: Copyright (c) 2026 Saurabh Chauhan, MIT; see ui/licenses/beui-MIT.txt.

import {
  AnimatePresence,
  animate,
  motion,
  useMotionValue,
  usePresence,
  useReducedMotion,
  useTransform,
} from "motion/react";
import {
  cloneElement,
  createContext,
  isValidElement,
  type ReactElement,
  type ReactNode,
  type Ref,
  useCallback,
  useContext,
  useEffect,
  useId,
  useLayoutEffect,
  useMemo,
  useRef,
  useState,
} from "react";
import { createPortal } from "react-dom";
import { usePopoverPortalPosition } from "@/components/motion/popover-position";
import { EASE_OUT } from "@/lib/ease";
import { cn } from "@/lib/utils";

import { useMorphContext, type Side, type Align } from "./popover-morph";
// Full-distance clip travel needs a shorter curve than SPRING_PANEL's slow
// overdamped tail. A non-bouncing spring reaches its rest within 260 ms.
const MORPH_ENTER = { type: "spring", duration: 0.26, bounce: 0 } as const;
const MORPH_EXIT = { duration: 0.2, ease: EASE_OUT } as const;

// Keep the panel and content at their final size. An inset clip reveals the
// nearest corner without scaling borders, shadows or keyboard-focus geometry.
// A full-size clip keeps content at its final coordinates without box-to-box
// scale correction. One progress value keeps clip and opacity synchronized;
// clip-path and the shadow may paint, so this is not a compositor-only morph.
function clipAt(side: Side, align: Align, radius: number, inset: number) {
  return `inset(${side === "top" ? inset : 0}% ${align === "start" ? inset : 0}% ${side === "bottom" ? inset : 0}% ${align === "end" ? inset : 0}% round ${radius}px)`;
}

export interface MorphPopoverContentProps {
  children: ReactNode;
  side?: Side;
  align?: Align;
  /** Gap between trigger and panel, in px. Default 8. */
  sideOffset?: number;
  /** Panel corner radius, in px. Default 16. */
  radius?: number;
  /** Draw the surface shadow. Default true. */
  shadow?: boolean;
  /** Runs after the portalled surface is positioned and visible. */
  onOpenAutoFocus?: (content: HTMLDivElement) => void;
  className?: string;
}

export function MorphPopoverContent(props: MorphPopoverContentProps) {
  const ctx = useMorphContext("MorphPopoverContent");
  const [portalReady, setPortalReady] = useState(false);
  useEffect(() => setPortalReady(true), []);
  if (!portalReady) return null;
  return createPortal(
    <AnimatePresence>
      {ctx.open && <MorphPopoverSurface {...props} />}
    </AnimatePresence>,
    document.body,
  );
}

// Measurement belongs to the mounted portal session: reopening must not start
// an entrance at the previous session's coordinates before measuring this one.
function MorphPopoverSurface({
  children,
  side = "bottom",
  align = "end",
  sideOffset = 8,
  radius = 16,
  shadow = true,
  onOpenAutoFocus,
  className,
}: MorphPopoverContentProps) {
  const ctx = useMorphContext("MorphPopoverContent");
  const reduce = useReducedMotion() ?? false;
  const [isPresent, safeToRemove] = usePresence();
  const layout = usePopoverPortalPosition(
    ctx.triggerRef,
    ctx.contentRef,
    isPresent,
  );

  const preferredLeft = layout
    ? align === "end"
      ? layout.trigger.left + layout.trigger.width - layout.content.width
      : layout.trigger.left
    : 0;
  // Keep larger compositions, such as calendars, inside the viewport. Prefer
  // the requested side, then use the roomier side when it cannot fit there.
  const gutter = 12;
  const below = layout
    ? Math.max(
        0,
        window.innerHeight -
          layout.trigger.top -
          layout.trigger.height -
          sideOffset -
          gutter,
      )
    : 0;
  const above = layout
    ? Math.max(0, layout.trigger.top - sideOffset - gutter)
    : 0;
  const requestedSpace = side === "bottom" ? below : above;
  const otherSpace = side === "bottom" ? above : below;
  const resolvedSide =
    layout &&
    layout.content.height > requestedSpace &&
    otherSpace > requestedSpace
      ? side === "bottom"
        ? "top"
        : "bottom"
      : side;
  const availableHeight = resolvedSide === "bottom" ? below : above;
  const left = layout
    ? Math.max(
        gutter,
        Math.min(
          preferredLeft,
          window.innerWidth - layout.content.width - gutter,
        ),
      )
    : 0;
  const top = layout
    ? resolvedSide === "bottom"
      ? layout.trigger.top + layout.trigger.height + sideOffset
      : Math.max(
          gutter,
          layout.trigger.top - layout.content.height - sideOffset,
        )
    : 0;

  const progress = useMotionValue(0);
  const clipPath = useTransform(progress, (p) =>
    clipAt(resolvedSide, align, radius, 92 * (1 - p)),
  );
  const surfaceOpacity = useTransform(progress, [0, 0.15], [0, 1]);
  // Reduced motion has no travel, so a short fade carries the whole entrance
  // and exit on its own.
  const fade = useMotionValue(reduce ? 0 : 1);
  const ready = layout !== null;
  const focusedOnOpen = useRef(false);
  useEffect(() => {
    if (
      !ready ||
      !isPresent ||
      focusedOnOpen.current ||
      !ctx.contentRef.current
    )
      return;
    focusedOnOpen.current = true;
    onOpenAutoFocus?.(ctx.contentRef.current);
  }, [ready, isPresent, ctx.contentRef, onOpenAutoFocus]);
  // `safeToRemove` is a new function on every render: AnimatePresence
  // rebuilds its context each time its parent renders. Read it through a ref
  // so a render during the exit (a consumer's state change, a list streaming
  // in) does not restart the exit from where it got to — restarted every
  // render, it would close in ever smaller steps and never report completion,
  // leaving an inert, invisible panel mounted for good.
  const safeToRemoveRef = useRef(safeToRemove);
  useLayoutEffect(() => {
    safeToRemoveRef.current = safeToRemove;
  });
  useEffect(() => {
    if (!ready) {
      if (!isPresent) safeToRemoveRef.current?.();
      return;
    }
    const settle = () => {
      if (!isPresent) safeToRemoveRef.current?.();
    };
    if (reduce) {
      progress.jump(1);
      const animation = animate(fade, isPresent ? 1 : 0, {
        duration: 0.12,
        onComplete: settle,
      });
      return () => animation.stop();
    }
    const animation = animate(progress, isPresent ? 1 : 0, {
      ...(isPresent ? MORPH_ENTER : MORPH_EXIT),
      onComplete: settle,
    });
    return () => animation.stop();
  }, [fade, progress, ready, isPresent, reduce]);

  return (
    <motion.div
      data-morph-popover-portal=""
      inert={!isPresent}
      style={{
        left,
        top,
        opacity: reduce ? fade : surfaceOpacity,
        pointerEvents: isPresent ? "auto" : "none",
        visibility: layout ? "visible" : "hidden",
      }}
      // A drop-shadow follows the clipped surface; box-shadow would be cut
      // off by its own clip. Caller surface classes retain their old target.
      className={cn(
        "fixed z-[9999]",
        shadow && "[filter:drop-shadow(0_10px_18px_rgba(0,0,0,0.14))]",
      )}
    >
      <motion.div
        ref={ctx.contentRef}
        id={ctx.contentId}
        role="dialog"
        aria-labelledby={ctx.triggerId}
        style={{
          clipPath: reduce ? undefined : clipPath,
          borderRadius: radius,
          maxHeight: layout ? availableHeight : undefined,
          overflowX: "hidden",
          overflowY: "auto",
        }}
        className={cn(
          "overflow-hidden border border-border bg-background",
          className,
        )}
      >
        {children}
      </motion.div>
    </motion.div>
  );
}
