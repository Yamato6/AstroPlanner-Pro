import { IconButton } from '@astroplanner/ui-kit';
import { X } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { useToasts } from '../state/toasts';
import classes from './Toasts.module.css';

const styles = classes as Readonly<Record<'region' | 'toast' | 'message', string>>;

/** Notices at the bottom right (spec §9.4): read politely by screen readers, never focused. */
export function Toasts() {
  const { t } = useTranslation();
  const toasts = useToasts((state) => state.toasts);
  const dismiss = useToasts((state) => state.dismiss);

  return (
    <section className={styles.region} aria-label={t('toasts.label')}>
      <ol aria-live="polite" aria-relevant="additions">
        {toasts.map((toast) => (
          <li key={toast.id} className={styles.toast} data-tone={toast.tone}>
            <span className={styles.message}>{toast.message}</span>
            <IconButton
              label={t('toasts.close')}
              icon={X}
              tooltipPlacement="top"
              onPress={() => {
                dismiss(toast.id);
              }}
            />
          </li>
        ))}
      </ol>
    </section>
  );
}
