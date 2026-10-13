// beUI source: Copyright (c) 2026 Saurabh Chauhan, MIT; see ui/licenses/beui-MIT.txt.
"use client";
// beui.dev/components/blocks/notification-stack

import { ChevronDown, BellOff } from "lucide-react";
import { motion, type Transition, useReducedMotion } from "motion/react";
import {
  type FocusEvent,
  type KeyboardEvent,
  type PointerEvent,
  type ReactNode,
  useCallback,
  useRef,
  useState,
} from "react";
import { ActionSwapText } from "@/components/motion/action-swap";
import { EASE_OUT, SPRING_LAYOUT } from "@/lib/ease";
import { useDismiss } from "@/lib/hooks/use-dismiss";
import { useHoverGesture } from "@/lib/hooks/use-hover-gesture";
import { useTapGesture } from "@/lib/hooks/use-tap-gesture";
import { cn } from "@/lib/utils";

import {STACK_PEEK,STACK_INSET,useControllableExpanded,NotificationCardContent,type NotificationStackProps} from "./notification-stack-parts";
export type {NotificationStackItem,NotificationStackProps,NotificationStackClassNames} from "./notification-stack-parts";
export function NotificationStack({
  items,
  expanded,
  defaultExpanded = false,
  onExpandedChange,
  onViewAll,
  maxVisible = 3,
  collapsedLabel = "Notifications",
  expandedLabel = "View all",
  emptyLabel = "All caught up",
  ariaLabel,
  className,
  classNames,
}: NotificationStackProps) {
  const reduce = useReducedMotion();
  const hasFocus = useRef(false);
  const rootRef = useRef<HTMLDivElement>(null);
  const hover = useHoverGesture();
  // What the last gesture on the stack was, and whether it was already
  // expanded when that gesture started. A click reports neither.
  const tap = useTapGesture<boolean>();
  const [isExpanded, setIsExpanded] = useControllableExpanded({
    expanded,
    defaultExpanded,
    onExpandedChange,
  });
  // Set by the tap that expands the stack, and the reason the outside-tap
  // dismisser exists at all — a hovering pointer has its own way out.
  const [tapExpanded, setTapExpanded] = useState(false);

  const collapse = useCallback(() => {
    setTapExpanded(false);
    setIsExpanded(false);
  }, [setIsExpanded]);

  // A pointer leaving the stack is what collapses it, and a finger never
  // leaves: without this the stack stays open for good once tapped, and when
  // `onViewAll` is set the next tap follows the link instead of closing. The
  // tap that lands somewhere else stands in for the pointer leaving, and it is
  // consumed rather than passed through — the expanded stack covers the page,
  // so the tap that dismisses it is aimed at nothing else.
  useDismiss(tapExpanded && isExpanded, collapse, rootRef, {
    behavior: "consume",
  });

  const visibleItems = isExpanded ? items : items.slice(0, Math.max(1, maxVisible));
  const primaryItem = visibleItems[0];
  const transition: Transition = reduce ? { duration: 0 } : SPRING_LAYOUT;
  const cardTransition: Transition = reduce
    ? { duration: 0 }
    : { duration: 0.32, ease: EASE_OUT };
  const backgroundTransition: Transition = reduce
    ? { duration: 0 }
    : { duration: 0.26, ease: EASE_OUT };

  if (!primaryItem) {
    return (
      <div
        className={cn(
          "flex w-full max-w-[22rem] items-center justify-center gap-2 rounded-3xl bg-muted/70 px-5 py-8 text-sm font-medium text-muted-foreground",
          className,
        )}
      >
        <BellOff className="h-4 w-4" aria-hidden="true" />
        {emptyLabel}
      </div>
    );
  }

  const handleBlur = (event: FocusEvent<HTMLDivElement>) => {
    if (event.currentTarget.contains(event.relatedTarget)) return;
    hasFocus.current = false;
    collapse();
  };

  const handleKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    // A key press is the start of a keyboard activation, never part of a tap:
    // whatever a taken-away gesture left behind must not be read as one.
    tap.drop();
    if (event.key !== "Escape") return;
    event.preventDefault();
    collapse();
    // Focus stays where the keyboard put it. Blurring here collapsed the stack
    // *and* threw the user back to the top of the document.
  };

  const handleClick = () => {
    const gesture = tap.take();
    // Read from where the gesture started, not from now: a browser that
    // focuses the stack on contact expands it mid-tap, and the first tap would
    // then follow `onViewAll` instead of opening the list it was meant to.
    const wasExpanded = gesture ? gesture.state : isExpanded;

    if (!wasExpanded) {
      setIsExpanded(true);
      if (gesture && gesture.pointerType !== "mouse") setTapExpanded(true);
      return;
    }

    if (onViewAll) {
      onViewAll();
      return;
    }

    collapse();
  };

  return (
    <motion.div
      ref={rootRef}
      role="region"
      tabIndex={0}
      initial={false}
      aria-expanded={isExpanded}
      aria-label={ariaLabel ?? (
        isExpanded
          ? `${items.length} notifications. ${expandedLabel}.`
          : `${items.length} notifications. Expand notifications.`
      )}
      // A tap reports as a hover on its way past — enter, leave, then click —
      // so an unfiltered hover path expands, collapses and expands again in
      // the space of one tap, springs and all. The tap has its own route
      // through `handleClick`.
      onPointerEnter={(event: PointerEvent<HTMLDivElement>) => {
        if (hover.enter(event)) setIsExpanded(true);
      }}
      onPointerLeave={(event: PointerEvent<HTMLDivElement>) => {
        if (hover.leave(event) && !hasFocus.current) collapse();
      }}
      onPointerDown={(event: PointerEvent<HTMLDivElement>) => {
        tap.start(event, isExpanded);
      }}
      // The platform can take the gesture away mid-press — a scroll, a system
      // swipe — and no click follows it.
      onPointerCancel={tap.drop}
      onFocus={() => {
        hasFocus.current = true;
        setIsExpanded(true);
      }}
      onBlur={handleBlur}
      onKeyDown={handleKeyDown}

      className={cn(
        "relative z-10 block w-full max-w-[22rem] cursor-pointer rounded-3xl text-left text-foreground outline-none",
        "focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 focus-visible:ring-offset-background",
        className,
      )}
    >
      <span className="relative block p-3">
        <motion.span
          aria-hidden="true"
          layout
          initial={false}
          transition={backgroundTransition}
          className="absolute inset-0 rounded-3xl bg-muted"
        />
        <span
          className={cn(
            "relative z-10 grid gap-1",
            !isExpanded && "pb-2",
            classNames?.stack,
          )}
        >
          {visibleItems.map((item, index) => {
            const isPrimary = index === 0;

            return (
              <motion.span
                key={item.id}
                layout="position"
                initial={false}
                animate={{
                  y: isExpanded ? 0 : index * STACK_PEEK,
                  clipPath: isExpanded
                    ? "inset(0px 0px round 16px)"
                    : `inset(0px ${index * STACK_INSET}px round 16px)`,
                }}
                transition={cardTransition}
                className={cn(
                  "block rounded-2xl border border-border/60 bg-background px-4",
                  classNames?.card,
                )}
                style={{
                  zIndex: visibleItems.length - index,
                  gridColumn: 1,
                  gridRow: isExpanded ? index + 1 : 1,
                }}
              >
                <span
                  inert={!isPrimary&&!isExpanded} aria-hidden={!isPrimary&&!isExpanded}
                  className={cn(
                    "block",
                    !isPrimary && !isExpanded && "invisible",
                  )}
                >
                  <NotificationCardContent
                    item={item}
                    classNames={classNames}
                  />
                </span>
              </motion.span>
            );
          })}
        </span>

        <motion.button
          type="button" onClick={handleClick} aria-expanded={isExpanded}
          layout="position"
          transition={transition}
          className={cn(
            "relative z-10 mt-2 flex min-h-9 items-center gap-2 px-1",
            classNames?.footer,
          )}
        >
          <span
            className={cn(
              "grid size-7 shrink-0 place-items-center rounded-full bg-orange-600 text-xs font-medium text-white shadow-[inset_0_1px_2px_rgb(0_0_0/0.2),inset_0_-1px_0_rgb(255_255_255/0.16)] dark:bg-orange-500",
              classNames?.count,
            )}
          >
            {items.length}
          </span>
          <span className="flex items-center text-sm font-medium">
            <ActionSwapText
              value={isExpanded ? "expanded" : "collapsed"}
              animation="roll"
            >
              {isExpanded ? (
                <span className="inline-flex items-center gap-1">
                  {expandedLabel}
                  <ChevronDown className="size-4" aria-hidden="true" />
                </span>
              ) : (
                collapsedLabel
              )}
            </ActionSwapText>
          </span>
        </motion.button>
      </span>
    </motion.div>
  );
}
