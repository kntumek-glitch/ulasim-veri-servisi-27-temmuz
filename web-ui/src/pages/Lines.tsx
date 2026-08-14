import React, { useState, useEffect } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Search, ChevronLeft, ChevronRight, ArrowLeft } from 'lucide-react';
import { getRoutes, getRouteDirections, getRouteStops, getRouteShape, RouteDto } from '../api';
import { useMapState } from '../context/MapContext';

import { useLocation } from 'react-router-dom';

const Lines: React.FC = () => {
  const location = useLocation();
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const [selectedRoute, setSelectedRoute] = useState<RouteDto | null>(location.state?.route || null);
  const [isCollapsed, setIsCollapsed] = useState(false);

  const { data: routesData, isLoading: isLoadingRoutes } = useQuery({
    queryKey: ['routes', search, page],
    queryFn: () => getRoutes(search, page, 20),
    placeholderData: (prev) => prev,
  });

  const handleSearch = (e: React.ChangeEvent<HTMLInputElement>) => {
    setSearch(e.target.value);
    setPage(1);
  };

  if (selectedRoute) {
    return <LineDetail route={selectedRoute} onBack={() => setSelectedRoute(null)} />;
  }

  return (
    <div className={`planner-container glass-panel ${isCollapsed ? 'collapsed' : ''}`}>
      <button 
        className="collapse-toggle-btn"
        onClick={() => setIsCollapsed(!isCollapsed)}
        title={isCollapsed ? "Genişlet" : "Daralt"}
      >
        {isCollapsed ? <ChevronRight size={16} /> : <ChevronLeft size={16} />}
      </button>

      {!isCollapsed && (
        <>
      <div className="planner-header">
        <h2>Hatlar</h2>
      </div>

      <div className="planner-form" style={{ paddingBottom: 0 }}>
        <div className="input-group">
          <Search className="input-icon" size={18} />
          <input
            type="text"
            className="glass-input"
            placeholder="Hat numarası veya adı ile ara..."
            value={search}
            onChange={handleSearch}
          />
        </div>
      </div>

      <div className="planner-results">
        {isLoadingRoutes && <div className="empty-state">Hatlar yükleniyor...</div>}

        {!isLoadingRoutes && routesData?.items.length === 0 && (
          <div className="empty-state">Hat bulunamadı.</div>
        )}

        <div className="route-list">
          {routesData?.items.map((route) => (
            <div 
              key={route.routeId} 
              className="itinerary-card" 
              style={{ display: 'flex', alignItems: 'center', gap: 16 }}
              onClick={() => setSelectedRoute(route)}
            >
              <div style={{
                background: `#${route.routeColor || '333'}`,
                color: `#${route.routeTextColor || 'fff'}`,
                fontWeight: 'bold',
                padding: '8px 16px',
                borderRadius: '8px',
                minWidth: '60px',
                textAlign: 'center'
              }}>
                {route.routeShortName}
              </div>
              <div style={{ flex: 1, fontWeight: 600 }}>
                {route.routeLongName}
              </div>
            </div>
          ))}
        </div>

        {routesData && routesData.totalPages > 1 && (
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginTop: 16, padding: '0 16px' }}>
            <button 
              className="action-btn" 
              style={{ flex: 'none', padding: '8px 12px' }}
              disabled={!routesData.hasPreviousPage}
              onClick={() => setPage(p => p - 1)}
            >
              <ChevronLeft size={16} /> Önceki
            </button>
            <span style={{ fontSize: 13, color: 'var(--color-text-muted)' }}>
              Sayfa {routesData.page} / {routesData.totalPages}
            </span>
            <button 
              className="action-btn" 
              style={{ flex: 'none', padding: '8px 12px' }}
              disabled={!routesData.hasNextPage}
              onClick={() => setPage(p => p + 1)}
            >
              Sonraki <ChevronRight size={16} />
            </button>
          </div>
        )}
      </div>
        </>)}
    </div>
  );
};

const LineDetail: React.FC<{ route: RouteDto, onBack: () => void }> = ({ route, onBack }) => {
  const { setSelectedRouteShape, setSelectedRouteColor } = useMapState();
  const [activeDirectionId, setActiveDirectionId] = useState<number | null>(null);

  // Fetch directions
  const { data: dirData, isLoading: isLoadingDirs } = useQuery({
    queryKey: ['route-directions', route.routeId],
    queryFn: () => getRouteDirections(route.routeId),
  });

  // Set default direction when data loads
  useEffect(() => {
    if (dirData && dirData.directions.length > 0 && activeDirectionId === null) {
      setActiveDirectionId(dirData.directions[0].directionId);
    }
  }, [dirData, activeDirectionId]);

  // Fetch stops for active direction
  const { data: stops } = useQuery({
    queryKey: ['route-stops', route.routeId, activeDirectionId],
    queryFn: () => getRouteStops(route.routeId, activeDirectionId!),
    enabled: activeDirectionId !== null,
  });

  // Fetch shape for active direction
  const { data: shape } = useQuery({
    queryKey: ['route-shape', route.routeId, activeDirectionId],
    queryFn: () => getRouteShape(route.routeId, activeDirectionId!),
    enabled: activeDirectionId !== null,
  });

  // Sync shape to map
  useEffect(() => {
    if (shape) {
      setSelectedRouteShape(shape);
      setSelectedRouteColor(`#${route.routeColor || '00f0ff'}`);
    } else {
      setSelectedRouteShape(null);
    }
    
    return () => {
      setSelectedRouteShape(null); // Cleanup on unmount
    };
  }, [shape, route.routeColor, setSelectedRouteShape, setSelectedRouteColor]);

  const [isCollapsed, setIsCollapsed] = useState(false);

  return (
    <div className={`planner-container glass-panel ${isCollapsed ? 'collapsed' : ''}`}>
      <button 
        className="collapse-toggle-btn"
        onClick={() => setIsCollapsed(!isCollapsed)}
        title={isCollapsed ? "Genişlet" : "Daralt"}
      >
        {isCollapsed ? <ChevronRight size={16} /> : <ChevronLeft size={16} />}
      </button>

      {!isCollapsed && (
        <>
      <div className="planner-header" style={{ display: 'flex', gap: 12, alignItems: 'center' }}>
        <button className="clear-btn" style={{ position: 'relative', right: 0 }} onClick={onBack}>
          <ArrowLeft size={20} />
        </button>
        <div style={{ flex: 1 }}>
          <h2 style={{ fontSize: 18 }}>Hat {route.routeShortName}</h2>
          <div style={{ fontSize: 12, color: 'var(--color-text-muted)' }}>{route.routeLongName}</div>
        </div>
      </div>

      <div className="planner-results" style={{ paddingTop: 0 }}>
        {isLoadingDirs && <div className="empty-state">Hat detayları yükleniyor...</div>}

        {dirData && dirData.directions.length > 0 && (
          <div style={{ display: 'flex', gap: 8, marginBottom: 24, background: 'rgba(0,0,0,0.3)', padding: 4, borderRadius: 8 }}>
            {dirData.directions.map(d => (
              <button 
                key={d.directionId}
                className="action-btn"
                style={{ 
                  flex: 1, 
                  background: activeDirectionId === d.directionId ? 'rgba(255,255,255,0.1)' : 'transparent',
                  color: activeDirectionId === d.directionId ? 'var(--color-text-main)' : 'var(--color-text-muted)'
                }}
                onClick={() => setActiveDirectionId(d.directionId)}
              >
                {(() => {
                  if (d.headsigns && d.headsigns.length > 0) return `Yön: ${d.headsigns[0]}`;
                  if (route.routeLongName && route.routeLongName.includes('-')) {
                    const parts = route.routeLongName.split('-');
                    return `Yön: ${d.directionId === 0 ? parts[parts.length - 1].trim() : parts[0].trim()}`;
                  }
                  return `Yön ${d.directionId}`;
                })()}
              </button>
            ))}
          </div>
        )}

        <div className="timeline">
          {stops?.map((stop, idx) => (
            <div key={idx} className="timeline-leg" style={{ marginBottom: 16 }}>
              <div className="timeline-dot" style={{ background: 'var(--color-bg-base)', borderColor: 'var(--color-text-muted)', top: 2 }}></div>
              <div className="timeline-content">
                <div className="leg-title" style={{ fontSize: 14 }}>
                  {stop.stopName}
                </div>
                <div className="leg-description" style={{ fontSize: 11 }}>
                  Durak ID: {stop.stopId}
                </div>
              </div>
            </div>
          ))}
          
          {!stops && !isLoadingDirs && (
            <div className="empty-state">Duraklar yükleniyor...</div>
          )}
        </div>

        </div>
        </>)}
    </div>
  );
};

export default Lines;
