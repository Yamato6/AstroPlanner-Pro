import type { LucideIcon } from 'lucide-react';
import { Maximize2, Minimize2, Minus, X } from 'lucide-react';
import {
  type KeyboardEvent,
  type PointerEvent,
  type ReactNode,
  useEffect,
  useId,
  useRef,
  useState,
} from 'react';
import { IconButton } from '../IconButton';
import classes from './FloatingWindow.module.css';
import {
  type Arrangement,
  type Edges,
  type Rect,
  type WindowSize,
  type WindowState,
  clampRect,
  dockWidthIn,
  frameOf,
  magnet,
  resizeRect,
  snapZone,
} from './layout';

const styles = classes as Readonly<
  Record<
    | 'window'
    | 'active'
    | 'titleBar'
    | 'titleIcon'
    | 'title'
    | 'buttons'
    | 'body'
    | 'handle'
    | 'snapPreview'
    | 'hint',
    string
  >
>;

/** Texts of the window chrome (the page translates them). */
export interface WindowLabels {
  readonly minimize: string;
  readonly maximize: string;
  readonly restore: string;
  readonly close: string;
  /** How to move and resize the window with the keyboard (read by screen readers). */
  readonly keyboardHint: string;
}

export interface FloatingWindowProps {
  readonly title: string;
  readonly icon?: LucideIcon;
  readonly state: WindowState;
  readonly size: WindowSize;
  /** The part of the viewport windows may use. */
  readonly area: Rect;
  /** Frames of the other windows showing, for the magnet. */
  readonly others: readonly Rect[];
  /** In front: its title bar is highlighted and keyboard commands act on it. */
  readonly isActive: boolean;
  readonly zIndex: number;
  /** Changes when the window must take the keyboard focus (opened, cycled to). */
  readonly focusRequest: number;
  readonly labels: WindowLabels;
  readonly onFocus: () => void;
  readonly onClose: () => void;
  readonly onMinimize: () => void;
  readonly onArrange: (arrangement: Arrangement) => void;
  /** The floating rectangle after a move or a resize. */
  readonly onPlace: (rect: Rect) => void;
  readonly onDockWidth: (width: number) => void;
  readonly children: ReactNode;
}

type Gesture =
  | {
      readonly kind: 'move';
      readonly pointerId: number;
      readonly startX: number;
      readonly startY: number;
      readonly from: Rect;
    }
  | {
      readonly kind: 'resize';
      readonly pointerId: number;
      readonly startX: number;
      readonly startY: number;
      readonly from: Rect;
      readonly edges: Edges;
    };

interface Preview {
  readonly frame: Rect;
  readonly snap: Arrangement | null;
}

const HANDLES: readonly (readonly [string, Edges])[] = [
  ['n', { top: true }],
  ['s', { bottom: true }],
  ['w', { left: true }],
  ['e', { right: true }],
  ['nw', { top: true, left: true }],
  ['ne', { top: true, right: true }],
  ['sw', { bottom: true, left: true }],
  ['se', { bottom: true, right: true }],
];

/** Which resize handles an arrangement has: all when floating, the inner edge of a side sheet, none when maximised. */
function handlesFor(arrangement: Arrangement): readonly (readonly [string, Edges])[] {
  switch (arrangement) {
    case 'floating':
      return HANDLES;
    case 'dockLeft':
      return HANDLES.filter(([name]) => name === 'e');
    case 'dockRight':
      return HANDLES.filter(([name]) => name === 'w');
    default:
      return [];
  }
}

const KEY_STEP = 16;

/**
 * A non-modal window over the sky (spec §3.3): moved by its title bar, resized by its edges,
 * snapped to the left or right half (a side sheet) or maximised by dragging it to an edge, and
 * operable with the keyboard while it has the focus: arrows move it, Shift+arrows resize it,
 * Ctrl+←/→ dock it, Ctrl+↑ maximises, Ctrl+↓ restores, Esc closes it.
 */
export function FloatingWindow(props: FloatingWindowProps) {
  const { title, icon: Icon, state, size, area, others, isActive, zIndex, focusRequest, labels } = props;
  const titleId = useId();
  const hintId = useId();
  const section = useRef<HTMLElement>(null);
  const gesture = useRef<Gesture | null>(null);
  const [preview, setPreview] = useState<Preview | null>(null);

  useEffect(() => {
    if (focusRequest > 0 && !section.current?.contains(document.activeElement)) {
      section.current?.focus();
    }
  }, [focusRequest]);

  const frame = preview?.frame ?? frameOf(state, area, size);
  const floating = state.arrangement === 'floating';

  const startMove = (event: PointerEvent<HTMLElement>): void => {
    if (event.button !== 0 || (event.target as HTMLElement).closest('button')) {
      return;
    }
    event.preventDefault();
    section.current?.focus();
    props.onFocus();
    // A docked or maximised window comes off under the pointer with its floating size.
    const from: Rect = floating
      ? frame
      : {
          ...state.rect,
          x: event.clientX - state.rect.width * ((event.clientX - frame.x) / frame.width),
          y: frame.y,
        };
    gesture.current = {
      kind: 'move',
      pointerId: event.pointerId,
      startX: event.clientX,
      startY: event.clientY,
      from,
    };
    event.currentTarget.setPointerCapture(event.pointerId);
  };

  const startResize = (edges: Edges) => (event: PointerEvent<HTMLElement>) => {
    if (event.button !== 0) {
      return;
    }
    event.preventDefault();
    event.stopPropagation();
    props.onFocus();
    gesture.current = {
      kind: 'resize',
      pointerId: event.pointerId,
      startX: event.clientX,
      startY: event.clientY,
      from: frame,
      edges,
    };
    event.currentTarget.setPointerCapture(event.pointerId);
  };

  const onPointerMove = (event: PointerEvent<HTMLElement>): void => {
    const g = gesture.current;
    if (g?.pointerId !== event.pointerId) {
      return;
    }
    const dx = event.clientX - g.startX;
    const dy = event.clientY - g.startY;
    if (g.kind === 'move') {
      const moved = clampRect({ ...g.from, x: g.from.x + dx, y: g.from.y + dy }, area, size);
      setPreview({
        frame: magnet(moved, area, others),
        snap: snapZone({ x: event.clientX, y: event.clientY }, area),
      });
    } else {
      const resized = resizeRect(g.from, g.edges, dx, dy, area, size);
      const docked = state.arrangement === 'dockLeft' || state.arrangement === 'dockRight';
      setPreview({
        frame: docked
          ? {
              ...g.from,
              ...(state.arrangement === 'dockRight'
                ? { x: g.from.x + g.from.width - dockWidthIn(resized.width, area, size) }
                : {}),
              width: dockWidthIn(resized.width, area, size),
            }
          : resized,
        snap: null,
      });
    }
  };

  const onPointerUp = (event: PointerEvent<HTMLElement>): void => {
    const g = gesture.current;
    if (g?.pointerId !== event.pointerId) {
      return;
    }
    gesture.current = null;
    const result = preview;
    setPreview(null);
    if (!result) {
      return; // a click on the title bar
    }
    if (g.kind === 'move') {
      if (result.snap) {
        props.onArrange(result.snap);
      } else {
        props.onPlace(result.frame);
      }
    } else if (state.arrangement === 'floating') {
      props.onPlace(result.frame);
    } else {
      props.onDockWidth(result.frame.width);
    }
  };

  const onKeyDown = (event: KeyboardEvent<HTMLElement>): void => {
    if (event.key === 'Escape' && !event.defaultPrevented) {
      event.preventDefault();
      props.onClose();
      return;
    }
    if (event.target !== section.current || event.altKey || event.metaKey) {
      return; // arrows inside the content belong to the content
    }
    const arrows: Record<string, readonly [number, number]> = {
      ArrowLeft: [-1, 0],
      ArrowRight: [1, 0],
      ArrowUp: [0, -1],
      ArrowDown: [0, 1],
    };
    const arrow = arrows[event.key];
    if (!arrow) {
      return;
    }
    event.preventDefault();
    const [dx, dy] = arrow;
    if (event.ctrlKey) {
      const target: Arrangement =
        event.key === 'ArrowLeft'
          ? 'dockLeft'
          : event.key === 'ArrowRight'
            ? 'dockRight'
            : event.key === 'ArrowUp'
              ? 'maximized'
              : 'floating';
      props.onArrange(target);
      return;
    }
    const from = floating ? frame : state.rect;
    props.onPlace(
      event.shiftKey
        ? clampRect(
            { ...from, width: from.width + dx * KEY_STEP, height: from.height + dy * KEY_STEP },
            area,
            size,
          )
        : clampRect({ ...from, x: from.x + dx * KEY_STEP, y: from.y + dy * KEY_STEP }, area, size),
    );
  };

  const maximized = state.arrangement === 'maximized';
  const snapFrame = preview?.snap
    ? frameOf({ ...state, arrangement: preview.snap, dockWidth: Math.round(area.width / 2) }, area, size)
    : null;

  return (
    <>
      {snapFrame ? (
        <div
          className={styles.snapPreview}
          aria-hidden="true"
          style={{
            transform: `translate(${snapFrame.x}px, ${snapFrame.y}px)`,
            width: snapFrame.width,
            height: snapFrame.height,
            zIndex,
          }}
        />
      ) : null}
      <section
        ref={section}
        role="dialog"
        aria-modal="false"
        aria-labelledby={titleId}
        aria-describedby={hintId}
        tabIndex={-1}
        data-window={state.id}
        data-arrangement={state.arrangement}
        className={isActive ? `${styles.window} ${styles.active}` : styles.window}
        style={{
          transform: `translate(${frame.x}px, ${frame.y}px)`,
          width: frame.width,
          height: frame.height,
          zIndex,
        }}
        onPointerDownCapture={props.onFocus}
        onFocusCapture={props.onFocus}
        onKeyDown={onKeyDown}
        onPointerMove={onPointerMove}
        onPointerUp={onPointerUp}
        onPointerCancel={onPointerUp}
      >
        <header
          className={styles.titleBar}
          onPointerDown={startMove}
          onDoubleClick={(event) => {
            if (!(event.target as HTMLElement).closest('button')) {
              props.onArrange(maximized ? 'floating' : 'maximized');
            }
          }}
        >
          {Icon ? (
            <Icon className={styles.titleIcon} size={16} strokeWidth={1.75} aria-hidden="true" />
          ) : null}
          <h2 id={titleId} className={styles.title}>
            {title}
          </h2>
          <div className={styles.buttons}>
            <IconButton label={labels.minimize} icon={Minus} onPress={props.onMinimize} />
            <IconButton
              label={maximized ? labels.restore : labels.maximize}
              icon={maximized ? Minimize2 : Maximize2}
              onPress={() => {
                props.onArrange(maximized ? 'floating' : 'maximized');
              }}
            />
            <IconButton label={labels.close} icon={X} onPress={props.onClose} />
          </div>
        </header>
        <p id={hintId} className={styles.hint}>
          {labels.keyboardHint}
        </p>
        <div className={styles.body}>{props.children}</div>
        {handlesFor(state.arrangement).map(([name, edges]) => (
          <div
            key={name}
            className={styles.handle}
            data-edge={name}
            aria-hidden="true"
            onPointerDown={startResize(edges)}
          />
        ))}
      </section>
    </>
  );
}
