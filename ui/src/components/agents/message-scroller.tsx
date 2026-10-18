"use client";
// beUI source: Copyright (c) 2026 Saurabh Chauhan, MIT; see ui/licenses/beui-MIT.txt.

import { useReducedMotion } from "motion/react";
import {
  type ComponentPropsWithRef,
  type Ref,
  useCallback,
  useEffect,
  useLayoutEffect,
  useRef,
  useState,
} from "react";
import {
  PreviewRail,
  type PreviewRailItem,
} from "@/components/motion/preview-rail";
import { cn } from "@/lib/utils";

import { useMessageRail } from "./use-message-rail";

export interface MessageScrollerProps extends ComponentPropsWithRef<"div"> {
  /** Keep streamed output pinned while the reader remains near the end. */
  followOutput?: boolean;
  /** Distance from the end that still counts as following the output. */
  followThreshold?: number;
  /** Smoothly follow growing content. */
  smooth?: boolean;
  /** Reports when the reader leaves or returns to the live edge. */
  onFollowChange?: (following: boolean) => void;
  /** Accessible label for the scrollable transcript. */
  label?: string;
  /** Marks the transcript as waiting for more streamed content. */
  busy?: boolean;
  /** Adds a compact rail for navigating between rendered Message rows. */
  navigation?: "rail";
  /** Accessible label for the optional message navigation rail. */
  navigationLabel?: string;
  viewportClassName?: string;
  contentClassName?: string;
  railClassName?: string;
  viewportRef?: Ref<HTMLElement>;
  viewportProps?: Omit<
    ComponentPropsWithRef<"section">,
    "children" | "className" | "ref"
  >;
  contentProps?: Omit<
    ComponentPropsWithRef<"div">,
    "children" | "className" | "ref"
  >;
}

export function MessageScroller({
  followOutput = true,
  followThreshold = 56,
  smooth = true,
  onFollowChange,
  label = "Conversation",
  busy,
  navigation,
  navigationLabel = "Message navigation",
  viewportClassName,
  contentClassName,
  railClassName,
  viewportRef: externalViewportRef,
  viewportProps,
  contentProps,
  className,
  children,
  ...props
}: MessageScrollerProps) {
  const reduce = useReducedMotion() ?? false;
  const viewportRef = useRef<HTMLElement>(null);
  const contentRef = useRef<HTMLDivElement>(null);
  const followingRef = useRef(followOutput);
  const programmaticScrollRef = useRef(false);
  const scrollTimerRef = useRef<number | undefined>(undefined);
  const frameRef = useRef<number | undefined>(undefined);
  const { railItems, activeRailId, setActiveRailId, railOverflowing, railTargetsRef, updateActiveRailItem, scheduleRailSync } = useMessageRail(navigation, followThreshold, viewportRef, contentRef);
  const {
    onScroll: onViewportScroll,
    onWheel: onViewportWheel,
    onTouchStart: onViewportTouchStart,
    onKeyDown: onViewportKeyDown,
    ...restViewportProps
  } = viewportProps ?? {};

  const setViewportRef = useCallback(
    (node: HTMLElement | null) => {
      viewportRef.current = node;
      if (typeof externalViewportRef === "function") {
        externalViewportRef(node);
      } else if (externalViewportRef) {
        externalViewportRef.current = node;
      }
    },
    [externalViewportRef],
  );

  const setFollowing = useCallback(
    (next: boolean) => {
      if (followingRef.current === next) return;
      followingRef.current = next;
      onFollowChange?.(next);
    },
    [onFollowChange],
  );

  const scrollToEnd = useCallback((behavior: ScrollBehavior) => {
    const viewport = viewportRef.current;
    if (!viewport) return;

    programmaticScrollRef.current = true;
    if (typeof viewport.scrollTo === "function") {
      viewport.scrollTo({ top: viewport.scrollHeight, behavior });
    } else {
      viewport.scrollTop = viewport.scrollHeight;
    }
    if (scrollTimerRef.current) window.clearTimeout(scrollTimerRef.current);
    scrollTimerRef.current = window.setTimeout(() => {
      programmaticScrollRef.current = false;
    }, behavior === "smooth" ? 320 : 0);
  }, []);

  const handleScroll = useCallback(() => {
    const viewport = viewportRef.current;
    if (!viewport || programmaticScrollRef.current) return;

    const distance =
      viewport.scrollHeight - viewport.scrollTop - viewport.clientHeight;
    setFollowing(distance <= followThreshold);
    updateActiveRailItem();
  }, [followThreshold, setFollowing, updateActiveRailItem]);

  const leaveLiveEdge = useCallback(() => {
    programmaticScrollRef.current = false;
  }, []);

  useLayoutEffect(() => {
    followingRef.current = followOutput;
    if (!followOutput) return;

    frameRef.current = requestAnimationFrame(() => scrollToEnd("auto"));
    return () => {
      if (frameRef.current) cancelAnimationFrame(frameRef.current);
    };
  }, [followOutput, scrollToEnd]);

  useEffect(() => {
    const content = contentRef.current;
    if (!content || typeof ResizeObserver === "undefined") return;

    const observer = new ResizeObserver(() => {
      scheduleRailSync();
      if (!followOutput || !followingRef.current) return;
      scrollToEnd(reduce || !smooth ? "auto" : "smooth");
    });
    observer.observe(content);

    return () => observer.disconnect();
  }, [followOutput, reduce, scheduleRailSync, scrollToEnd, smooth]);

  useEffect(
    () => () => {
      if (scrollTimerRef.current) window.clearTimeout(scrollTimerRef.current);
      if (frameRef.current) cancelAnimationFrame(frameRef.current);
    },
    [],
  );

  const scrollToRailItem = useCallback(
    (item: PreviewRailItem) => {
      const viewport = viewportRef.current;
      const target = railTargetsRef.current.get(item.id);
      if (!viewport || !target) return;

      const lastItem = railItems.at(-1)?.id === item.id;
      setActiveRailId(item.id);
      if (lastItem) {
        setFollowing(true);
        scrollToEnd(reduce || !smooth ? "auto" : "smooth");
        return;
      }

      setFollowing(false);
      programmaticScrollRef.current = true;
      const viewportRect = viewport.getBoundingClientRect();
      const targetRect = target.getBoundingClientRect();
      const top =
        viewport.scrollTop +
        targetRect.top -
        viewportRect.top -
        (viewport.clientHeight - targetRect.height) / 2;
      const behavior = reduce || !smooth ? "auto" : "smooth";

      if (typeof viewport.scrollTo === "function") {
        viewport.scrollTo({ top, behavior });
      } else {
        viewport.scrollTop = top;
      }
      if (scrollTimerRef.current) window.clearTimeout(scrollTimerRef.current);
      scrollTimerRef.current = window.setTimeout(() => {
        programmaticScrollRef.current = false;
      }, behavior === "smooth" ? 320 : 0);
    },
    [railItems, reduce, scrollToEnd, setFollowing, smooth],
  );

  const viewport = (
    <section
      ref={setViewportRef}
      aria-label={label}
      {...restViewportProps}
      onScroll={(event) => {
        handleScroll();
        onViewportScroll?.(event);
      }}
      onWheel={(event) => {
        leaveLiveEdge();
        onViewportWheel?.(event);
      }}
      onTouchStart={(event) => {
        leaveLiveEdge();
        onViewportTouchStart?.(event);
      }}
      onKeyDown={(event) => {
        if (["ArrowUp", "PageUp", "Home"].includes(event.key)) {
          leaveLiveEdge();
        }
        onViewportKeyDown?.(event);
      }}
      className={cn(
        "h-full overflow-y-auto overscroll-contain outline-none [overflow-anchor:none] focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-ring",
        navigation === "rail"
          ? "[-ms-overflow-style:none] [scrollbar-width:none] [&::-webkit-scrollbar]:hidden"
          : "[scrollbar-gutter:stable]",
        viewportClassName,
        navigation === "rail" && railOverflowing && "pr-10",
      )}
    >
      <div
        ref={contentRef}
        role="log"
        aria-live="polite"
        aria-relevant="additions text"
        aria-busy={busy}
        className={contentClassName}
        {...contentProps}
      >
        {children}
      </div>
    </section>
  );

  return (
    <div
      data-slot="message-scroller"
      className={cn("min-h-0", className)}
      {...props}
    >
      {navigation === "rail" ? (
        <PreviewRail
          items={railOverflowing ? railItems : []}
          label={navigationLabel}
          activeId={activeRailId}
          onItemSelect={scrollToRailItem}
          previewSide="before"
          highlightActive
          itemSize={14}
          className="h-full min-h-0 overflow-hidden"
          previewContainerClassName="right-8 left-3"
          previewClassName="mr-1 w-64 max-w-full [&_[data-slot=preview-rail-card]]:h-20 [&_[data-slot=preview-rail-card]]:overflow-hidden [&_[data-slot=preview-rail-card]]:p-3 [&_[data-slot=preview-rail-title]]:line-clamp-1 [&_[data-slot=preview-rail-title]]:text-xs [&_[data-slot=preview-rail-title]]:leading-4 [&_[data-slot=preview-rail-description]]:line-clamp-2 [&_[data-slot=preview-rail-description]]:text-xs [&_[data-slot=preview-rail-description]]:leading-4"
          railClassName={cn(
            "absolute inset-y-3 right-1 w-7 content-center py-1 [&_[data-slot=preview-rail-item]]:w-7 [&_[data-slot=preview-rail-item]]:justify-end [&_[data-slot=preview-rail-tick]]:h-px [&_[data-slot=preview-rail-tick]]:w-4 [&_[data-slot=preview-rail-tick]]:origin-right",
            railOverflowing
              ? "pointer-events-auto opacity-100"
              : "pointer-events-none opacity-0",
            railClassName,
          )}
        >
          {viewport}
        </PreviewRail>
      ) : (
        viewport
      )}
    </div>
  );
}
