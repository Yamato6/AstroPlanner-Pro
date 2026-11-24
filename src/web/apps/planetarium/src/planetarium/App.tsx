import { QueryClientProvider } from '@tanstack/react-query';
import { queryClient } from './app/hostQueries';
import { useSkyShortcuts } from './app/useSkyShortcuts';
import { TopBar } from './shell/TopBar';
import { SkyCanvas } from './SkyCanvas';
import { useUi } from './state/ui';
import { useHostTheme } from './useHostTheme';

/** The shell over the sky. R1a: top bar and the keyboard; windows, rail and dock follow (R1b, R1c). */
function Shell() {
  useHostTheme();
  useSkyShortcuts();
  const immersive = useUi((state) => state.immersive);
  return (
    <>
      <SkyCanvas />
      {immersive ? null : <TopBar />}
    </>
  );
}

export function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <Shell />
    </QueryClientProvider>
  );
}
