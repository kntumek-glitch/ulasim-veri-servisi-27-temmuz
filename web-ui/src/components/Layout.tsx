import React, { ReactNode } from 'react';
import { NavLink } from 'react-router-dom';
import { Map, Activity, ListOrdered, Navigation2, Bus } from 'lucide-react';
import MapBackground from './MapBackground';
import ErrorBoundary from './ErrorBoundary';

import { useMapState } from '../context/MapContext';

interface LayoutProps {
  children: ReactNode;
}

const Layout: React.FC<LayoutProps> = ({ children }) => {
  const { setUserLocation } = useMapState();

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
        <nav className="glass-panel sidebar">
          <div className="logo">
            <Navigation2 className="logo-icon" size={28} />
            <h2>TransitFlow</h2>
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
          <div style={{ marginTop: 'auto', padding: '20px' }}>
            <button
              className="action-btn"
              style={{ width: '100%', background: 'rgba(255,255,255,0.1)' }}
              onClick={() => {
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
                  (err) => alert('Konum alınamadı: ' + err.message)
                );
              }}
            >
              <Navigation2 size={16} style={{ display: 'inline', marginRight: 8 }} />
              Konumum
            </button>
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
