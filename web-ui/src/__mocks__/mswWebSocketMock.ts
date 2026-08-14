// Mock for @mswjs/interceptors/WebSocket to satisfy Jest
export const WebSocket = class {
  // minimal stub implementation
  url: string;
  constructor(url: string) {
    this.url = url;
  }
  addEventListener(_type: string, _listener: any) {}
  removeEventListener(_type: string, _listener: any) {}
  close() {}
  send(_data: any) {}
};
