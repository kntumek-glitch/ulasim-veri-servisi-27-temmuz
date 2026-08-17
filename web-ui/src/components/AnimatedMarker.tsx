import React, { useEffect, useState, useRef } from 'react';
import { Marker } from 'react-map-gl';

interface AnimatedMarkerProps {
  longitude: number;
  latitude: number;
  children: React.ReactNode;
  [key: string]: any;
}

const AnimatedMarker: React.FC<AnimatedMarkerProps> = ({ longitude, latitude, children, ...props }) => {
  const [pos, setPos] = useState({ lng: longitude, lat: latitude });
  const prevTarget = useRef({ lng: longitude, lat: latitude });
  const reqRef = useRef<number>();

  useEffect(() => {
    // If target hasn't changed, do nothing
    if (longitude === prevTarget.current.lng && latitude === prevTarget.current.lat) {
      return;
    }

    const startLng = pos.lng;
    const startLat = pos.lat;
    const targetLng = longitude;
    const targetLat = latitude;
    
    // Distance check: if distance is huge (e.g. > ~2km), just snap to it (avoids flying across the city if data glitches)
    const dist = Math.sqrt(Math.pow(targetLng - startLng, 2) + Math.pow(targetLat - startLat, 2));
    if (dist > 0.02) { 
      setPos({ lng: targetLng, lat: targetLat });
      prevTarget.current = { lng: targetLng, lat: targetLat };
      return;
    }

    const startTime = performance.now();
    const duration = 2000; // 2 seconds animation

    const animate = (time: number) => {
      let progress = (time - startTime) / duration;
      if (progress > 1) progress = 1;

      // Easing function (easeOutQuart) for smooth start and slow end
      const ease = 1 - Math.pow(1 - progress, 4);

      const currentLng = startLng + (targetLng - startLng) * ease;
      const currentLat = startLat + (targetLat - startLat) * ease;

      setPos({ lng: currentLng, lat: currentLat });

      if (progress < 1) {
        reqRef.current = requestAnimationFrame(animate);
      }
    };

    if (reqRef.current) cancelAnimationFrame(reqRef.current);
    reqRef.current = requestAnimationFrame(animate);
    
    prevTarget.current = { lng: targetLng, lat: targetLat };

    return () => {
      if (reqRef.current) cancelAnimationFrame(reqRef.current);
    };
  }, [longitude, latitude]);

  return (
    <Marker longitude={pos.lng} latitude={pos.lat} {...props}>
      {children}
    </Marker>
  );
};

export default AnimatedMarker;
