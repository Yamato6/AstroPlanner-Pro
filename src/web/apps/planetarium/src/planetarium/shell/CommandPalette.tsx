import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Globe, Search } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  Autocomplete,
  Dialog,
  Header,
  Input,
  type Key,
  Keyboard,
  Menu,
  MenuItem,
  MenuSection,
  Modal,
  ModalOverlay,
  SearchField,
  Text,
  useFilter,
} from 'react-aria-components';
import { useSelectionCommand } from '../app/hostQueries';
import { useShortcutList } from '../app/useAppShortcuts';
import { host } from '../host';
import { useUi } from '../state/ui';
import { toast } from '../state/toasts';
import { useWindows } from '../state/windows';
import { type WindowId, windowIds } from '../windows/registry';
import classes from './CommandPalette.module.css';

const styles = classes as Readonly<
  Record<
    | 'overlay'
    | 'modal'
    | 'dialog'
    | 'field'
    | 'icon'
    | 'input'
    | 'menu'
    | 'section'
    | 'header'
    | 'item'
    | 'detail'
    | 'kbd'
    | 'empty',
    string
  >
>;

/** Matches come from the catalog this long after the last keystroke. */
const QUERY_DELAY_MS = 150;
const RESULT_LIMIT = 12;

/** The text a little after typing stopped. */
function useSettled(text: string): string {
  const [settled, setSettled] = useState(text);
  useEffect(() => {
    const timer = window.setTimeout(() => {
      setSettled(text);
    }, QUERY_DELAY_MS);
    return () => {
      window.clearTimeout(timer);
    };
  }, [text]);
  return settled;
}

/**
 * Search and commands in one box (spec §9.1): catalog objects (and, for a name the catalog
 * lacks, an online search through Sesame), the windows and every command with its key. Enter
 * selects the object and turns the view to it.
 */
export function CommandPalette() {
  const { t } = useTranslation();
  const open = useUi((state) => state.paletteOpen);
  const setOpen = useUi((state) => state.setPaletteOpen);
  const [text, setText] = useState('');
  const settled = useSettled(text.trim());
  const shortcuts = useShortcutList();
  const openWindow = useWindows((state) => state.open);
  const go = useSelectionCommand('search.go');
  const client = useQueryClient();
  const { contains } = useFilter({ sensitivity: 'base' });

  const results = useQuery({
    queryKey: ['search.query', settled],
    queryFn: ({ signal }) => host.request('search.query', { text: settled, limit: RESULT_LIMIT }, { signal }),
    enabled: open && host.connected && settled.length > 0,
    staleTime: 30_000,
  });

  const close = (): void => {
    setOpen(false);
    setText('');
  };

  const resolveOnline = (name: string): void => {
    host
      .request('search.resolve', { text: name })
      .then((answer) => {
        toast(answer.message, answer.found ? 'info' : 'error');
        void client.invalidateQueries({ queryKey: ['selection.get'] });
      })
      .catch(() => {
        toast(t('palette.resolveFailed', { name }), 'error');
      });
  };

  const onAction = (key: Key): void => {
    const id = String(key);
    if (id.startsWith('object:')) {
      go.mutate({ id: id.slice('object:'.length) });
    } else if (id === 'online') {
      resolveOnline(text.trim());
    } else if (id.startsWith('window:')) {
      openWindow(id.slice('window:'.length) as WindowId);
    } else if (id.startsWith('command:')) {
      shortcuts.find((s) => s.id === id.slice('command:'.length))?.run();
    }
    close();
  };

  const objects = settled.length > 0 && settled === text.trim() ? (results.data?.results ?? []) : [];
  const commands = shortcuts.filter((s) => !['commandPalette', 'search'].includes(s.id));

  return (
    <ModalOverlay
      className={styles.overlay}
      isOpen={open}
      onOpenChange={(isOpen) => {
        if (!isOpen) {
          close();
        }
      }}
      isDismissable
    >
      <Modal className={styles.modal}>
        <Dialog className={styles.dialog} aria-label={t('palette.label')}>
          <Autocomplete
            inputValue={text}
            onInputChange={setText}
            filter={(textValue, inputValue, node) =>
              String(node.key).startsWith('object:') ||
              node.key === 'online' ||
              contains(textValue, inputValue)
            }
          >
            <SearchField className={styles.field} aria-label={t('palette.label')} autoFocus>
              <Search className={styles.icon} size={16} aria-hidden="true" />
              <Input className={styles.input} placeholder={t('palette.placeholder')} />
            </SearchField>
            <Menu
              className={styles.menu}
              onAction={onAction}
              renderEmptyState={() => <p className={styles.empty}>{t('palette.nothing')}</p>}
            >
              {objects.length > 0 ? (
                <MenuSection className={styles.section}>
                  <Header className={styles.header}>{t('palette.objects')}</Header>
                  {objects.map((result) => (
                    <MenuItem
                      key={result.id}
                      id={`object:${result.id}`}
                      className={styles.item}
                      textValue={result.title}
                    >
                      <Text slot="label">{result.title}</Text>
                      <Text slot="description" className={styles.detail}>
                        {[
                          result.typeName,
                          result.magnitude ? t('card.magnitude', { magnitude: result.magnitude }) : null,
                          result.constellation,
                        ]
                          .filter(Boolean)
                          .join(' · ')}
                      </Text>
                    </MenuItem>
                  ))}
                </MenuSection>
              ) : null}
              <MenuSection className={styles.section}>
                <Header className={styles.header}>{t('palette.windows')}</Header>
                {windowIds.map((id) => (
                  <MenuItem
                    key={id}
                    id={`window:${id}`}
                    className={styles.item}
                    textValue={t(`windows.${id}.title`)}
                  >
                    <Text slot="label">{t(`windows.${id}.title`)}</Text>
                  </MenuItem>
                ))}
              </MenuSection>
              <MenuSection className={styles.section}>
                <Header className={styles.header}>{t('palette.commands')}</Header>
                {commands.map((shortcut) => (
                  <MenuItem
                    key={shortcut.id}
                    id={`command:${shortcut.id}`}
                    className={styles.item}
                    textValue={t(`shortcuts.${shortcut.id}`)}
                  >
                    <Text slot="label">{t(`shortcuts.${shortcut.id}`)}</Text>
                    <Keyboard className={styles.kbd}>{shortcut.display}</Keyboard>
                  </MenuItem>
                ))}
              </MenuSection>
              {text.trim().length > 1 ? (
                <MenuSection className={styles.section}>
                  <MenuItem
                    id="online"
                    className={styles.item}
                    textValue={t('palette.online', { name: text.trim() })}
                  >
                    <Globe size={14} aria-hidden="true" />
                    <Text slot="label">{t('palette.online', { name: text.trim() })}</Text>
                  </MenuItem>
                </MenuSection>
              ) : null}
            </Menu>
          </Autocomplete>
        </Dialog>
      </Modal>
    </ModalOverlay>
  );
}
