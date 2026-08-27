// Jest provides globals for describe, it, expect, beforeEach; no import needed
import { getRouteVehicles, RouteVehiclesResponse } from './api';

describe('API functions', () => {
  beforeEach(() => {
    jest.resetAllMocks();
  });

  it('getRouteVehicles should fetch and return live vehicles', async () => {
    const mockResponse: RouteVehiclesResponse = {
      routeId: '100',
      vehicles: [
        {
          busId: '35 TEST 123',
          direction: 'Buca',
          latitude: 38.4,
          longitude: 27.1,
          locationContext: 'Some location',
          destinationName: 'Destination',
          originDepartureTime: '08:00'
        }
      ]
    };

    globalThis.fetch = jest.fn().mockResolvedValue({
      ok: true,
      json: async () => mockResponse
    });

    const result = await getRouteVehicles('100');

    expect(globalThis.fetch).toHaveBeenCalledWith(expect.stringContaining('/api/v1/routes/100/vehicles'));
    expect(result.vehicles).toHaveLength(1);
    expect(result.vehicles[0].busId).toBe('35 TEST 123');
  });

  it('getRouteVehicles should throw an error when response is not ok', async () => {
    globalThis.fetch = jest.fn().mockResolvedValue({
      ok: false
    });

    await expect(getRouteVehicles('100')).rejects.toThrow('Failed to fetch live vehicles');
  });
});
