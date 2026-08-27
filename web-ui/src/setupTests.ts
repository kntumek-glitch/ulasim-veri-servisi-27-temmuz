// jest-dom provides custom matchers for asserting on DOM nodes.
import '@testing-library/jest-dom';

// Polyfill fetch and Request for msw in Jest environment
import 'whatwg-fetch';

// Polyfill TextEncoder/TextDecoder (Node < 11 support)
import { TextEncoder, TextDecoder } from 'util';
global.TextEncoder = TextEncoder as any;
global.TextDecoder = TextDecoder as any;

// Polyfill BroadcastChannel for msw in Node environment
class BroadcastChannelMock {
  name: string;
  constructor(name: string) { this.name = name; }
  postMessage(_: any) {}
  close() {}
  addEventListener(_type: string, _listener: any) {}
  removeEventListener(_type: string, _listener: any) {}
}
// @ts-ignore
global.BroadcastChannel = BroadcastChannelMock as any;

// Polyfill WritableStream and ReadableStream for msw SSE support
// @ts-ignore
import { WritableStream, ReadableStream } from 'web-streams-polyfill/dist/ponyfill.js';
global.WritableStream = WritableStream as any;
// @ts-ignore
global.ReadableStream = ReadableStream as any;

// You can add any global test setup here, e.g., mock timers, fetch, etc.
