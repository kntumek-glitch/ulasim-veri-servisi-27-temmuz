import React, { ReactNode, useState, useEffect, useCallback, useRef } from 'react';
import { NavLink } from 'react-router-dom';
import { Map, Activity, ListOrdered, Bus, ChevronLeft, LocateFixed, Route, Sun, Moon } from 'lucide-react';
import MapBackground from './MapBackground';
import ErrorBoundary from './ErrorBoundary';

import { useMapState } from '../context/MapContext';
import { useGeolocation } from '../hooks/useGeolocation';

interface LayoutProps {
  children: ReactNode;
}

const Layout: React.FC<LayoutProps> = ({ children }) => {
  const { userLocation, setUserLocation, theme, setTheme } = useMapState();
  const [isSidebarCollapsed, setIsSidebarCollapsed] = useState(false);
  const [userAddress, setUserAddress] = useState('');
  const [manualLat, setManualLat] = useState('');
  const [manualLng, setManualLng] = useState('');
const { status, position, error, requestLocation, stopWatch } = useGeolocation({ enableHighAccuracy: true, timeout: 15000, maximumAge: 0 }, false);

  // Sync geolocation result with context
useEffect(() => {
  if (status === 'requesting') {
    // Clear previous address while a new location is being fetched
    setUserAddress('');
  }
  if (status === 'granted' && position) {
    const { latitude, longitude } = position.coords;
    setUserLocation({ latitude, longitude });
    // reverse geocode
    (async () => {
      try {
        const apiKey = process.env.REACT_APP_GOOGLE_MAPS_API_KEY;
        const res = await fetch(`https://maps.googleapis.com/maps/api/geocode/json?latlng=${latitude},${longitude}&key=${apiKey}`);
        const data = await res.json();
        if (data.results && data.results[0]) {
          setUserAddress(data.results[0].formatted_address);
        } else {
          setUserAddress('');
        }
      } catch (e) {
        console.error('Geocode hatası:', e);
        setUserAddress('');
      }
    })();
  } else if (status === 'denied' || status === 'unavailable') {
    setUserLocation(null);
    setUserAddress('');
  }
}, [status, position]);

return (
  <div className="app-container">
    {/* Background 3D/MapLibre Map */}
      <div className="map-container">
        <ErrorBoundary>
          <MapBackground />
        </ErrorBoundary>
      </div>

      {/* Foreground UI overlay */}
      <div className="ui-layer">
        
        {/* Sidebar Navigation */}
        <nav className={`glass-panel sidebar ${isSidebarCollapsed ? 'collapsed' : ''}`}>
          
          <div className="logo" style={{ justifyContent: isSidebarCollapsed ? 'center' : 'space-between' }}>
            {isSidebarCollapsed ? (
              <div 
                style={{ cursor: 'pointer', padding: '8px' }} 
                onClick={() => setIsSidebarCollapsed(false)}
                title="Menüyü Genişlet"
              >
                <Route className="logo-icon" size={28} />
              </div>
            ) : (
              <>
                <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
                  <Route size={24} color="var(--color-accent-primary)" />
                  <h2>Rotaİzmir</h2>
                </div>
                <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                  <button
                    onClick={() => setTheme(theme === 'dark' ? 'light' : 'dark')}
                    style={{ background: 'transparent', border: 'none', color: 'var(--color-text-muted)', cursor: 'pointer', display: 'flex', padding: '4px' }}
                    title={theme === 'dark' ? "Aydınlık Moda Geç" : "Karanlık Moda Geç"}
                  >
                    {theme === 'dark' ? <Sun size={20} /> : <Moon size={20} />}
                  </button>
                  <button 
                    onClick={() => setIsSidebarCollapsed(true)}
                    style={{ background: 'transparent', border: 'none', color: 'var(--color-text-muted)', cursor: 'pointer', display: 'flex', padding: '4px' }}
                    title="Menüyü Daralt"
                  >
                    <ChevronLeft size={20} />
                  </button>
                </div>
              </>
            )}
          </div>
          
          <ul className="nav-links">
            <li>
              <NavLink to="/" className={({ isActive }) => isActive ? 'nav-item active' : 'nav-item'}>
                <Map size={20} />
                <span>Rota Planlayıcı</span>
              </NavLink>
            </li>
            <li>
              <NavLink to="/lines" className={({ isActive }) => isActive ? 'nav-item active' : 'nav-item'}>
                <ListOrdered size={20} />
                <span>Hatlar</span>
              </NavLink>
            </li>
            <li>
              <NavLink to="/stops" className={({ isActive }) => isActive ? 'nav-item active' : 'nav-item'}>
                <Bus size={20} />
                <span>Duraklar</span>
              </NavLink>
            </li>
            <li>
              <NavLink to="/live-buses" className={({ isActive }) => isActive ? 'nav-item active' : 'nav-item'}>
                <Bus size={20} style={{ color: 'var(--color-accent-primary)' }} />
                <span>Canlı Otobüs Konumları</span>
              </NavLink>
            </li>
            <li>
              <NavLink to="/status" className={({ isActive }) => isActive ? 'nav-item active' : 'nav-item'}>
                <Activity size={20} />
                <span>Sistem Durumu</span>
<span className="geo-status-badge" style={{
  marginLeft: 8,
  fontSize: '0.75rem',
  color: status === 'granted' ? 'var(--color-success)' :
         status === 'requesting' ? 'var(--color-warning)' :
         status === 'denied' ? 'var(--color-error)' : 'var(--color-muted)'
}}>{status}</span>
              </NavLink>
            </li>
          </ul>

          {/* Location Button inside Menu */}
          <div style={{ marginTop: 'auto', padding: isSidebarCollapsed ? '20px 0' : '20px', display: 'flex', flexDirection: 'column', alignItems: 'center' }}>
            {isSidebarCollapsed ? (
  <div
    style={{ cursor: 'pointer', padding: '8px' }}
    onClick={() => {
       if (userLocation) {
         stopWatch();
         setUserLocation(null);
         setUserAddress('');
       } else {
         // Clear any stale location/address before requesting a new one
         setUserLocation(null);
         setUserAddress('');
         requestLocation();
       }
     }}
    title={userLocation ? "Konumu Gizle" : "Konumumu Göster"}
  >
    <LocateFixed size={28} style={{ color: userLocation ? 'var(--color-accent-primary)' : 'var(--color-text-muted)' }} />
  </div>
) : (
              <button
                className="action-btn"
                style={{ 
                  width: '100%', 
                  background: userLocation ? 'rgba(0, 240, 255, 0.1)' : 'var(--color-glass-bg)',
                  border: userLocation ? '1px solid var(--color-accent-primary)' : 'none'
                }}
                onClick={() => {
                  if (userLocation) {
                    stopWatch();
                    setUserLocation(null);
                    setUserAddress('');
                  } else {
                    requestLocation();
                  }
                }}
                title={userLocation ? "Konumu Gizle" : "Konumumu Göster"}
              >
                <LocateFixed size={16} style={{ display: 'inline', marginRight: 8, color: userLocation ? 'var(--color-accent-primary)' : 'inherit' }} />
                <span style={{ color: userLocation ? 'var(--color-accent-primary)' : 'inherit' }}>{userLocation ? 'Gizle' : 'Konumum'}</span>
              </button>
            )}
            {userAddress && (
              <div className="address-display" style={{ marginTop: '8px', color: 'var(--color-text-muted)', fontSize: '0.9rem' }}>
                {userAddress}
              </div>
            )}

          </div>
        </nav>

        {/* Main Content Area */}
        <main className="content-area">
          <ErrorBoundary>
            {children}
          </ErrorBoundary>
        </main>
      </div>
    </div>
  );
};

export default Layout;
