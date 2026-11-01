import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render } from '@testing-library/react';
import type { ReactElement } from 'react';
import { MemoryRouter } from 'react-router';

/** @param client A client to use instead of a plain one, e.g. the app's own (`createQueryClient`) to test how it reacts to a 401. */
export default function renderWithProviders(
  ui: ReactElement,
  route = '/',
  client = new QueryClient({ defaultOptions: { queries: { retry: false } } }),
) {
  const queryClient = client;

  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[route]}>{ui}</MemoryRouter>
    </QueryClientProvider>,
  );
}
