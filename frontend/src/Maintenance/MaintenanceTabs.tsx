import type { KeyboardEvent, ReactNode } from 'react';

import styles from './MaintenanceTabs.module.css';

export interface TabDefinition {
  id: string;
  label: string;
  count?: number;
}

interface MaintenanceTabsProps {
  tabs: TabDefinition[];
  selected: string;
  onSelect: (id: string) => void;
  children: ReactNode;
}

/**
 * Sub-tabs of the Maintenance page, with the WAI-ARIA tabs pattern: arrow keys, Home and End move between
 * tabs, the selected one alone is in the tab order, and the panel is labelled by its tab.
 */
export default function MaintenanceTabs({ tabs, selected, onSelect, children }: Readonly<MaintenanceTabsProps>) {
  const onKeyDown = (event: KeyboardEvent<HTMLButtonElement>) => {
    const index = tabs.findIndex((tab) => tab.id === selected);
    const moves: Record<string, number> = {
      ArrowRight: index + 1,
      ArrowLeft: index - 1,
      Home: 0,
      End: tabs.length - 1,
    };
    const target = moves[event.key];
    if (target === undefined) {
      return;
    }
    event.preventDefault();
    const next = tabs[(target + tabs.length) % tabs.length];
    if (next) {
      onSelect(next.id);
      document.getElementById(`tab-${next.id}`)?.focus();
    }
  };

  return (
    <div>
      <div className={styles.tabList} role="tablist">
        {tabs.map((tab) => (
          <button
            aria-controls={`panel-${tab.id}`}
            aria-selected={tab.id === selected}
            className={tab.id === selected ? styles.selected : styles.tab}
            id={`tab-${tab.id}`}
            key={tab.id}
            onClick={() => onSelect(tab.id)}
            onKeyDown={onKeyDown}
            role="tab"
            tabIndex={tab.id === selected ? 0 : -1}
            type="button"
          >
            {tab.label}
            {tab.count === undefined ? null : <span className={styles.count}>{tab.count}</span>}
          </button>
        ))}
      </div>
      <div aria-labelledby={`tab-${selected}`} className={styles.panel} id={`panel-${selected}`} role="tabpanel">
        {children}
      </div>
    </div>
  );
}
