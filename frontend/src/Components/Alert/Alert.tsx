import type { ReactNode } from 'react';

import type * as kinds from 'Helpers/Props/kinds';

import styles from './Alert.module.css';

interface AlertProps {
  kind: typeof kinds.WARNING | typeof kinds.DANGER | typeof kinds.INFO;
  children: ReactNode;
}

export default function Alert({ kind, children }: AlertProps) {
  return (
    <div className={`${styles.alert} ${styles[kind]}`} role={kind === 'info' ? 'status' : 'alert'}>
      {children}
    </div>
  );
}
