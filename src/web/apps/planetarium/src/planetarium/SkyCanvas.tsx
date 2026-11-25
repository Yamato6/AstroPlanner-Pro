import { useEffect, useRef } from 'react';
import { mountSky } from '@astroplanner/sky-engine';
import styles from './SkyCanvas.module.css';

/** The live sky behind everything. The engine draws on its own loop; React never re-renders it. */
export function SkyCanvas() {
  const container = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (container.current) {
      // The shell's shortcut registry owns the keyboard on this page; right clicks open its menu.
      mountSky(container.current, { keyboard: false, contextMenu: true });
    }
  }, []);

  return <div ref={container} className={styles.sky} />;
}
