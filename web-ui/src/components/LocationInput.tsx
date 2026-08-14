import React, { useState, useEffect, useRef } from 'react';
import { useQuery } from '@tanstack/react-query';
import { MapPin, Navigation, Map as MapIcon, X } from 'lucide-react';
import { searchStops, LocationPoint } from '../api';

interface LocationInputProps {
  placeholder: string;
  iconColorClass?: string;
  value: LocationPoint | null;
  onChange: (loc: LocationPoint | null) => void;
  onMapPickRequest: () => void;
}

const LocationInput: React.FC<LocationInputProps> = ({ 
  placeholder, iconColorClass = '', value, onChange, onMapPickRequest
}) => {
  const [query, setQuery] = useState('');
  const [isFocused, setIsFocused] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);

  // Sync internal text state with external value if it's set by map/gps
  useEffect(() => {
    if (value) {
      if (value.name) setQuery(value.name);
      else setQuery(`${value.latitude.toFixed(4)}, ${value.longitude.toFixed(4)}`);
    } else {
      setQuery('');
    }
  }, [value]);

  const { data: searchResults, isLoading } = useQuery({
    queryKey: ['stopsSearch', query],
    queryFn: () => searchStops(query),
    enabled: query.length >= 2 && isFocused,
  });

  // Handle outside click to close dropdown
  useEffect(() => {
    const handleClickOutside = (event: MouseEvent) => {
      if (containerRef.current && !containerRef.current.contains(event.target as Node)) {
        setIsFocused(false);
      }
    };
    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, []);

  const handleGeolocation = () => {
    if (!navigator.geolocation) {
      alert("Geolocation is not supported by your browser");
      return;
    }
    navigator.geolocation.getCurrentPosition((pos) => {
      onChange({
        latitude: pos.coords.latitude,
        longitude: pos.coords.longitude,
        name: 'Mevcut Konumum'
      });
      setIsFocused(false);
    }, () => {
      alert("Konum bilginiz alınamadı");
    }, { 
      enableHighAccuracy: true, 
      maximumAge: 0, 
      timeout: 10000 
    });
  };

  return (
    <div className="input-group" ref={containerRef}>
      <MapPin className={`input-icon ${iconColorClass}`} size={18} />
      <input
        type="text"
        className="glass-input"
        placeholder={placeholder}
        value={query}
        onChange={(e) => {
          setQuery(e.target.value);
          if (value) onChange(null); // break binding if they type
        }}
        onFocus={() => setIsFocused(true)}
      />

      {query && (
        <button className="clear-btn" onClick={() => { setQuery(''); onChange(null); }} type="button">
          <X size={16} />
        </button>
      )}

      {isFocused && (
        <div className="autocomplete-dropdown glass-panel">
          
          <div className="dropdown-actions">
            <button type="button" className="action-btn" onClick={handleGeolocation}>
              <Navigation size={14} /> Mevcut Konumu Kullan
            </button>
            <button type="button" className="action-btn" onClick={() => { setIsFocused(false); onMapPickRequest(); }}>
              <MapIcon size={14} /> Haritadan Seç
            </button>
          </div>

          {isLoading && <div className="dropdown-msg">Aranıyor...</div>}
          
          {searchResults?.items.length === 0 && query.length >= 2 && !isLoading && (
            <div className="dropdown-msg">Durak bulunamadı.</div>
          )}

          <ul className="suggestions-list">
            {searchResults?.items.map(stop => (
              <li 
                key={stop.id} 
                className="suggestion-item"
                onClick={() => {
                  onChange({
                    id: stop.id,
                    name: stop.name,
                    latitude: stop.latitude,
                    longitude: stop.longitude
                  });
                  setIsFocused(false);
                }}
              >
                <div className="suggestion-name">{stop.name}</div>
                <div className="suggestion-sub">#{stop.externalStopId} • {stop.routes.slice(0,3).join(', ')}{stop.routes.length > 3 ? '...' : ''}</div>
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  );
};

export default LocationInput;
