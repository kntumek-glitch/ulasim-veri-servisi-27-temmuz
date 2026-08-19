import React, { useEffect, useRef, useState } from 'react';
import { Source, Layer, useMap } from 'react-map-gl/maplibre';
import { RouteVehicleItem } from '../api';

interface LiveBusesLayerProps {
  vehicles: RouteVehicleItem[];
}

// Stores the state of each bus for interpolation
interface BusState {
  lng: number;
  lat: number;
  bearing: number;
  targetLng: number;
  targetLat: number;
  lastUpdate: number;
}

const MODEL_URL = 'https://maplibre.org/maplibre-gl-js/docs/assets/34M_17/34M_17.gltf';
const MODEL_ID = 'bus-model-3d';

const LiveBusesLayer: React.FC<LiveBusesLayerProps> = ({ vehicles }) => {
  const { current: map } = useMap();
  const [modelLoaded, setModelLoaded] = useState(false);
  const busStatesRef = useRef<Record<string, BusState>>({});
  const reqRef = useRef<number>();

  // 1. Load the 3D Model into the map
  useEffect(() => {
    if (!map) return;
    const mapInstance = map.getMap() as any; // Cast to any to access addModel
    
    // Check if map already has the model
    if (!mapInstance.hasModel || !mapInstance.addModel) {
      console.warn("MapLibre version does not support 3D models natively.");
      return;
    }

    if (!mapInstance.hasModel(MODEL_ID)) {
      mapInstance.addModel(MODEL_ID, MODEL_URL).then(() => {
        setModelLoaded(true);
      }).catch((err: any) => {
        console.error("Failed to load 3D model:", err);
      });
    } else {
      setModelLoaded(true);
    }
  }, [map]);

  // 2. Interpolate bus positions and update GeoJSON at 60fps
  useEffect(() => {
    if (!map || !modelLoaded) return;
    const mapInstance = map.getMap();

    // Update targets
    vehicles.forEach(v => {
      if (!v.longitude || !v.latitude) return;
      const id = v.busId;
      const state = busStatesRef.current[id];
      if (!state) {
        busStatesRef.current[id] = {
          lng: v.longitude,
          lat: v.latitude,
          bearing: 0,
          targetLng: v.longitude,
          targetLat: v.latitude,
          lastUpdate: performance.now()
        };
      } else {
        // Only update target if it moved significantly
        const dist = Math.sqrt(Math.pow(v.longitude - state.targetLng, 2) + Math.pow(v.latitude - state.targetLat, 2));
        if (dist > 0.00001) {
          state.targetLng = v.longitude;
          state.targetLat = v.latitude;
          state.lastUpdate = performance.now();
          
          // Calculate new bearing
          let newBearing = Math.atan2(v.longitude - state.lng, v.latitude - state.lat) * (180 / Math.PI);
          if (newBearing < 0) newBearing += 360;
          state.bearing = newBearing;
        }
      }
    });

    const animate = () => {
      const now = performance.now();
      const features: GeoJSON.Feature<GeoJSON.Point>[] = [];

      Object.entries(busStatesRef.current).forEach(([id, state]) => {
        // Interpolate over 2 seconds (2000ms)
        const progress = Math.min((now - state.lastUpdate) / 2000, 1);
        const ease = 1 - Math.pow(1 - progress, 4);

        state.lng = state.lng + (state.targetLng - state.lng) * ease;
        state.lat = state.lat + (state.targetLat - state.lat) * ease;

        // MapLibre native models require properties to be passed
        features.push({
          type: 'Feature',
          geometry: { type: 'Point', coordinates: [state.lng, state.lat] },
          properties: {
            busId: id,
            bearing: state.bearing,
          }
        });
      });

      const source = mapInstance.getSource('live-buses-3d-source');
      if (source && source.type === 'geojson') {
        (source as any).setData({ type: 'FeatureCollection', features });
      }

      reqRef.current = requestAnimationFrame(animate);
    };

    reqRef.current = requestAnimationFrame(animate);

    return () => {
      if (reqRef.current) cancelAnimationFrame(reqRef.current);
    };
  }, [vehicles, map, modelLoaded]);

  if (!modelLoaded) return null;

  return (
    <Source 
      id="live-buses-3d-source" 
      type="geojson" 
      data={{ type: 'FeatureCollection', features: [] }}
    >
      <Layer 
        id="live-buses-3d-layer"
        type={"model" as any}
        source="live-buses-3d-source"
        layout={{
          'model-id': MODEL_ID,
        } as any}
        paint={{
          'model-rotation': [0, 0, ['get', 'bearing']],
          'model-scale': [1000, 1000, 1000], // the satellite is tiny, needs a large scale
          'model-translation': [0, 0, 0]
        } as any}
      />
    </Source>
  );
};

export default LiveBusesLayer;
