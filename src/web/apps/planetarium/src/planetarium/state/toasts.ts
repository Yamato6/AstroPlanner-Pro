// Non-blocking notices at the bottom right (spec §9.4): announced politely, never focused, gone
// after a few seconds or when closed.
import { create } from 'zustand';

export interface Toast {
  readonly id: number;
  readonly message: string;
  /** An error stays until it is closed. */
  readonly tone: 'info' | 'error';
}

/** How long an informative toast stays. */
export const TOAST_MS = 5000;
/** At most this many are shown; older ones make way. */
const MAX_TOASTS = 3;

interface ToastState {
  readonly toasts: readonly Toast[];
  readonly show: (message: string, tone?: Toast['tone']) => void;
  readonly dismiss: (id: number) => void;
}

let nextId = 1;

export const useToasts = create<ToastState>()((set, get) => ({
  toasts: [],
  show: (message, tone = 'info') => {
    const toast: Toast = { id: nextId++, message, tone };
    set((state) => ({ toasts: [...state.toasts, toast].slice(-MAX_TOASTS) }));
    if (tone === 'info') {
      window.setTimeout(() => {
        get().dismiss(toast.id);
      }, TOAST_MS);
    }
  },
  dismiss: (id) => {
    set((state) => ({ toasts: state.toasts.filter((t) => t.id !== id) }));
  },
}));

/** Shows a toast from anywhere (outside React too). */
export function toast(message: string, tone: Toast['tone'] = 'info'): void {
  useToasts.getState().show(message, tone);
}
