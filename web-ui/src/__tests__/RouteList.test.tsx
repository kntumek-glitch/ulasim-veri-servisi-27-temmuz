import { render, screen } from '@testing-library/react';
import { renderWithProviders } from '../test-utils';
import RouteList from '../components/RouteList';

const mockRoutes = [
  { planId: 'plan1', departureTime: '2026-08-15T08:00:00Z', arrivalTime: '2026-08-15T09:00:00Z', totalDurationMinutes: 60 },
  { planId: 'plan2', departureTime: '2026-08-15T08:30:00Z', arrivalTime: '2026-08-15T09:20:00Z', totalDurationMinutes: 50 },
];

describe('RouteList Component', () => {
  test('renders list of routes', () => {
    renderWithProviders(<RouteList routes={mockRoutes} />);
    expect(screen.getByText(/plan1/i)).toBeInTheDocument();
    expect(screen.getByText(/plan2/i)).toBeInTheDocument();
  });

  test('shows empty state when no routes', () => {
    renderWithProviders(<RouteList routes={[]} />);
    expect(screen.getByText(/uçacak bir rota bulunamadı/i)).toBeInTheDocument();
  });
});
