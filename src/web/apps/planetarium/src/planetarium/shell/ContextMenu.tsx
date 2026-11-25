import { type SkyEventMap, skyControl, skyEvents } from '@astroplanner/sky-engine';
import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { type Key, Menu, MenuItem, Popover, Separator } from 'react-aria-components';
import { formatRaDec } from '../app/format';
import { useSelectionCommand, useSkyCommand } from '../app/hostQueries';
import { host } from '../host';
import { toast } from '../state/toasts';
import classes from './ContextMenu.module.css';

const styles = classes as Readonly<
  Record<'anchor' | 'popover' | 'menu' | 'item' | 'heading' | 'separator', string>
>;

type Opened = SkyEventMap['contextMenu'];

/** Puts text on the clipboard and says so. */
async function copy(text: string, done: string, failed: string): Promise<void> {
  try {
    await navigator.clipboard.writeText(text);
    toast(done);
  } catch {
    toast(failed, 'error');
  }
}

/**
 * Right click on the sky (spec §3.2): on an object (which the click selected) its actions; on
 * empty sky, the point's coordinates.
 */
export function ContextMenu() {
  const { t } = useTranslation();
  const client = useQueryClient();
  const [opened, setOpened] = useState<Opened | null>(null);
  const anchor = useRef<HTMLDivElement>(null);
  const center = useSelectionCommand('selection.center');
  const frame = useSelectionCommand('selection.frame');
  const add = useSelectionCommand('selection.addToPlan');
  const setViewMode = useSkyCommand('sky.setViewMode');

  useEffect(() => skyEvents.on('contextMenu', setOpened), []);

  const copyCoordinates = async (menu: Opened): Promise<void> => {
    const done = t('contextMenu.copied');
    const failed = t('contextMenu.copyFailed');
    if (menu.picked) {
      // The object's catalog coordinates, as the card shows them.
      const selection = await host.request('selection.get', {});
      client.setQueryData(['selection.get'], selection);
      const text = selection.target?.coordinates;
      if (text) {
        await copy(text, done, failed);
        return;
      }
    }
    if (menu.point) {
      await copy(formatRaDec(menu.point.raHours, menu.point.decDegrees), done, failed);
    }
  };

  const onAction = (key: Key): void => {
    const menu = opened;
    if (!menu) {
      return;
    }
    switch (key) {
      case 'center':
        center.mutate({});
        break;
      case 'frame':
        frame.mutate({});
        break;
      case 'track':
        if (!skyControl.setTracking(true)) {
          toast(t('sky.nothingToTrack'));
        }
        break;
      case 'finder':
        center.mutate({});
        setViewMode.mutate({ mode: 'finder' });
        break;
      case 'plan':
        add.mutate(
          {},
          {
            onSuccess: (selection) => {
              toast(t('contextMenu.added', { title: selection.target?.title ?? menu.picked?.label ?? '' }));
            },
          },
        );
        break;
      case 'copy':
        void copyCoordinates(menu);
        break;
    }
  };

  return (
    <>
      <div
        ref={anchor}
        className={styles.anchor}
        style={opened ? { transform: `translate(${opened.x}px, ${opened.y}px)` } : undefined}
        aria-hidden="true"
      />
      <Popover
        className={styles.popover}
        triggerRef={anchor}
        isOpen={opened !== null}
        onOpenChange={(open) => {
          if (!open) {
            setOpened(null);
          }
        }}
        placement="bottom start"
        offset={2}
      >
        <Menu
          className={styles.menu}
          aria-label={t('contextMenu.label')}
          onAction={onAction}
          autoFocus="first"
        >
          {opened?.picked ? (
            <>
              <MenuItem id="center" className={styles.item}>
                {t('contextMenu.center')}
              </MenuItem>
              <MenuItem id="track" className={styles.item}>
                {t('contextMenu.track')}
              </MenuItem>
              <MenuItem id="frame" className={styles.item}>
                {t('contextMenu.frame')}
              </MenuItem>
              <MenuItem id="finder" className={styles.item}>
                {t('contextMenu.finder')}
              </MenuItem>
              <MenuItem id="plan" className={styles.item}>
                {t('contextMenu.addToPlan')}
              </MenuItem>
              <Separator className={styles.separator} />
            </>
          ) : null}
          <MenuItem id="copy" className={styles.item} isDisabled={!opened?.picked && !opened?.point}>
            {t('contextMenu.copy')}
          </MenuItem>
        </Menu>
      </Popover>
    </>
  );
}
