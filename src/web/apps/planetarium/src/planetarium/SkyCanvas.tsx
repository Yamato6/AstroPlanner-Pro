import { useEffect, useRef } from 'react';
import { mountSky } from '@astroplanner/sky-engine';
import styles from './SkyCanvas.module.css';

/** The live sky behind everything. The engine draws on its own loop; React never re-renders it. */
export function SkyCanvas() {
  const container = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (container.current) {
      mountSky(container.current);
    }
  }, []);

  return <div ref={container} className={styles.sky} />;
}
