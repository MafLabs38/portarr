import { Navigate, Route, Routes } from 'react-router-dom';

import Status from 'System/Status/Status';

export default function AppRoutes() {
  return (
    <Routes>
      {/* The Overview becomes the home page in V5; until then, land on System → Status. */}
      <Route element={<Navigate replace to="/system/status" />} path="/" />
      <Route element={<Status />} path="/system/status" />
      <Route element={<Navigate replace to="/" />} path="*" />
    </Routes>
  );
}
