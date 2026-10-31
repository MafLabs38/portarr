import * as icons from 'Helpers/Props/icons';
import type { DataAsset } from 'typings/Data';
import translate from 'Utilities/String/translate';

import styles from './AssetRow.module.css';
import CopyCell from './CopyCell';
import CoverageLabel from './CoverageLabel';
import { describeWindow } from './describeCopy';

interface AssetRowProps {
  asset: DataAsset;
}

/** Kinds that are databases; the others (volume, files, app configuration…) are files on a disk. */
const databaseKinds = new Set(['postgresql', 'mariadb', 'mysql', 'mongodb', 'influxdb', 'redis', 'vault', 'sqlite']);

function kindLabel(kind: string): string {
  const key = `Kind_${kind}`;
  const text = translate(key);
  return text === key ? kind : text;
}

/**
 * One followed data asset: its state (the worst of its two copies), what it is, where it lives and the accepted
 * loss window, then its local and off-site copies side by side.
 */
export default function AssetRow({ asset }: Readonly<AssetRowProps>) {
  const KindIcon = databaseKinds.has(asset.kind) ? icons.DATABASE : icons.APP_DATA;
  const { where } = asset;
  const place = [where.environmentName, where.namespace, where.workload, where.claim].filter(Boolean).join(' · ');

  return (
    <li aria-label={asset.name} className={styles.row}>
      <CoverageLabel value={asset.state} />

      <div className={styles.what}>
        <span className={styles.name}>{asset.name}</span>
        <span className={styles.meta}>
          <span className={styles.kind}>
            <KindIcon aria-hidden="true" size={14} strokeWidth={2.25} />
            {kindLabel(asset.kind)}
          </span>
          <span>{describeWindow(asset)}</span>
        </span>
        <span className={styles.meta}>
          <span>{place}</span>
          {where.path ? <span className={styles.path}>{where.path}</span> : null}
        </span>
      </div>

      <CopyCell copy={asset.local} target="local" />
      <CopyCell copy={asset.offsite} target="offsite" />
    </li>
  );
}
