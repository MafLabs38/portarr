import ProgressBar from 'Components/ProgressBar/ProgressBar';
import translate from 'Utilities/String/translate';

import styles from './CoreBars.module.css';

interface CoreBarsProps {
  /** Load of each logical core, cpu 0 first, in %. */
  cores: number[];
  machineName: string;
}

/** One compact bar per logical core, like Unraid's CPU 0…N (D-72). */
export default function CoreBars({ cores, machineName }: Readonly<CoreBarsProps>) {
  if (cores.length === 0) {
    return null;
  }

  return (
    <ul aria-label={translate('CoresOf', { name: machineName })} className={styles.cores}>
      {cores
        .map((load, cpu) => ({ cpu, load }))
        .map(({ cpu: index, load }) => {
          const value = `${Math.round(load)} %`;
          return (
            // A core is its cpu number: cpu 0…N never reorder.
            <li className={styles.core} key={`cpu-${index}`}>
              <span className={styles.name}>{translate('CoreName', { index })}</span>
              <ProgressBar
                label={translate('CoreOf', { index, name: machineName })}
                progress={load / 100}
                size="small"
                valueText={value}
              />
              <span className={styles.value}>{value}</span>
            </li>
          );
        })}
    </ul>
  );
}
