// test-utils.tsx – Provides a wrapper for rendering components with required providers
import React from 'react';
import { render } from '@testing-library/react';
import { BrowserRouter } from 'react-router-dom';
import { ThemeProvider } from './theme/ThemeProvider';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MapProvider } from './context/MapContext';

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: false,
      staleTime: Infinity,
    },
  },
});

export function renderWithProviders(ui: React.ReactElement) {
  return render(
    <BrowserRouter>
      <ThemeProvider>
        <QueryClientProvider client={queryClient}>
          <MapProvider>{ui}</MapProvider>
        </QueryClientProvider>
      </ThemeProvider>
    </BrowserRouter>
  );
}
