import { TargetUrlIssue } from '../types';

const ABSOLUTE_HTTP_URL: RegExp = /^https?:\/\/[^\s/?#\\]+/i;
const SERVER_RELATIVE_PATH: RegExp = /^\/(?![/\\])/;

export function isAbsoluteHttpUrl(value: string): boolean {
  if (!ABSOLUTE_HTTP_URL.test(value)) {
    return false;
  }

  try {
    const parsed = new URL(value);
    return (parsed.protocol === 'http:' || parsed.protocol === 'https:') && parsed.hostname.length > 0;
  } catch {
    return false;
  }
}

export function isServerRelativePath(value: string): boolean {
  return SERVER_RELATIVE_PATH.test(value) && !/\s/.test(value);
}

export function isValidSourceUrl(value: string): boolean {
  return isAbsoluteHttpUrl(value.trim());
}

export function validateTargetUrl(value: string): TargetUrlIssue | undefined {
  const trimmed = value.trim();

  if (trimmed.length === 0) {
    return 'required';
  }

  return isAbsoluteHttpUrl(trimmed) || isServerRelativePath(trimmed) ? undefined : 'invalidTargetUrl';
}

export function resolveTargetUrl(value: string, siteOrigin: string): string {
  const trimmed = value.trim();

  return isServerRelativePath(trimmed) ? siteOrigin.replace(/\/+$/, '') + trimmed : trimmed;
}
