import React, { useState, useEffect } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Search, ChevronLeft, ChevronRight, Navigation } from 'lucide-react';
import { useMapState } from '../context/MapContext';
import { getRouteVehicles } from '../api';
import ErrorBanner from '../components/ErrorBanner';
import { getErrorMessage } from '../utils/apiErrorMessages';

const LiveBuses: React.FC = () => {
  const [searchInput, setSearchInput] = useState('');
  const [activeRoute, setActiveRoute] = useState('');
  const [isCollapsed, setIsCollapsed] = useState(false);
  const { setLiveVehicles } = useMapState();
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
    refetchInterval: 3000,
    onError: (err) => {
      const msg = getErrorMessage(err as any);
      setGlobalError(msg);
    },
    onSuccess: () => {
      setGlobalError(null);
    },
  });

  // Sync with context
  useEffect(() => {
    if (data?.vehicles) {
      setLiveVehicles(data.vehicles);
    } else {
      setLiveVehicles([]);
    }
    
    return () => {
      setLiveVehicles([]);
    };
  }, [data, setLiveVehicles]);

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
                  {data.vehicles.map((v, i) => (
                    <div key={`vehicle-${v.busId}`} className="itinerary-card" style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
                      <div style={{
                        background: 'rgba(255,255,255,0.1)',
                        padding: '12px',
                        borderRadius: '50%',
                        display: 'flex',
                        alignItems: 'center',
                        justifyContent: 'center'
                      }}>
                        <span style={{ fontSize: 20 }}>🚌</span>
                      </div>
                      <div style={{ flex: 1 }}>
                        <div style={{ fontWeight: 600, fontSize: 15 }}>Plaka: {v.busId || 'Bilinmiyor'}</div>
                        <div style={{ fontSize: 12, color: 'var(--color-text-muted)', marginTop: 4 }}>
                          Yön: {v.direction || 'Belirtilmedi'}
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
