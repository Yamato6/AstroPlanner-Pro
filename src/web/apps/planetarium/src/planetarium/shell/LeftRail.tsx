import { Panel, ToggleIconButton } from '@astroplanner/ui-kit';
import { useTranslation } from 'react-i18next';
import { useWindows } from '../state/windows';
import { type WindowId, windowDefinitions, windowIds } from '../windows/registry';
import classes from './LeftRail.module.css';

const styles = classes as Readonly<Record<'rail' | 'item' | 'minimized' | 'separator', string>>;

/**
 * The windows (spec §3.2): one button each, pressed while its window shows. A press opens it,
 * brings it to the front, or minimises it when it already is in front; a minimised window keeps
 * a dot here until it is restored.
 */
export function LeftRail() {
  const { t } = useTranslation();
  const layout = useWindows((state) => state.layout);
  const press = useWindows((state) => state.press);

  const item = (id: WindowId) => {
    const definition = windowDefinitions[id];
    const window = layout[id];
    const minimized = Boolean(window?.isOpen && window.isMinimized);
    const title = t(`windows.${id}.title`);
    return (
      <li key={id} className={minimized ? `${styles.item} ${styles.minimized}` : styles.item}>
        <ToggleIconButton
          label={minimized ? t('rail.minimized', { title }) : title}
          icon={definition.icon}
          {...(definition.shortcut ? { shortcut: definition.shortcut } : {})}
          tooltipPlacement="end"
          isSelected={Boolean(window?.isOpen && !window.isMinimized)}
          onChange={() => {
            press(id);
          }}
        />
      </li>
    );
  };

  const planner = windowIds.filter((id) => windowDefinitions[id].group === 'planner');
  const app = windowIds.filter((id) => windowDefinitions[id].group === 'app');

  return (
    <Panel className={styles.rail} role="navigation" aria-label={t('rail.label')}>
      <ul>{planner.map(item)}</ul>
      <hr className={styles.separator} />
      <ul>{app.map(item)}</ul>
    </Panel>
  );
}
