import * as nodeCrypto from 'crypto';

if (!globalThis.crypto) {
  Object.defineProperty(globalThis, 'crypto', { value: (nodeCrypto as unknown as { webcrypto: Crypto }).webcrypto });
}
