import { FloatingWindow, type Rect, frameOf, topWindow } from '@astroplanner/ui-kit';
import { useEffect, useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { useWindows } from '../state/windows';
import { HelpWindow } from './HelpWindow';
import { PlaceholderWindow } from './PlaceholderWindow';
import { type WindowId, windowDefinitions } from './registry';
import { SettingsWindow } from './SettingsWindow';

/** What the windows may cover: the viewport less the top bar, the rail and a small margin. */
export function windowArea(viewportWidth: number, viewportHeight: number): Rect {
  const top = 60; // 8 + top bar 44 + 8
  const left = 64; // 8 + rail 48 + 8
  const margin = 8;
  return {
    x: left,
    y: top,
    width: Math.max(0, viewportWidth - left - margin),
    height: Math.max(0, viewportHeight - top - margin),
  };
}

function Content({ id }: { readonly id: WindowId }) {
  switch (id) {
    case 'settings':
      return <SettingsWindow />;
    case 'help':
      return <HelpWindow />;
    default:
      return <PlaceholderWindow id={id} />;
  }
}

/** The open windows over the sky, in stacking order (spec §3.3). */
export function WindowLayer() {
  const { t } = useTranslation();
  const layout = useWindows((state) => state.layout);
  const area = useWindows((state) => state.area);
  const focus = useWindows((state) => state.focus);
  const actions = useWindows.getState();

  useEffect(() => {
    const update = (): void => {
      useWindows.getState().setArea(windowArea(window.innerWidth, window.innerHeight));
    };
    update();
    window.addEventListener('resize', update);
    return () => {
      window.removeEventListener('resize', update);
    };
  }, []);

  const labels = useMemo(
    () => ({
      minimize: t('window.minimize'),
      maximize: t('window.maximize'),
      restore: t('window.restore'),
      close: t('window.close'),
      keyboardHint: t('window.keyboardHint'),
    }),
    [t],
  );

  const top = topWindow(layout);
  const shown = Object.values(layout)
    .filter((w) => w.isOpen && !w.isMinimized && Object.hasOwn(windowDefinitions, w.id))
    .sort((a, b) => a.zOrder - b.zOrder);
  const frames = new Map(
    shown.map((w) => [w.id, frameOf(w, area, windowDefinitions[w.id as WindowId].size)]),
  );

  return (
    <>
      {shown.map((w, index) => {
        const id = w.id as WindowId;
        const definition = windowDefinitions[id];
        return (
          <FloatingWindow
            key={id}
            title={t(`windows.${id}.title`)}
            icon={definition.icon}
            state={w}
            size={definition.size}
            area={area}
            others={shown.flatMap((o) => (o.id === id ? [] : (frames.get(o.id) ?? [])))}
            isActive={top === id}
            zIndex={10 + index}
            focusRequest={focus.id === id ? focus.serial : 0}
            labels={labels}
            onFocus={() => {
              actions.bringToFront(id);
            }}
            onClose={() => {
              actions.close(id);
            }}
            onMinimize={() => {
              actions.minimize(id);
            }}
            onArrange={(arrangement) => {
              actions.arrange(id, arrangement);
            }}
            onPlace={(rect) => {
              actions.place(id, rect);
            }}
            onDockWidth={(width) => {
              actions.dock(id, width);
            }}
          >
            <Content id={id} />
          </FloatingWindow>
        );
      })}
    </>
  );
}
