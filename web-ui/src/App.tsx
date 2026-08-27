import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MapProvider } from './context/MapContext';
import Layout from './components/Layout';
import { ThemeProvider } from './context/ThemeContext';
import TripPlanner from './pages/TripPlanner';
import Lines from './pages/Lines';
import Stops from './pages/Stops';
import LiveBuses from './pages/LiveBuses';
import SystemStatus from './pages/SystemStatus';
import { MapProvider as ReactMapGLProvider } from 'react-map-gl/maplibre';

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: 1,
      refetchOnWindowFocus: false,
    },
  },
});

function App() {
  return (
    <ThemeProvider>
      <QueryClientProvider client={queryClient}>
        <ReactMapGLProvider>
          <MapProvider>
            <BrowserRouter>
              <Layout>
                <Routes>
                  <Route path="/" element={<TripPlanner />} />
                  <Route path="/lines" element={<Lines />} />
                  <Route path="/stops" element={<Stops />} />
                  <Route path="/live-buses" element={<LiveBuses />} />
                  <Route path="/status" element={<SystemStatus />} />
                  <Route path="*" element={<Navigate to="/" replace />} />
                </Routes>
              </Layout>
            </BrowserRouter>
          </MapProvider>
        </ReactMapGLProvider>
      </QueryClientProvider>
    </ThemeProvider>
  );
}

export default App;
