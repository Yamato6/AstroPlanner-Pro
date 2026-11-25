import { Button } from '@astroplanner/ui-kit';
import { useQuery } from '@tanstack/react-query';
import { ExternalLink, FolderOpen } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { Link, Tab, TabList, TabPanel, Tabs } from 'react-aria-components';
import type { ShortcutGroup } from '../app/shortcuts';
import { useShortcutList } from '../app/useAppShortcuts';
import { host } from '../host';
import classes from './windows.module.css';

const styles = classes as Readonly<
  Record<
    | 'tabs'
    | 'tabList'
    | 'tab'
    | 'section'
    | 'heading'
    | 'keys'
    | 'kbd'
    | 'muted'
    | 'note'
    | 'about'
    | 'version'
    | 'credits'
    | 'link'
    | 'folder'
    | 'path'
    | 'row',
    string
  >
>;

const groups: readonly ShortcutGroup[] = ['time', 'sky', 'interface', 'windows'];

/** Keys that work inside a window (FloatingWindow), listed with the registry's. */
const windowKeys = ['escape', 'move', 'resize', 'dock', 'maximize'] as const;
const windowKeyDisplay: Record<(typeof windowKeys)[number], string> = {
  escape: 'Esc',
  move: '← → ↑ ↓',
  resize: 'Shift + ← → ↑ ↓',
  dock: 'Ctrl + ← →',
  maximize: 'Ctrl + ↑ ↓',
};

function openLink(url: string): void {
  host.request('app.openLink', { url }).catch((error: unknown) => {
    console.warn('app.openLink failed', error);
  });
}

function KeyboardMap() {
  const { t } = useTranslation();
  const shortcuts = useShortcutList();
  return (
    <>
      {groups.map((group) => (
        <section key={group} className={styles.section} aria-labelledby={`keys-${group}`}>
          <h3 id={`keys-${group}`} className={styles.heading}>
            {t(`help.groups.${group}`)}
          </h3>
          <dl className={styles.keys}>
            {shortcuts
              .filter((s) => s.group === group)
              .map((s) => (
                <div key={s.id}>
                  <dt>
                    <kbd className={styles.kbd}>{s.display}</kbd>
                  </dt>
                  <dd>{t(`shortcuts.${s.id}`)}</dd>
                </div>
              ))}
            {group === 'windows'
              ? windowKeys.map((key) => (
                  <div key={key}>
                    <dt>
                      <kbd className={styles.kbd}>{windowKeyDisplay[key]}</kbd>
                    </dt>
                    <dd>{t(`help.windowKeys.${key}`)}</dd>
                  </div>
                ))
              : null}
          </dl>
        </section>
      ))}
    </>
  );
}

function About() {
  const { t } = useTranslation();
  const about = useQuery({
    queryKey: ['app.about'],
    queryFn: ({ signal }) => host.request('app.about', {}, { signal }),
    enabled: host.connected,
  });

  if (!host.connected) {
    return <p className={styles.note}>{t('help.offline')}</p>;
  }
  if (about.isPending) {
    return (
      <p className={styles.note} aria-live="polite">
        {t('help.loading')}
      </p>
    );
  }
  if (about.isError) {
    return <p className={styles.note}>{t('help.failed')}</p>;
  }

  const data = about.data;
  return (
    <div className={styles.about}>
      <p className={styles.version}>
        {t('app.name')} <span className={styles.muted}>{t('help.version', { version: data.version })}</span>
      </p>
      <p>{data.description}</p>

      <section className={styles.section} aria-labelledby="about-credits">
        <h3 id="about-credits" className={styles.heading}>
          {t('help.credits')}
        </h3>
        <dl className={styles.credits}>
          {data.credits.map(({ title, text, url }) => (
            <div key={title}>
              <dt>{title}</dt>
              <dd>
                {url ? (
                  <Link
                    className={styles.link}
                    onPress={() => {
                      openLink(url);
                    }}
                  >
                    {text}
                    <ExternalLink size={12} aria-hidden="true" />
                  </Link>
                ) : (
                  text
                )}
              </dd>
            </div>
          ))}
        </dl>
      </section>

      <section className={styles.section} aria-labelledby="about-data">
        <h3 id="about-data" className={styles.heading}>
          {t('help.yourData')}
        </h3>
        <p>{data.yourData}</p>
        <div className={styles.folder}>
          <code className={styles.path} title={data.dataFolder}>
            {data.dataFolder}
          </code>
          <Button
            icon={FolderOpen}
            onPress={() => {
              host.request('app.openDataFolder', {}).catch((error: unknown) => {
                console.warn('app.openDataFolder failed', error);
              });
            }}
          >
            {t('help.openFolder')}
          </Button>
        </div>
      </section>

      <section className={styles.section} aria-labelledby="about-license">
        <h3 id="about-license" className={styles.heading}>
          {t('help.license')}
        </h3>
        <p>{data.license}</p>
        <Link
          className={styles.link}
          onPress={() => {
            openLink(data.repositoryUrl);
          }}
        >
          {data.repositoryUrl.replace(/^https:\/\//, '')}
          <ExternalLink size={12} aria-hidden="true" />
        </Link>
      </section>
    </div>
  );
}

/** Help (spec §3.4): the keyboard map, from the shortcut registry, and About (version, credits, data folder, license). */
export function HelpWindow() {
  const { t } = useTranslation();
  return (
    <Tabs className={styles.tabs}>
      <TabList className={styles.tabList} aria-label={t('windows.help.title')}>
        <Tab id="keyboard" className={styles.tab}>
          {t('help.keyboard')}
        </Tab>
        <Tab id="about" className={styles.tab}>
          {t('help.about')}
        </Tab>
      </TabList>
      <TabPanel id="keyboard">
        <KeyboardMap />
      </TabPanel>
      <TabPanel id="about">
        <About />
      </TabPanel>
    </Tabs>
  );
}
