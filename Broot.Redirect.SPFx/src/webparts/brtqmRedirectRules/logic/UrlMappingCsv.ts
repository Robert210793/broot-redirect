import { isExportable } from './RedirectRuleStatus';
import { swissDateTime } from './SwissTime';

export const URL_MAPPING_HEADER: string = 'source-url';
export const URL_MAPPING_FILE_NAME: string = 'url-mapping.csv';
export const MAX_UNIQUE_NAME_ATTEMPTS: number = 99;

const LINE_BREAK: string = '\r\n';
const CONTAINS_LINE_BREAK: RegExp = /[\r\n]/;
const URL_MAPPING_FILE_PATTERN: RegExp = /^url-mapping.*\.csv$/i;

export interface UrlMappingSource {
  id: number;
  sourceUrl: string;
  status: string;
}

export interface UrlMappingContent {
  content: string;
  sourceUrlCount: number;
  skippedCount: number;
}

export function buildUrlMappingCsv(entries: UrlMappingSource[]): UrlMappingContent {
  const lines: string[] = [];
  let skippedCount = 0;

  entries
    .filter((entry) => isExportable(entry.status))
    .sort((left, right) => left.id - right.id)
    .forEach((entry) => {
      const sourceUrl = (entry.sourceUrl || '').trim();

      if (sourceUrl.length === 0 || CONTAINS_LINE_BREAK.test(sourceUrl)) {
        skippedCount += 1;
        return;
      }
      lines.push(sourceUrl);
    });

  return {
    content: [URL_MAPPING_HEADER].concat(lines).join(LINE_BREAK),
    sourceUrlCount: lines.length,
    skippedCount
  };
}

export function isUrlMappingFileName(name: string): boolean {
  return URL_MAPPING_FILE_PATTERN.test(name);
}

export function isCurrentUrlMappingFile(name: string): boolean {
  return name.toLowerCase() === URL_MAPPING_FILE_NAME;
}

export function urlMappingArchiveFileName(timeLastModified: string): string {
  const stamp = swissDateTime(new Date(timeLastModified));

  return `url-mapping_${stamp.date}.csv`;
}

export function uniqueFileName(baseName: string, attempt: number): string {
  if (attempt <= 1) {
    return baseName;
  }

  const extensionStart = baseName.lastIndexOf('.');

  return extensionStart > 0
    ? `${baseName.slice(0, extensionStart)}_${attempt}${baseName.slice(extensionStart)}`
    : `${baseName}_${attempt}`;
}
