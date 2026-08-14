import React, { useState, useEffect } from 'react';
import { Search, ArrowUpDown, Clock, ChevronDown, ChevronUp, Footprints, Bus, ChevronLeft, ChevronRight, X } from 'lucide-react';
import { useMutation } from '@tanstack/react-query';
import LocationInput from '../components/LocationInput';
import { searchJourney, JourneyPlanRequest } from '../api';
import { useMapState } from '../context/MapContext';

const TripPlanner: React.FC = () => {
  const [timeMode, setTimeMode] = useState<'DEPART_AT' | 'ARRIVE_BY'>('DEPART_AT');
  const [timeValue, setTimeValue] = useState<string>('12:00');
  const [dateValue] = useState<string>(new Date().toISOString().split('T')[0]);
  const [maxTransfers, setMaxTransfers] = useState<number>(2);
  const [maxWalking, setMaxWalking] = useState<number>(1000);

  const { mapOrigin, mapDestination, setMapOrigin, setMapDestination, selectedItinerary, setSelectedItinerary, itineraries, setItineraries, setPickingLocationFor } = useMapState();

  const [expandedIdx, setExpandedIdx] = useState<number | null>(null);
  const [expandedStops, setExpandedStops] = useState<Record<string, boolean>>({});
  
  const [validationErrors, setValidationErrors] = useState<{origin?: boolean, destination?: boolean}>({});

  // Sync sidebar expansion with map selection
  useEffect(() => {
    if (selectedItinerary && itineraries.length > 0) {
      const idx = itineraries.findIndex(i => i === selectedItinerary);
      if (idx >= 0 && idx !== expandedIdx) {
        setExpandedIdx(idx);
        setTimeout(() => {
          const cards = document.querySelectorAll('.itinerary-card');
          if (cards[idx]) {
            cards[idx].scrollIntoView({ behavior: 'smooth', block: 'nearest' });
          }
        }, 100);
      }
    }
  }, [selectedItinerary, itineraries, expandedIdx]);

  // Map state cleanup on unmount
  useEffect(() => {
    return () => {
      setSelectedItinerary(null);
      setItineraries([]);
      setMapOrigin(null);
      setMapDestination(null);
      setPickingLocationFor(null);
    };
  }, [setSelectedItinerary, setItineraries, setMapOrigin, setMapDestination, setPickingLocationFor]);

  const searchMutation = useMutation({
    mutationFn: searchJourney,
    onSuccess: (data) => {
      setExpandedIdx(0);
      setExpandedStops({});
      if (data.itineraries.length > 0) {
        setSelectedItinerary(data.itineraries[0]);
      } else {
        setSelectedItinerary(null);
      }
      setItineraries(data.itineraries);
    },
    onError: () => {
      setItineraries([]);
    }
  });

  const handleSwap = () => {
    const temp = mapOrigin;
    setMapOrigin(mapDestination);
    setMapDestination(temp);
  };

  const handleSearch = () => {
    const errors = { origin: !mapOrigin, destination: !mapDestination };
    setValidationErrors(errors);
    
    if (errors.origin || errors.destination) {
      return;
    }
    
    let isoDateTime = new Date().toISOString();
    if (dateValue && timeValue) {
      const localDateObj = new Date(`${dateValue}T${timeValue}`);
      isoDateTime = localDateObj.toISOString();
    }

    const requestPayload: JourneyPlanRequest = {
      origin: { lat: mapOrigin!.latitude, lon: mapOrigin!.longitude },
      destination: { lat: mapDestination!.latitude, lon: mapDestination!.longitude },
      dateTime: isoDateTime,
      searchMode: timeMode === 'DEPART_AT' ? 0 : 1,
      maxTransfers: maxTransfers,
      maxWalkingMeters: maxWalking,
      includeIntermediateStops: true
    };
    searchMutation.mutate(requestPayload);
  };

  const handleCancelResults = () => {
    searchMutation.reset();
    setSelectedItinerary(null);
    setItineraries([]);
  };

  const toggleStops = (e: React.MouseEvent, key: string) => {
    e.stopPropagation();
    setExpandedStops(prev => ({ ...prev, [key]: !prev[key] }));
  };

  const [isCollapsed, setIsCollapsed] = useState(false);
  const hasResults = searchMutation.isSuccess && searchMutation.data;

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
          <div className="planner-header" style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <h2>{hasResults ? 'Rotalar' : 'Rota Planlayıcı'}</h2>
            {hasResults && (
              <button className="clear-btn" onClick={handleCancelResults} title="İptal">
                <X size={20} />
              </button>
            )}
          </div>
      
          {!hasResults ? (
            <div className="planner-form">
              <div>
                <LocationInput 
                  placeholder="Başlangıç (örn. Bostanlı)" 
                  value={mapOrigin}
                  onChange={(v) => { setMapOrigin(v); setValidationErrors(p => ({...p, origin: false})); }}
                  onMapPickRequest={() => setPickingLocationFor('origin')}
                />
                {validationErrors.origin && <div className="error-message" style={{ color: 'var(--color-error)', fontSize: '12px', marginTop: '4px', paddingLeft: '32px' }}>Başlangıç noktası seçmelisiniz.</div>}
              </div>
              
              <div className="input-line"></div>
              <div className="swap-btn-container">
                <button className="swap-btn" onClick={handleSwap} title="Değiştir">
                  <ArrowUpDown size={14} />
                </button>
              </div>
              
              <div>
                <LocationInput 
                  placeholder="Varış (örn. Konak)" 
                  iconColorClass="color-secondary"
                  value={mapDestination}
                  onChange={(v) => { setMapDestination(v); setValidationErrors(p => ({...p, destination: false})); }}
                  onMapPickRequest={() => setPickingLocationFor('destination')}
                />
                {validationErrors.destination && <div className="error-message" style={{ color: 'var(--color-error)', fontSize: '12px', marginTop: '4px', paddingLeft: '32px' }}>Varış noktası seçmelisiniz.</div>}
              </div>
              
              <div className="time-group">
                <Clock className="input-icon" size={18} style={{ marginTop: '15px' }} />
                <select 
                  className="glass-input time-select"
                  value={timeMode}
                  onChange={e => setTimeMode(e.target.value as 'DEPART_AT' | 'ARRIVE_BY')}
                >
                  <option value="DEPART_AT">Çıkış saati</option>
                  <option value="ARRIVE_BY">Varış saati</option>
                </select>
                <input 
                  type="time" 
                  className="glass-input time-input" 
                  value={timeValue}
                  onChange={e => setTimeValue(e.target.value)}
                />
              </div>

              <div className="filters-group">
                <select 
                  className="glass-input" style={{flex: 1, paddingLeft: 16}}
                  value={maxTransfers}
                  onChange={e => setMaxTransfers(Number(e.target.value))}
                >
                  <option value={0}>Aktarmasız</option>
                  <option value={1}>1 Aktarma</option>
                  <option value={2}>2 Aktarma</option>
                  <option value={3}>3 Aktarma</option>
                  <option value={4}>4 Aktarma</option>
                  <option value={5}>5 Aktarma</option>
                </select>
                <select 
                  className="glass-input" style={{flex: 1, paddingLeft: 16}}
                  value={maxWalking}
                  onChange={e => setMaxWalking(Number(e.target.value))}
                >
                  <option value={500}>Maks. yürüyüş 500m</option>
                  <option value={1000}>Maks. yürüyüş 1km</option>
                  <option value={2000}>Maks. yürüyüş 2km</option>
                  <option value={5000}>Maks. yürüyüş 5km</option>
                </select>
              </div>
              
              {searchMutation.isError && (
                <div className="error-message" style={{ color: 'var(--color-error)', marginTop: 10, fontSize: '0.9rem' }}>
                  {searchMutation.error instanceof Error ? searchMutation.error.message : 'Arama sırasında bir hata oluştu.'}
                </div>
              )}

              <button 
                className="primary-btn" 
                onClick={handleSearch}
                disabled={searchMutation.isPending}
              >
                {searchMutation.isPending ? <div className="spinner"></div> : <Search size={18} />}
                <span>{searchMutation.isPending ? 'Aranıyor...' : 'Rota Bul'}</span>
              </button>
            </div>
          ) : (
            <div className="planner-results" style={{ maxHeight: 'calc(100vh - 120px)' }}>
              {searchMutation.data?.itineraries.length === 0 && (
                <div className="empty-state">
                  Bu kriterlere uygun rota bulunamadı. Yürüme mesafesini veya aktarma sayısını artırmayı deneyin.
                </div>
              )}

              {searchMutation.data?.itineraries.map((itinerary, idx) => {
                const walkTime = itinerary.legs.filter(l => l.mode === 'WALK').reduce((sum, l) => sum + Math.round(l.durationSeconds / 60), 0);
                const transitTime = itinerary.legs.filter(l => l.mode === 'TRANSIT').reduce((sum, l) => sum + Math.round(l.durationSeconds / 60), 0);
                const waitTime = Math.round(itinerary.totalWaitingTimeSeconds / 60);
                const lines = itinerary.legs.filter(l => l.mode === 'TRANSIT' && l.routeShortName).map(l => l.routeShortName);
                const isExpanded = expandedIdx === idx;

                return (
                  <div 
                    key={idx} 
                    className={`itinerary-card ${idx === expandedIdx ? 'highlighted' : ''}`}
                    onClick={() => {
                      const newIdx = isExpanded ? null : idx;
                      setExpandedIdx(newIdx);
                      setSelectedItinerary(newIdx !== null ? itinerary : null);
                    }}
                  >
                    <div className="itinerary-main-info">
                      <div className="itinerary-time-line">
                        <span className="time-text">
                          {new Date(itinerary.departureTime).toLocaleTimeString([], {hour: '2-digit', minute:'2-digit'})}
                        </span>
                        <div className="duration-line"></div>
                        <span className="time-text">
                          {new Date(itinerary.arrivalTime).toLocaleTimeString([], {hour: '2-digit', minute:'2-digit'})}
                        </span>
                      </div>
                    </div>
                    
                    <div className="itinerary-sub-info">
                      {itinerary.totalDurationMinutes} dk | {itinerary.transferCount} aktarma
                    </div>

                    <div className="itinerary-breakdown">
                      <div className="breakdown-item">Yürüme: {walkTime}dk</div>
                      <div className="breakdown-item">| Araç: {transitTime}dk</div>
                      <div className="breakdown-item">| Bekleme: {waitTime}dk</div>
                    </div>

                    {lines.length > 0 && (
                      <div className="itinerary-lines">
                        Hatlar: 
                        {lines.map((line, lIdx) => (
                          <React.Fragment key={lIdx}>
                            <span className="line-badge">{line}</span>
                            {lIdx < lines.length - 1 && <span className="line-arrow">{'>'}</span>}
                          </React.Fragment>
                        ))}
                      </div>
                    )}

                    {isExpanded && (
                      <div className="itinerary-details" onClick={e => e.stopPropagation()}>
                        <div className="timeline">
                          {itinerary.legs.map((leg, lIdx) => {
                            const legKey = `leg-${idx}-${lIdx}`;
                            const hasStops = leg.intermediateStops && leg.intermediateStops.length > 0;
                            const stopsExpanded = !!expandedStops[legKey];

                            if (leg.mode === 'WALK') {
                              return (
                                <div key={lIdx} className="timeline-leg">
                                  <div className="timeline-dot"></div>
                                  <div className="timeline-content">
                                    <div className="leg-title">
                                      <Footprints size={16} color="var(--color-text-muted)" />
                                      Yürüme
                                    </div>
                                    <div className="leg-description">
                                      {leg.distanceMeters} metre, yakl. {Math.round(leg.durationSeconds / 60)} dk
                                    </div>
                                  </div>
                                </div>
                              );
                            }

                            if (leg.mode === 'TRANSIT' && leg.routeShortName && leg.fromStopName && leg.toStopName) {
                              return (
                                <div key={lIdx} className="timeline-leg">
                                  <div className="timeline-dot transit"></div>
                                  <div className="timeline-content">
                                    <div className="timeline-header">
                                      <div className="leg-title">
                                        <Bus size={16} color="var(--color-accent-primary)" />
                                        Hat {leg.routeShortName}
                                      </div>
                                      <div className="leg-time">
                                        {new Date(leg.departureTime).toLocaleTimeString([], {hour: '2-digit', minute:'2-digit'})}
                                      </div>
                                    </div>
                                    <div className="leg-description">
                                      <div>Biniş: <strong>{leg.fromStopName}</strong></div>
                                      {leg.headsign && (
                                        <div className="leg-headsign">Yön: {leg.headsign}</div>
                                      )}
                                      
                                      {hasStops && (
                                        <div>
                                          <button 
                                            className="intermediate-stops-toggle"
                                            onClick={(e) => toggleStops(e, legKey)}
                                          >
                                            {stopsExpanded ? <ChevronUp size={14} /> : <ChevronDown size={14} />}
                                            {leg.intermediateStops!.length} ara durak
                                          </button>
                                          
                                          {stopsExpanded && (
                                            <div className="intermediate-stops-list">
                                              {leg.intermediateStops!.map((istop, iIdx) => (
                                                <div key={iIdx} className="intermediate-stop-item">
                                                  <span className="istop-time">{new Date(istop.arrivalTime).toLocaleTimeString([], {hour: '2-digit', minute:'2-digit'})}</span>
                                                  <span className="istop-name">{istop.name}</span>
                                                </div>
                                              ))}
                                            </div>
                                          )}
                                        </div>
                                      )}
                                      
                                      <div style={{marginTop: 8}}>İniş: <strong>{leg.toStopName}</strong></div>
                                      <div style={{color: 'var(--color-text-muted)', fontSize: 12}}>
                                        Varış: {new Date(leg.arrivalTime).toLocaleTimeString([], {hour: '2-digit', minute:'2-digit'})}
                                      </div>
                                    </div>
                                  </div>
                                </div>
                              );
                            }
                            
                            return null;
                          })}
                          
                          <div className="timeline-leg" style={{marginBottom: 0}}>
                            <div className="timeline-dot"></div>
                            <div className="timeline-content">
                              <div className="leg-title">Varış Noktası</div>
                              <div className="leg-time" style={{color: 'var(--color-text-main)', marginTop: 4, fontSize: 13}}>
                                Varış saati {new Date(itinerary.arrivalTime).toLocaleTimeString([], {hour: '2-digit', minute:'2-digit'})}
                              </div>
                            </div>
                          </div>
                        </div>
                      </div>
                    )}
                  </div>
                );
              })}
            </div>
          )}
        </>
      )}
    </div>
  );
};

export default TripPlanner;
