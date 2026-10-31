import 'Components/Chart/chartSetup';

import type { ChartData, ChartDataset, ChartOptions } from 'chart.js';
import { Line } from 'react-chartjs-2';

import { themeColor, withAlpha } from 'Components/Chart/chartSetup';

import styles from './CpuChart.module.css';

/** A point of the chart: its time, its main value and the second series (VMs or I/O wait), in the chart's unit. */
export interface CpuChartPoint {
  at: string;
  value: number | null;
  extra: number | null;
}

interface CpuChartProps {
  points: CpuChartPoint[];
  /** The values are % of the machine (0–100 axis); false: cores, axis from 0 to the highest value. */
  percent: boolean;
  /** The live window, in seconds: the time axis always spans it, so a young chart fills from the right. */
  windowSeconds: number;
  /**
   * The second series: <c>guests</c> stacked on top (a hypervisor's VMs, D-59), <c>iowait</c> drawn from zero
   * under the load it is part of (a host agent, D-72); none: no second series.
   */
  extra?: 'guests' | 'iowait';
  label: string;
}

type Point = { x: number; y: number | null };

/**
 * CPU over the live window (D-54), 0–100 % of the machine like the Unraid dashboard, on a time axis: points
 * are drawn where they were measured (every second from a host agent, every 3 s for Docker, every ~15 s for
 * metrics-server), never invented. Updated in place without animation, straight segments, no parsing: a
 * redraw every second stays cheap.
 */
export default function CpuChart({ points, percent, windowSeconds, extra, label }: Readonly<CpuChartProps>) {
  const end = points.length > 0 ? Date.parse(points[points.length - 1]?.at ?? '') : 0;
  const line = themeColor('--secondary');
  const grid = themeColor('--hairline');
  const ink = themeColor('--inkMuted');
  const times = points.map((point) => Date.parse(point.at));

  const datasets: ChartDataset<'line', Point[]>[] = [
    {
      // A stable label: react-chartjs-2 matches datasets by it, so each tick updates the same objects in place
      // (without, the second series is re-created every second).
      label: 'load',
      data: points.map((point, index) => ({ x: times[index] ?? 0, y: point.value })),
      borderColor: line,
      backgroundColor: withAlpha(line, 0.25),
      borderWidth: 1.5,
      fill: 'origin',
      pointRadius: 0,
      tension: 0,
      spanGaps: false,
      order: 1,
    },
  ];
  if (extra) {
    const color = themeColor(extra === 'guests' ? '--pink' : '--warningColor');
    datasets.push({
      label: extra,
      data: points.map((point, index) => ({ x: times[index] ?? 0, y: point.extra })),
      borderColor: color,
      backgroundColor: withAlpha(color, extra === 'guests' ? 0.25 : 0.45),
      borderWidth: 1,
      fill: extra === 'guests' ? '-1' : 'origin',
      pointRadius: 0,
      tension: 0,
      spanGaps: false,
      order: 0,
    });
  }

  const data: ChartData<'line', Point[]> = { datasets };

  const options: ChartOptions<'line'> = {
    animation: false,
    events: [],
    maintainAspectRatio: false,
    normalized: true,
    parsing: false,
    plugins: { legend: { display: false }, tooltip: { enabled: false } },
    scales: {
      x: { type: 'linear', display: false, min: end - windowSeconds * 1_000, max: end },
      y: {
        min: 0,
        max: percent ? 100 : undefined,
        stacked: extra === 'guests',
        border: { display: false },
        grid: { color: grid },
        ticks: { color: ink, maxTicksLimit: 3, callback: (value) => (percent ? `${value} %` : String(value)) },
      },
    },
  };

  return (
    <div className={styles.chart}>
      <Line aria-label={label} data={data} options={options} role="img" updateMode="none" />
    </div>
  );
}
