import React, { useState, useEffect } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Search, ChevronLeft, ChevronRight, MapPin, ArrowLeft, Navigation } from 'lucide-react';
import { useNavigate, useLocation } from 'react-router-dom';
import { getGtfsStops, getGtfsStopRoutes, GtfsStopResponse } from '../api';
import { useMapState } from '../context/MapContext';
import { useMap } from 'react-map-gl/maplibre';

const Stops: React.FC = () => {
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const [selectedStop, setSelectedStop] = useState<GtfsStopResponse | null>(null);
  const [isCollapsed, setIsCollapsed] = useState(false);
  const location = useLocation();

  useEffect(() => {
    if (location.state && location.state.autoSelectStop) {
      setSelectedStop(location.state.autoSelectStop);
    }
  }, [location.state]);

  const { data: stopsData, isLoading: isLoadingStops } = useQuery({
    queryKey: ['stops', search, page],
    queryFn: () => getGtfsStops(search, page, 20),
    placeholderData: (prev) => prev,
  });

  const handleSearch = (e: React.ChangeEvent<HTMLInputElement>) => {
    setSearch(e.target.value);
    setPage(1);
  };

  if (selectedStop) {
    return <StopDetail stop={selectedStop} onBack={() => setSelectedStop(null)} />;
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
        <h2>Duraklar</h2>
      </div>

      <div className="planner-form" style={{ paddingBottom: 0 }}>
        <div className="input-group">
          <Search className="input-icon" size={18} />
          <input
            type="text"
            className="glass-input"
            placeholder="Ad, ID veya kod ile ara..."
            value={search}
            onChange={handleSearch}
          />
        </div>
      </div>

      <div className="planner-results">
        {isLoadingStops && <div className="empty-state">Duraklar yükleniyor...</div>}

        {!isLoadingStops && stopsData?.items.length === 0 && (
          <div className="empty-state">Durak bulunamadı.</div>
        )}

        <div className="route-list">
          {stopsData?.items.map((stop) => (
            <div 
              key={stop.stopId} 
              className="itinerary-card" 
              style={{ display: 'flex', alignItems: 'center', gap: 16 }}
              onClick={() => setSelectedStop(stop)}
            >
              <div style={{
                background: 'rgba(255,255,255,0.1)',
                padding: '12px',
                borderRadius: '50%',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center'
              }}>
                <MapPin size={20} color="var(--color-accent-secondary)" />
              </div>
              <div style={{ flex: 1 }}>
                <div style={{ fontWeight: 600, fontSize: 15 }}>{stop.stopName}</div>
                <div style={{ fontSize: 12, color: 'var(--color-text-muted)', marginTop: 4 }}>
                  ID: {stop.stopId} {stop.stopCode ? `• Kod: ${stop.stopCode}` : ''}
                </div>
              </div>
            </div>
          ))}
        </div>

        {stopsData && stopsData.totalPages > 1 && (
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginTop: 16, padding: '0 16px' }}>
            <button 
              className="action-btn" 
              style={{ flex: 'none', padding: '8px 12px' }}
              disabled={page <= 1}
              onClick={() => setPage(p => p - 1)}
            >
              <ChevronLeft size={16} /> Önceki
            </button>
            <span style={{ fontSize: 13, color: 'var(--color-text-muted)' }}>
              Sayfa {stopsData.page} / {stopsData.totalPages}
            </span>
            <button 
              className="action-btn" 
              style={{ flex: 'none', padding: '8px 12px' }}
              disabled={page >= stopsData.totalPages}
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

const StopDetail: React.FC<{ stop: GtfsStopResponse, onBack: () => void }> = ({ stop, onBack }) => {
  const { setSelectedStop } = useMapState();
  const { current: map } = useMap();
  const navigate = useNavigate();

  // Fetch passing routes
  const { data: routes, isLoading: isLoadingRoutes } = useQuery({
    queryKey: ['stop-routes', stop.stopId],
    queryFn: () => getGtfsStopRoutes(stop.stopId),
  });

  // Sync stop to map context
  useEffect(() => {
    setSelectedStop({
      latitude: stop.latitude,
      longitude: stop.longitude,
      name: stop.stopName,
      id: Number(stop.stopId) // ID is string in GTFS, but Number works as placeholder here
    });

    if (map) {
      map.flyTo({
        center: [stop.longitude, stop.latitude],
        zoom: 16,
        duration: 1500
      });
    }

    return () => {
      setSelectedStop(null); // Cleanup
    };
  }, [stop, setSelectedStop, map]);

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
          <h2 style={{ fontSize: 18 }}>{stop.stopName}</h2>
          <div style={{ fontSize: 12, color: 'var(--color-text-muted)' }}>
            Durak ID: {stop.stopId} {stop.stopCode ? `• Kod: ${stop.stopCode}` : ''}
          </div>
        </div>
      </div>

      <div className="planner-results" style={{ paddingTop: 0 }}>
        
        <div style={{ marginBottom: 24 }}>
          <div style={{ fontSize: 13, color: 'var(--color-text-muted)', marginBottom: 8 }}>Koordinatlar & Rota</div>
          <div style={{ background: 'rgba(0,0,0,0.3)', padding: '12px 16px', borderRadius: 8, fontSize: 14, display: 'flex', gap: 8, alignItems: 'center', justifyContent: 'space-between' }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
              <MapPin size={16} color="var(--color-accent-secondary)" />
              {stop.latitude.toFixed(5)}, {stop.longitude.toFixed(5)}
            </div>
            <a 
              href={`https://www.google.com/maps/dir/?api=1&destination=${stop.latitude},${stop.longitude}`} 
              target="_blank" 
              rel="noopener noreferrer"
              className="action-btn"
              style={{ fontSize: 12, padding: '6px 12px', textDecoration: 'none', display: 'flex', alignItems: 'center', gap: 6 }}
            >
              <Navigation size={14} /> Yol Tarifi
            </a>
          </div>
        </div>

        <div style={{ fontSize: 13, color: 'var(--color-text-muted)', marginBottom: 12 }}>Geçen Hatlar</div>
        
        {isLoadingRoutes && <div className="empty-state" style={{ padding: 12 }}>Hatlar yükleniyor...</div>}

        {!isLoadingRoutes && routes?.length === 0 && (
          <div className="empty-state" style={{ padding: 12 }}>Bu duraktan geçen hat bulunmamaktadır.</div>
        )}

        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8 }}>
          {routes?.map(route => (
            <div 
              key={route.routeId} 
              style={{
                background: `#${route.routeColor || '333'}`,
                color: `#${route.routeTextColor || 'fff'}`,
                padding: '6px 12px',
                borderRadius: '16px',
                fontSize: 13,
                fontWeight: 600,
                display: 'flex',
                alignItems: 'center',
                gap: 8,
                border: '1px solid rgba(255,255,255,0.1)',
                cursor: 'pointer'
              }}
              title={route.routeLongName}
              onClick={() => navigate('/lines', { state: { route } })}
            >
              {route.routeShortName}
            </div>
          ))}
        </div>

      </div>
        </>)}
    </div>
  );
};

export default Stops;
