import MaintenanceTabs from 'Maintenance/MaintenanceTabs';
import { useState } from 'react';

import Alert from 'Components/Alert/Alert';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageHeading from 'Components/Page/PageHeading';
import SettingsNav from 'Settings/SettingsNav';
import translate from 'Utilities/String/translate';

import ActivityTable from './ActivityTable';
import RolesEditor from './RolesEditor';
import UsersTable from './UsersTable';
import { useActivity, useRoles, useUsers } from './useUsersAuth';

type Tab = 'users' | 'roles' | 'activity';

/**
 * Settings, Users & Auth (V7, D-93, F-SET-05), behind `users.manage`: the users, the roles (named sets of permissions;
 * the built-in four are fixed, custom roles are edited here) and the latest 200 events of the accounts. One page with
 * tabs while the sidebar has no second level.
 */
export default function UsersAuthPage() {
  const [tab, setTab] = useState<Tab>('users');
  const users = useUsers();
  const roles = useRoles();
  const activity = useActivity(tab === 'activity');

  const tabs = [
    { id: 'users', label: translate('Users'), count: users.data?.length },
    { id: 'roles', label: translate('Roles'), count: roles.data?.roles.length },
    { id: 'activity', label: translate('Activity') },
  ];

  return (
    <PageContent title={translate('UsersAndAuth')}>
      <PageContentBody>
        <PageHeading scope={translate('Settings')} title={translate('UsersAndAuth')} />
        <SettingsNav />
        {users.isError || roles.isError ? <Alert kind="danger">{translate('ErrorLoadingUsers')}</Alert> : null}
        <MaintenanceTabs onSelect={(id) => setTab(id as Tab)} selected={tab} tabs={tabs}>
          {tab === 'users' && users.data && roles.data ? (
            <UsersTable roles={roles.data.roles} users={users.data} />
          ) : null}
          {tab === 'roles' && roles.data ? <RolesEditor list={roles.data} /> : null}
          {tab === 'activity' ? <ActivityTable activity={activity.data} failed={activity.isError} /> : null}
        </MaintenanceTabs>
      </PageContentBody>
    </PageContent>
  );
}
