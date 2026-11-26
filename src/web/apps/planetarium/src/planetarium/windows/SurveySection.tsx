import type { RequestOf, SurveySnapshot } from '@astroplanner/bridge-client';
import { Button } from '@astroplanner/ui-kit';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Trash2 } from 'lucide-react';
import { type Key, Label, RadioButton, RadioField, RadioGroup, Text } from 'react-aria-components';
import { useTranslation } from 'react-i18next';
import { useSnapshot } from '../app/hostQueries';
import { host } from '../host';
import { SegmentedChoice } from './SegmentedChoice';
import classes from './windows.module.css';

const styles = classes as Readonly<
  Record<
    | 'section'
    | 'heading'
    | 'row'
    | 'rowText'
    | 'muted'
    | 'radioGroup'
    | 'radioField'
    | 'radio'
    | 'radioMark'
    | 'radioDescription',
    string
  >
>;

/** The radio value of "no survey" (surveyId null). */
const NONE = 'none';
const MODES = ['realistic', 'survey'] as const satisfies readonly SurveySnapshot['mode'][];
const isMode = (key: Key): key is SurveySnapshot['mode'] => (MODES as readonly Key[]).includes(key);
/** Tile cache sizes offered, MB (the host accepts 256–10240). */
const CACHE_SIZES = [256, 1024, 2048, 5120, 10240] as const;

/** Runs a `survey.*` command and stores the snapshot it answers with. */
function useSurveyCommand<
  M extends 'survey.set' | 'survey.setMode' | 'survey.setCacheLimit' | 'survey.clearCache',
>(method: M) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (params: RequestOf<M>) => host.request(method, params),
    onSuccess: (snapshot) => {
      client.setQueryData(['survey.get'], snapshot);
    },
    onError: (error) => {
      console.warn(method, 'failed', error);
    },
  });
}

function Option(props: { readonly value: string; readonly label: string; readonly description: string }) {
  return (
    <RadioField className={styles.radioField} value={props.value}>
      <RadioButton className={styles.radio}>
        <span className={styles.radioMark} aria-hidden="true" />
        {props.label}
      </RadioButton>
      <Text slot="description" className={styles.radioDescription}>
        {props.description}
      </Text>
    </RadioField>
  );
}

/**
 * Settings › Survey (spec §8.3): the photographic survey the sky shows as the field narrows (DSS2
 * colour by default, Pan-STARRS north of −30°, or none), realistic or at full contrast, and the
 * disk cache of its tiles.
 */
export function SurveySection() {
  const { t } = useTranslation();
  const { data: snapshot } = useSnapshot('survey.get');
  const setSurvey = useSurveyCommand('survey.set');
  const setMode = useSurveyCommand('survey.setMode');
  const setCacheLimit = useSurveyCommand('survey.setCacheLimit');
  const clearCache = useSurveyCommand('survey.clearCache');
  const disabled = !host.connected || !snapshot;

  const megabytes = (value: number): string =>
    value >= 1024
      ? t('settings.survey.gigabytes', { value: value / 1024 })
      : t('settings.survey.megabytes', { value });

  return (
    <section className={styles.section} aria-labelledby="settings-survey">
      <h3 id="settings-survey" className={styles.heading}>
        {t('settings.survey.title')}
      </h3>
      {snapshot && !snapshot.downloads ? (
        <p className={styles.muted}>{t('settings.survey.offline')}</p>
      ) : null}
      <RadioGroup
        className={styles.radioGroup}
        value={snapshot?.surveyId ?? NONE}
        isDisabled={disabled}
        onChange={(value) => {
          setSurvey.mutate({ surveyId: value === NONE ? null : value });
        }}
      >
        <Label className={styles.muted}>{t('settings.survey.label')}</Label>
        {(snapshot?.surveys ?? []).map((survey) => (
          <Option key={survey.id} value={survey.id} label={survey.name} description={survey.credit} />
        ))}
        <Option
          value={NONE}
          label={t('settings.survey.none')}
          description={t('settings.survey.noneDescription')}
        />
      </RadioGroup>
      <SegmentedChoice
        id="settings-survey-mode"
        label={t('settings.survey.mode')}
        description={t('settings.survey.modeDescription')}
        options={MODES.map((mode) => ({ id: mode, label: t(`settings.survey.modes.${mode}`) }))}
        selected={snapshot?.mode ?? 'realistic'}
        isDisabled={disabled || snapshot.surveyId === null}
        onChange={(mode) => {
          if (isMode(mode)) {
            setMode.mutate({ mode });
          }
        }}
      />
      <SegmentedChoice
        id="settings-survey-cache"
        label={t('settings.survey.cache')}
        description={
          snapshot
            ? t('settings.survey.cacheUsed', { used: megabytes(snapshot.cacheMegabytes) })
            : t('settings.survey.cacheDescription')
        }
        options={CACHE_SIZES.map((size) => ({ id: String(size), label: megabytes(size) }))}
        selected={String(snapshot?.cacheLimitMegabytes ?? 1024)}
        isDisabled={disabled}
        onChange={(size) => {
          setCacheLimit.mutate({ cacheLimitMegabytes: Number(size) });
        }}
      />
      <div className={styles.row}>
        <span className={styles.rowText}>
          {t('settings.survey.clear')}
          <span className={styles.muted}>{t('settings.survey.clearDescription')}</span>
        </span>
        <Button
          icon={Trash2}
          isDisabled={disabled || snapshot.cacheMegabytes === 0 || clearCache.isPending}
          onPress={() => {
            clearCache.mutate({});
          }}
        >
          {t('settings.survey.clearButton')}
        </Button>
      </div>
    </section>
  );
}
