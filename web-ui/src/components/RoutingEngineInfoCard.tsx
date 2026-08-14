import React from 'react';

interface SnapshotData {
  active_import_id?: number;
  feed_hash?: string;
  stop_count?: number;
  pattern_count?: number;
  trip_count?: number;
  transfer_count?: number;
  build_duration_ms?: number;
  estimated_memory_bytes?: number;
  created_at?: string;
}

interface RoutingEngineInfoCardProps {
  data?: SnapshotData;
  isLoading: boolean;
  isError: boolean;
}

const RoutingEngineInfoCard: React.FC<RoutingEngineInfoCardProps> = ({ data, isLoading, isError }) => {
  if (isLoading) {
    return <div className="status-card"><p className="status-value">Yükleniyor...</p></div>;
  }
  if (isError) {
    return <div className="status-card"><p className="status-value error">Hata</p></div>;
  }
  return (
    <div className="status-card" style={{ gridColumn: '1 / -1' }}>
      <h4>Routing Engine Bilgileri</h4>
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))', gap: '12px', marginTop: '12px', fontSize: '13px' }}>
        <div><span style={{ color: 'var(--color-text-muted)' }}>Aktif İçe Aktarım ID:</span> <br/> <strong>{data?.active_import_id ?? '--'}</strong></div>
        <div style={{ wordBreak: 'break-all' }}><span style={{ color: 'var(--color-text-muted)' }}>Feed Hash:</span> <br/> <strong>{data?.feed_hash ?? '--'}</strong></div>
        <div><span style={{ color: 'var(--color-text-muted)' }}>Durak Sayısı:</span> <br/> <strong>{data?.stop_count ?? '--'}</strong></div>
        <div><span style={{ color: 'var(--color-text-muted)' }}>Desen Sayısı:</span> <br/> <strong>{data?.pattern_count ?? '--'}</strong></div>
        <div><span style={{ color: 'var(--color-text-muted)' }}>Sefer Sayısı:</span> <br/> <strong>{data?.trip_count ?? '--'}</strong></div>
        <div><span style={{ color: 'var(--color-text-muted)' }}>Transfer Kenarı:</span> <br/> <strong>{data?.transfer_count ?? '--'}</strong></div>
        <div><span style={{ color: 'var(--color-text-muted)' }}>Derleme Süresi (ms):</span> <br/> <strong>{data?.build_duration_ms ?? '--'}</strong></div>
        <div><span style={{ color: 'var(--color-text-muted)' }}>Tahmini Bellek:</span> <br/> <strong>{data?.estimated_memory_bytes ? `${(data.estimated_memory_bytes / 1024 / 1024).toFixed(1)} MB` : '--'}</strong></div>
        <div><span style={{ color: 'var(--color-text-muted)' }}>Oluşturulma:</span> <br/> <strong>{data?.created_at ? new Date(data.created_at).toLocaleString() : '--'}</strong></div>
      </div>
    </div>
  );
};

export default RoutingEngineInfoCard;
