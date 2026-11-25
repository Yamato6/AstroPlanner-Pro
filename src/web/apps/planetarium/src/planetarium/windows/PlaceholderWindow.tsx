import { useTranslation } from 'react-i18next';
import { type WindowId, windowDefinitions } from './registry';
import classes from './windows.module.css';

const styles = classes as Readonly<Record<'empty' | 'emptyIcon' | 'emptyTitle' | 'muted', string>>;

/** A window that has not moved to the new interface yet: says so and where to find it meanwhile. */
export function PlaceholderWindow({ id }: { readonly id: WindowId }) {
  const { t } = useTranslation();
  const Icon = windowDefinitions[id].icon;
  const title = t(`windows.${id}.title`);
  return (
    <div className={styles.empty}>
      <Icon className={styles.emptyIcon} size={28} strokeWidth={1.5} aria-hidden="true" />
      <p className={styles.emptyTitle}>{t('placeholder.title', { title })}</p>
      <p className={styles.muted}>{t(`windows.${id}.summary`)}</p>
      <p className={styles.muted}>{t('placeholder.meanwhile')}</p>
    </div>
  );
}
