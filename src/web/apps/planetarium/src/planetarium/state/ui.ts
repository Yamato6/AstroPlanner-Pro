// Shell state that lives only in the page (spec §7.1, zustand). Host state is in TanStack Query.
import { create } from 'zustand';

interface UiState {
  /** "H": every toolbar hidden, only the sky (spec §3.1). */
  readonly immersive: boolean;
  readonly toggleImmersive: () => void;
  /** The command palette (Ctrl+K, "/"). */
  readonly paletteOpen: boolean;
  readonly setPaletteOpen: (open: boolean) => void;
}

export const useUi = create<UiState>()((set) => ({
  immersive: false,
  toggleImmersive: () => {
    set((state) => ({ immersive: !state.immersive }));
  },
  paletteOpen: false,
  setPaletteOpen: (open) => {
    set({ paletteOpen: open });
  },
}));
