import type { ChartData, ChartOptions } from 'chart.js';
import { Doughnut } from 'react-chartjs-2';

import { themeColor } from 'Components/Chart/chartSetup';

import styles from './MemoryDonut.module.css';

/** A part of the ring: its size and its color token (e.g. --purple). */
export interface MemorySegment {
  label: string;
  bytes: number;
  token: string;
}

interface MemoryDonutProps {
  /** The used parts, in order; the rest of the total is drawn as free. */
  segments: MemorySegment[];
  total: number | null;
  label: string;
  /** Center text: the used share. */
  center: string;
}

/** Memory as a ring like the Unraid dashboard: containers, VMs… then free. Updated in place, no animation. */
export default function MemoryDonut({ segments, total, label, center }: Readonly<MemoryDonutProps>) {
  const used = segments.reduce((sum, s) => sum + s.bytes, 0);
  const free = total === null ? 0 : Math.max(total - used, 0);
  const empty = total === null && used === 0;

  const data: ChartData<'doughnut', number[], string> = {
    labels: [...segments.map((s) => s.label), ''],
    datasets: [
      {
        data: empty ? [...segments.map(() => 0), 1] : [...segments.map((s) => s.bytes), free],
        backgroundColor: [...segments.map((s) => themeColor(s.token)), themeColor('--paperSunken')],
        borderWidth: 0,
      },
    ],
  };

  const options: ChartOptions<'doughnut'> = {
    animation: false,
    cutout: '70%',
    maintainAspectRatio: false,
    plugins: { legend: { display: false }, tooltip: { enabled: false } },
  };

  return (
    <div className={styles.donut}>
      <Doughnut aria-label={label} data={data} options={options} role="img" updateMode="none" />
      <span aria-hidden="true" className={styles.center}>
        {center}
      </span>
    </div>
  );
}
