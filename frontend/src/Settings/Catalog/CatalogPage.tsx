import MaintenanceTabs from 'Maintenance/MaintenanceTabs';
import { useState } from 'react';

import usePermissions from 'Auth/usePermissions';
import Alert from 'Components/Alert/Alert';
import form from 'Components/Form/Form.module.css';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageHeading from 'Components/Page/PageHeading';
import { SETTINGS_MANAGE } from 'Helpers/Props/permissions';
import { usePortal } from 'Settings/General/usePortal';
import SettingsNav from 'Settings/SettingsNav';
import { catalogSections } from 'typings/Catalog';
import translate from 'Utilities/String/translate';

import styles from './Catalog.module.css';
import SectionPanel from './SectionPanel';

/**
 * Settings, Catalog: the description of the infrastructure, edited here as through the API and the MCP tools (the same
 * path, the same checks). Every change ends up in the catalog file, which stays readable and versionable; this page is one
 * way to write it.
 */
export default function CatalogPage() {
  const [tab, setTab] = useState<string>('apps');
  const portal = usePortal();
  const { can } = usePermissions();
  const writable = portal.data?.writable === true;

  const tabs = catalogSections.map((section) => ({ id: section, label: translate(`CatalogSection_${section}`) }));

  return (
    <PageContent title={translate('Catalog')}>
      <PageContentBody>
        <PageHeading scope={translate('Settings')} title={translate('Catalog')} />
        <SettingsNav />
        <p className={form.help}>{translate('CatalogHelp')}</p>
        {portal.data && !writable ? <Alert kind="warning">{translate('CatalogReadOnly')}</Alert> : null}
        {can(SETTINGS_MANAGE) ? (
          <div className={styles.toolbar}>
            <a className={form.secondary} download="catalog.yaml" href="/api/v1/catalog/export">
              {translate('CatalogExport')}
            </a>
          </div>
        ) : null}
        <MaintenanceTabs onSelect={setTab} selected={tab} tabs={tabs}>
          <SectionPanel key={tab} section={tab as (typeof catalogSections)[number]} writable={writable} />
        </MaintenanceTabs>
      </PageContentBody>
    </PageContent>
  );
}
