// Every keyboard shortcut of the page (spec §3.6): the sky's, the interface's and the windows'.
// They are installed once here; Help lists the same array.
import { createContext, useContext, useMemo } from 'react';
import { useWindows } from '../state/windows';
import { useSnapshot, useUiCommand } from './hostQueries';
import { type Shortcut, useShortcuts } from './shortcuts';
import { useSkyShortcuts } from './useSkyShortcuts';

function useShellShortcuts(): readonly Shortcut[] {
  const { data: ui } = useSnapshot('ui.get');
  const setTheme = useUiCommand('ui.setTheme');
  const setFullScreen = useUiCommand('ui.setFullScreen');
  const { open, closeTop, cycle } = useWindows.getState();

  return useMemo(
    (): Shortcut[] => [
      {
        id: 'nightVision',
        group: 'interface',
        key: 'n',
        ctrl: true,
        shift: true,
        display: 'Ctrl+Shift+N',
        run: () => {
          setTheme.mutate({ theme: ui?.theme === 'night' ? 'dark' : 'night' });
        },
      },
      {
        id: 'fullScreen',
        group: 'interface',
        key: 'F11',
        display: 'F11',
        run: () => {
          setFullScreen.mutate({ enabled: !(ui?.isFullScreen ?? false) });
        },
      },
      {
        id: 'settings',
        group: 'windows',
        key: ',',
        ctrl: true,
        display: 'Ctrl+,',
        run: () => {
          open('settings');
        },
      },
      {
        id: 'help',
        group: 'windows',
        key: 'F1',
        display: 'F1',
        run: () => {
          open('help');
        },
      },
      { id: 'closeWindow', group: 'windows', key: 'w', ctrl: true, display: 'Ctrl+W', run: closeTop },
      {
        id: 'nextWindow',
        group: 'windows',
        key: 'Tab',
        ctrl: true,
        display: 'Ctrl+Tab',
        run: () => {
          cycle(1);
        },
      },
      {
        id: 'previousWindow',
        group: 'windows',
        key: 'Tab',
        ctrl: true,
        shift: true,
        display: 'Ctrl+Shift+Tab',
        run: () => {
          cycle(-1);
        },
      },
    ],
    [ui, setTheme, setFullScreen, open, closeTop, cycle],
  );
}

/** Installs every shortcut and returns them (for Help). */
export function useAppShortcuts(): readonly Shortcut[] {
  const sky = useSkyShortcuts();
  const shell = useShellShortcuts();
  const all = useMemo(() => [...sky, ...shell], [sky, shell]);
  useShortcuts(all);
  return all;
}

/** The page's shortcuts, for Help. */
export const ShortcutsContext = createContext<readonly Shortcut[]>([]);

export function useShortcutList(): readonly Shortcut[] {
  return useContext(ShortcutsContext);
}
