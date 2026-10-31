import DataIndex from 'Data/DataIndex';
import InfrastructureIndex from 'Infrastructure/InfrastructureIndex';
import MaintenanceIndex from 'Maintenance/MaintenanceIndex';
import { Navigate, Route, Routes } from 'react-router-dom';

import AccountPage from 'Account/AccountPage';
import AppsIndex from 'Apps/AppsIndex';
import AssistantIndex from 'Assistant/AssistantIndex';
import Home from 'Auth/Home';
import RequirePermission from 'Auth/RequirePermission';
import UsersAuthPage from 'Settings/UsersAuth/UsersAuthPage';
import Status from 'System/Status/Status';
import WorkloadDetails from 'Workloads/Details/WorkloadDetails';

/**
 * Every page declares the permission its role needs (V7); a role without it is sent to the first page it may see. The
 * API checks the same permissions: hiding a route is a convenience, not the protection.
 */
export default function AppRoutes() {
  return (
    <Routes>
      {/* The Overview becomes the home page in a later vertical; until then, the first page the role allows. */}
      <Route element={<Home />} path="/" />
      <Route
        element={
          <RequirePermission permission="apps.view">
            <AppsIndex />
          </RequirePermission>
        }
        path="/apps"
      />
      <Route
        element={
          <RequirePermission permission="infrastructure.view">
            <InfrastructureIndex />
          </RequirePermission>
        }
        path="/infrastructure"
      />
      <Route
        element={
          <RequirePermission permission="data.view">
            <DataIndex />
          </RequirePermission>
        }
        path="/data"
      />
      <Route
        element={
          <RequirePermission permission="maintenance.view">
            <MaintenanceIndex />
          </RequirePermission>
        }
        path="/maintenance"
      />
      <Route
        element={
          <RequirePermission permission="assistant.view">
            <AssistantIndex />
          </RequirePermission>
        }
        path="/assistant"
      />
      {/* The Workloads list became the Infrastructure dashboard (D-54); workload pages stay. */}
      <Route element={<Navigate replace to="/infrastructure" />} path="/workloads" />
      <Route
        element={
          <RequirePermission permission="infrastructure.view">
            <WorkloadDetails />
          </RequirePermission>
        }
        path="/workloads/:environmentId/:name"
      />
      <Route
        element={
          <RequirePermission permission="system.view">
            <Status />
          </RequirePermission>
        }
        path="/system/status"
      />
      <Route element={<Navigate replace to="/settings/users" />} path="/settings" />
      <Route
        element={
          <RequirePermission permission="users.manage">
            <UsersAuthPage />
          </RequirePermission>
        }
        path="/settings/users"
      />
      <Route element={<Navigate replace to="/account/profile" />} path="/account" />
      <Route element={<AccountPage tab="profile" />} path="/account/profile" />
      <Route element={<AccountPage tab="api-keys" />} path="/account/api-keys" />
      <Route element={<Navigate replace to="/" />} path="*" />
    </Routes>
  );
}
