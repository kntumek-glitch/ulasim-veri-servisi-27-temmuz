import React, { ReactNode, useState } from 'react';
import { NavLink } from 'react-router-dom';
import { Map, Activity, ListOrdered, Navigation2, Bus, ChevronLeft, ChevronRight, LocateFixed, Route, Sun, Moon } from 'lucide-react';
import MapBackground from './MapBackground';
import ErrorBoundary from './ErrorBoundary';

import { useMapState } from '../context/MapContext';

interface LayoutProps {
  children: ReactNode;
}

const Layout: React.FC<LayoutProps> = ({ children }) => {
  const { userLocation, setUserLocation, theme, setTheme } = useMapState();
  const [isSidebarCollapsed, setIsSidebarCollapsed] = useState(false);

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
              </NavLink>
            </li>
          </ul>

          {/* Location Button inside Menu */}
          <div style={{ marginTop: 'auto', padding: isSidebarCollapsed ? '20px 0' : '20px', display: 'flex', justifyContent: 'center' }}>
            {isSidebarCollapsed ? (
              <div 
                style={{ cursor: 'pointer', padding: '8px' }}
                onClick={() => {
                  if (userLocation) {
                    setUserLocation(null);
                    return;
                  }
                  if (!navigator.geolocation) {
                    alert('Tarayıcınız konum özelliğini desteklemiyor.');
                    return;
                  }
                  navigator.geolocation.getCurrentPosition(
                    (pos) => {
                      setUserLocation({
                        latitude: pos.coords.latitude,
                        longitude: pos.coords.longitude
                      });
                    },
                    (err) => alert('Konum alınamadı: ' + err.message),
                    { enableHighAccuracy: true, timeout: 10000, maximumAge: 0 }
                  );
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
                    setUserLocation(null);
                    return;
                  }
                  if (!navigator.geolocation) {
                    alert('Tarayıcınız konum özelliğini desteklemiyor.');
                    return;
                  }
                  navigator.geolocation.getCurrentPosition(
                    (pos) => {
                      setUserLocation({
                        latitude: pos.coords.latitude,
                        longitude: pos.coords.longitude
                      });
                    },
                    (err) => alert('Konum alınamadı: ' + err.message),
                    { enableHighAccuracy: true, timeout: 10000, maximumAge: 0 }
                  );
                }}
                title={userLocation ? "Konumu Gizle" : "Konumumu Göster"}
              >
                <LocateFixed size={16} style={{ display: 'inline', marginRight: 8, color: userLocation ? 'var(--color-accent-primary)' : 'inherit' }} />
                <span style={{ color: userLocation ? 'var(--color-accent-primary)' : 'inherit' }}>{userLocation ? 'Gizle' : 'Konumum'}</span>
              </button>
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
