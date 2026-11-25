import type { LucideIcon } from 'lucide-react';
import type { ReactNode } from 'react';
import { Button, type PressEvent, ToggleButton, Tooltip, TooltipTrigger } from 'react-aria-components';
import classes from './IconButton.module.css';

const styles = classes as Readonly<Record<'button' | 'tooltip' | 'kbd' | 'reason', string>>;

interface CommonProps {
  /** The accessible name, also shown as the tooltip. */
  readonly label: string;
  readonly icon: LucideIcon;
  /** Shown after the label in the tooltip, e.g. "Space". */
  readonly shortcut?: string;
  readonly isDisabled?: boolean;
  /** Extra tooltip text when disabled (why it is unavailable). */
  readonly disabledReason?: string;
  readonly tooltipPlacement?: 'top' | 'bottom' | 'start' | 'end';
}

export interface IconButtonProps extends CommonProps {
  /** The press, with its modifier keys (Shift, Ctrl, Alt). */
  readonly onPress?: (event: PressEvent) => void;
}

export interface ToggleIconButtonProps extends CommonProps {
  readonly isSelected: boolean;
  readonly onChange: (selected: boolean) => void;
}

function TooltipText({ label, shortcut, isDisabled, disabledReason }: CommonProps): ReactNode {
  return (
    <>
      {label}
      {shortcut ? <kbd className={styles.kbd}>{shortcut}</kbd> : null}
      {isDisabled && disabledReason ? <span className={styles.reason}>{disabledReason}</span> : null}
    </>
  );
}

/** A square icon button with a tooltip (its label). */
export function IconButton(props: IconButtonProps) {
  const { label, icon: Icon, isDisabled, onPress, tooltipPlacement = 'bottom' } = props;
  return (
    <TooltipTrigger delay={500}>
      <Button
        className={styles.button}
        aria-label={label}
        isDisabled={isDisabled ?? false}
        {...(onPress ? { onPress } : {})}
      >
        <Icon size={18} strokeWidth={1.75} aria-hidden="true" />
      </Button>
      <Tooltip className={styles.tooltip} placement={tooltipPlacement} offset={6}>
        <TooltipText {...props} />
      </Tooltip>
    </TooltipTrigger>
  );
}

/** An icon button that stays pressed while its option is on (layers, modes). */
export function ToggleIconButton(props: ToggleIconButtonProps) {
  const { label, icon: Icon, isDisabled, isSelected, onChange, tooltipPlacement = 'bottom' } = props;
  return (
    <TooltipTrigger delay={500}>
      <ToggleButton
        className={styles.button}
        aria-label={label}
        isDisabled={isDisabled ?? false}
        isSelected={isSelected}
        onChange={onChange}
      >
        <Icon size={18} strokeWidth={1.75} aria-hidden="true" />
      </ToggleButton>
      <Tooltip className={styles.tooltip} placement={tooltipPlacement} offset={6}>
        <TooltipText {...props} />
      </Tooltip>
    </TooltipTrigger>
  );
}
