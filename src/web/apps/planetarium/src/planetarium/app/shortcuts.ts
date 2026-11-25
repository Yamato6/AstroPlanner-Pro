// The one keyboard registry (spec §3.6): every shortcut is declared here, so Help and the command
// palette can list them, and none fires while a text field has focus.
import { useEffect, useRef } from 'react';

/** Where Help lists a shortcut. */
export type ShortcutGroup = 'time' | 'sky' | 'interface' | 'windows';

export interface Shortcut {
  /** Stable id; also the i18n key under "shortcuts". */
  readonly id: string;
  readonly group: ShortcutGroup;
  /** `KeyboardEvent.key` (case-insensitive for letters), e.g. " ", "F5", "c". */
  readonly key: string;
  /** Shown to the user, e.g. "Space". */
  readonly display: string;
  readonly ctrl?: boolean;
  readonly shift?: boolean;
  readonly run: () => void;
}

/** True when typing goes into the focused element, so a shortcut must not steal the key. */
export function isTextEntry(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) {
    return false;
  }
  if (target.isContentEditable) {
    return true;
  }
  if (target instanceof HTMLTextAreaElement || target instanceof HTMLSelectElement) {
    return true;
  }
  if (target instanceof HTMLInputElement) {
    return !['button', 'checkbox', 'radio', 'range', 'submit', 'reset'].includes(target.type);
  }
  return false;
}

/** The shortcut an event triggers, if any. */
export function match(shortcuts: readonly Shortcut[], event: KeyboardEvent): Shortcut | undefined {
  if (event.altKey || event.metaKey || event.repeat || isTextEntry(event.target)) {
    return undefined;
  }
  return shortcuts.find(
    (s) =>
      s.key.toLowerCase() === event.key.toLowerCase() &&
      (s.ctrl ?? false) === event.ctrlKey &&
      (s.shift ?? false) === event.shiftKey,
  );
}

/** Installs the shortcuts on the window while the component is mounted. */
export function useShortcuts(shortcuts: readonly Shortcut[]): void {
  const current = useRef(shortcuts);
  useEffect(() => {
    current.current = shortcuts;
  }, [shortcuts]);

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent): void => {
      const shortcut = match(current.current, event);
      if (shortcut) {
        event.preventDefault();
        shortcut.run();
      }
    };
    window.addEventListener('keydown', onKeyDown);
    return () => {
      window.removeEventListener('keydown', onKeyDown);
    };
  }, []);
}
