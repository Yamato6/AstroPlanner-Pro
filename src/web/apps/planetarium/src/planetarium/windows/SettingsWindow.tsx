import { Button, Switch } from '@astroplanner/ui-kit';
import { RotateCcw } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { useSnapshot, useUiCommand } from '../app/hostQueries';
import { host } from '../host';
import { useWindows } from '../state/windows';
import classes from './windows.module.css';

const styles = classes as Readonly<
  Record<'section' | 'heading' | 'row' | 'rowText' | 'muted' | 'note', string>
>;

/**
 * Settings (spec §3.4). R1b: the interface's own options. The planner's settings (site, online
 * services, calibration…) move here with their windows; until then they are in the classic window.
 */
export function SettingsWindow() {
  const { t } = useTranslation();
  const { data: ui } = useSnapshot('ui.get');
  const setTheme = useUiCommand('ui.setTheme');
  const setReduceTransparency = useUiCommand('ui.setReduceTransparency');
  const setFullScreen = useUiCommand('ui.setFullScreen');
  const setAutoHide = useUiCommand('ui.setAutoHide');
  const reset = useWindows((state) => state.reset);
  const unavailable = !host.connected || !ui;

  return (
    <>
      <section className={styles.section} aria-labelledby="settings-interface">
        <h3 id="settings-interface" className={styles.heading}>
          {t('settings.interface')}
        </h3>
        <Switch
          isSelected={ui?.theme === 'night'}
          isDisabled={unavailable}
          description={t('settings.nightVisionDescription')}
          onChange={(on) => {
            setTheme.mutate({ theme: on ? 'night' : 'dark' });
          }}
        >
          {t('settings.nightVision')}
        </Switch>
        <Switch
          isSelected={ui?.reduceTransparency ?? false}
          isDisabled={unavailable}
          description={t('settings.reduceTransparencyDescription')}
          onChange={(enabled) => {
            setReduceTransparency.mutate({ enabled });
          }}
        >
          {t('settings.reduceTransparency')}
        </Switch>
        <Switch
          isSelected={ui?.autoHideToolbars ?? false}
          isDisabled={unavailable}
          description={t('settings.autoHideDescription')}
          onChange={(enabled) => {
            setAutoHide.mutate({ enabled });
          }}
        >
          {t('settings.autoHide')}
        </Switch>
        <Switch
          isSelected={ui?.isFullScreen ?? false}
          isDisabled={unavailable}
          description={t('settings.fullScreenDescription')}
          onChange={(enabled) => {
            setFullScreen.mutate({ enabled });
          }}
        >
          {t('settings.fullScreen')}
        </Switch>
      </section>

      <section className={styles.section} aria-labelledby="settings-windows">
        <h3 id="settings-windows" className={styles.heading}>
          {t('settings.windows')}
        </h3>
        <div className={styles.row}>
          <span className={styles.rowText}>
            {t('settings.resetLayout')}
            <span className={styles.muted}>{t('settings.resetLayoutDescription')}</span>
          </span>
          <Button icon={RotateCcw} onPress={reset}>
            {t('settings.reset')}
          </Button>
        </div>
      </section>

      <p className={styles.note}>{t('settings.classicNote')}</p>
    </>
  );
}
