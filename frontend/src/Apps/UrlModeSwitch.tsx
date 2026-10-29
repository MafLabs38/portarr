import translate from 'Utilities/String/translate';

import { resolveScope, type UrlMode, useUrlModeStore } from './urlMode';
import styles from './UrlModeSwitch.module.css';

const modes: UrlMode[] = ['auto', 'public', 'lan'];
const labels: Record<UrlMode, string> = { auto: 'UrlModeAuto', public: 'UrlModePublic', lan: 'UrlModeLan' };

export default function UrlModeSwitch() {
  const { mode, setMode } = useUrlModeStore();
  const effective = resolveScope(mode, window.location.hostname);

  return (
    <div aria-label={translate('OpenAppsWith')} className={styles.switch} role="radiogroup">
      {modes.map((value) => (
        <button
          aria-checked={mode === value}
          className={mode === value ? styles.active : styles.option}
          key={value}
          onClick={() => setMode(value)}
          role="radio"
          type="button"
        >
          {translate(labels[value])}
          {value === 'auto' ? ` (${translate(effective === 'lan' ? 'UrlModeLan' : 'UrlModePublic')})` : null}
        </button>
      ))}
    </div>
  );
}
