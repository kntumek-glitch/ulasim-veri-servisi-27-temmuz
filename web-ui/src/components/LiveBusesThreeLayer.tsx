import React, { useEffect, useRef, useState } from 'react';
import { useMap } from 'react-map-gl/maplibre';
import * as THREE from 'three';
import { GLTFLoader } from 'three/examples/jsm/loaders/GLTFLoader.js';
import { DRACOLoader } from 'three/examples/jsm/loaders/DRACOLoader.js';
import { MercatorCoordinate } from 'maplibre-gl';
import { RouteVehicleItem } from '../api';

interface LiveBusesThreeLayerProps {
  vehicles: RouteVehicleItem[];
}

interface BusState {
  lng: number;
  lat: number;
  bearing: number;
  targetLng: number;
  targetLat: number;
  lastUpdate: number;
  mesh?: THREE.Object3D;
}

const MODEL_URL = '/ferrari.glb';
const LAYER_ID = '3d-model-buses';

const LiveBusesThreeLayer: React.FC<LiveBusesThreeLayerProps> = ({ vehicles }) => {
  const { current: mapRef } = useMap();
  const busStatesRef = useRef<Record<string, BusState>>({});
  const layerAdded = useRef(false);
  const sceneRef = useRef<THREE.Scene | null>(null);
  const modelTemplateRef = useRef<THREE.Object3D | null>(null);
  
  const [debugInfo, setDebugInfo] = useState<string[]>(['Başlatılıyor...']);
  const debugRef = useRef(setDebugInfo);
  debugRef.current = setDebugInfo;
  
  const addDebug = (msg: string) => {
    debugRef.current(prev => [...prev.slice(-8), `${new Date().toLocaleTimeString()}: ${msg}`]);
  };

  useEffect(() => {
    if (!mapRef) {
      addDebug('❌ mapRef yok');
      return;
    }
    
    addDebug(`✅ mapRef var, vehicles: ${vehicles.length}`);
    const map = mapRef.getMap();

    if (layerAdded.current || map.getLayer(LAYER_ID)) {
      addDebug('⚠️ Katman zaten ekli');
      return;
    }

    let camera: THREE.Camera;
    let scene: THREE.Scene;
    let renderer: THREE.WebGLRenderer;

    const customLayer: any = {
      id: LAYER_ID,
      type: 'custom',
      renderingMode: '3d',

      onAdd(_map: any, gl: WebGLRenderingContext) {
        try {
          camera = new THREE.Camera();
          scene = new THREE.Scene();
          sceneRef.current = scene;

          const ambientLight = new THREE.AmbientLight(0xffffff, 2);
          scene.add(ambientLight);

          const dirLight1 = new THREE.DirectionalLight(0xffffff, 3);
          dirLight1.position.set(1, -1, 1).normalize();
          scene.add(dirLight1);

          const dirLight2 = new THREE.DirectionalLight(0xffffff, 1.5);
          dirLight2.position.set(-1, 1, 1).normalize();
          scene.add(dirLight2);

          renderer = new THREE.WebGLRenderer({
            canvas: _map.getCanvas(),
            context: gl,
            antialias: true,
          });
          renderer.autoClear = false;

          addDebug('✅ Scene + Renderer oluşturuldu');

          const loader = new GLTFLoader();
          const dracoLoader = new DRACOLoader();
          dracoLoader.setDecoderPath('https://www.gstatic.com/draco/versioned/decoders/1.5.6/');
          loader.setDRACOLoader(dracoLoader);

          addDebug('⏳ Model yükleniyor...');

          loader.load(MODEL_URL, (gltf) => {
            const model = gltf.scene;
            const box = new THREE.Box3().setFromObject(model);
            const center = box.getCenter(new THREE.Vector3());
            model.position.sub(center);

            model.traverse((child: any) => {
              if (child.isMesh && child.material) {
                child.material.envMapIntensity = 0;
                child.material.needsUpdate = true;
              }
            });

            const group = new THREE.Group();
            group.add(model);
            modelTemplateRef.current = group;
            addDebug(`✅ Model yüklendi!`);
          }, (progress) => {
            if (progress.total) {
              addDebug(`⏳ Model: ${Math.round(progress.loaded / progress.total * 100)}%`);
            }
          }, (error) => {
            addDebug(`❌ Model hatası: ${error}`);
          });

        } catch (err) {
          addDebug(`❌ onAdd hatası: ${err}`);
        }
      },

      render(_gl: WebGLRenderingContext, matrix: number[]) {
        try {
          if (!renderer || !scene || !camera) return;

          const mapCenter = map.getCenter();
          const centerMerc = MercatorCoordinate.fromLngLat([mapCenter.lng, mapCenter.lat], 0);

          const m = new THREE.Matrix4().fromArray(matrix);
          const offset = new THREE.Matrix4().makeTranslation(centerMerc.x, centerMerc.y, 0);
          camera.projectionMatrix = m.multiply(offset);

          const now = performance.now();
          Object.values(busStatesRef.current).forEach(state => {
            if (!modelTemplateRef.current) return;

            if (!state.mesh) {
              state.mesh = modelTemplateRef.current.clone();
              scene.add(state.mesh);
            }

            const t = Math.min((now - state.lastUpdate) / 2000, 1);
            const ease = 1 - Math.pow(1 - t, 4);

            const lng = state.lng + (state.targetLng - state.lng) * ease;
            const lat = state.lat + (state.targetLat - state.lat) * ease;

            const busMerc = MercatorCoordinate.fromLngLat([lng, lat], 0);

            const dx = busMerc.x - centerMerc.x;
            const dy = busMerc.y - centerMerc.y;

            const scale = busMerc.meterInMercatorCoordinateUnits() * 10;
            const zOffset = busMerc.meterInMercatorCoordinateUnits() * 2; // Lift 2 meters up

            // Apply rotation
            // We use state.angle which is calculated in Mercator space (where +X is East, +Y is South)
            // If the 3D model natively points a different way, we adjust this offset:
            const MODEL_NATIVE_OFFSET = -Math.PI / 2; // Fixed exact 90-degree offset for the Ferrari model
            
            const transform = new THREE.Matrix4()
              .makeTranslation(dx, dy, zOffset)
              .scale(new THREE.Vector3(scale, scale, scale))
              .multiply(new THREE.Matrix4().makeRotationZ(state.bearing + MODEL_NATIVE_OFFSET))
              .multiply(new THREE.Matrix4().makeRotationX(Math.PI / 2));

            state.mesh.matrixAutoUpdate = false;
            state.mesh.matrix = transform;
          });

          renderer.resetState();
          renderer.render(scene, camera);
          map.triggerRepaint();
        } catch (err) {
          // silent
        }
      }
    };

    const addLayer = () => {
      if (layerAdded.current || map.getLayer(LAYER_ID)) return;
      try {
        map.addLayer(customLayer);
        layerAdded.current = true;
        addDebug('✅ Katman haritaya eklendi');
      } catch (err) {
        addDebug(`❌ addLayer hatası: ${err}`);
      }
    };

    if (map.isStyleLoaded()) {
      addDebug('Stil yüklü, katman ekleniyor...');
      addLayer();
    } else {
      addDebug('⏳ Stil bekleniyor...');
      map.once('styledata', () => {
        addDebug('Stil yüklendi, katman ekleniyor...');
        addLayer();
      });
    }

    return () => {
      try {
        if (layerAdded.current && map.getLayer(LAYER_ID)) {
          map.removeLayer(LAYER_ID);
        }
      } catch (e) { /* ignore */ }
      layerAdded.current = false;
      if (sceneRef.current) {
        Object.values(busStatesRef.current).forEach(s => {
          if (s.mesh) sceneRef.current?.remove(s.mesh);
        });
      }
    };
  }, [mapRef]);

  useEffect(() => {
    vehicles.forEach(v => {
      if (!v.longitude || !v.latitude) return;
      const id = v.busId;
      const state = busStatesRef.current[id];
      if (!state) {
        busStatesRef.current[id] = {
          lng: v.longitude, lat: v.latitude, bearing: 0,
          targetLng: v.longitude, targetLat: v.latitude,
          lastUpdate: performance.now(),
        };
      } else {
        const dist = Math.sqrt(
          Math.pow(v.longitude - state.targetLng, 2) + Math.pow(v.latitude - state.targetLat, 2)
        );
        // Only update direction if the bus moved significantly (approx 5 meters)
        // This prevents GPS drift from making stopped buses spin randomly
        if (dist > 0.00005) {
          const prevLng = state.targetLng;
          const prevLat = state.targetLat;
          
          state.lng = prevLng;
          state.lat = prevLat;
          state.targetLng = v.longitude;
          state.targetLat = v.latitude;
          state.lastUpdate = performance.now();
          
          // Calculate angle in MapLibre Mercator space
          const currentMerc = MercatorCoordinate.fromLngLat([prevLng, prevLat], 0);
          const nextMerc = MercatorCoordinate.fromLngLat([v.longitude, v.latitude], 0);
          
          const dX = nextMerc.x - currentMerc.x;
          const dY = nextMerc.y - currentMerc.y;
          
          state.bearing = Math.atan2(dY, dX); // Saves the angle directly in radians
        }
      }
    });

    const currentIds = new Set(vehicles.map(v => v.busId));
    Object.keys(busStatesRef.current).forEach(id => {
      if (!currentIds.has(id)) {
        if (busStatesRef.current[id].mesh && sceneRef.current) {
          sceneRef.current.remove(busStatesRef.current[id].mesh!);
        }
        delete busStatesRef.current[id];
      }
    });
  }, [vehicles]);

  return null;
};

export default LiveBusesThreeLayer;
