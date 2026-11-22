import { SkyCanvas } from './SkyCanvas';
import { useHostTheme } from './useHostTheme';

/** R0: the full-window sky only; the shell (toolbars, windows) arrives in R1. */
export function App() {
  useHostTheme();
  return <SkyCanvas />;
}
