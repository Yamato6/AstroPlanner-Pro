import { type SkyEventMap, type SkyPoint, skyEvents } from '@astroplanner/sky-engine';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { formatDec, formatDegrees, formatRa } from '../app/format';
import { useSnapshot } from '../app/hostQueries';
import classes from './Hud.module.css';

const styles = classes as Readonly<Record<'hud' | 'list' | 'item' | 'name' | 'value', string>>;

/**
 * The HUD (spec §3.2): field of view, the sky under the pointer (J2000 RA/Dec, Alt/Az), the
 * zenith sky quality and the faintest stars drawn. It follows the sky's own events, so moving the
 * pointer costs no messages to the app.
 */
export function Hud() {
  const { t } = useTranslation();
  const { data: observer } = useSnapshot('observer.get');
  const { data: sky } = useSnapshot('sky.get');
  const [view, setView] = useState<SkyEventMap['view'] | null>(null);
  const [cursor, setCursor] = useState<SkyPoint | null>(null);
  const [tracking, setTracking] = useState(false);

  useEffect(() => {
    const stopView = skyEvents.on('view', setView);
    const stopCursor = skyEvents.on('cursor', setCursor);
    const stopTracking = skyEvents.on('tracking', (event) => {
      setTracking(event.active);
    });
    return () => {
      stopView();
      stopCursor();
      stopTracking();
    };
  }, []);

  const limit = view?.starLimitMagnitude ?? sky?.starLimitMagnitude;
  const dash = '—';

  return (
    <div className={styles.hud} role="group" aria-label={t('hud.label')}>
      <dl className={styles.list}>
        <div className={styles.item}>
          <dt className={styles.name}>{t('hud.fov')}</dt>
          <dd className={styles.value}>
            {view ? formatDegrees(view.fovDegrees, view.fovDegrees < 10 ? 2 : 1) : dash}
          </dd>
        </div>
        <div className={styles.item}>
          <dt className={styles.name}>{t('hud.raDec')}</dt>
          <dd className={styles.value}>
            {cursor ? `${formatRa(cursor.raHours)} ${formatDec(cursor.decDegrees)}` : dash}
          </dd>
        </div>
        <div className={styles.item}>
          <dt className={styles.name}>{t('hud.altAz')}</dt>
          <dd className={styles.value}>
            {cursor
              ? `${formatDegrees(cursor.altitudeDegrees)} ${formatDegrees(cursor.azimuthDegrees)}`
              : dash}
          </dd>
        </div>
        <div className={styles.item}>
          <dt className={styles.name}>{t('hud.sqm')}</dt>
          <dd className={styles.value}>{observer ? observer.zenithSqm.toFixed(2) : dash}</dd>
        </div>
        <div className={styles.item}>
          <dt className={styles.name}>{t('hud.limit')}</dt>
          <dd className={styles.value}>{limit === undefined ? dash : limit.toFixed(1)}</dd>
        </div>
        {tracking ? (
          <div className={styles.item}>
            <dt className={styles.name}>{t('hud.tracking')}</dt>
            <dd className={styles.value}>{t('hud.trackingOn')}</dd>
          </div>
        ) : null}
      </dl>
    </div>
  );
}
