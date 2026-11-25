import { QueryClientProvider } from '@tanstack/react-query';
import { queryClient } from './app/hostQueries';
import { ShortcutsContext, useAppShortcuts } from './app/useAppShortcuts';
import { useHostInterface } from './app/useHostInterface';
import { LeftRail } from './shell/LeftRail';
import { TopBar } from './shell/TopBar';
import { SkyCanvas } from './SkyCanvas';
import { useUi } from './state/ui';
import { useLayoutPersistence } from './windows/useLayoutPersistence';
import { WindowLayer } from './windows/WindowLayer';

/** The shell over the sky: top bar, rail and floating windows (R1a, R1b); dock and HUD follow (R1c). */
function Shell() {
  useHostInterface();
  useLayoutPersistence();
  const shortcuts = useAppShortcuts();
  const immersive = useUi((state) => state.immersive);
  return (
    <ShortcutsContext value={shortcuts}>
      <SkyCanvas />
      {immersive ? null : (
        <>
          <TopBar />
          <LeftRail />
          <WindowLayer />
        </>
      )}
    </ShortcutsContext>
  );
}

export function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <Shell />
    </QueryClientProvider>
  );
}
