import { Panel, ToggleIconButton } from '@astroplanner/ui-kit';
import { Clock, Flashlight, MapPin, Moon } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { useSnapshot, useUiCommand } from '../app/hostQueries';
import { host } from '../host';
import classes from './TopBar.module.css';
import { useSkyTime } from './useSkyTime';

const styles = classes as Readonly<
  Record<
    'bar' | 'brand' | 'mark' | 'name' | 'badge' | 'facts' | 'fact' | 'value' | 'muted' | 'status' | 'tools',
    string
  >
>;

/** "2026-01-15T19:39:00" (site wall clock) → "19:39". */
export function wallClock(local: string | null | undefined): string | null {
  return local ? local.slice(11, 16) : null;
}

/** The site's local time of a UTC instant, "21:42". */
export function siteTime(utcMs: number, timeZone: string): string {
  try {
    return new Intl.DateTimeFormat('en-GB', {
      timeZone,
      hour: '2-digit',
      minute: '2-digit',
      hour12: false,
    }).format(utcMs);
  } catch {
    return new Date(utcMs).toISOString().slice(11, 16); // unknown zone: UTC rather than nothing
  }
}

/** Site, tonight's darkness and the sky time (spec §3.2). Search and menus join it in R1c. */
export function TopBar() {
  const { t } = useTranslation();
  const observer = useSnapshot('observer.get');
  const sky = useSnapshot('sky.get');
  const skyTime = useSkyTime(sky.data, sky.dataUpdatedAt);
  const ui = useSnapshot('ui.get');
  const setTheme = useUiCommand('ui.setTheme');

  const night = observer.data?.night;
  const dusk = wallClock(night?.astronomicalDuskLocal);
  const dawn = wallClock(night?.astronomicalDawnLocal);

  return (
    <Panel className={styles.bar} role="banner" aria-label={t('topBar.label')}>
      <div className={styles.brand}>
        <span className={styles.mark} aria-hidden="true" />
        <span className={styles.name}>{t('app.name')}</span>
        <span className={styles.badge}>{t('app.preview')}</span>
      </div>

      {!host.connected ? (
        <p className={styles.status}>{t('topBar.offline')}</p>
      ) : observer.isPending || sky.isPending ? (
        <p className={styles.status} aria-live="polite">
          {t('topBar.loading')}
        </p>
      ) : (
        <dl className={styles.facts}>
          {observer.data ? (
            <div className={styles.fact}>
              <dt>
                <MapPin size={15} aria-label={t('topBar.site')} />
              </dt>
              <dd className={styles.value}>{observer.data.locationName}</dd>
            </div>
          ) : null}
          {observer.data ? (
            <div className={styles.fact}>
              <dt>
                <Moon size={15} aria-hidden="true" />
              </dt>
              <dd className={styles.muted}>
                {dusk && dawn ? t('topBar.night', { dusk, dawn }) : t('topBar.noDarkness')}
              </dd>
            </div>
          ) : null}
          {sky.data && skyTime !== null && observer.data ? (
            <div className={styles.fact}>
              <dt>
                <Clock size={15} aria-label={t('topBar.skyTime')} />
              </dt>
              <dd className={styles.value}>
                <time dateTime={new Date(skyTime).toISOString()}>
                  {siteTime(skyTime, observer.data.timeZoneId)}
                </time>{' '}
                <span className={styles.muted}>
                  {sky.data.isPlaying ? t('topBar.playing', { rate: sky.data.rate }) : t('topBar.paused')}
                </span>
              </dd>
            </div>
          ) : null}
        </dl>
      )}

      <div className={styles.tools}>
        <ToggleIconButton
          label={t('topBar.nightVision')}
          icon={Flashlight}
          shortcut="Ctrl+Shift+N"
          isDisabled={!ui.data}
          isSelected={ui.data?.theme === 'night'}
          onChange={(on) => {
            setTheme.mutate({ theme: on ? 'night' : 'dark' });
          }}
        />
      </div>
    </Panel>
  );
}
