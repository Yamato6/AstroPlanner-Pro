import type { HTMLAttributes } from 'react';
import classes from './Panel.module.css';

const styles = classes as Readonly<Record<'panel', string>>;

/**
 * A glass surface over the sky (hairline border, blur). With "reduce transparency" the tokens
 * make it opaque. Purely visual: the caller gives it a role and a label when it is a landmark.
 */
export function Panel({ className, ...props }: HTMLAttributes<HTMLDivElement>) {
  return <div {...props} className={className ? `${styles.panel} ${className}` : styles.panel} />;
}
