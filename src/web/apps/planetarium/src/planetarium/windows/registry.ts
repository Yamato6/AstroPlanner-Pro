// Every window of the planetarium UI (spec §3.4), in rail order. Titles are i18n keys
// "windows.<id>.title". R1b ships Settings and Help; the others arrive with R4 and meanwhile open
// a window that says so.
import type { WindowLayoutState } from '@astroplanner/bridge-client';
import type { WindowSize } from '@astroplanner/ui-kit';
import {
  CalendarClock,
  CircleHelp,
  Crosshair,
  type LucideIcon,
  MapPin,
  MoonStar,
  NotebookPen,
  Settings,
  Sparkles,
  Telescope,
} from 'lucide-react';

export type WindowId = WindowLayoutState['id'];

export interface WindowDefinition {
  readonly icon: LucideIcon;
  readonly size: WindowSize;
  /** Rail group: the planner's windows, or the app's own (at the end of the rail). */
  readonly group: 'planner' | 'app';
  /** Its key, shown in the rail's tooltip. */
  readonly shortcut?: string;
}

const planner: WindowSize = { width: 560, height: 520, minWidth: 360, minHeight: 240 };

export const windowDefinitions: Readonly<Record<WindowId, WindowDefinition>> = {
  tonight: { icon: MoonStar, size: planner, group: 'planner' },
  targets: { icon: Crosshair, size: planner, group: 'planner' },
  plan: { icon: CalendarClock, size: planner, group: 'planner' },
  logbook: { icon: NotebookPen, size: planner, group: 'planner' },
  equipment: { icon: Telescope, size: planner, group: 'planner' },
  sites: { icon: MapPin, size: planner, group: 'planner' },
  milkyWay: { icon: Sparkles, size: planner, group: 'planner' },
  settings: {
    icon: Settings,
    size: { width: 460, height: 520, minWidth: 340, minHeight: 260 },
    group: 'app',
    shortcut: 'Ctrl+,',
  },
  help: {
    icon: CircleHelp,
    size: { width: 600, height: 640, minWidth: 380, minHeight: 300 },
    group: 'app',
    shortcut: 'F1',
  },
};

export const windowIds = Object.keys(windowDefinitions) as WindowId[];
