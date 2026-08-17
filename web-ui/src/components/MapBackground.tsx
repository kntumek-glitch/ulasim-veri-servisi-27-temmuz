import React, { useEffect, useRef } from 'react';
import Map, { Source, Layer, Marker, useMap, NavigationControl, GeolocateControl } from 'react-map-gl/maplibre';
import maplibregl from 'maplibre-gl';
import 'maplibre-gl/dist/maplibre-gl.css';
import { useQuery } from '@tanstack/react-query';
import { useMapState } from '../context/MapContext';
import { Leg, Itinerary } from '../api';
import lineSlice from '@turf/line-slice';
import { point } from '@turf/helpers';
import AnimatedMarker from './AnimatedMarker';

const fetchShape = async (tripId: string) => {
  const res = await fetch(`/api/v1/gtfs/shapes?tripId=${encodeURIComponent(tripId)}&format=geojson`);
  if (!res.ok) throw new Error('Failed to fetch shape');
  const data = await res.json();
  
  if (data.geoJson) {
    if (!data.geoJson.properties) {
      data.geoJson.properties = {};
    }
  }
  
  return data.geoJson;
};

const TransitShapeLayer: React.FC<{ leg: Leg; color: string; opacity?: number; layerId: string }> = ({ leg, color, opacity = 0.8, layerId }) => {
  const { data: geojson } = useQuery({
    queryKey: ['shape', leg.tripId],
    queryFn: () => fetchShape(leg.tripId!),
    enabled: !!leg.tripId,
    staleTime: Infinity,
  });

  const slicedGeojson = React.useMemo(() => {
    if (!geojson || !leg.fromStopLat || !leg.fromStopLon || !leg.toStopLat || !leg.toStopLon) return geojson;
    try {
      const startPt = point([leg.fromStopLon, leg.fromStopLat]);
      const stopPt = point([leg.toStopLon, leg.toStopLat]);
      return lineSlice(startPt, stopPt, geojson);
    } catch (e) {
      console.warn('Failed to slice route shape', e);
      return geojson;
    }
  }, [geojson, leg]);

  if (!slicedGeojson) return null;

  return (
    <Source id={`${layerId}-source`} type="geojson" data={slicedGeojson}>
      <Layer
        id={layerId}
        type="line"
        paint={{
          'line-color': color,
          'line-width': opacity >= 0.8 ? 5 : 3,
          'line-opacity': opacity,
        }}
        layout={{
          'line-cap': 'round',
          'line-join': 'round',
        }}
      />
    </Source>
  );
};

// Component to handle Map FlyTo logic
const MapController: React.FC = () => {
  const { current: map } = useMap();
  const { mapOrigin, mapDestination, selectedItinerary, userLocation, travelMode, directRoute, selectedLiveBusId, liveVehicles } = useMapState();

  useEffect(() => {
    if (!map) return;
    
    if (travelMode !== 'TRANSIT' && directRoute && mapOrigin && mapDestination) {
      map.fitBounds(
        [
          [Math.min(mapOrigin.longitude, mapDestination.longitude), Math.min(mapOrigin.latitude, mapDestination.latitude)],
          [Math.max(mapOrigin.longitude, mapDestination.longitude), Math.max(mapOrigin.latitude, mapDestination.latitude)]
        ],
        { padding: 100, duration: 1000 }
      );
    } else if (travelMode === 'TRANSIT' && mapOrigin && mapDestination && !selectedItinerary) {
      map.fitBounds(
        [
          [Math.min(mapOrigin.longitude, mapDestination.longitude), Math.min(mapOrigin.latitude, mapDestination.latitude)],
          [Math.max(mapOrigin.longitude, mapDestination.longitude), Math.max(mapOrigin.latitude, mapDestination.latitude)]
        ],
        { padding: 100, duration: 1000 }
      );
    }
  }, [map, mapOrigin, mapDestination, selectedItinerary, travelMode, directRoute]);

  useEffect(() => {
    if (!map || !userLocation) return;
    map.flyTo({
      center: [userLocation.longitude, userLocation.latitude],
      zoom: 14,
      duration: 1000
    });
  }, [map, userLocation]);

  useEffect(() => {
    if (!map || !selectedLiveBusId || !liveVehicles.length) return;
    const bus = liveVehicles.find(v => v.busId === selectedLiveBusId);
    if (bus && bus.longitude && bus.latitude) {
      map.flyTo({
        center: [bus.longitude, bus.latitude],
        zoom: 16,
        duration: 1200
      });
    }
  }, [map, selectedLiveBusId, liveVehicles]);

  return null;
};

export default function MapBackground({ children }: { children?: React.ReactNode }) {
  const mapRef = useRef<any>(null);
  const { 
    mapOrigin, 
    mapDestination, 
    selectedItinerary, 
    userLocation,
    pickingLocationFor,
    setPickingLocationFor,
    setMapOrigin,
    setMapDestination,
    itineraries,
    selectedRouteShape, 
    selectedRouteColor, 
    selectedStop,
    setSelectedItinerary,
    travelMode, 
    directRoute,
    liveVehicles,
    selectedLiveBusId,
    setSelectedLiveBusId,
    selectedDirectRouteIdx,
    theme
  } = useMapState();

  const handleMapClick = (e: any) => {
    if (pickingLocationFor === 'origin') {
      setMapOrigin({ latitude: e.lngLat.lat, longitude: e.lngLat.lng, name: `Lat: ${e.lngLat.lat.toFixed(4)}, Lon: ${e.lngLat.lng.toFixed(4)}` });
      setPickingLocationFor(null);
    } else if (pickingLocationFor === 'destination') {
      setMapDestination({ latitude: e.lngLat.lat, longitude: e.lngLat.lng, name: `Lat: ${e.lngLat.lat.toFixed(4)}, Lon: ${e.lngLat.lng.toFixed(4)}` });
      setPickingLocationFor(null);
    } else if (e.features && e.features.length > 0) {
      const feature = e.features[0];
      if (feature.layer.id.startsWith('transit-shape-')) {
        const parts = feature.layer.id.split('-');
        const idx = parseInt(parts[2], 10);
        if (!isNaN(idx) && itineraries[idx]) {
          setSelectedItinerary(itineraries[idx]);
        }
      }
    }
  };

  // Convert selectedRouteShape to GeoJSON LineString
  const routeShapeGeoJson = React.useMemo(() => {
    if (!selectedRouteShape || selectedRouteShape.length === 0) return null;
    return {
      type: 'Feature',
      properties: {},
      geometry: {
        type: 'LineString',
        coordinates: selectedRouteShape.map(p => [p.longitude, p.latitude])
      }
    };
  }, [selectedRouteShape]);

  // Determine interactive layer IDs for clicking routes
  const interactiveLayerIds = ['transit-shapes'];
  itineraries.slice(0, 3).forEach((itinerary, idx) => {
    itinerary.legs.forEach((leg, i) => {
      if ((leg.mode === 'TRANSIT' || leg.routeType !== undefined) && leg.tripId) {
        interactiveLayerIds.push(`transit-shape-${idx}-${i}`);
      }
    });
  });

  const LEG_COLORS = ['#00f0ff', '#ff3366', '#33cc33', '#ffcc00', '#cc33ff'];

  const renderItinerary = (itinerary: Itinerary, idx: number, isActive: boolean) => {
    let transitLegCount = 0;

    return itinerary.legs.map((leg, i) => {
      // In GTFS some legs might be TRANSIT but mode is missing or something, checking tripId is safest
      const isTransit = leg.mode === 'TRANSIT' || !!leg.tripId;
      
      let color = '';
      let opacity = isActive ? 0.9 : 0.65;
      
      if (isActive) {
        if (isTransit) {
           color = LEG_COLORS[transitLegCount % LEG_COLORS.length];
           transitLegCount++;
        } else {
           color = 'var(--color-text-muted)';
        }
      } else {
        color = '#add8e6'; // Faded pale blue for unselected alternative paths
      }
      
      const layerId = `transit-shape-${idx}-${i}`;
      
      return (
        <React.Fragment key={`${idx}-${i}`}>
          {isTransit && leg.tripId && (
            <TransitShapeLayer leg={leg} color={color} opacity={opacity} layerId={layerId} />
          )}

          {!isTransit && (leg.geometryGeoJson || (leg.fromStopLon && leg.fromStopLat && leg.toStopLon && leg.toStopLat)) && (
            <Source id={`${layerId}-source`} type="geojson" data={{
              type: 'Feature',
              properties: {},
              geometry: leg.geometryGeoJson || {
                type: 'LineString',
                coordinates: [
                  [leg.fromStopLon, leg.fromStopLat],
                  [leg.toStopLon, leg.toStopLat]
                ]
              }
            }}>
              <Layer
                id={layerId}
                type="line"
                paint={{
                  'line-color': color,
                  'line-width': isActive ? 5 : 4,
                  'line-dasharray': [0, 2],
                  'line-opacity': opacity
                }}
                layout={{
                  'line-cap': 'round',
                  'line-join': 'round',
                }}
              />
            </Source>
          )}

          {isTransit && leg.fromStopLon && leg.fromStopLat && isActive && (
            <Marker longitude={leg.fromStopLon} latitude={leg.fromStopLat} anchor="center">
              <div style={{ width: 10, height: 10, background: '#fff', border: `2px solid ${color}`, borderRadius: '50%' }} />
            </Marker>
          )}
        </React.Fragment>
      );
    });
  };

  // Sort so active itinerary renders on top
  const routesToRender = itineraries.slice(0, 3).map((itinerary, idx) => {
    return { itinerary, idx, isActive: selectedItinerary?.planId === itinerary.planId };
  }).sort((a, b) => (a.isActive === b.isActive ? 0 : a.isActive ? 1 : -1));

  return (
    <div style={{ width: '100%', height: '100%', position: 'relative' }}>
      <Map
        ref={mapRef}
        mapLib={maplibregl as any}
        initialViewState={{
          longitude: 27.1428,
          latitude: 38.4237,
          zoom: 12,
          pitch: 45,
          bearing: -17.6
        }}
        style={{ width: '100%', height: '100%' }}
        mapStyle={theme === 'light' ? "https://basemaps.cartocdn.com/gl/positron-gl-style/style.json" : "https://basemaps.cartocdn.com/gl/dark-matter-gl-style/style.json"}
        interactiveLayerIds={interactiveLayerIds}
        onClick={handleMapClick}
        cursor={pickingLocationFor ? 'crosshair' : 'grab'}
      >
        <NavigationControl position="bottom-right" />
        <MapController />

      {/* Live Buses */}
      {liveVehicles.map(vehicle => (
          vehicle.latitude && vehicle.longitude && (
            <AnimatedMarker
              key={vehicle.busId}
              longitude={vehicle.longitude}
              latitude={vehicle.latitude}
              anchor="bottom"
              onClick={(e: any) => {
                e.originalEvent.stopPropagation();
                setSelectedLiveBusId(vehicle.busId);
              }}
              style={{ cursor: 'pointer', zIndex: selectedLiveBusId === vehicle.busId ? 100 : 10 }}
            >
              <div style={{
                background: selectedLiveBusId === vehicle.busId ? 'var(--color-accent-primary)' : 'rgba(0, 0, 0, 0.8)',
                color: selectedLiveBusId === vehicle.busId ? '#000' : 'white',
                padding: '4px 8px',
                borderRadius: '12px',
                border: '2px solid white',
                fontSize: '14px',
                boxShadow: '0 2px 4px rgba(0,0,0,0.3)',
                whiteSpace: 'nowrap',
                fontWeight: 'bold',
                transform: selectedLiveBusId === vehicle.busId ? 'scale(1.15)' : 'scale(1)',
                transition: 'transform 0.2s, background 0.2s'
              }}>
                🚌 {vehicle.busId}
              </div>
            </AnimatedMarker>
          )
        ))}

      {/* Origin Marker */}
      {mapOrigin && (
        <Marker longitude={mapOrigin.longitude} latitude={mapOrigin.latitude} anchor="bottom">
          <div style={{ width: 16, height: 16, background: 'var(--color-text-main)', border: '3px solid #000', borderRadius: '50%', boxShadow: '0 0 10px rgba(255,255,255,0.5)' }} />
        </Marker>
      )}

      {/* Destination Marker */}
      {mapDestination && (
        <Marker longitude={mapDestination.longitude} latitude={mapDestination.latitude} anchor="bottom">
          <div style={{ width: 16, height: 16, background: 'var(--color-accent-secondary)', border: '3px solid #000', borderRadius: '50%', boxShadow: '0 0 10px rgba(176,38,255,0.5)' }} />
        </Marker>
      )}

      {/* Itinerary Overlays (Transit Mode) */}
      {travelMode === 'TRANSIT' && routesToRender.map(({ itinerary, idx, isActive }) => renderItinerary(itinerary, idx, isActive))}

      {/* Direct Route Overlay (Walk/Drive Mode) */}
      {travelMode !== 'TRANSIT' && directRoute && (
        <>
          {[directRoute, ...(directRoute.alternatives || [])].map((route, idx) => {
            if (!route.geometry || idx === selectedDirectRouteIdx) return null;
            const isWalk = travelMode === 'WALK';
            return (
              <Source key={`direct-route-inactive-${idx}`} type="geojson" data={route.geometry as any}>
                <Layer
                  type="line"
                  paint={{
                    'line-color': isWalk ? 'rgba(0,240,255,0.7)' : 'rgba(176,38,255,0.6)',
                    'line-width': 4,
                    'line-dasharray': isWalk ? [0, 2] : [1],
                    'line-opacity': 0.8
                  }}
                  layout={{ 'line-cap': 'round', 'line-join': 'round' }}
                />
              </Source>
            );
          })}
          {[directRoute, ...(directRoute.alternatives || [])].map((route, idx) => {
            if (!route.geometry || idx !== selectedDirectRouteIdx) return null;
            const isWalk = travelMode === 'WALK';
            return (
              <Source key={`direct-route-active-${idx}`} type="geojson" data={route.geometry as any}>
                <Layer
                  type="line"
                  paint={{
                    'line-color': isWalk ? '#00f0ff' : '#b026ff',
                    'line-width': isWalk ? 5 : 6,
                    'line-dasharray': isWalk ? [0, 2] : [1],
                    'line-opacity': 1.0
                  }}
                  layout={{ 'line-cap': 'round', 'line-join': 'round' }}
                />
              </Source>
            );
          })}
        </>
      )}

      {/* Selected Route Shape (From Lines Page) */}
      {routeShapeGeoJson && (
        <Source type="geojson" data={routeShapeGeoJson as any}>
          <Layer
            type="line"
            paint={{
              'line-color': selectedRouteColor,
              'line-width': 5,
              'line-opacity': 0.9,
            }}
            layout={{
              'line-cap': 'round',
              'line-join': 'round',
            }}
          />
        </Source>
      )}

      {/* Selected Stop (From Stops Page) */}
      {selectedStop && (
        <Marker longitude={selectedStop.longitude} latitude={selectedStop.latitude} anchor="bottom">
          <div style={{
            width: 20, 
            height: 20, 
            background: 'var(--color-accent-secondary)', 
            border: '4px solid #fff', 
            borderRadius: '50%', 
            boxShadow: '0 0 15px var(--color-accent-secondary)',
            animation: 'pulse 2s infinite'
          }} />
        </Marker>
      )}
      {/* User Location Marker */}
      {userLocation && (
        <Marker longitude={userLocation.longitude} latitude={userLocation.latitude} anchor="center">
          <div style={{
            width: 16, 
            height: 16, 
            background: '#00f0ff', 
            border: '3px solid #fff', 
            borderRadius: '50%', 
            boxShadow: '0 0 20px #00f0ff',
            animation: 'pulse 2s infinite'
          }} />
        </Marker>
      )}
    </Map>
  </div>
  );
};
