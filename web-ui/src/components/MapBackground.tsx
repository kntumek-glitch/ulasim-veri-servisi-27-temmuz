import React, { useEffect, useRef, useState } from 'react';
import Map, { Source, Layer, Marker, useMap, NavigationControl, GeolocateControl } from 'react-map-gl/maplibre';
import maplibregl from 'maplibre-gl';
import 'maplibre-gl/dist/maplibre-gl.css';
import { useQuery } from '@tanstack/react-query';
import { useMapState } from '../context/MapContext';
import { Leg, Itinerary } from '../api';
import lineSlice from '@turf/line-slice';
import { point } from '@turf/helpers';
import LiveBusesThreeLayer from './LiveBusesThreeLayer';

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
  const { data: geojson, isError } = useQuery({
    queryKey: ['shape', leg.tripId],
    queryFn: () => fetchShape(leg.tripId!),
    enabled: !!leg.tripId,
    staleTime: Infinity,
    retry: 1, // only retry once to fail faster
  });

  const finalGeojson = React.useMemo(() => {
    // If shape fetch failed or returned nothing, create a fallback straight line
    if (isError || (!geojson && !leg.tripId)) {
      if (!leg.fromStopLat || !leg.fromStopLon || !leg.toStopLat || !leg.toStopLon) return null;
      
      const coordinates = [[leg.fromStopLon, leg.fromStopLat]];
      if (leg.intermediateStops && leg.intermediateStops.length > 0) {
        leg.intermediateStops.forEach((stop: any) => {
          if (stop.longitude && stop.latitude) {
            coordinates.push([stop.longitude, stop.latitude]);
          }
        });
      }
      coordinates.push([leg.toStopLon, leg.toStopLat]);

      return {
        type: 'Feature',
        properties: {},
        geometry: {
          type: 'LineString',
          coordinates
        }
      };
    }

    if (!geojson || !leg.fromStopLat || !leg.fromStopLon || !leg.toStopLat || !leg.toStopLon) return geojson;
    
    try {
      const startPt = point([leg.fromStopLon, leg.fromStopLat]);
      const stopPt = point([leg.toStopLon, leg.toStopLat]);
      return lineSlice(startPt, stopPt, geojson);
    } catch (e) {
      console.warn('Failed to slice route shape', e);
      return geojson;
    }
  }, [geojson, leg, isError]);

  if (!finalGeojson) return null;

  return (
    <Source id={`${layerId}-source`} type="geojson" data={finalGeojson}>
      <Layer
        id={layerId}
        type="line"
        beforeId="3d-buildings"
        paint={{
          'line-color': color,
          'line-width': opacity >= 0.8 ? 6 : 5,
          'line-opacity': opacity,
        }}
        layout={{
          'line-cap': 'round',
          'line-join': 'round',
        }}
      />
      <Layer
        id={`${layerId}-arrows`}
        type="symbol"
        layout={{
          'symbol-placement': 'line',
          'symbol-spacing': 40,
          'text-field': '>',
          'text-font': ['Open Sans Bold', 'Arial Unicode MS Bold'],
          'text-size': 20,
          'text-keep-upright': false,
          'text-pitch-alignment': 'map',
          'text-rotation-alignment': 'map'
        }}
        paint={{
          'text-color': '#ffffff',
          'text-halo-color': color,
          'text-halo-width': 2,
          'text-opacity': opacity
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

  // Enforce layer order manually to guarantee lines are under 3D models
  useEffect(() => {
    if (!map) return;
    
    const enforceOrder = () => {
      try {
        const layersToMove = [
          'selected-route-line',
          'direct-route-active-0',
          'direct-route-active-1',
          'direct-route-active-2',
          'direct-route-inactive-0',
          'direct-route-inactive-1',
          'direct-route-inactive-2'
        ];
        
        layersToMove.forEach(layerId => {
          if (map.getLayer(layerId)) {
            // Move it before 3d-buildings if it exists
            if (map.getLayer('3d-buildings')) {
              map.moveLayer(layerId, '3d-buildings');
            } else if (map.getLayer('3d-model-buses')) {
              // Or at least before the buses
              map.moveLayer(layerId, '3d-model-buses');
            }
          }
        });

        // Always keep 3D buses at the very top of all 3D layers
        if (map.getLayer('3d-model-buses')) {
           map.moveLayer('3d-model-buses'); // Moves to end
        }
      } catch (e) {
        // ignore layer not found errors
      }
    };

    enforceOrder();
    
    // Also enforce on styledata (when basemap changes) or source data changes
    map.on('styledata', enforceOrder);
    map.on('sourcedata', enforceOrder);
    
    return () => {
      map.off('styledata', enforceOrder);
      map.off('sourcedata', enforceOrder);
    };
  }, [map]);

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

  const [is2D, setIs2D] = useState(false);

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

  const activeDirection = React.useMemo(() => {
    if (!selectedLiveBusId || !liveVehicles) return null;
    const bus = liveVehicles.find(v => v.busId === selectedLiveBusId);
    return bus ? parseInt(bus.direction) : null;
  }, [selectedLiveBusId, liveVehicles]);

  // Convert selectedRouteShape to GeoJSON FeatureCollection
  const routeShapeGeoJson = React.useMemo(() => {
    if (!selectedRouteShape || selectedRouteShape.length === 0) return null;
    return {
      type: 'FeatureCollection',
      features: selectedRouteShape.map((shape, idx) => {
        if (!shape || shape.length === 0) return null;
        return {
          type: 'Feature',
          properties: { direction: idx },
          geometry: {
            type: 'LineString',
            coordinates: shape.map(p => [p.longitude, p.latitude])
          }
        };
      }).filter(Boolean)
    };
  }, [selectedRouteShape]);

  const activePaintProps = React.useMemo(() => {
    if (activeDirection === null) {
      return {
        'line-color': selectedRouteColor,
        'line-width': 5,
        'line-opacity': 0.9,
      };
    }
    return {
      'line-color': selectedRouteColor,
      'line-width': ['case', ['==', ['get', 'direction'], activeDirection], 7, 3],
      'line-opacity': ['case', ['==', ['get', 'direction'], activeDirection], 1.0, 0.3]
    };
  }, [selectedRouteColor, activeDirection]);

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
      let opacity = isActive ? 1.0 : 0.4;
      
      if (isActive) {
        if (isTransit) {
           color = LEG_COLORS[transitLegCount % LEG_COLORS.length];
           transitLegCount++;
        } else {
           color = '#ffffff'; // Bright white for walking to make it highly visible
        }
      } else {
        color = '#888888'; // Grey for unselected alternative paths
      }
      
      const layerId = `transit-shape-${idx}-${i}`;
      
      return (
        <React.Fragment key={`${idx}-${i}`}>
          {isTransit && leg.tripId && (
            <TransitShapeLayer leg={leg} color={color} opacity={opacity} layerId={layerId} />
          )}

          {isActive && isTransit && leg.intermediateStops && leg.intermediateStops.map((stop, stopIdx) => (
            <Marker key={`inter-${stop.stopId}-${idx}-${i}-${stopIdx}`} longitude={stop.lon} latitude={stop.lat} anchor="center">
              <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', pointerEvents: 'none' }}>
                <div 
                  style={{ 
                    width: 10, 
                    height: 10, 
                    backgroundColor: '#fff', 
                    border: `3px solid ${color}`, 
                    borderRadius: '50%', 
                    boxShadow: '0 0 5px rgba(0,0,0,0.5)',
                    pointerEvents: 'auto',
                    cursor: 'help' 
                  }} 
                  title={`${stop.stopName} (${new Date(stop.arrivalTime).toLocaleTimeString([], {hour: '2-digit', minute:'2-digit'})})`} 
                />
                <div style={{
                  marginTop: 2,
                  padding: '1px 4px',
                  background: 'rgba(0,0,0,0.6)',
                  color: 'white',
                  borderRadius: 4,
                  fontSize: 9,
                  fontWeight: 'bold',
                  whiteSpace: 'nowrap',
                  textShadow: '0 1px 2px rgba(0,0,0,0.8)'
                }}>
                  {stop.stopName}
                </div>
              </div>
            </Marker>
          ))}

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
                  'line-width': isActive ? 6 : 4,
                  'line-dasharray': [0, 2],
                  'line-opacity': opacity
                }}
                layout={{
                  'line-cap': 'round',
                  'line-join': 'round',
                }}
              />
              <Layer
                id={`${layerId}-arrows`}
                type="symbol"
                layout={{
                  'symbol-placement': 'line',
                  'symbol-spacing': 40,
                  'text-field': '>',
                  'text-font': ['Open Sans Bold', 'Arial Unicode MS Bold'],
                  'text-size': 18,
                  'text-keep-upright': false,
                  'text-pitch-alignment': 'map',
                  'text-rotation-alignment': 'map'
                }}
                paint={{
                  'text-color': '#000000',
                  'text-halo-color': '#ffffff',
                  'text-halo-width': 2,
                  'text-opacity': 1.0
                }}
              />
            </Source>
          )}

          {isTransit && leg.fromStopLon && leg.fromStopLat && isActive && (
            <Marker longitude={leg.fromStopLon} latitude={leg.fromStopLat} anchor="center" style={{ zIndex: 10 }}>
              <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', pointerEvents: 'none' }}>
                <div style={{ width: 14, height: 14, background: '#fff', border: `4px solid ${color}`, borderRadius: '50%', boxShadow: '0 0 5px rgba(0,0,0,0.5)' }} />
                <div style={{
                  marginTop: 2,
                  padding: '2px 5px',
                  background: color,
                  color: 'white',
                  borderRadius: 4,
                  fontSize: 10,
                  fontWeight: 'bold',
                  whiteSpace: 'nowrap',
                  textShadow: '0 1px 2px rgba(0,0,0,0.8)',
                  boxShadow: '0 2px 4px rgba(0,0,0,0.3)'
                }}>
                  (Biniş) {leg.fromStopName}
                </div>
              </div>
            </Marker>
          )}

          {isTransit && leg.toStopLon && leg.toStopLat && isActive && (
            <Marker longitude={leg.toStopLon} latitude={leg.toStopLat} anchor="center" style={{ zIndex: 10 }}>
              <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', pointerEvents: 'none' }}>
                <div style={{ width: 14, height: 14, background: '#fff', border: `4px solid ${color}`, borderRadius: '50%', boxShadow: '0 0 5px rgba(0,0,0,0.5)' }} />
                <div style={{
                  marginTop: 2,
                  padding: '2px 5px',
                  background: color,
                  color: 'white',
                  borderRadius: 4,
                  fontSize: 10,
                  fontWeight: 'bold',
                  whiteSpace: 'nowrap',
                  textShadow: '0 1px 2px rgba(0,0,0,0.8)',
                  boxShadow: '0 2px 4px rgba(0,0,0,0.3)'
                }}>
                  (İniş) {leg.toStopName}
                </div>
              </div>
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

      {/* Live Buses (Three.js 3D Models) */}
      <LiveBusesThreeLayer vehicles={liveVehicles} />

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
                  beforeId="3d-buildings"
                  paint={{
                    'line-color': isWalk ? 'rgba(0,240,255,0.7)' : 'rgba(176,38,255,0.6)',
                    'line-width': 4,
                    'line-dasharray': isWalk ? [0, 2] : [1],
                    'line-opacity': 0.8
                  }}
                  layout={{ 'line-cap': 'round', 'line-join': 'round' }}
                />
                  <Layer
                    id={`direct-route-inactive-arrows-${idx}`}
                    type="symbol"
                    layout={{
                      'symbol-placement': 'line',
                      'symbol-spacing': 60,
                      'text-field': '>',
                      'text-font': ['Open Sans Bold', 'Arial Unicode MS Bold'],
                      'text-size': 18,
                      'text-keep-upright': false,
                      'text-pitch-alignment': 'map',
                      'text-rotation-alignment': 'map'
                    }}
                    paint={{
                      'text-color': '#ffffff',
                      'text-halo-color': '#000000',
                      'text-halo-width': 2,
                      'text-opacity': 0.8
                    }}
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
                  beforeId="3d-buildings"
                  paint={{
                    'line-color': isWalk ? '#00f0ff' : '#b026ff',
                    'line-width': isWalk ? 5 : 6,
                    'line-dasharray': isWalk ? [0, 2] : [1],
                    'line-opacity': 1.0
                  }}
                  layout={{ 'line-cap': 'round', 'line-join': 'round' }}
                />
                <Layer
                  id={`direct-route-active-arrows-${idx}`}
                  type="symbol"
                  layout={{
                    'symbol-placement': 'line',
                    'symbol-spacing': 40,
                    'text-field': '>',
                    'text-font': ['Open Sans Bold', 'Arial Unicode MS Bold'],
                    'text-size': 20,
                    'text-keep-upright': false,
                    'text-pitch-alignment': 'map',
                    'text-rotation-alignment': 'map'
                  }}
                  paint={{
                    'text-color': '#000000',
                    'text-halo-color': '#ffffff',
                    'text-halo-width': 2,
                    'text-opacity': 1.0
                  }}
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
            id="selected-route-line"
            type="line"
            beforeId="3d-buildings"
            paint={activePaintProps as any}
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

      {/* 3D Buildings Layer (Only visible when zoomed in) */}
      <Layer
        id="3d-buildings"
        source="carto"
        source-layer="building"
        type="fill-extrusion"
        minzoom={14}
        layout={{
          visibility: is2D ? 'none' : 'visible'
        }}
        paint={{
          'fill-extrusion-color': theme === 'light' ? '#e5e5e5' : '#2a2a2a',
          'fill-extrusion-height': [
            'interpolate', ['linear'], ['zoom'],
            14, 0,
            14.05, ['coalesce', ['get', 'render_height'], ['*', 3, ['get', 'levels']], 15]
          ],
          'fill-extrusion-base': [
            'interpolate', ['linear'], ['zoom'],
            14, 0,
            14.05, ['coalesce', ['get', 'render_min_height'], 0]
          ],
          'fill-extrusion-opacity': theme === 'light' ? 0.6 : 0.8
        }}
      />

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

      <div style={{ position: 'absolute', bottom: 180, right: 10, zIndex: 10 }}>
        <button
          onClick={(e) => {
            e.stopPropagation();
            if (mapRef.current) {
              const p = mapRef.current.getPitch();
              if (p > 10) {
                mapRef.current.flyTo({ pitch: 0, bearing: 0, duration: 1000 });
                setIs2D(true);
              } else {
                mapRef.current.flyTo({ pitch: 45, bearing: -17.6, duration: 1000 });
                setIs2D(false);
              }
            }
          }}
          style={{
            background: 'var(--color-surface)',
            color: 'var(--color-text-main)',
            border: '1px solid rgba(255,255,255,0.1)',
            borderRadius: '4px',
            width: '29px',
            height: '29px',
            cursor: 'pointer',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            fontWeight: 'bold',
            fontSize: '11px',
            boxShadow: '0 0 0 2px rgba(0,0,0,0.1)'
          }}
          title="2D / 3D Görünüm"
        >
          {is2D ? '3D' : '2D'}
        </button>
      </div>
    </div>
  );
};

