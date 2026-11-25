import type { RequestOf } from '@astroplanner/bridge-client';
import { Button } from '@astroplanner/ui-kit';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Mountain } from 'lucide-react';
import { useState } from 'react';
import { Label, RadioButton, RadioField, RadioGroup, Text } from 'react-aria-components';
import { useTranslation } from 'react-i18next';
import { useSnapshot } from '../app/hostQueries';
import { host } from '../host';
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
    | 'radioDescription'
    | 'confirm',
    string
  >
>;

/** The radio value of "the site's horizon profile" (landscapeId null). */
const PROFILE = 'profile';

/** One option of the landscape list: its name and, below, a description (or the photo's credit). */
function Choice(props: { readonly value: string; readonly label: string; readonly description: string }) {
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

/** Runs a `landscape.*` command and stores the snapshot it answers with. */
function useLandscapeCommand<M extends 'landscape.set' | 'landscape.useHorizon'>(method: M) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (params: RequestOf<M>) => host.request(method, params),
    onSuccess: (snapshot) => {
      client.setQueryData(['landscape.get'], snapshot);
    },
    onError: (error) => {
      console.warn(method, 'failed', error);
    },
  });
}

/**
 * Settings › Landscape (spec §8.5): what the sky shows around the active saved site — a 360°
 * landscape, a flat ground, or the site's horizon profile — and, on request, the landscape's
 * horizon copied into the site's horizon mask so that every calculation uses it.
 */
export function LandscapeSection() {
  const { t } = useTranslation();
  const { data: snapshot } = useSnapshot('landscape.get');
  const setLandscape = useLandscapeCommand('landscape.set');
  const useHorizon = useLandscapeCommand('landscape.useHorizon');
  const [confirming, setConfirming] = useState(false);

  const site = snapshot?.siteName ?? null;
  const selected = snapshot?.landscapes.find((l) => l.id === snapshot.landscapeId) ?? null;
  const disabled = !host.connected || !snapshot || site === null;

  let note: string | null = null;
  if (snapshot && !snapshot.mediaAvailable) {
    note = t('settings.landscape.notInstalled');
  } else if (snapshot && site === null) {
    note = t('settings.landscape.noSite');
  }

  return (
    <section className={styles.section} aria-labelledby="settings-landscape">
      <h3 id="settings-landscape" className={styles.heading}>
        {t('settings.landscape.title')}
      </h3>
      {note ? <p className={styles.muted}>{note}</p> : null}
      <RadioGroup
        className={styles.radioGroup}
        value={snapshot?.landscapeId ?? PROFILE}
        isDisabled={disabled}
        onChange={(value) => {
          setConfirming(false);
          setLandscape.mutate({ landscapeId: value === PROFILE ? null : value });
        }}
      >
        <Label className={styles.muted}>
          {site ? t('settings.landscape.forSite', { site }) : t('settings.landscape.label')}
        </Label>
        <Choice
          value={PROFILE}
          label={t('settings.landscape.profile')}
          description={t('settings.landscape.profileDescription')}
        />
        <Choice
          value="flat"
          label={t('settings.landscape.flat')}
          description={t('settings.landscape.flatDescription')}
        />
        {(snapshot?.landscapes ?? []).map((landscape) => (
          <Choice
            key={landscape.id}
            value={landscape.id}
            label={landscape.name}
            description={t('settings.landscape.credit', {
              author: landscape.author,
              license: landscape.license,
            })}
          />
        ))}
      </RadioGroup>

      {selected ? (
        <div className={styles.row}>
          <span className={styles.rowText}>
            {t('settings.landscape.useHorizon')}
            <span className={styles.muted}>
              {snapshot?.horizonFromLandscape
                ? t('settings.landscape.horizonInUse')
                : t('settings.landscape.useHorizonDescription')}
            </span>
          </span>
          <Button
            icon={Mountain}
            isDisabled={disabled || snapshot.horizonFromLandscape || confirming}
            onPress={() => {
              setConfirming(true);
            }}
          >
            {t('settings.landscape.use')}
          </Button>
        </div>
      ) : null}
      {selected && confirming ? (
        <div className={styles.confirm} role="group" aria-label={t('settings.landscape.useHorizon')}>
          <p className={styles.muted}>
            {t('settings.landscape.confirm', { site, landscape: selected.name })}
          </p>
          <Button
            onPress={() => {
              setConfirming(false);
              useHorizon.mutate({});
            }}
          >
            {t('settings.landscape.replace')}
          </Button>
          <Button
            onPress={() => {
              setConfirming(false);
            }}
          >
            {t('settings.landscape.cancel')}
          </Button>
        </div>
      ) : null}
    </section>
  );
}
