import type { SelectedObject } from '@astroplanner/bridge-client';
import { IconButton, Panel } from '@astroplanner/ui-kit';
import { ChevronDown, ChevronUp, Crosshair, Frame, ListPlus, ListX, X } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Button, Disclosure, DisclosurePanel } from 'react-aria-components';
import { formatDegrees } from '../app/format';
import { useSelectionCommand, useSnapshot } from '../app/hostQueries';
import { wallClock } from './TopBar';
import classes from './ObjectCard.module.css';

const styles = classes as Readonly<
  Record<
    | 'card'
    | 'header'
    | 'title'
    | 'subtitle'
    | 'facts'
    | 'fact'
    | 'verdict'
    | 'dot'
    | 'score'
    | 'why'
    | 'reasons'
    | 'recommended'
    | 'actions'
    | 'collapsed',
    string
  >
>;

/** "Nebula · Orion · mag 4.0". */
function subtitle(target: SelectedObject, magnitude: (value: string) => string): string {
  return [target.typeName, target.constellation, target.magnitude ? magnitude(target.magnitude) : null]
    .filter((part): part is string => Boolean(part))
    .join(' · ');
}

/**
 * The selected object (spec §3.2): what and where it is, tonight's transit and best window, and
 * its feasibility for the active rig with the reasons behind the verdict, with the actions that
 * act on it. Collapsible; nothing is shown without a selection.
 */
export function ObjectCard() {
  const { t } = useTranslation();
  const { data } = useSnapshot('selection.get');
  const center = useSelectionCommand('selection.center');
  const frame = useSelectionCommand('selection.frame');
  const add = useSelectionCommand('selection.addToPlan');
  const remove = useSelectionCommand('selection.removeFromPlan');
  const clear = useSelectionCommand('selection.clear');
  const [collapsed, setCollapsed] = useState(false);

  const target = data?.target;
  if (!target) {
    return null;
  }
  const catalog = target.id !== null;
  const transit = wallClock(target.transitLocal);

  return (
    <Panel
      className={collapsed ? `${styles.card} ${styles.collapsed}` : styles.card}
      role="region"
      aria-label={t('card.label')}
    >
      <div className={styles.header}>
        <h2 className={styles.title}>{target.title}</h2>
        <IconButton
          label={collapsed ? t('card.expand') : t('card.collapse')}
          icon={collapsed ? ChevronDown : ChevronUp}
          onPress={() => {
            setCollapsed(!collapsed);
          }}
        />
        <IconButton
          label={t('card.clear')}
          icon={X}
          shortcut="Esc"
          onPress={() => {
            clear.mutate({});
          }}
        />
      </div>

      {collapsed ? null : (
        <>
          {catalog ? (
            <p className={styles.subtitle}>
              {subtitle(target, (m) => t('card.magnitude', { magnitude: m }))}
            </p>
          ) : null}

          {catalog ? (
            <dl className={styles.facts}>
              {target.altitudeDegrees !== null && target.azimuthDegrees !== null ? (
                <div className={styles.fact}>
                  <dt>{t('card.altAz')}</dt>
                  <dd>
                    {formatDegrees(target.altitudeDegrees)} · {formatDegrees(target.azimuthDegrees)}
                  </dd>
                </div>
              ) : null}
              {transit ? (
                <div className={styles.fact}>
                  <dt>{t('card.transit')}</dt>
                  <dd>
                    {transit}
                    {target.maximumAltitudeDegrees !== null
                      ? ` · ${formatDegrees(target.maximumAltitudeDegrees)}`
                      : ''}
                  </dd>
                </div>
              ) : null}
              {target.bestWindow ? (
                <div className={styles.fact}>
                  <dt>{t('card.bestWindow')}</dt>
                  <dd>{target.bestWindow}</dd>
                </div>
              ) : null}
              {target.coordinates ? (
                <div className={styles.fact}>
                  <dt>{t('card.coordinates')}</dt>
                  <dd>{target.coordinates}</dd>
                </div>
              ) : null}
            </dl>
          ) : null}

          {target.verdict && target.verdictText ? (
            <Disclosure className={styles.why}>
              <div className={styles.verdict}>
                <span className={styles.dot} data-verdict={target.verdict} aria-hidden="true" />
                <span>{target.verdictText}</span>
                {target.score !== null ? <span className={styles.score}>{target.score}</span> : null}
                {target.reasons.length > 0 ? <Button slot="trigger">{t('card.why')}</Button> : null}
              </div>
              {target.recommended ? <p className={styles.recommended}>{target.recommended}</p> : null}
              <DisclosurePanel>
                <ul className={styles.reasons}>
                  {target.reasons.map((reason) => (
                    <li key={reason}>{reason}</li>
                  ))}
                </ul>
              </DisclosurePanel>
            </Disclosure>
          ) : null}

          {catalog ? (
            <div className={styles.actions} role="group" aria-label={t('card.actions')}>
              <IconButton
                label={t('card.center')}
                icon={Crosshair}
                onPress={() => {
                  center.mutate({});
                }}
              />
              <IconButton
                label={t('card.frame')}
                icon={Frame}
                onPress={() => {
                  frame.mutate({});
                }}
              />
              {target.isInPlan ? (
                <IconButton
                  label={t('card.removeFromPlan')}
                  icon={ListX}
                  onPress={() => {
                    remove.mutate({});
                  }}
                />
              ) : (
                <IconButton
                  label={t('card.addToPlan')}
                  icon={ListPlus}
                  onPress={() => {
                    add.mutate({});
                  }}
                />
              )}
            </div>
          ) : null}
        </>
      )}
    </Panel>
  );
}
