import styles from './ProgressBar.module.css';

interface ProgressBarProps {
  /** Between 0 and 1; values outside are clamped. */
  progress: number;
  /** Accessible name, e.g. "CPU of sonarr". */
  label: string;
  /** Read by assistive technologies, e.g. "12 %". */
  valueText: string;
  /** small (5 px), medium (15 px), large (20 px), as in the *arr ProgressBar. */
  size?: 'small' | 'medium' | 'large';
  /** Bar color role. */
  color?: 'cpu' | 'memory' | 'guests' | 'swap';
  /** A second part stacked after the first (0 to 1, e.g. the VMs of a hypervisor), in the guests color. */
  stacked?: number;
}

export default function ProgressBar({
  progress,
  label,
  valueText,
  size = 'small',
  color = 'cpu',
  stacked,
}: Readonly<ProgressBarProps>) {
  const value = Math.min(Math.max(progress, 0), 1);
  const extra = stacked === undefined ? 0 : Math.min(Math.max(stacked, 0), 1 - value);

  return (
    <div
      aria-label={label}
      aria-valuemax={100}
      aria-valuemin={0}
      aria-valuenow={Math.round((value + extra) * 100)}
      aria-valuetext={valueText}
      className={`${styles.track} ${styles[size]}`}
      role="progressbar"
    >
      <div className={`${styles.bar} ${styles[color]}`} style={{ width: `${(value * 100).toFixed(1)}%` }} />
      {stacked === undefined ? null : (
        <div className={`${styles.bar} ${styles.guests}`} style={{ width: `${(extra * 100).toFixed(1)}%` }} />
      )}
    </div>
  );
}
