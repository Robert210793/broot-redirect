import {
  buildUrlMappingCsv,
  isCurrentUrlMappingFile,
  isUrlMappingFileName,
  URL_MAPPING_FILE_NAME,
  UrlMappingContent,
  urlMappingArchiveFileName
} from '../logic/UrlMappingCsv';
import { ExportArchiveResult, LibraryFile, UrlMappingResult, UrlMappingStep } from '../types';
import { classifyLibraryError, IRedirectExportLibrary } from './RedirectExportLibrary';
import { IRedirectRulesService } from './RedirectRulesService';

function failed(step: UrlMappingStep, error: unknown): UrlMappingResult {
  return { kind: 'failed', step, reason: classifyLibraryError(error) };
}

export class UrlMappingPublisher {
  private readonly rules: IRedirectRulesService;
  private readonly library: IRedirectExportLibrary;

  constructor(rules: IRedirectRulesService, library: IRedirectExportLibrary) {
    this.rules = rules;
    this.library = library;
  }

  public async publish(): Promise<UrlMappingResult> {
    let csv: UrlMappingContent;

    try {
      csv = buildUrlMappingCsv(await this.rules.getAllEntries());
    } catch (error) {
      return failed('read', error);
    }

    try {
      if (!(await this.library.libraryExists())) {
        return { kind: 'failed', step: 'library', reason: 'notFound' };
      }
    } catch (error) {
      return failed('library', error);
    }

    let rootFiles: LibraryFile[];
    try {
      await this.library.ensureArchiveFolder();
    } catch (error) {
      return failed('archive', error);
    }

    try {
      rootFiles = (await this.library.listRootFiles()).filter((file) => isUrlMappingFileName(file.name));
    } catch (error) {
      return failed('library', error);
    }

    const current = rootFiles.filter((file) => isCurrentUrlMappingFile(file.name))[0];
    const others = rootFiles.filter((file) => file !== current);
    const archivedFiles: string[] = [];
    let currentCopy: string | undefined;

    if (current) {
      try {
        currentCopy = await this.library.addFileWithUniqueName(
          'archive',
          urlMappingArchiveFileName(current.timeLastModified),
          await this.library.readFile(current)
        );
      } catch (error) {
        return failed('archive', error);
      }
    }

    try {
      await this.library.addFile('root', URL_MAPPING_FILE_NAME, csv.content, true);
    } catch (error) {
      if (currentCopy) {
        await this.library.deleteFile('archive', currentCopy).catch(() => undefined);
      }
      return failed('replace', error);
    }

    if (currentCopy) {
      archivedFiles.push(currentCopy);
    }

    const notMovedFiles: string[] = [];

    for (const file of others) {
      try {
        archivedFiles.push(await this.library.moveToArchiveWithUniqueName(file, urlMappingArchiveFileName(file.timeLastModified)));
      } catch (error) {
        if (classifyLibraryError(error) !== 'notFound') {
          notMovedFiles.push(file.name);
        }
      }
    }

    return notMovedFiles.length > 0
      ? { kind: 'partial', sourceUrlCount: csv.sourceUrlCount, skippedCount: csv.skippedCount, archivedFiles, notMovedFiles }
      : { kind: 'published', sourceUrlCount: csv.sourceUrlCount, skippedCount: csv.skippedCount, archivedFiles };
  }

  public async archiveExport(fileName: string, content: string): Promise<ExportArchiveResult> {
    try {
      return { kind: 'archived', fileName: await this.library.addFileWithUniqueName('root', fileName, content) };
    } catch (error) {
      return { kind: 'failed', reason: classifyLibraryError(error) };
    }
  }
}
