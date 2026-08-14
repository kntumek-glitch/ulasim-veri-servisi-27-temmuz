import { render, screen, fireEvent } from '@testing-library/react';
import { renderWithProviders } from '../test-utils';
import StopSearch from '../components/StopSearch';

// Mock fetch globally for this test file
global.fetch = jest.fn();

const mockResults = {
  items: [
    { id: 1, externalStopId: 'ST001', name: 'İstanbul', latitude: 41.0, longitude: 29.0, routes: [] },
    { id: 2, externalStopId: 'ST002', name: 'Ankara', latitude: 39.9, longitude: 32.8, routes: [] },
  ],
  page: 1,
  pageSize: 5,
  totalCount: 2,
  totalPages: 1,
};

describe('StopSearch Component', () => {
  beforeEach(() => {
    (global.fetch as jest.Mock).mockClear();
  });

  test('renders autocomplete and shows results', async () => {
    (global.fetch as jest.Mock).mockResolvedValue({ ok: true, json: async () => mockResults });

    renderWithProviders(<StopSearch />);

    const input = screen.getByPlaceholderText(/durak ara/i);
    fireEvent.change(input, { target: { value: 'İstanbul' } });

    // Wait for the results to appear
    const option = await screen.findByText(/İstanbul/i);
    expect(option).toBeInTheDocument();
    expect(global.fetch).toHaveBeenCalled();
  });

  test('shows empty state when no results', async () => {
    (global.fetch as jest.Mock).mockResolvedValue({ ok: true, json: async () => ({ ...mockResults, items: [] }) });

    renderWithProviders(<StopSearch />);
    const input = screen.getByPlaceholderText(/durak ara/i);
    fireEvent.change(input, { target: { value: 'Yok' } });

    const emptyMsg = await screen.findByText(/uçacak bir durak bulunamadı/i);
    expect(emptyMsg).toBeInTheDocument();
  });
});
