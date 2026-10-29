import { screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { Route, Routes } from 'react-router-dom';

import renderWithProviders from '__mocks__/renderWithProviders';
import { server } from '__mocks__/server';
import WorkloadDetails from 'Workloads/Details/WorkloadDetails';

function renderAt(path: string) {
  window.history.pushState({}, '', path);
  return renderWithProviders(
    <Routes>
      <Route element={<WorkloadDetails />} path="/workloads/:environmentId/:name" />
    </Routes>,
    path,
  );
}

describe('WorkloadDetails', () => {
  beforeEach(() =>
    vi.stubGlobal(
      'EventSource',
      class {
        addEventListener() {}
        close() {}
      },
    ),
  );
  afterEach(() => vi.unstubAllGlobals());

  it('shows the live usage of a catalog container', async () => {
    renderAt('/workloads/server-1/sonarr');

    expect(await screen.findByText('0.004 vCPU')).toBeInTheDocument();
    expect(screen.getByText('132 MB of 512 MB')).toBeInTheDocument();
  });

  it('says when the container is not in the inventory', async () => {
    renderAt('/workloads/server-1/ghost');

    expect(await screen.findByRole('alert')).toHaveTextContent('This container is not in the inventory');
  });

  it('does not ask for logs or usage of a container no app declares', async () => {
    let usageCalls = 0;
    server.use(
      http.get('/api/v1/workloads/:env/:name/usage', () => {
        usageCalls += 1;
        return HttpResponse.json({});
      }),
    );

    renderAt('/workloads/server-2/grafana');

    expect(await screen.findByText(/only available for containers declared by an app/)).toBeInTheDocument();
    expect(usageCalls).toBe(0);
  });
});
