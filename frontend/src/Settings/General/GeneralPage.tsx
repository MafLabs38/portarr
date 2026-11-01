import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageHeading from 'Components/Page/PageHeading';
import SettingsNav from 'Settings/SettingsNav';
import translate from 'Utilities/String/translate';

import Identity from './Identity';

/** Settings, General: what is Portarr's own to configure and is not an account, today the identity of the portal (D-91). */
export default function GeneralPage() {
  return (
    <PageContent title={translate('General')}>
      <PageContentBody>
        <PageHeading scope={translate('Settings')} title={translate('General')} />
        <SettingsNav />
        <Identity />
      </PageContentBody>
    </PageContent>
  );
}
