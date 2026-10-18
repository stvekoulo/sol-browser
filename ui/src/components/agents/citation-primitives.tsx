"use client";
// beUI source: Copyright (c) 2026 Saurabh Chauhan, MIT; see ui/licenses/beui-MIT.txt.

import { BookOpenText, ChevronDown, ExternalLink, Globe2 } from "lucide-react";
import { AnimatePresence, motion, useReducedMotion } from "motion/react";
import {
  type ReactNode,
  useCallback,
  useId,
  useState,
} from "react";
import { AgentDisclosure } from "@/components/agents/agent-disclosure";
import { EASE_OUT, SPRING_LAYOUT, SPRING_SWAP } from "@/lib/ease";
import { useFavicon } from "@/lib/hooks/use-favicon";
import { cn } from "@/lib/utils";

export interface CitationItem {
  id: string;
  title: ReactNode;
  domain?: ReactNode;
  url?: string;
  /**
   * Replaces the URL favicon, e.g. a document icon for non-web sources. An svg
   * child is sized to match the favicon; falsy values fall back to the favicon.
   */
  icon?: ReactNode;
}

export interface CitationsProps {
  citations: CitationItem[];
  title?: ReactNode;
  open?: boolean;
  defaultOpen?: boolean;
  onOpenChange?: (open: boolean) => void;
  idPrefix?: string;
  className?: string;
}

export interface CitationProps {
  citationId: string;
  index: number;
  /** Must match the related Citations idPrefix. */
  idPrefix: string;
  /**
   * Accessible name for the marker. Defaults to `View citation {index}`. Include
   * the index so voice control users can say the number they see.
   */
  label?: string;
  className?: string;
}

export interface CitationListProps {
  citations: CitationItem[];
  idPrefix?: string;
  className?: string;
}

export interface CitationStackProps {
  citations: CitationItem[];
  limit?: number;
  className?: string;
}

export function citationTargetId(prefix: string, citationId: string) {
  return `${prefix}-${citationId.replace(/[^a-zA-Z0-9_-]/g, "-")}`;
}

export function Citation({
  citationId,
  index,
  idPrefix,
  label,
  className,
}: CitationProps) {
  return (
    <a
      href={`#${citationTargetId(idPrefix, citationId)}`}
      aria-label={label ?? `View citation ${index}`}
      className={cn(
        "mx-0.5 inline-flex min-w-4 -translate-y-0.5 items-center justify-center rounded-md bg-muted/60 px-1 py-0.5 text-[10px] font-semibold leading-none text-muted-foreground no-underline outline-none transition-colors hover:text-foreground focus-visible:ring-2 focus-visible:ring-ring",
        className,
      )}
    >
      {index}
    </a>
  );
}

export function CitationFavicon({
  url,
  icon,
  className,
}: {
  url?: string;
  /** Drawn instead of the favicon when truthy. */
  icon?: ReactNode;
  className?: string;
}) {
  const favicon = useFavicon(icon ? undefined : url);

  return (
    <span
      aria-hidden="true"
      className={cn(
        "grid size-5 shrink-0 place-items-center text-muted-foreground [&>svg]:size-3.5",
        className,
      )}
    >
      {icon ? (
        icon
      ) : favicon.src ? (
        // biome-ignore lint/performance/noImgElement: Dynamic cross-site favicons keep this framework-agnostic registry component portable.
        <img
          ref={favicon.ref}
          src={favicon.src}
          alt=""
          width={16}
          height={16}
          referrerPolicy="no-referrer"
          className="size-4 rounded-sm object-contain"
        />
      ) : (
        <Globe2 className="size-3.5" />
      )}
    </span>
  );
}
