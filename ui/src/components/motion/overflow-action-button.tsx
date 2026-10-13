// beUI source: Copyright (c) 2026 Saurabh Chauhan, MIT; see ui/licenses/beui-MIT.txt.
"use client";
// beui.dev/components/blocks/overflow-actions

import { MoreHorizontal, X } from "lucide-react";
import {
  AnimatePresence,
  motion,
  useReducedMotion,
  type Transition,
  type Variants,
} from "motion/react";
import {
  useCallback,
  useId,
  useLayoutEffect,
  useRef,
  useState,
  type ReactNode,
} from "react";
import { EASE_OUT } from "@/lib/ease";
import { useHoverCapable } from "@/lib/hooks/use-hover-capable";
import { cn } from "@/lib/utils";

import {ACTION_SIZE_CLASS,ICON_SIZE_CLASS,type OverflowActionItem,type OverflowActionsSize} from "./overflow-actions";
export function ActionButton({
  item,
  size,
  reduce,
  canHover,
  overflow,
  visible = true,
  variants,
  onAction,
  layoutTransition,
  className,
  iconClassName,
  labelClassName,
}: {
  item: OverflowActionItem;
  size: OverflowActionsSize;
  reduce: boolean | null;
  canHover: boolean;
  overflow?: boolean;
  visible?: boolean;
  variants?: Variants;
  onAction: (item: OverflowActionItem) => void;
  layoutTransition: Transition;
  className?: string;
  iconClassName?: string;
  labelClassName?: string;
}) {
  const label = typeof item.label === "string" ? item.label : undefined;

  return (
    <motion.span
      layout="position"
      variants={variants}
      initial={variants ? (reduce ? { opacity: 0 } : "hidden") : undefined}
      animate={variants ? (reduce ? { opacity: 1 } : "visible") : undefined}
      exit={variants ? (reduce ? { opacity: 0 } : "exit") : undefined}
      whileTap={reduce || item.disabled ? undefined : { scale: 0.97 }}
      whileHover={
        reduce || !canHover || item.disabled ? undefined : { scale: 1.008 }
      }
      transition={layoutTransition}
      className="inline-flex shrink-0"
    >
      <button
        type="button"
        disabled={item.disabled}
        aria-label={item.ariaLabel}
        tabIndex={overflow && !visible ? -1 : undefined}
        title={label}
        onClick={() => onAction(item)}
        className={cn(
          "inline-flex shrink-0 items-center justify-center rounded-full bg-background font-medium text-foreground outline-none",
          "disabled:pointer-events-none disabled:opacity-45",
          "focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 focus-visible:ring-offset-background",
          ACTION_SIZE_CLASS[size],
          className,
        )}
      >
        {item.icon ? (
          <span
            className={cn(
              "inline-flex shrink-0 items-center justify-center",
              ICON_SIZE_CLASS[size],
              iconClassName,
            )}
          >
            {item.icon}
          </span>
        ) : null}
        <span className={cn("whitespace-nowrap", labelClassName)}>
          {item.label}
        </span>
      </button>
    </motion.span>
  );
}
