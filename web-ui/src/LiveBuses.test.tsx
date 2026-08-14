import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import LiveBuses from './pages/LiveBuses';
import { MapProvider } from './context/MapContext';
import * as api from './api';

// Mock the API and MapContext
jest.mock('./api', () => ({
  getRouteVehicles: jest.fn(),
}));

const queryClient = new QueryClient({
  defaultOptions: {
    queries: { retry: false },
  },
});

const renderWithProviders = (component: React.ReactElement) => {
  return render(
    <QueryClientProvider client={queryClient}>
      <MapProvider>
        {component}
      </MapProvider>
    </QueryClientProvider>
  );
};

describe('LiveBuses Component', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('renders the initial search UI', () => {
    renderWithProviders(<LiveBuses />);
    
    expect(screen.getByText('Canlı Otobüs Konumları')).toBeInTheDocument();
    expect(screen.getByPlaceholderText('Örn: 100, 200, 800...')).toBeInTheDocument();
  });

  it('searches for a route and displays live buses', async () => {
    const mockResponse = {
      routeId: '290',
      vehicles: [
        {
          busId: '35 ESHOT 01',
          direction: 'Bostanlı İskele',
          latitude: 38.4,
          longitude: 27.1
        }
      ]
    };

    (api.getRouteVehicles as any).mockResolvedValue(mockResponse);

    renderWithProviders(<LiveBuses />);

    const input = screen.getByPlaceholderText('Örn: 100, 200, 800...');
    fireEvent.change(input, { target: { value: '290' } });

    const searchButton = screen.getByRole('button', { name: 'Araçları Bul' }); 
    fireEvent.click(searchButton);

    expect(api.getRouteVehicles).toHaveBeenCalledWith('290');

    await waitFor(() => {
      expect(screen.getByText('Plaka: 35 ESHOT 01')).toBeInTheDocument();
      expect(screen.getByText('Yön: Bostanlı İskele')).toBeInTheDocument();
    });
  });
});
