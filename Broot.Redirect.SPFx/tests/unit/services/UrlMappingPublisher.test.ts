import { IRedirectExportLibrary, LibraryError } from '../../../src/webparts/brtqmRedirectRules/services/RedirectExportLibrary';
import { IRedirectRulesService } from '../../../src/webparts/brtqmRedirectRules/services/RedirectRulesService';
import { UrlMappingPublisher } from '../../../src/webparts/brtqmRedirectRules/services/UrlMappingPublisher';
import { ExportEntry, LibraryFailure, LibraryFile, LibraryFolder } from '../../../src/webparts/brtqmRedirectRules/types';
import { MAX_UNIQUE_NAME_ATTEMPTS, uniqueFileName } from '../../../src/webparts/brtqmRedirectRules/logic/UrlMappingCsv';

interface StoredFile {
  content: string;
  modified: string;
}

type Operation = 'exists' | 'ensureArchive' | 'list' | 'read' | 'add' | 'move' | 'delete';

class FakeExportLibrary implements IRedirectExportLibrary {
  public exists: boolean = true;
  public root: Record<string, StoredFile> = {};
  public archive: Record<string, StoredFile> | undefined = {};
  public failures: Partial<Record<Operation, (name?: string) => LibraryFailure | undefined>> = {};
  public calls: string[] = [];
  public vanished: string[] = [];

  public async libraryExists(): Promise<boolean> {
    this.fail('exists');
    return this.exists;
  }

  public async ensureArchiveFolder(): Promise<void> {
    this.fail('ensureArchive');
    if (!this.archive) {
      this.calls.push('createArchive');
      this.archive = {};
    }
  }

  public async listRootFiles(): Promise<LibraryFile[]> {
    this.fail('list');
    return Object.keys(this.root).map((name) => ({ name, serverRelativeUrl: `/sites/qm/RedirectExport/${name}`, timeLastModified: this.root[name].modified }));
  }

  public async readFile(file: LibraryFile): Promise<ArrayBuffer> {
    this.fail('read', file.name);
    return new TextEncoder().encode(this.root[file.name].content).buffer as ArrayBuffer;
  }

  public async addFile(folder: LibraryFolder, name: string, content: string | ArrayBuffer, overwrite: boolean): Promise<void> {
    this.fail('add', `${folder}/${name}`);
    const target = this.folder(folder);
    if (target[name] && !overwrite) {
      throw new LibraryError('conflict', 'exists');
    }
    this.calls.push(`add ${folder}/${name}${overwrite ? ' overwrite' : ''}`);
    target[name] = { content: typeof content === 'string' ? content : new TextDecoder().decode(content), modified: '2026-11-18T05:00:00Z' };
  }

  public async addFileWithUniqueName(folder: LibraryFolder, baseName: string, content: string | ArrayBuffer): Promise<string> {
    for (let attempt = 1; attempt <= MAX_UNIQUE_NAME_ATTEMPTS; attempt++) {
      const name = uniqueFileName(baseName, attempt);
      try {
        await this.addFile(folder, name, content, false);
        return name;
      } catch (error) {
        if (!(error instanceof LibraryError) || error.reason !== 'conflict') {
          throw error;
        }
      }
    }
    throw new LibraryError('conflict', 'no free name');
  }

  public async moveToArchiveWithUniqueName(file: LibraryFile, baseName: string): Promise<string> {
    this.fail('move', file.name);
    if (this.vanished.indexOf(file.name) >= 0) {
      throw new LibraryError('notFound', 'gone');
    }
    let attempt = 1;
    while (this.folder('archive')[uniqueFileName(baseName, attempt)]) {
      attempt++;
    }
    const name = uniqueFileName(baseName, attempt);
    this.calls.push(`move ${file.name} -> archive/${name}`);
    this.folder('archive')[name] = this.root[file.name];
    delete this.root[file.name];
    return name;
  }

  public async deleteFile(folder: LibraryFolder, name: string): Promise<void> {
    this.fail('delete', name);
    this.calls.push(`delete ${folder}/${name}`);
    delete this.folder(folder)[name];
  }

  private folder(folder: LibraryFolder): Record<string, StoredFile> {
    if (folder === 'archive') {
      if (!this.archive) {
        throw new LibraryError('notFound', 'no archive');
      }
      return this.archive;
    }
    return this.root;
  }

  private fail(operation: Operation, name?: string): void {
    const rule = this.failures[operation];
    const failure = rule ? rule(name) : undefined;
    if (failure) {
      throw new LibraryError(failure, `${operation} failed`);
    }
  }
}

function exportEntry(id: number, sourceUrl: string, status: string = 'aktiv'): ExportEntry {
  return { id, uniqueId: `guid-${id}`, area: 'A', sourceUrl, targetUrl: 'https://new', status, modified: '', modifiedBy: '' };
}

function setup(entries: ExportEntry[] | Error = [exportEntry(1, 'http://old/a/'), exportEntry(2, 'http://old/b/', 'vorbereitet')]): {
  library: FakeExportLibrary;
  publisher: UrlMappingPublisher;
} {
  const library = new FakeExportLibrary();
  const rules = {
    getAllEntries: () => (entries instanceof Error ? Promise.reject(entries) : Promise.resolve(entries))
  } as unknown as IRedirectRulesService;
  return { library, publisher: new UrlMappingPublisher(rules, library) };
}

const PREVIOUS: StoredFile = { content: 'source-url\r\nhttp://old/previous', modified: '2026-11-17T05:00:00Z' };

describe('UrlMappingPublisher', () => {
  it('creates url-mapping.csv on the first run without archiving anything', async () => {
    const { library, publisher } = setup();
    library.root['Beispieldatei url-mapping.csv'] = { content: 'x', modified: '2026-01-01T00:00:00Z' };

    const result = await publisher.publish();

    expect(result).toEqual({ kind: 'published', sourceUrlCount: 1, skippedCount: 0, archivedFiles: [] });
    expect(library.root['url-mapping.csv'].content).toBe('source-url\r\nhttp://old/a/');
    expect(library.root['Beispieldatei url-mapping.csv'].content).toBe('x');
    expect(Object.keys(library.archive || {})).toEqual([]);
  });

  it('archives the previous file under its Swiss timestamp and replaces it in place', async () => {
    const { library, publisher } = setup();
    library.root['url-mapping.csv'] = PREVIOUS;

    const result = await publisher.publish();

    expect(result).toEqual({ kind: 'published', sourceUrlCount: 1, skippedCount: 0, archivedFiles: ['url-mapping_2026-11-17.csv'] });
    expect(library.archive!['url-mapping_2026-11-17.csv'].content).toBe(PREVIOUS.content);
    expect(library.root['url-mapping.csv'].content).toBe('source-url\r\nhttp://old/a/');
    expect(library.calls).toEqual(['add archive/url-mapping_2026-11-17.csv', 'add root/url-mapping.csv overwrite']);
  });

  it('adds a suffix when the archive name is taken', async () => {
    const { library, publisher } = setup();
    library.root['url-mapping.csv'] = PREVIOUS;
    library.archive!['url-mapping_2026-11-17.csv'] = { content: 'older', modified: '' };

    const result = await publisher.publish();

    expect(result.kind === 'published' && result.archivedFiles).toEqual(['url-mapping_2026-11-17_2.csv']);
    expect(library.archive!['url-mapping_2026-11-17.csv'].content).toBe('older');
  });

  it('creates the archive folder when it is missing', async () => {
    const { library, publisher } = setup();
    library.archive = undefined;
    library.root['url-mapping.csv'] = PREVIOUS;

    expect((await publisher.publish()).kind).toBe('published');
    expect(library.calls[0]).toBe('createArchive');
  });

  it('changes nothing when the library is missing', async () => {
    const { library, publisher } = setup();
    library.exists = false;

    expect(await publisher.publish()).toEqual({ kind: 'failed', step: 'library', reason: 'notFound' });
    expect(library.calls).toEqual([]);
  });

  it('changes nothing when reading the list fails', async () => {
    const { library, publisher } = setup(Object.assign(new Error('400 brtqmStatus does not exist'), { status: 400 }));
    library.root['url-mapping.csv'] = PREVIOUS;

    expect(await publisher.publish()).toEqual({ kind: 'failed', step: 'read', reason: 'failed' });
    expect(library.calls).toEqual([]);
    expect(library.root['url-mapping.csv']).toBe(PREVIOUS);
  });

  it('keeps the previous file and removes its archive copy when replacing fails', async () => {
    const { library, publisher } = setup();
    library.root['url-mapping.csv'] = PREVIOUS;
    library.failures.add = (name) => (name === 'root/url-mapping.csv' ? 'forbidden' : undefined);

    expect(await publisher.publish()).toEqual({ kind: 'failed', step: 'replace', reason: 'forbidden' });
    expect(library.root['url-mapping.csv']).toBe(PREVIOUS);
    expect(Object.keys(library.archive!)).toEqual([]);
  });

  it('keeps the previous file untouched when archiving it fails', async () => {
    const { library, publisher } = setup();
    library.root['url-mapping.csv'] = PREVIOUS;
    library.failures.add = (name) => (name && name.indexOf('archive/') === 0 ? 'failed' : undefined);

    expect(await publisher.publish()).toEqual({ kind: 'failed', step: 'archive', reason: 'failed' });
    expect(library.root['url-mapping.csv']).toBe(PREVIOUS);
  });

  it('moves further url-mapping files only after replacing and reports a partial result when one cannot be moved', async () => {
    const { library, publisher } = setup();
    library.root['url-mapping.csv'] = PREVIOUS;
    library.root['url-mapping (1).csv'] = { content: 'copy', modified: '2026-11-10T05:00:00Z' };
    library.root['URL-Mapping_alt.csv'] = { content: 'alt', modified: '2026-11-11T05:00:00Z' };
    library.failures.move = (name) => (name === 'URL-Mapping_alt.csv' ? 'forbidden' : undefined);

    const result = await publisher.publish();

    expect(result).toEqual({
      kind: 'partial',
      sourceUrlCount: 1,
      skippedCount: 0,
      archivedFiles: ['url-mapping_2026-11-17.csv', 'url-mapping_2026-11-10.csv'],
      notMovedFiles: ['URL-Mapping_alt.csv']
    });
    expect(library.calls.indexOf('add root/url-mapping.csv overwrite')).toBeLessThan(library.calls.indexOf('move url-mapping (1).csv -> archive/url-mapping_2026-11-10.csv'));
    expect(library.root['URL-Mapping_alt.csv'].content).toBe('alt');
  });

  it('treats a further file that is already gone as done', async () => {
    const { library, publisher } = setup();
    library.root['url-mapping (1).csv'] = { content: 'copy', modified: '2026-11-10T05:00:00Z' };
    library.vanished = ['url-mapping (1).csv'];

    expect((await publisher.publish()).kind).toBe('published');
  });

  it('reports skipped entries', async () => {
    const { publisher } = setup([exportEntry(1, ''), exportEntry(2, 'http://old/a'), exportEntry(3, 'http://old/x\nhttp://old/y')]);

    expect(await publisher.publish()).toEqual({ kind: 'published', sourceUrlCount: 1, skippedCount: 2, archivedFiles: [] });
  });

  describe('manual export', () => {
    it('stores the export in the library root under its name', async () => {
      const { library, publisher } = setup();

      expect(await publisher.archiveExport('Redirect-Regeln_2026-11-18_1430.csv', 'csv')).toEqual({ kind: 'archived', fileName: 'Redirect-Regeln_2026-11-18_1430.csv' });
      expect(library.root['Redirect-Regeln_2026-11-18_1430.csv'].content).toBe('csv');
    });

    it('adds a suffix instead of overwriting an export with the same name', async () => {
      const { library, publisher } = setup();
      library.root['Redirect-Regeln_2026-11-18_1430.csv'] = { content: 'first', modified: '' };

      expect(await publisher.archiveExport('Redirect-Regeln_2026-11-18_1430.csv', 'second')).toEqual({ kind: 'archived', fileName: 'Redirect-Regeln_2026-11-18_1430_2.csv' });
      expect(library.root['Redirect-Regeln_2026-11-18_1430.csv'].content).toBe('first');
    });

    it.each(['forbidden', 'notFound', 'failed'] as const)('reports %s when the export cannot be stored', async (reason) => {
      const { library, publisher } = setup();
      library.failures.add = () => reason;

      expect(await publisher.archiveExport('Redirect-Regeln.csv', 'csv')).toEqual({ kind: 'failed', reason });
    });
  });
});
