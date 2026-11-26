import type { UiSnapshot } from '@astroplanner/bridge-client';
import { Button, Switch } from '@astroplanner/ui-kit';
import type { Key } from 'react-aria-components';
import { RotateCcw } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { useSnapshot, useUiCommand } from '../app/hostQueries';
import { host } from '../host';
import { useWindows } from '../state/windows';
import { LandscapeSection } from './LandscapeSection';
import { SegmentedChoice as Choice } from './SegmentedChoice';
import { SurveySection } from './SurveySection';
import classes from './windows.module.css';

const styles = classes as Readonly<
  Record<'section' | 'heading' | 'row' | 'rowText' | 'muted' | 'note' | 'segmented' | 'segment', string>
>;

const QUALITIES = ['low', 'medium', 'high'] as const satisfies readonly UiSnapshot['skyQuality'][];
const TINTS = ['sodium', 'led', 'neutral'] as const satisfies readonly UiSnapshot['lightPollutionTint'][];
const isQuality = (key: Key): key is UiSnapshot['skyQuality'] => (QUALITIES as readonly Key[]).includes(key);
const isTint = (key: Key): key is UiSnapshot['lightPollutionTint'] => (TINTS as readonly Key[]).includes(key);

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
  const setSkyInertia = useUiCommand('ui.setSkyInertia');
  const setSkyQuality = useUiCommand('ui.setSkyQuality');
  const setTint = useUiCommand('ui.setLightPollutionTint');
  const setScintillation = useUiCommand('ui.setScintillation');
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

      <section className={styles.section} aria-labelledby="settings-sky">
        <h3 id="settings-sky" className={styles.heading}>
          {t('settings.sky')}
        </h3>
        <Switch
          isSelected={ui?.skyInertia ?? true}
          isDisabled={unavailable}
          description={t('settings.skyInertiaDescription')}
          onChange={(enabled) => {
            setSkyInertia.mutate({ enabled });
          }}
        >
          {t('settings.skyInertia')}
        </Switch>
        <Choice
          id="settings-sky-quality"
          label={t('settings.skyQuality')}
          description={t('settings.skyQualityDescription')}
          options={QUALITIES.map((q) => ({ id: q, label: t(`settings.quality.${q}`) }))}
          selected={ui?.skyQuality ?? 'medium'}
          isDisabled={unavailable}
          onChange={(quality) => {
            if (isQuality(quality)) {
              setSkyQuality.mutate({ quality });
            }
          }}
        />
        <Choice
          id="settings-tint"
          label={t('settings.lightPollutionTint')}
          description={t('settings.lightPollutionTintDescription')}
          options={TINTS.map((tint) => ({ id: tint, label: t(`settings.tint.${tint}`) }))}
          selected={ui?.lightPollutionTint ?? 'led'}
          isDisabled={unavailable}
          onChange={(tint) => {
            if (isTint(tint)) {
              setTint.mutate({ tint });
            }
          }}
        />
        <Switch
          isSelected={ui?.scintillation ?? false}
          isDisabled={unavailable || ui.skyQuality !== 'high'}
          description={t('settings.scintillationDescription')}
          onChange={(enabled) => {
            setScintillation.mutate({ enabled });
          }}
        >
          {t('settings.scintillation')}
        </Switch>
      </section>

      <LandscapeSection />

      <SurveySection />

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
