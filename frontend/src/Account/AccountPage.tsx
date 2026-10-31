import MaintenanceTabs from 'Maintenance/MaintenanceTabs';
import { useNavigate } from 'react-router-dom';

import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageHeading from 'Components/Page/PageHeading';
import translate from 'Utilities/String/translate';

import layout from './Account.module.css';
import ApiKeysCard from './ApiKeysCard';
import PasswordCard from './PasswordCard';
import ProfileCard from './ProfileCard';

interface AccountPageProps {
  tab: 'profile' | 'api-keys';
}

/** The signed-in user's own account (V7): profile and password, and API keys. Reached from the account menu. */
export default function AccountPage({ tab }: Readonly<AccountPageProps>) {
  const navigate = useNavigate();
  const tabs = [
    { id: 'profile', label: translate('Profile') },
    { id: 'api-keys', label: translate('ApiKeys') },
  ];

  return (
    <PageContent title={translate('Account')}>
      <PageContentBody>
        <PageHeading scope={translate('Account')} title={translate(tab === 'profile' ? 'Profile' : 'ApiKeys')} />
        <MaintenanceTabs onSelect={(id) => navigate(`/account/${id}`)} selected={tab} tabs={tabs}>
          {tab === 'profile' ? (
            <div className={layout.cards}>
              <ProfileCard />
              <PasswordCard />
            </div>
          ) : (
            <div className={layout.cards}>
              <ApiKeysCard />
            </div>
          )}
        </MaintenanceTabs>
      </PageContentBody>
    </PageContent>
  );
}
