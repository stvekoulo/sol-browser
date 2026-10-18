"use client";
// beUI source: Copyright (c) 2026 Saurabh Chauhan, MIT; see ui/licenses/beui-MIT.txt.

import { ChevronDown } from "lucide-react";
import {
  type HTMLMotionProps,
  motion,
  useReducedMotion,
} from "motion/react";
import {
  cloneElement,
  type ComponentPropsWithRef,
  createContext,
  type ReactElement,
  type ReactNode,
  type Ref,
  useCallback,
  useContext,
  useId,
  useState,
} from "react";
import {
  EASE_OUT,
  SPRING_LAYOUT,
  SPRING_SWAP,
} from "@/lib/ease";
import { cn } from "@/lib/utils";
import { MessageSideContext } from "@/components/agents/message-context";

export type MessageBubbleVariant =
  | "solid"
  | "soft"
  | "tint"
  | "outline"
  | "ghost"
  | "danger";
export type MessageBubbleAlign = "start" | "end";

export interface MessageBubbleContextValue {
  align?: MessageBubbleAlign;
  animateIn: boolean;
  variant: MessageBubbleVariant;
}

export const MessageBubbleContext = createContext<MessageBubbleContextValue>({
  animateIn: true,
  variant: "soft",
});
export const MessageBubbleLayoutContext = createContext<() => void>(() => {});

export interface MessageBubbleProps
  extends Omit<HTMLMotionProps<"div">, "children"> {
  variant?: MessageBubbleVariant;
  /** Defaults to the surrounding Message alignment when omitted. */
  align?: MessageBubbleAlign;
  /** Plays the bubble entrance once when this component mounts. */
  animateIn?: boolean;
  children?: ReactNode;
}

export interface MessageBubbleContentProps
  extends ComponentPropsWithRef<"div"> {
  /** Replaces the content element while preserving bubble styling. */
  render?: ReactElement;
}

export interface MessageBubbleGroupProps extends ComponentPropsWithRef<"div"> {
  spacing?: "compact" | "default";
}

export interface MessageBubbleCollapsibleProps
  extends ComponentPropsWithRef<"div"> {
  open?: boolean;
  defaultOpen?: boolean;
  onOpenChange?: (open: boolean) => void;
  collapsedLines?: 2 | 3 | 4 | 5 | 6;
  moreLabel?: ReactNode;
  lessLabel?: ReactNode;
  contentClassName?: string;
  triggerClassName?: string;
  children?: ReactNode;
}

export function mergeRefs<T>(...refs: Array<Ref<T> | undefined>) {
  return (node: T | null) => {
    for (const ref of refs) {
      if (typeof ref === "function") ref(node);
      else if (ref) ref.current = node;
    }
  };
}

export const BUBBLE_CONTENT_REVEAL = {
  duration: 0.12,
  ease: EASE_OUT,
  delay: 0.04,
} as const;

// Sent bubbles should pop into place quickly with one restrained overshoot.
export const BUBBLE_POP = {
  type: "spring",
  stiffness: 520,
  damping: 27,
  mass: 0.52,
} as const;
