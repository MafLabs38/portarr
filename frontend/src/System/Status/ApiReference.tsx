import Card from 'Components/Card';
import * as icons from 'Helpers/Props/icons';
import translate from 'Utilities/String/translate';

import styles from './ApiReference.module.css';

/**
 * Where the API is documented (F-API-01): the interactive reference and the OpenAPI document, both served by
 * Portarr itself. Plain links, not routes: the backend answers them.
 */
export default function ApiReference() {
  return (
    <Card title={translate('Api')}>
      <p className={styles.hint}>{translate('ApiHint')}</p>
      <ul className={styles.links}>
        <li>
          <a className={styles.link} href="/docs/">
            <icons.API aria-hidden="true" size={16} strokeWidth={2.25} />
            {translate('ApiReference')}
          </a>
        </li>
        <li>
          <a className={styles.link} href="/openapi/v1.json">
            <icons.API aria-hidden="true" size={16} strokeWidth={2.25} />
            {translate('OpenApiDocument')}
          </a>
        </li>
      </ul>
    </Card>
  );
}
