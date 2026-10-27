export const SOURCE_KEY_VERSION: string = 'v1';

const SCHEME_AND_AUTHORITY: RegExp = /^[a-z][a-z0-9+.-]*:\/\/[^/?#]*/i;
const PERCENT_ENCODED_SEQUENCE: RegExp = /(%[0-9a-f]{2})+/gi;

function decodeLenient(value: string): string {
  return value.replace(PERCENT_ENCODED_SEQUENCE, (sequence: string) => {
    try {
      return decodeURIComponent(sequence);
    } catch {
      return sequence;
    }
  });
}

function normalizePart(value: string): string {
  return decodeLenient(value.toLowerCase()).toLowerCase();
}

function pathSegments(path: string): string[] {
  return path
    .split('/')
    .filter((segment) => segment.length > 0)
    .map(normalizePart);
}

function queryPairs(query: string): string[][] {
  const seen: Record<string, boolean> = {};
  const pairs: string[][] = [];

  query
    .split('&')
    .filter((part) => part.length > 0)
    .forEach((part) => {
      const separatorIndex = part.indexOf('=');
      const key = normalizePart(separatorIndex >= 0 ? part.substring(0, separatorIndex) : part);
      const value = normalizePart(separatorIndex >= 0 ? part.substring(separatorIndex + 1) : '');
      const identity = JSON.stringify([key, value]);

      if (!seen[identity]) {
        seen[identity] = true;
        pairs.push([key, value]);
      }
    });

  return pairs.sort((left, right) => {
    const leftIdentity = JSON.stringify(left);
    const rightIdentity = JSON.stringify(right);
    return leftIdentity < rightIdentity ? -1 : leftIdentity > rightIdentity ? 1 : 0;
  });
}

export function canonicalizeSourceUrl(sourceUrl: string): string {
  const withoutAuthority = sourceUrl.trim().replace(SCHEME_AND_AUTHORITY, '');
  const fragmentIndex = withoutAuthority.indexOf('#');
  const withoutFragment = fragmentIndex >= 0 ? withoutAuthority.substring(0, fragmentIndex) : withoutAuthority;
  const queryIndex = withoutFragment.indexOf('?');
  const path = queryIndex >= 0 ? withoutFragment.substring(0, queryIndex) : withoutFragment;
  const query = queryIndex >= 0 ? withoutFragment.substring(queryIndex + 1) : '';

  return JSON.stringify({ path: pathSegments(path), query: queryPairs(query) });
}

function toHex(buffer: ArrayBuffer): string {
  const bytes = new Uint8Array(buffer);
  let hex = '';

  for (let index = 0; index < bytes.length; index++) {
    hex += ('0' + bytes[index].toString(16)).slice(-2);
  }

  return hex;
}

export async function computeSourceKey(sourceUrl: string): Promise<string> {
  const canonical = new TextEncoder().encode(canonicalizeSourceUrl(sourceUrl));
  const digest = await crypto.subtle.digest('SHA-256', canonical);

  return `${SOURCE_KEY_VERSION}:${toHex(digest)}`;
}
