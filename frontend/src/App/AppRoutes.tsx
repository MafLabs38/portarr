import { Navigate, Route, Routes } from 'react-router-dom';

import AppsIndex from 'Apps/AppsIndex';
import Status from 'System/Status/Status';
import WorkloadDetails from 'Workloads/Details/WorkloadDetails';
import WorkloadsIndex from 'Workloads/WorkloadsIndex';

export default function AppRoutes() {
  return (
    <Routes>
      {/* The Overview becomes the home page in a later vertical; until then, land on Apps. */}
      <Route element={<Navigate replace to="/apps" />} path="/" />
      <Route element={<AppsIndex />} path="/apps" />
      <Route element={<WorkloadsIndex />} path="/workloads" />
      <Route element={<WorkloadDetails />} path="/workloads/:environmentId/:name" />
      <Route element={<Status />} path="/system/status" />
      <Route element={<Navigate replace to="/" />} path="*" />
    </Routes>
  );
}
