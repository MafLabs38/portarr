import type App from 'typings/App';
import type { Repository } from 'typings/Maintenance';

import styles from './MaintenanceIndex.module.css';
import RepositoryRow from './RepositoryRow';

interface RepositoryGroupProps {
  title: string;
  repositories: Repository[];
  apps: Map<string, App>;
}

/** The repositories of one group (running in the fleet, or not deployed here); nothing when the group is empty. */
export default function RepositoryGroup({ title, repositories, apps }: Readonly<RepositoryGroupProps>) {
  if (repositories.length === 0) {
    return null;
  }
  return (
    <div className={styles.group}>
      <h3 className={styles.groupHeading}>
        <span className={styles.groupName}>{title}</span>
        <span className={styles.meta}>{repositories.length}</span>
      </h3>
      <ul aria-label={title} className={styles.list}>
        {repositories.map((repository) => (
          <RepositoryRow apps={apps} key={repository.fullName} repository={repository} />
        ))}
      </ul>
    </div>
  );
}
