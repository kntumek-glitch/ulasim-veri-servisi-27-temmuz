export interface LocationPoint {
  latitude: number;
  longitude: number;
  name?: string;
  id?: number;
}

export interface StopSearchResponse {
  items: {
    id: number;
    externalStopId: string;
    name: string;
    latitude: number;
    longitude: number;
    routes: string[];
  }[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface JourneyPlanRequest {
  origin: {
    lat: number;
    lon: number;
  };
  destination: {
    lat: number;
    lon: number;
  };
  dateTime: string;
  searchMode: number;
  maxTransfers: number;
  maxWalkingMeters: number;
  includeIntermediateStops?: boolean;
}

export interface JourneyPlanResponse {
  searchId: string;
  isFeedStale: boolean;
  algorithm: string;
  itineraries: Itinerary[];
}

export interface Itinerary {
  planId: string;
  departureTime: string;
  arrivalTime: string;
  totalDurationMinutes: number;
  totalJourneyTimeSeconds: number;
  transferCount: number;
  totalWalkingDistanceMeters: number;
  totalWalkingTimeSeconds: number;
  totalWaitingTimeSeconds: number;
  totalInVehicleTimeSeconds: number;
  legs: Leg[];
}

export interface Leg {
  mode: 'WALK' | 'TRANSIT';
  
  // TRANSIT ONLY
  routeType?: number;
  patternId?: string;
  shapeId?: string;
  routeId?: string;
  routeShortName?: string;
  tripId?: string;
  directionId?: number;
  headsign?: string;
  
  departureTime: string;
  arrivalTime: string;
  durationSeconds: number;
  distanceMeters: number;
  
  fromStopId?: number;
  fromStopName?: string;
  fromStopSequence?: number;
  fromStopLat?: number;
  fromStopLon?: number;
  
  toStopId?: number;
  toStopName?: string;
  toStopSequence?: number;
  toStopLat?: number;
  toStopLon?: number;
  
  intermediateStops?: { id: number; name: string; arrivalTime: string }[]; // Optional depending on IncludeIntermediateStops
  geometryGeoJson?: any; // Walk geometry from OSRM
}

const API_BASE = import.meta.env.MODE === 'test' ? 'http://localhost:5108/api' : (import.meta.env.VITE_API_BASE_URL ?? '/api');

export const searchStops = async (query: string): Promise<StopSearchResponse> => {
  const res = await fetch(`${API_BASE}/v1/stops?search=${encodeURIComponent(query)}&pageSize=5`);
  if (!res.ok) throw new Error('Failed to fetch stops');
  return res.json();
};

export const searchJourney = async (request: JourneyPlanRequest): Promise<JourneyPlanResponse> => {
  const res = await fetch(`${API_BASE}/v2/journey-plans/search`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(request)
  });
  if (!res.ok) {
    let errorDetail = 'Network response was not ok';
    try {
      const errorJson = await res.json();
      if (errorJson.detail) errorDetail = errorJson.detail;
      else if (errorJson.errors) errorDetail = JSON.stringify(errorJson.errors);
      else if (errorJson.title) errorDetail = errorJson.title;
    } catch (e) {
      // Ignored if not JSON
    }
    throw new Error(errorDetail);
  }
  return res.json();
};

export interface WalkRouteRequest {
  origin: { lat: number; lon: number };
  destination: { lat: number; lon: number };
  includeGeometry?: boolean;
}

export interface DirectRoute {
  distanceMeters: number;
  durationSeconds: number;
  geometry: any;
  source: string;
  isApproximate: boolean;
  retrievedAt: string;
  alternatives?: DirectRoute[];
}

export interface WalkRouteResponse {
  distanceMeters: number;
  durationSeconds: number;
  source: string;
  isApproximate: boolean;
  retrievedAt: string;
  geometry?: any;
  alternatives?: WalkRouteResponse[];
}

export const getWalkRoute = async (request: WalkRouteRequest): Promise<WalkRouteResponse> => {
  const res = await fetch(`${API_BASE}/v1/routing/walk`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(request)
  });
  if (!res.ok) {
    let errorDetail = 'Yürüme rotası alınamadı';
    try {
      const errorJson = await res.json();
      if (errorJson.detail) errorDetail = errorJson.detail;
    } catch (e) {}
    throw new Error(errorDetail);
  }
  return res.json();
};

export const getDriveRoute = async (request: WalkRouteRequest): Promise<WalkRouteResponse> => {
  const res = await fetch(`${API_BASE}/v1/routing/drive`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(request)
  });
  if (!res.ok) {
    let errorDetail = 'Araba rotası alınamadı';
    try {
      const errorJson = await res.json();
      if (errorJson.detail) errorDetail = errorJson.detail;
    } catch (e) {}
    throw new Error(errorDetail);
  }
  return res.json();
};

export interface RouteDto {
  routeId: string;
  agencyId: string;
  routeShortName: string;
  routeLongName: string;
  routeDesc?: string;
  routeType?: number;
  routeColor?: string;
  routeTextColor?: string;
}

export interface PaginatedRoutes {
  items: RouteDto[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasNextPage: boolean;
  hasPreviousPage: boolean;
}

export interface DirectionInfo {
  directionId: number;
  headsigns: string[];
}

export interface RouteDirectionsResponse {
  routeId: string;
  directions: DirectionInfo[];
}

export interface RouteStopDto {
  stopId: string;
  stopCode?: string;
  stopName: string;
  latitude: number;
  longitude: number;
  stopSequence: number;
}

export interface ShapePointDto {
  latitude: number;
  longitude: number;
  sequence: number;
}

export const getRoutes = async (search: string, page: number = 1, pageSize: number = 20): Promise<PaginatedRoutes> => {
  const query = new URLSearchParams({ page: page.toString(), pageSize: pageSize.toString() });
  if (search) query.append('search', search);
  
  const res = await fetch(`${API_BASE}/v1/gtfs/routes?${query.toString()}`);
  if (!res.ok) throw new Error('Failed to fetch routes');
  return res.json();
};

export const getRouteDirections = async (routeId: string): Promise<RouteDirectionsResponse> => {
  const res = await fetch(`${API_BASE}/v1/gtfs/routes/${encodeURIComponent(routeId)}/directions`);
  if (!res.ok) throw new Error('Failed to fetch directions');
  return res.json();
};

export const getRouteStops = async (routeId: string, directionId: number): Promise<RouteStopDto[]> => {
  const res = await fetch(`${API_BASE}/v1/gtfs/routes/${encodeURIComponent(routeId)}/stops?directionId=${directionId}`);
  if (!res.ok) throw new Error('Failed to fetch route stops');
  return res.json();
};

export const getRouteShape = async (routeId: string, directionId: number): Promise<ShapePointDto[]> => {
  const res = await fetch(`${API_BASE}/v1/gtfs/routes/${encodeURIComponent(routeId)}/shape?directionId=${directionId}`);
  if (!res.ok) throw new Error('Failed to fetch route shape');
  return res.json();
};

export interface GtfsStopResponse {
  stopId: string;
  stopCode?: string;
  stopName: string;
  latitude: number;
  longitude: number;
}

export interface PaginatedGtfsStops {
  items: GtfsStopResponse[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number; // Added to fix Stops.tsx crash
}

export interface GtfsStopRouteResponse {
  routeId: string;
  routeShortName: string;
  routeLongName: string;
  routeColor?: string;
  routeTextColor?: string;
}

export interface RouteVehicleItem {
  busId: string;
  direction: string;
  latitude?: number;
  longitude?: number;
  locationContext: string;
  destinationName: string;
  originDepartureTime: string;
}

export interface RouteVehiclesResponse {
  routeId: string;
  vehicles: RouteVehicleItem[];
}

export const getGtfsStops = async (search: string, page: number = 1, pageSize: number = 20): Promise<PaginatedGtfsStops> => {
  const query = new URLSearchParams({ page: page.toString(), pageSize: pageSize.toString() });
  if (search) query.append('search', search);
  
  const res = await fetch(`${API_BASE}/v1/gtfs/stops?${query.toString()}`);
  if (!res.ok) throw new Error('Failed to fetch stops');
  return res.json();
};

export const getGtfsStopRoutes = async (stopId: string): Promise<GtfsStopRouteResponse[]> => {
  const res = await fetch(`${API_BASE}/v1/gtfs/stops/${encodeURIComponent(stopId)}/routes`);
  if (!res.ok) throw new Error('Failed to fetch stop routes');
  return res.json();
};

export const getRouteVehicles = async (routeNumber: string): Promise<RouteVehiclesResponse> => {
  const res = await fetch(`${API_BASE}/v1/routes/${encodeURIComponent(routeNumber)}/vehicles`);
  if (!res.ok) throw new Error('Failed to fetch live vehicles');
  return res.json();
};
