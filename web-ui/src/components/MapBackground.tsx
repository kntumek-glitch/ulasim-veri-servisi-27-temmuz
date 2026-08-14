import React, { useEffect, useRef } from 'react';
import Map, { Source, Layer, Marker, useMap, NavigationControl, GeolocateControl } from 'react-map-gl/maplibre';
import maplibregl from 'maplibre-gl';
import 'maplibre-gl/dist/maplibre-gl.css';
import { useQuery } from '@tanstack/react-query';
import { useMapState } from '../context/MapContext';
import { Leg, Itinerary } from '../api';
import lineSlice from '@turf/line-slice';
import { point } from '@turf/helpers';

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
  const { mapOrigin, mapDestination, selectedItinerary, userLocation } = useMapState();

  useEffect(() => {
    if (!map) return;
    
    // Fit bounds if both origin and destination exist and no itinerary selected yet
    if (mapOrigin && mapDestination && !selectedItinerary) {
      map.fitBounds(
        [
          [mapOrigin.longitude, mapOrigin.latitude],
          [mapDestination.longitude, mapDestination.latitude]
        ],
        { padding: 100, duration: 1000 }
      );
    }
  }, [map, mapOrigin, mapDestination, selectedItinerary]);

  useEffect(() => {
    if (!map || !userLocation) return;
    map.flyTo({
      center: [userLocation.longitude, userLocation.latitude],
      zoom: 14,
      duration: 1000
    });
  }, [map, userLocation]);

  return null;
};

const MapBackground: React.FC = () => {
  const mapRef = useRef<any>(null);
  const { current: map } = useMap();
  const { 
    mapOrigin, setMapOrigin,
    mapDestination, setMapDestination,
    selectedItinerary, setSelectedItinerary,
    itineraries,
    selectedRouteShape, 
    selectedRouteColor, 
    selectedStop,
    pickingLocationFor, setPickingLocationFor,
    userLocation
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

  const renderItinerary = (itinerary: Itinerary, idx: number, isActive: boolean) => {
    return itinerary.legs.map((leg, i) => {
      // In GTFS some legs might be TRANSIT but mode is missing or something, checking tripId is safest
      const isTransit = leg.mode === 'TRANSIT' || !!leg.tripId;
      const color = isTransit ? (isActive ? '#00f0ff' : '#888888') : (isActive ? 'var(--color-text-muted)' : '#555555');
      const opacity = isActive ? 0.9 : 0.4;
      const layerId = `transit-shape-${idx}-${i}`;
      
      return (
        <React.Fragment key={`${idx}-${i}`}>
          {isTransit && leg.tripId && (
            <TransitShapeLayer leg={leg} color={color} opacity={opacity} layerId={layerId} />
          )}

          {!isTransit && (leg.geometryGeoJson || (leg.fromStopLon && leg.fromStopLat && leg.toStopLon && leg.toStopLat)) && (
            <Source id={`${layerId}-source`} type="geojson" data={leg.geometryGeoJson || {
              type: 'Feature',
              properties: {},
              geometry: {
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
                  'line-width': isActive ? 4 : 3,
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
              <div style={{ width: 10, height: 10, background: '#fff', border: '2px solid #00f0ff', borderRadius: '50%' }} />
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

  const { liveVehicles } = useMapState();

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
        mapStyle="https://basemaps.cartocdn.com/gl/dark-matter-gl-style/style.json"
        interactiveLayerIds={interactiveLayerIds}
        onClick={handleMapClick}
        cursor={pickingLocationFor ? 'crosshair' : 'grab'}
      >
        <NavigationControl position="bottom-right" />
        <MapController />

      {/* Live Buses */}
      {liveVehicles && liveVehicles.map((vehicle, idx) => {
        if (!vehicle.longitude || !vehicle.latitude) return null;
        return (
          <Marker key={`bus-${vehicle.busId}`} longitude={vehicle.longitude} latitude={vehicle.latitude} anchor="bottom">
            <div style={{
              width: 32,
              height: 32,
              background: 'var(--color-accent-primary)',
              borderRadius: '8px 8px 4px 4px',
              border: '2px solid #fff',
              boxShadow: '0 4px 8px rgba(0,0,0,0.5), 0 0 15px rgba(0,240,255,0.6)',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              color: '#000',
              fontWeight: 'bold',
              position: 'relative'
            }}>
              <span style={{ fontSize: 18 }}>🚌</span>
              {vehicle.direction && (
                <div style={{
                  position: 'absolute',
                  top: -24,
                  background: 'rgba(0,0,0,0.8)',
                  color: '#fff',
                  padding: '2px 6px',
                  borderRadius: 4,
                  fontSize: 10,
                  whiteSpace: 'nowrap'
                }}>
                  {vehicle.direction}
                </div>
              )}
            </div>
          </Marker>
        );
      })}

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

      {/* Itinerary Overlays */}
      {routesToRender.map(({ itinerary, idx, isActive }) => renderItinerary(itinerary, idx, isActive))}

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

export default MapBackground;
