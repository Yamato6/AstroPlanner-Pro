import type { LucideIcon } from 'lucide-react';
import type { ReactNode } from 'react';
import { Button as AriaButton, SwitchButton, SwitchField, Text } from 'react-aria-components';
import classes from './Controls.module.css';

const styles = classes as Readonly<
  Record<'button' | 'primary' | 'field' | 'switch' | 'track' | 'thumb' | 'description', string>
>;

export interface ButtonProps {
  readonly children: ReactNode;
  readonly icon?: LucideIcon;
  /** The main action of its group (at most one). */
  readonly isPrimary?: boolean;
  readonly isDisabled?: boolean;
  readonly onPress?: () => void;
}

/** A text button. */
export function Button({ children, icon: Icon, isPrimary, isDisabled, onPress }: ButtonProps) {
  return (
    <AriaButton
      className={isPrimary ? `${styles.button} ${styles.primary}` : styles.button}
      isDisabled={isDisabled ?? false}
      {...(onPress ? { onPress } : {})}
    >
      {Icon ? <Icon size={15} strokeWidth={1.75} aria-hidden="true" /> : null}
      {children}
    </AriaButton>
  );
}

export interface SwitchProps {
  readonly children: ReactNode;
  /** A line under the label: what the option does. */
  readonly description?: string;
  readonly isSelected: boolean;
  readonly isDisabled?: boolean;
  readonly onChange: (selected: boolean) => void;
}

/** An on/off option: its label on the left, the switch on the right, an optional description below. */
export function Switch({ children, description, isSelected, isDisabled, onChange }: SwitchProps) {
  return (
    <SwitchField
      className={styles.field}
      isSelected={isSelected}
      isDisabled={isDisabled ?? false}
      onChange={onChange}
    >
      <SwitchButton className={styles.switch}>
        {children}
        <span className={styles.track} aria-hidden="true">
          <span className={styles.thumb} />
        </span>
      </SwitchButton>
      {description ? (
        <Text slot="description" className={styles.description}>
          {description}
        </Text>
      ) : null}
    </SwitchField>
  );
}
