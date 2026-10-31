import DataIndex from 'Data/DataIndex';
import InfrastructureIndex from 'Infrastructure/InfrastructureIndex';
import MaintenanceIndex from 'Maintenance/MaintenanceIndex';
import { Navigate, Route, Routes } from 'react-router-dom';

import AppsIndex from 'Apps/AppsIndex';
import Status from 'System/Status/Status';
import WorkloadDetails from 'Workloads/Details/WorkloadDetails';

export default function AppRoutes() {
  return (
    <Routes>
      {/* The Overview becomes the home page in a later vertical; until then, land on Apps. */}
      <Route element={<Navigate replace to="/apps" />} path="/" />
      <Route element={<AppsIndex />} path="/apps" />
      <Route element={<InfrastructureIndex />} path="/infrastructure" />
      <Route element={<DataIndex />} path="/data" />
      <Route element={<MaintenanceIndex />} path="/maintenance" />
      {/* The Workloads list became the Infrastructure dashboard (D-54); workload pages stay. */}
      <Route element={<Navigate replace to="/infrastructure" />} path="/workloads" />
      <Route element={<WorkloadDetails />} path="/workloads/:environmentId/:name" />
      <Route element={<Status />} path="/system/status" />
      <Route element={<Navigate replace to="/" />} path="*" />
    </Routes>
  );
}
