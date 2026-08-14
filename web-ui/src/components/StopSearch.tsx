import React, { useState, useEffect } from 'react';

type Stop = {
  id: number;
  externalStopId: string;
  name: string;
  latitude: number;
  longitude: number;
  routes: any[];
};

const StopSearch: React.FC = () => {
  const [query, setQuery] = useState('');
  const [results, setResults] = useState<Stop[]>([]);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    if (!query) {
      setResults([]);
      return;
    }
    const controller = new AbortController();
    const fetchStops = async () => {
      setLoading(true);
      try {
        const res = await fetch(`/v1/stops?search=${encodeURIComponent(query)}&pageSize=5`, {
          signal: controller.signal,
        });
        if (res.ok) {
          const data = await res.json();
          setResults(data.items || []);
        } else {
          setResults([]);
        }
      } catch (_) {
        setResults([]);
      } finally {
        setLoading(false);
      }
    };
    fetchStops();
    return () => controller.abort();
  }, [query]);

  return (
    <div>
      <input
        placeholder="Durak ara"
        value={query}
        onChange={(e) => setQuery(e.target.value)}
      />
      {loading && <div>Loading...</div>}
      {results.length === 0 && query && !loading && (
        <div>Uçacak bir durak bulunamadı</div>
      )}
      <ul>
        {results.map((stop) => (
          <li key={stop.id}>{stop.name}</li>
        ))}
      </ul>
    </div>
  );
};

export default StopSearch;
