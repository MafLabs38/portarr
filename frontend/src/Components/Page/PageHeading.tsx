import styles from './PageHeading.module.css';

interface PageHeadingProps {
  scope: string;
  title: string;
}

export default function PageHeading({ scope, title }: PageHeadingProps) {
  return (
    <div className={styles.heading}>
      <span className={styles.scope}>{scope}</span>
      <h1 className={styles.title}>{title}</h1>
    </div>
  );
}
