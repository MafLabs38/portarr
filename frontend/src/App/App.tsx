import { QueryClientProvider } from '@tanstack/react-query';
import { useState } from 'react';
import { BrowserRouter } from 'react-router-dom';

import AuthGate from 'Auth/AuthGate';
import Page from 'Components/Page/Page';

import AppRoutes from './AppRoutes';
import { createQueryClient } from './queryClient';

export default function App() {
  const [queryClient] = useState(createQueryClient);

  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <AuthGate>
          <Page>
            <AppRoutes />
          </Page>
        </AuthGate>
      </BrowserRouter>
    </QueryClientProvider>
  );
}
