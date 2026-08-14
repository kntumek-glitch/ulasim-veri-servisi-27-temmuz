import { render, screen } from '@testing-library/react';
import { renderWithProviders } from '../test-utils';
import MapRenderer from '../components/MapRenderer';

// Mock fetch for shape data
global.fetch = jest.fn();

const mockShape = [{ latitude: 41.0, longitude: 29.0, sequence: 0 }];

describe('MapRenderer Component', () => {
  beforeEach(() => {
    (global.fetch as jest.Mock).mockClear();
  });

  test('renders map container and loads shape data', async () => {
    (global.fetch as jest.Mock).mockResolvedValue({ ok: true, json: async () => mockShape });
    renderWithProviders(<MapRenderer routeId="R1" directionId={0} />);
    // Map container should be in the document
    const mapDiv = screen.getByTestId('map-container');
    expect(mapDiv).toBeInTheDocument();
    // Wait for fetch to be called
    expect(global.fetch).toHaveBeenCalled();
  });
});
