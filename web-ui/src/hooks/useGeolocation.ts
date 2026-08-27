import { useState, useEffect, useCallback, useRef } from 'react';

export type GeolocationStatus = 'idle' | 'requesting' | 'granted' | 'denied' | 'unavailable';

export interface GeolocationResult {
  status: GeolocationStatus;
  position: GeolocationPosition | null;
  error: GeolocationPositionError | null;
  /** Manual trigger – call to start a new location request */
  requestLocation: () => void;
  /** Stop any ongoing watchPosition */
  stopWatch: () => void;
}

/**
 * Browser Geolocation API wrapper.
 *
 * @param options  PositionOptions passed to `navigator.geolocation.getCurrentPosition`.
 * @param autoStart If true, a location request is started on mount. Default **false**.
 * @returns        GeolocationResult with current status, position, error and a trigger function.
 */
export const useGeolocation = (
  options?: PositionOptions,
  autoStart: boolean = false
): GeolocationResult => {
  const [status, setStatus] = useState<GeolocationStatus>('idle');
  const [position, setPosition] = useState<GeolocationPosition | null>(null);
  const [error, setError] = useState<GeolocationPositionError | null>(null);

  const watchIdRef = useRef<number | null>(null);
  const optionsRef = useRef(options);

  useEffect(() => {
    optionsRef.current = options;
  }, [options]);

  const requestLocation = useCallback(() => {
    if (!navigator.geolocation) {
      setStatus('unavailable');
        setError({
          code: 1,
          message: 'Geolocation not supported by this browser.',
          PERMISSION_DENIED: 1,
          POSITION_UNAVAILABLE: 2,
          TIMEOUT: 3,
        });
      return;
    }
    
    // Clear previous watch if requested again
    if (watchIdRef.current !== null) {
      navigator.geolocation.clearWatch(watchIdRef.current);
      watchIdRef.current = null;
    }

    setStatus('requesting');
    const defaultOpts: PositionOptions = { enableHighAccuracy: true, maximumAge: 0 };
    const mergedOpts = { ...defaultOpts, ...(optionsRef.current || {}) };
    
    // Use watchPosition for continuous high-accuracy updates
    const id = navigator.geolocation.watchPosition(
      (pos) => {
        setPosition(pos);
        setError(null);
        setStatus('granted');
        // If accuracy is good enough, stop watching to conserve resources
        if (pos.coords.accuracy && pos.coords.accuracy <= 30) {
          if (watchIdRef.current !== null) {
            navigator.geolocation.clearWatch(watchIdRef.current);
            watchIdRef.current = null;
          }
        }
      },
      (err) => {
        setError(err);
        setPosition(null);
        setStatus(err.code === err.PERMISSION_DENIED ? 'denied' : 'unavailable');
        // Stop any ongoing watch on error
        if (watchIdRef.current !== null) {
          navigator.geolocation.clearWatch(watchIdRef.current);
          watchIdRef.current = null;
        }
      },
      mergedOpts
    );
    watchIdRef.current = id;
  }, []);

  const stopWatch = useCallback(() => {
    if (watchIdRef.current !== null) {
      navigator.geolocation.clearWatch(watchIdRef.current);
      watchIdRef.current = null;
    }
    setStatus('idle');
  }, []);

  useEffect(() => {
    if (autoStart) requestLocation();
    // Cleanup on unmount
    return () => {
      if (watchIdRef.current !== null) {
        navigator.geolocation.clearWatch(watchIdRef.current);
        watchIdRef.current = null;
      }
    };
  }, [autoStart, requestLocation]);

  return { status, position, error, requestLocation, stopWatch };
};
