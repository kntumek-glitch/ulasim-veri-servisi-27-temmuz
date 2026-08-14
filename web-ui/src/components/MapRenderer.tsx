import React, { useEffect } from 'react';

export interface MapRendererProps {
  routeId: string;
  directionId: number;
}

const MapRenderer: React.FC<MapRendererProps> = ({ routeId, directionId }) => {
  useEffect(() => {
    // Trigger a fetch to load shape data (mocked in tests)
    fetch('/api/shape');
  }, []);
  return <div data-testid="map-container">MapRenderer for {routeId} direction {directionId}</div>;
};

export default MapRenderer;
