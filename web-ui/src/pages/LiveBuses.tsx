import React, { useState, useEffect } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Search, ChevronLeft, ChevronRight, Navigation } from 'lucide-react';
import { useMapState } from '../context/MapContext';
import { getRouteVehicles, getRouteShape } from '../api';
import ErrorBanner from '../components/ErrorBanner';
import { getErrorMessage } from '../utils/apiErrorMessages';

const LiveBuses: React.FC = () => {
  const [searchInput, setSearchInput] = useState('');
  const [activeRoute, setActiveRoute] = useState('');
  const [isCollapsed, setIsCollapsed] = useState(false);
  const { setLiveVehicles, selectedLiveBusId, setSelectedLiveBusId, setSelectedRouteShape, setSelectedRouteColor } = useMapState();
  const [globalError, setGlobalError] = useState<string | null>(null);

  const handleRetry = () => {
    setGlobalError(null);
    refetch();
  };

  const handleSearch = (e: React.FormEvent) => {
    e.preventDefault();
    if (searchInput.trim()) {
      setActiveRoute(searchInput.trim());
    }
  };

  const { data, isLoading, isError, error, refetch } = useQuery({
    queryKey: ['live-vehicles', activeRoute],
    queryFn: () => getRouteVehicles(activeRoute),
    enabled: !!activeRoute,
    refetchInterval: 1000,
  });

  useEffect(() => {
    if (isError && error) {
      const msg = getErrorMessage(error as any);
      setGlobalError(msg);
    } else if (data) {
      setGlobalError(null);
    }
  }, [isError, error, data]);

  // Fetch Route Shape
  const { data: shapeData } = useQuery({
    queryKey: ['route-shape', data?.routeId],
    queryFn: () => getRouteShape(data!.routeId, 0),
    enabled: !!data?.routeId,
    staleTime: Infinity,
  });

  // Sync with context
  useEffect(() => {
    if (data?.vehicles) {
      setLiveVehicles(data.vehicles);
    } else {
      setLiveVehicles([]);
    }
    
    if (shapeData && shapeData.length > 0) {
      setSelectedRouteShape(shapeData.map((p: any) => ({ latitude: p.latitude, longitude: p.longitude, sequence: p.sequence })));
      setSelectedRouteColor('#22c55e'); // Green color for live route shape
    } else {
      setSelectedRouteShape(null);
    }

    return () => {
      setLiveVehicles([]);
      setSelectedRouteShape(null);
    };
  }, [data, shapeData, setLiveVehicles, setSelectedRouteShape, setSelectedRouteColor]);

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
          {globalError && (
            <ErrorBanner message={globalError} type="error" onRetry={handleRetry} />
          )}
          <div className="planner-header">
            <h2>Canlı Otobüs Konumları</h2>
            <div style={{ fontSize: 13, color: 'var(--color-text-muted)', marginTop: 4 }}>
              Hat numarasını girerek aktif araçları takip edin.
            </div>
          </div>

          <form className="planner-form" style={{ paddingBottom: 0 }} onSubmit={handleSearch}>
            <div className="input-group">
              <Search className="input-icon" size={18} />
              <input
                type="text"
                className="glass-input"
                placeholder="Örn: 100, 200, 800..."
                value={searchInput}
                onChange={(e) => setSearchInput(e.target.value)}
              />
            </div>
            <button type="submit" className="action-btn" style={{ marginTop: 12, width: '100%', background: 'var(--color-accent-primary)', color: '#000', fontWeight: 'bold' }}>
              Araçları Bul
            </button>
          </form>

          <div className="planner-results">
            {!activeRoute && (
              <div className="empty-state">
                <Navigation size={32} style={{ marginBottom: 16, opacity: 0.5 }} />
                Canlı konumları görmek için bir hat numarası arayın.
              </div>
            )}

            {activeRoute && isLoading && (
              <div className="empty-state">Araç konumları yükleniyor...</div>
            )}

            {activeRoute && isError && (
              <div className="empty-state" style={{ color: '#ff6b6b' }}>
                Hata oluştu: {error instanceof Error ? error.message : 'Bilinmeyen Hata'}
              </div>
            )}

            {activeRoute && data && data.vehicles.length === 0 && (
              <div className="empty-state">
                Bu hatta şu an aktif araç bulunmuyor.
              </div>
            )}

            {activeRoute && data && data.vehicles.length > 0 && (
              <>
                <div style={{ marginBottom: 12, fontSize: 13, color: 'var(--color-text-muted)', display: 'flex', justifyContent: 'space-between' }}>
                  <span>Hat {activeRoute}</span>
                  <span style={{ color: 'var(--color-accent-primary)' }}>{data.vehicles.length} Araç</span>
                </div>
                <div className="route-list">
                  {data.vehicles.map((v: any) => (
                    <div 
                      key={`vehicle-${v.busId}`} 
                      className={`itinerary-card ${selectedLiveBusId === v.busId ? 'selected' : ''}`} 
                      style={{ 
                        display: 'flex', 
                        alignItems: 'center', 
                        gap: 12,
                        cursor: 'pointer',
                        border: selectedLiveBusId === v.busId ? '2px solid var(--color-accent-primary)' : '1px solid rgba(255, 255, 255, 0.1)'
                      }}
                      onClick={() => setSelectedLiveBusId(v.busId)}
                    >
                      <div style={{
                        background: selectedLiveBusId === v.busId ? 'var(--color-accent-primary)' : 'rgba(255,255,255,0.1)',
                        color: selectedLiveBusId === v.busId ? '#000' : 'inherit',
                        padding: '12px',
                        borderRadius: '50%',
                        display: 'flex',
                        alignItems: 'center',
                        justifyContent: 'center'
                      }}>
                        <span style={{ fontSize: 20 }}>🚌</span>
                      </div>
                      <div style={{ flex: 1 }}>
                        <div style={{ fontWeight: 600, fontSize: 15, display: 'flex', justifyContent: 'space-between' }}>
                          <span>{v.destinationName || `Yön: ${v.direction}`}</span>
                          <span style={{ fontSize: 12, opacity: 0.7 }}>Plaka: {v.busId}</span>
                        </div>
                        <div style={{ fontSize: 13, color: 'var(--color-text-muted)', marginTop: 4, display: 'flex', flexDirection: 'column', gap: 4 }}>
                          <span style={{ display: 'flex', alignItems: 'center', gap: 4 }}>
                            <Navigation size={12} /> 📍 {v.locationContext || 'Konum Bilinmiyor'}
                          </span>
                          <span style={{ display: 'flex', alignItems: 'center', gap: 4 }}>
                            <span style={{ fontSize: 12 }}>🕒</span> Çıkış: {v.originDepartureTime || 'Bilinmiyor'}
                          </span>
                        </div>
                      </div>
                    </div>
                  ))}
                </div>
              </>
            )}
          </div>
        </>
      )}
    </div>
  );
};

export default LiveBuses;
