import React from 'react';
import { useQuery } from '@tanstack/react-query';
import { ChevronLeft, ChevronRight } from 'lucide-react';
import RoutingEngineInfoCard from '../components/RoutingEngineInfoCard';
import ErrorBanner from '../components/ErrorBanner';
import { getErrorMessage } from '../utils/apiErrorMessages';

const fetchSystemStatus = async () => {
  const res = await fetch('/api/v2/admin/routing/snapshot');
  if (!res.ok) throw new Error('Failed to fetch system status');
  return res.json();
};

const fetchMetadata = async () => {
  const res = await fetch('/api/v1/gtfs/metadata');
  if (!res.ok) throw new Error('Failed to fetch metadata');
  return res.json();
};

// Helper to safely parse health responses and catch HTML proxy fallbacks
const parseHealthResponse = async (res: Response) => {
  const text = await res.text();
  if (text.trim().toLowerCase().startsWith('<!doctype html>')) {
    console.error('Health endpoint returned HTML:', text.substring(0, 100));
    return { status: 'DOWN', checks: {}, dependencies: {} };
  }
  try {
    return JSON.parse(text);
  } catch {
    if (!res.ok) return { status: 'DOWN', checks: {}, dependencies: {} };
    // .NET returns raw strings like "Healthy" for some endpoints
    return { status: text.trim() === 'Healthy' ? 'UP' : text.trim(), checks: {}, dependencies: {} };
  }
};

const fetchLiveHealth = async () => {
  const res = await fetch('/health/live');
  return parseHealthResponse(res);
};

const fetchReadyHealth = async () => {
  const res = await fetch('/health/ready');
  return parseHealthResponse(res);
};

const fetchDepHealth = async () => {
  const res = await fetch('/health/dependencies');
  return parseHealthResponse(res);
};

const SystemStatus: React.FC = () => {
  const [isCollapsed, setIsCollapsed] = React.useState(false);

  const { data: snapshotData, isLoading: isLoadingSnapshot, isError: isErrorSnapshot, error: errorSnapshot, refetch: refetchSnapshot } = useQuery({
    queryKey: ['system-status'],
    queryFn: fetchSystemStatus,
    refetchInterval: 10000 // Refetch every 10 seconds
  });

  const { data: metadataData, isLoading: isLoadingMetadata, isError: isErrorMetadata, error: errorMetadata, refetch: refetchMetadata } = useQuery({
    queryKey: ['gtfs-metadata'],
    queryFn: fetchMetadata,
    refetchInterval: 30000
  });

  // Health checks
  const { data: liveHealth, isLoading: isLoadingLive } = useQuery({
    queryKey: ['health-live'],
    queryFn: fetchLiveHealth,
    refetchInterval: 15000
  });
  const { data: readyHealth, isLoading: isLoadingReady } = useQuery({
    queryKey: ['health-ready'],
    queryFn: fetchReadyHealth,
    refetchInterval: 15000
  });
  const { data: depHealth, isLoading: isLoadingDep } = useQuery({
    queryKey: ['health-deps'],
    queryFn: fetchDepHealth,
    refetchInterval: 15000
  });

  // Global error handling (Only for critical data, not health endpoints)
  const globalError =
    (isErrorSnapshot && getErrorMessage(errorSnapshot as any)) ||
    (isErrorMetadata && getErrorMessage(errorMetadata as any)) ||
    undefined;

  const handleRetry = () => {
    if (isErrorSnapshot) refetchSnapshot();
    if (isErrorMetadata) refetchMetadata();
  };

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
            <h2>Sistem Durumu</h2>
            <p className="text-muted">RAPTOR V2 Motorundan canlı metrikler</p>
          </div>
          
          <div className="status-grid">
              <div className="status-card">
                <h4>Aktif GTFS Verisi</h4>
                <p className="status-value highlight" style={{ fontSize: 16 }}>
                  {isLoadingMetadata ? 'Yükleniyor...' : isErrorMetadata ? 'Hata' : (metadataData?.dataVersion || `Run ID: ${snapshotData?.active_import_id || '--'}`)}
                </p>
                <div style={{ fontSize: 12, color: 'var(--color-text-muted)', marginTop: 4 }}>
                  {metadataData?.feedStartDate && metadataData?.feedEndDate ? `${metadataData.feedStartDate} - ${metadataData.feedEndDate}` : ''}
                </div>
                {/* Stale warning */}
                {metadataData?.isStale && (
                  <div style={{ color: '#d9534f', marginTop: 6, fontWeight: 'bold' }}>
                    ⚠️ Timetable data is out of date
                  </div>
                )}
              </div>
              <div className="status-card">
                <h4>Snapshot Belleği</h4>
                <p className="status-value">
                  {isLoadingSnapshot ? '-- MB' : isErrorSnapshot ? 'Hata' : (snapshotData?.is_ready ? `${(snapshotData?.estimated_memory_bytes / 1024 / 1024).toFixed(1)} MB` : '-- MB')}
                </p>
              </div>
              <div className="status-card">
                <h4>Motor Durumu</h4>
                <p className={`status-value ${snapshotData?.is_ready ? 'success' : 'error'}`}>
                  {isLoadingSnapshot ? 'Yükleniyor...' : isErrorSnapshot ? 'Çevrimdışı' : (snapshotData?.is_ready ? 'Sağlıklı' : 'Güncelleniyor')}
                </p>
              </div>
              {/* Health checks */}
              <div className="status-card">
                <h4>API (Live)</h4>
                <p className={`status-value ${liveHealth?.status === 'UP' ? 'success' : 'error'}`}>
                  {isLoadingLive ? 'Yükleniyor...' : liveHealth?.status || 'Bilinmiyor'}
                </p>
              </div>
              <div className="status-card">
                <h4>PostgreSQL</h4>
                <p className={`status-value ${readyHealth?.checks?.database === 'UP' ? 'success' : 'error'}`}>
                  {isLoadingReady ? 'Yükleniyor...' : readyHealth?.checks?.database || 'Bilinmiyor'}
                </p>
              </div>
              <div className="status-card">
                <h4>Routing Engine</h4>
                <p className={`status-value ${readyHealth?.checks?.routing_engine === 'UP' ? 'success' : 'error'}`}>
                  {isLoadingReady ? 'Yükleniyor...' : readyHealth?.checks?.routing_engine || 'Bilinmiyor'}
                </p>
              </div>
              <div className="status-card">
                <h4>GTFS Feed (Dep)</h4>
                <p className={`status-value ${depHealth?.dependencies?.gtfs_data?.status === 'UP' ? 'success' : 'error'}`}>
                  {isLoadingDep ? 'Yükleniyor...' : depHealth?.dependencies?.gtfs_data?.status || 'Bilinmiyor'}
                </p>
              </div>
              <RoutingEngineInfoCard data={snapshotData} isLoading={isLoadingSnapshot} isError={isErrorSnapshot} />
          </div>
        </>
      )}
    </div>
  );
};

export default SystemStatus;
