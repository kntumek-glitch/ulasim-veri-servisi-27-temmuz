import React from 'react';

type Route = {
  planId: string;
  departureTime: string;
  arrivalTime: string;
  totalDurationMinutes: number;
};

interface Props {
  routes: Route[];
}

const RouteList: React.FC<Props> = ({ routes }) => {
  if (routes.length === 0) {
    return <div data-testid="no-routes">Uçacak bir rota bulunamadı</div>;
  }
  return (
    <ul data-testid="route-list">
      {routes.map((r) => (
        <li key={r.planId}>
          <span>{r.planId} – </span>
          <span>{new Date(r.departureTime).toLocaleTimeString()} –</span>
          <span>{new Date(r.arrivalTime).toLocaleTimeString()}</span> |
          <span>{r.totalDurationMinutes} min</span>
        </li>
      ))}
    </ul>
  );
};

export default RouteList;
