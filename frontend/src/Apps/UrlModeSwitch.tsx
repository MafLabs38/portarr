import translate from 'Utilities/String/translate';

import { resolveScope, type UrlMode, useUrlModeStore } from './urlMode';
import styles from './UrlModeSwitch.module.css';

const modes: UrlMode[] = ['auto', 'public', 'lan'];
const labels: Record<UrlMode, string> = { auto: 'UrlModeAuto', public: 'UrlModePublic', lan: 'UrlModeLan' };

/** Which URL the tiles open (D-46); native radios give keyboard navigation for free. */
export default function UrlModeSwitch() {
  const { mode, setMode } = useUrlModeStore();
  const effective = translate(labels[resolveScope(mode, globalThis.location.hostname)]);

  return (
    <fieldset className={styles.switch}>
      <legend className={styles.legend}>{translate('OpenAppsWith')}</legend>
      {modes.map((value) => (
        <label className={mode === value ? styles.active : styles.option} key={value}>
          <input
            checked={mode === value}
            className={styles.input}
            name="urlMode"
            onChange={() => setMode(value)}
            type="radio"
            value={value}
          />
          {translate(labels[value])}
          {value === 'auto' ? ` (${effective})` : null}
        </label>
      ))}
    </fieldset>
  );
}
