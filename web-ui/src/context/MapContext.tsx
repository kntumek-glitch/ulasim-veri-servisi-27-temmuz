import React, { createContext, useContext, useState, ReactNode, useEffect } from 'react';
import { Itinerary, LocationPoint, ShapePointDto, WalkRouteResponse } from '../api';

export type TravelMode = 'TRANSIT' | 'WALK' | 'DRIVE';
export type Theme = 'dark' | 'light';

interface MapContextType {
  selectedItinerary: Itinerary | null;
  setSelectedItinerary: (itinerary: Itinerary | null) => void;
  itineraries: Itinerary[];
  setItineraries: (itineraries: Itinerary[]) => void;
  mapOrigin: LocationPoint | null;
  setMapOrigin: (loc: LocationPoint | null) => void;
  mapDestination: LocationPoint | null;
  setMapDestination: (loc: LocationPoint | null) => void;
  selectedRouteShape: ShapePointDto[][] | null;
  setSelectedRouteShape: (shape: ShapePointDto[][] | null) => void;
  selectedRouteColor: string;
  setSelectedRouteColor: (color: string) => void;
  selectedStop: LocationPoint | null;
  setSelectedStop: (stop: LocationPoint | null) => void;
  pickingLocationFor: 'origin' | 'destination' | null;
  setPickingLocationFor: (type: 'origin' | 'destination' | null) => void;
  liveVehicles: import('../api').RouteVehicleItem[];
  setLiveVehicles: (vehicles: import('../api').RouteVehicleItem[]) => void;
  selectedLiveBusId: string | null;
  setSelectedLiveBusId: (id: string | null) => void;
  userLocation: LocationPoint | null;
  setUserLocation: (loc: LocationPoint | null) => void;
  travelMode: TravelMode;
  setTravelMode: (mode: TravelMode) => void;
  directRoute: WalkRouteResponse | null;
  setDirectRoute: (route: WalkRouteResponse | null) => void;
  selectedDirectRouteIdx: number;
  setSelectedDirectRouteIdx: (idx: number) => void;
  theme: Theme;
  setTheme: (theme: Theme) => void;
}

const MapContext = createContext<MapContextType | undefined>(undefined);

export const MapProvider: React.FC<{children: ReactNode}> = ({ children }) => {
  const [selectedItinerary, setSelectedItinerary] = useState<Itinerary | null>(null);
  const [itineraries, setItineraries] = useState<Itinerary[]>([]);
  const [mapOrigin, setMapOrigin] = useState<LocationPoint | null>(null);
  const [mapDestination, setMapDestination] = useState<LocationPoint | null>(null);
  const [selectedRouteShape, setSelectedRouteShape] = useState<ShapePointDto[][] | null>(null);
  const [selectedRouteColor, setSelectedRouteColor] = useState<string>('#00f0ff');
  const [selectedStop, setSelectedStop] = useState<LocationPoint | null>(null);
  const [pickingLocationFor, setPickingLocationFor] = useState<'origin' | 'destination' | null>(null);
  const [liveVehicles, setLiveVehicles] = useState<import('../api').RouteVehicleItem[]>([]);
  const [selectedLiveBusId, setSelectedLiveBusId] = useState<string | null>(null);
  const [userLocation, setUserLocation] = useState<LocationPoint | null>(null);
  const [travelMode, setTravelMode] = useState<TravelMode>('TRANSIT');
  const [directRoute, setDirectRoute] = useState<WalkRouteResponse | null>(null);
  const [selectedDirectRouteIdx, setSelectedDirectRouteIdx] = useState<number>(0);
  const [theme, setTheme] = useState<Theme>(() => {
    const saved = localStorage.getItem('app-theme');
    if (saved === 'light' || saved === 'dark') return saved;
    return window.matchMedia && window.matchMedia('(prefers-color-scheme: light)').matches ? 'light' : 'dark';
  });

  useEffect(() => {
    localStorage.setItem('app-theme', theme);
    document.body.setAttribute('data-theme', theme);
  }, [theme]);

  return (
    <MapContext.Provider value={{
      selectedItinerary, setSelectedItinerary,
      itineraries, setItineraries,
      mapOrigin, setMapOrigin,
      mapDestination, setMapDestination,
      selectedRouteShape, setSelectedRouteShape,
      selectedRouteColor, setSelectedRouteColor,
      selectedStop, setSelectedStop,
      pickingLocationFor, setPickingLocationFor,
      liveVehicles, setLiveVehicles,
      selectedLiveBusId, setSelectedLiveBusId,
      userLocation, setUserLocation,
      travelMode, setTravelMode,
      directRoute, setDirectRoute,
      selectedDirectRouteIdx, setSelectedDirectRouteIdx,
      theme, setTheme
    }}>
      {children}
    </MapContext.Provider>
  );
};

export const useMapState = () => {
  const context = useContext(MapContext);
  if (context === undefined) {
    throw new Error('useMapState must be used within a MapProvider');
  }
  return context;
};
