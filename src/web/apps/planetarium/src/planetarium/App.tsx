import { QueryClientProvider } from '@tanstack/react-query';
import { I18nProvider } from 'react-aria-components';
import { queryClient, useSnapshot } from './app/hostQueries';
import { ShortcutsContext, useAppShortcuts } from './app/useAppShortcuts';
import { useHostInterface } from './app/useHostInterface';
import { useIdle } from './app/useIdle';
import { CommandPalette } from './shell/CommandPalette';
import { ContextMenu } from './shell/ContextMenu';
import { Dock } from './shell/Dock';
import { Hud } from './shell/Hud';
import { LeftRail } from './shell/LeftRail';
import { ObjectCard } from './shell/ObjectCard';
import { Toasts } from './shell/Toasts';
import { TopBar } from './shell/TopBar';
import { SkyCanvas } from './SkyCanvas';
import { useUi } from './state/ui';
import { useLayoutPersistence } from './windows/useLayoutPersistence';
import { WindowLayer } from './windows/WindowLayer';

/** The shell over the sky (spec §3.2): bars, rail, card, HUD, windows, menu, palette, toasts. */
function Shell() {
  useHostInterface();
  useLayoutPersistence();
  const shortcuts = useAppShortcuts();
  const immersive = useUi((state) => state.immersive);
  const { data: ui } = useSnapshot('ui.get');
  const idle = useIdle(ui?.autoHideToolbars ?? false);
  return (
    <ShortcutsContext value={shortcuts}>
      <SkyCanvas />
      <ContextMenu />
      {immersive ? null : (
        <>
          <TopBar idle={idle} />
          <LeftRail />
          <ObjectCard />
          <Hud />
          <Dock idle={idle} />
          <WindowLayer />
          <Toasts />
        </>
      )}
      <CommandPalette />
    </ShortcutsContext>
  );
}

export function App() {
  return (
    <QueryClientProvider client={queryClient}>
      {/* Day/month/year and a 24-hour clock in React Aria's dates (the strings are English). */}
      <I18nProvider locale="en-GB">
        <Shell />
      </I18nProvider>
    </QueryClientProvider>
  );
}
