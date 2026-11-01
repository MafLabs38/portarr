import * as icons from 'Helpers/Props/icons';
import translate from 'Utilities/String/translate';

import styles from './ActiveFilter.module.css';

interface ActiveFilterProps {
  /** The filter's name, already translated. */
  label: string;
  shown: number;
  total: number;
  onClear: () => void;
}

/** The filter applied to a list (Maintenance, Data), with how many items it keeps and a button to remove it. */
export default function ActiveFilter({ label, shown, total, onClear }: Readonly<ActiveFilterProps>) {
  return (
    <div className={styles.bar}>
      <span className={styles.pill}>
        {translate('FilteredBy', { filter: label })}
        <button
          aria-label={translate('ClearFilter', { filter: label })}
          className={styles.clear}
          onClick={onClear}
          type="button"
        >
          <icons.CLEAR aria-hidden="true" size={12} strokeWidth={3} />
        </button>
      </span>
      <span className={styles.count}>{translate('ShownOfTotal', { count: shown, total })}</span>
    </div>
  );
}
