import { screen, waitFor, fireEvent, act } from '@testing-library/react';
import { rest } from 'msw';
import { setupServer } from 'msw/node';
import React, { useEffect } from 'react';
import { renderWithProviders } from '../test-utils';
import TripPlanner from '../pages/TripPlanner';
import { useMapState } from '../context/MapContext';

const server = setupServer();

beforeAll(() => server.listen());
afterEach(() => server.resetHandlers());
afterAll(() => server.close());

// Helper component that sets MapContext state before rendering TripPlanner
const TripPlannerWithLocations: React.FC = () => {
  const { setMapOrigin, setMapDestination } = useMapState();
  useEffect(() => {
    setMapOrigin({ latitude: 38.4192, longitude: 27.1287, name: 'Bostanlı' });
    setMapDestination({ latitude: 38.4237, longitude: 27.1428, name: 'Konak' });
  }, [setMapOrigin, setMapDestination]);
  return <TripPlanner />;
};

describe('TripPlanner Component', () => {
  test('renders the planner form with inputs and search button', () => {
    renderWithProviders(<TripPlanner />);

    expect(screen.getByText(/rota planlayıcı/i)).toBeInTheDocument();
    expect(screen.getByPlaceholderText(/başlangıç/i)).toBeInTheDocument();
    expect(screen.getByPlaceholderText(/varış/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /rota bul/i })).toBeInTheDocument();
  });

  test('shows validation errors when searching without locations', async () => {
    renderWithProviders(<TripPlanner />);

    const searchBtn = screen.getByRole('button', { name: /rota bul/i });
    await act(async () => {
      fireEvent.click(searchBtn);
    });

    expect(screen.getByText(/başlangıç noktası seçmelisiniz/i)).toBeInTheDocument();
    expect(screen.getByText(/varış noktası seçmelisiniz/i)).toBeInTheDocument();
  });

  test('successful journey search displays results', async () => {
    server.use(
      rest.post('*/v2/journey-plans/search', (_req, res, ctx) => {
        return res(
          ctx.status(200),
          ctx.json({
            searchId: 'test',
            isFeedStale: false,
            algorithm: 'RAPTOR',
            itineraries: [
              {
                planId: 'plan1',
                departureTime: '2026-08-15T08:00:00Z',
                arrivalTime: '2026-08-15T09:00:00Z',
                totalDurationMinutes: 60,
                totalJourneyTimeSeconds: 3600,
                transferCount: 0,
                totalWalkingDistanceMeters: 0,
                totalWalkingTimeSeconds: 0,
                totalWaitingTimeSeconds: 0,
                totalInVehicleTimeSeconds: 3600,
                legs: []
              }
            ]
          })
        );
      })
    );

    renderWithProviders(<TripPlannerWithLocations />);

    // Wait for LocationInput effects to settle, then find the search button
    await waitFor(() => {
      expect(screen.getByRole('button', { name: /rota bul/i })).toBeInTheDocument();
    });

    const searchBtn = screen.getByRole('button', { name: /rota bul/i });
    await act(async () => {
      fireEvent.click(searchBtn);
    });

    // The component renders "60 dk | 0 aktarma" for the itinerary
    await waitFor(() => {
      const durationElems = screen.getAllByText(/60 dk/i);
      const transferElems = screen.getAllByText(/0 aktarma/i);
      expect(durationElems.length).toBeGreaterThan(0);
      expect(transferElems.length).toBeGreaterThan(0);
    });
  });

  test('shows error message when API returns an error', async () => {
    server.use(
      rest.post('*/v2/journey-plans/search', (_req, res, ctx) => {
        return res(ctx.status(400), ctx.json({ title: 'NO_ROUTE_FOUND' }));
      })
    );

    renderWithProviders(<TripPlannerWithLocations />);

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /rota bul/i })).toBeInTheDocument();
    });

    const searchBtn = screen.getByRole('button', { name: /rota bul/i });
    await act(async () => {
      fireEvent.click(searchBtn);
    });

    // The API error message from searchJourney: errorJson.title → "NO_ROUTE_FOUND"
    // Shown via: searchMutation.error.message
    await waitFor(() => {
      expect(screen.getByText(/NO_ROUTE_FOUND/i)).toBeInTheDocument();
    });
  });
});
