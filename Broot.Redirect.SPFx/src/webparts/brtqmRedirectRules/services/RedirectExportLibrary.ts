import { SPFI } from '@pnp/sp';
import '@pnp/sp/webs';
import '@pnp/sp/files';
import '@pnp/sp/folders';
import { MAX_UNIQUE_NAME_ATTEMPTS, uniqueFileName } from '../logic/UrlMappingCsv';
import { LibraryFailure, LibraryFile, LibraryFolder } from '../types';

export const EXPORT_LIBRARY_RELATIVE_URL: string = 'RedirectExport';
export const ARCHIVE_FOLDER_NAME: string = 'Archiv';

const CONFLICT_MESSAGE: RegExp = /already exist|existiert bereits|bereits vorhanden|-2130575257|-2147024713/i;

interface RequestError {
  status?: number;
  message?: string;
}

interface RawFile {
  Name?: string;
  ServerRelativeUrl?: string;
  TimeLastModified?: string;
}

export class LibraryError extends Error {
  public readonly reason: LibraryFailure;

  constructor(reason: LibraryFailure, message: string) {
    super(message);
    this.reason = reason;
  }
}

export function libraryUrlForSite(siteServerRelativeUrl: string): string {
  return `${siteServerRelativeUrl.replace(/\/+$/, '')}/${EXPORT_LIBRARY_RELATIVE_URL}`;
}

export function classifyLibraryError(error: unknown): LibraryFailure {
  if (error instanceof LibraryError) {
    return error.reason;
  }

  const request = error && typeof error === 'object' ? (error as RequestError) : {};

  if (CONFLICT_MESSAGE.test(request.message || '') || request.status === 409) {
    return 'conflict';
  }

  switch (request.status) {
    case 404:
      return 'notFound';
    case 401:
    case 403:
      return 'forbidden';
    default:
      return 'failed';
  }
}

function toLibraryError(error: unknown): LibraryError {
  if (error instanceof LibraryError) {
    return error;
  }
  return new LibraryError(classifyLibraryError(error), error instanceof Error ? error.message : String(error));
}

export interface IRedirectExportLibrary {
  libraryExists(): Promise<boolean>;
  ensureArchiveFolder(): Promise<void>;
  listRootFiles(): Promise<LibraryFile[]>;
  readFile(file: LibraryFile): Promise<ArrayBuffer>;
  addFile(folder: LibraryFolder, name: string, content: string | ArrayBuffer, overwrite: boolean): Promise<void>;
  addFileWithUniqueName(folder: LibraryFolder, baseName: string, content: string | ArrayBuffer): Promise<string>;
  moveToArchiveWithUniqueName(file: LibraryFile, baseName: string): Promise<string>;
  deleteFile(folder: LibraryFolder, name: string): Promise<void>;
}

export class RedirectExportLibrary implements IRedirectExportLibrary {
  private readonly sp: SPFI;
  private readonly libraryUrl: string;

  constructor(sp: SPFI, libraryUrl: string) {
    this.sp = sp;
    this.libraryUrl = libraryUrl;
  }

  public async libraryExists(): Promise<boolean> {
    return this.folderExists(this.libraryUrl);
  }

  public async ensureArchiveFolder(): Promise<void> {
    if (await this.folderExists(this.folderUrl('archive'))) {
      return;
    }

    try {
      await this.sp.web.folders.addUsingPath(this.folderUrl('archive'));
    } catch (error) {
      throw toLibraryError(error);
    }
  }

  public async listRootFiles(): Promise<LibraryFile[]> {
    try {
      const files: RawFile[] = await this.sp.web.getFolderByServerRelativePath(this.libraryUrl).files
        .select('Name', 'ServerRelativeUrl', 'TimeLastModified')();

      return files.map((file) => ({
        name: file.Name || '',
        serverRelativeUrl: file.ServerRelativeUrl || '',
        timeLastModified: file.TimeLastModified || ''
      }));
    } catch (error) {
      throw toLibraryError(error);
    }
  }

  public async readFile(file: LibraryFile): Promise<ArrayBuffer> {
    try {
      return await this.sp.web.getFileByServerRelativePath(file.serverRelativeUrl).getBuffer();
    } catch (error) {
      throw toLibraryError(error);
    }
  }

  public async addFile(folder: LibraryFolder, name: string, content: string | ArrayBuffer, overwrite: boolean): Promise<void> {
    try {
      await this.sp.web.getFolderByServerRelativePath(this.folderUrl(folder)).files.addUsingPath(name, content, { Overwrite: overwrite });
    } catch (error) {
      throw toLibraryError(error);
    }
  }

  public async addFileWithUniqueName(folder: LibraryFolder, baseName: string, content: string | ArrayBuffer): Promise<string> {
    return this.withUniqueName(baseName, (name) => this.addFile(folder, name, content, false));
  }

  public async moveToArchiveWithUniqueName(file: LibraryFile, baseName: string): Promise<string> {
    return this.withUniqueName(baseName, async (name) => {
      try {
        await this.sp.web.getFileByServerRelativePath(file.serverRelativeUrl).moveByPath(`${this.folderUrl('archive')}/${name}`, false);
      } catch (error) {
        throw toLibraryError(error);
      }
    });
  }

  public async deleteFile(folder: LibraryFolder, name: string): Promise<void> {
    try {
      await this.sp.web.getFileByServerRelativePath(`${this.folderUrl(folder)}/${name}`).delete();
    } catch (error) {
      throw toLibraryError(error);
    }
  }

  private folderUrl(folder: LibraryFolder): string {
    return folder === 'archive' ? `${this.libraryUrl}/${ARCHIVE_FOLDER_NAME}` : this.libraryUrl;
  }

  private async folderExists(url: string): Promise<boolean> {
    try {
      const folder: { Exists?: boolean } = await this.sp.web.getFolderByServerRelativePath(url).select('Exists')();
      return folder.Exists === true;
    } catch (error) {
      const failure = classifyLibraryError(error);
      if (failure === 'notFound') {
        return false;
      }
      throw toLibraryError(error);
    }
  }

  private async withUniqueName(baseName: string, write: (name: string) => Promise<void>): Promise<string> {
    for (let attempt = 1; attempt <= MAX_UNIQUE_NAME_ATTEMPTS; attempt++) {
      const name = uniqueFileName(baseName, attempt);

      try {
        await write(name);
        return name;
      } catch (error) {
        if (classifyLibraryError(error) !== 'conflict') {
          throw toLibraryError(error);
        }
      }
    }

    throw new LibraryError('conflict', `No free file name for '${baseName}' after ${MAX_UNIQUE_NAME_ATTEMPTS} attempts`);
  }
}
