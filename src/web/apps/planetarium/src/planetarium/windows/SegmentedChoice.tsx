import { type Key, Label, ToggleButton, ToggleButtonGroup } from 'react-aria-components';
import classes from './windows.module.css';

const styles = classes as Readonly<Record<'row' | 'rowText' | 'muted' | 'segmented' | 'segment', string>>;

/** A labelled one-of-a-few choice (a segmented control). */
export function SegmentedChoice(props: {
  readonly id: string;
  readonly label: string;
  readonly description: string;
  readonly options: readonly { readonly id: string; readonly label: string }[];
  readonly selected: string;
  readonly isDisabled: boolean;
  readonly onChange: (key: Key) => void;
}) {
  return (
    <div className={styles.row}>
      <span className={styles.rowText}>
        <Label id={props.id}>{props.label}</Label>
        <span className={styles.muted}>{props.description}</span>
      </span>
      <ToggleButtonGroup
        className={styles.segmented}
        aria-labelledby={props.id}
        selectionMode="single"
        disallowEmptySelection
        isDisabled={props.isDisabled}
        selectedKeys={[props.selected]}
        onSelectionChange={(keys: Set<Key>) => {
          const [key] = [...keys];
          if (key !== undefined) {
            props.onChange(key);
          }
        }}
      >
        {props.options.map((option) => (
          <ToggleButton key={option.id} id={option.id} className={styles.segment}>
            {option.label}
          </ToggleButton>
        ))}
      </ToggleButtonGroup>
    </div>
  );
}
