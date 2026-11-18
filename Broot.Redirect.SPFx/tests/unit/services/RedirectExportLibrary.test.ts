import { SPFI } from '@pnp/sp';
import {
  classifyLibraryError,
  LibraryError,
  libraryUrlForSite,
  RedirectExportLibrary
} from '../../../src/webparts/brtqmRedirectRules/services/RedirectExportLibrary';

interface Recorded {
  calls: string[];
  existing: Record<string, boolean>;
  addFailures: Record<string, { status: number; message: string }>;
}

function fakeSp(recorded: Recorded): SPFI {
  const folder = (url: string): unknown => ({
    select: () => () => (recorded.existing[url] === undefined
      ? Promise.reject({ status: 404, message: 'Not Found' })
      : Promise.resolve({ Exists: recorded.existing[url] })),
    files: Object.assign(
      () => Promise.resolve([]),
      {
        select: () => () => Promise.resolve([{ Name: 'url-mapping.csv', ServerRelativeUrl: `${url}/url-mapping.csv`, TimeLastModified: '2026-11-17T05:00:00Z' }]),
        addUsingPath: (name: string, _content: unknown, props: { Overwrite: boolean }) => {
          recorded.calls.push(`add ${url}/${name} overwrite=${props.Overwrite}`);
          const failure = recorded.addFailures[name];
          return failure ? Promise.reject(failure) : Promise.resolve({});
        }
      })
  });

  return {
    web: {
      getFolderByServerRelativePath: folder,
      folders: { addUsingPath: (url: string) => { recorded.calls.push(`createFolder ${url}`); return Promise.resolve({}); } },
      getFileByServerRelativePath: (url: string) => ({
        moveByPath: (dest: string, overwrite: boolean) => { recorded.calls.push(`move ${url} -> ${dest} overwrite=${overwrite}`); return Promise.resolve({}); },
        delete: () => { recorded.calls.push(`delete ${url}`); return Promise.resolve(); },
        getBuffer: () => Promise.resolve(new ArrayBuffer(0))
      })
    }
  } as unknown as SPFI;
}

function setup(): { recorded: Recorded; library: RedirectExportLibrary } {
  const recorded: Recorded = { calls: [], existing: {}, addFailures: {} };
  return { recorded, library: new RedirectExportLibrary(fakeSp(recorded), '/sites/qm/RedirectExport') };
}

describe('RedirectExportLibrary', () => {
  it('addresses the library below the site collection', () => {
    expect(libraryUrlForSite('/sites/Lhg-ims/')).toBe('/sites/Lhg-ims/RedirectExport');
  });

  it.each([
    [{ status: 404, message: 'Not Found' }, 'notFound'],
    [{ status: 403, message: 'Access denied' }, 'forbidden'],
    [{ status: 401, message: 'Unauthorized' }, 'forbidden'],
    [{ status: 400, message: '-2130575257, Microsoft.SharePoint.SPException: A file with the name already exists' }, 'conflict'],
    [{ status: 409, message: 'Conflict' }, 'conflict'],
    [{ status: 500, message: 'Server error' }, 'failed'],
    [new Error('network'), 'failed'],
    [new LibraryError('forbidden', 'x'), 'forbidden']
  ])('classifies %j as %s', (error, expected) => {
    expect(classifyLibraryError(error)).toBe(expected);
  });

  it('reports a missing library as not existing', async () => {
    const { library } = setup();

    expect(await library.libraryExists()).toBe(false);
  });

  it('creates the archive folder only when it is missing', async () => {
    const { recorded, library } = setup();
    await library.ensureArchiveFolder();
    recorded.existing['/sites/qm/RedirectExport/Archiv'] = true;
    await library.ensureArchiveFolder();

    expect(recorded.calls).toEqual(['createFolder /sites/qm/RedirectExport/Archiv']);
  });

  it('never overwrites when adding with a unique name and retries with a suffix on conflict', async () => {
    const { recorded, library } = setup();
    recorded.addFailures['url-mapping_2026-11-17.csv'] = { status: 400, message: 'A file with the name already exists' };

    const name = await library.addFileWithUniqueName('archive', 'url-mapping_2026-11-17.csv', 'x');

    expect(name).toBe('url-mapping_2026-11-17_2.csv');
    expect(recorded.calls).toEqual([
      'add /sites/qm/RedirectExport/Archiv/url-mapping_2026-11-17.csv overwrite=false',
      'add /sites/qm/RedirectExport/Archiv/url-mapping_2026-11-17_2.csv overwrite=false'
    ]);
  });

  it('gives up after 99 attempts', async () => {
    const { recorded, library } = setup();
    for (let attempt = 1; attempt <= 99; attempt++) {
      recorded.addFailures[attempt === 1 ? 'a.csv' : `a_${attempt}.csv`] = { status: 409, message: 'Conflict' };
    }

    await expect(library.addFileWithUniqueName('root', 'a.csv', 'x')).rejects.toMatchObject({ reason: 'conflict' });
    expect(recorded.calls).toHaveLength(99);
  });

  it('stops at the first error that is not a conflict', async () => {
    const { recorded, library } = setup();
    recorded.addFailures['a.csv'] = { status: 403, message: 'Access denied' };

    await expect(library.addFileWithUniqueName('root', 'a.csv', 'x')).rejects.toMatchObject({ reason: 'forbidden' });
    expect(recorded.calls).toHaveLength(1);
  });

  it('replaces a file with overwrite and moves files into the archive without overwriting', async () => {
    const { recorded, library } = setup();
    await library.addFile('root', 'url-mapping.csv', 'x', true);
    await library.moveToArchiveWithUniqueName({ name: 'url-mapping (1).csv', serverRelativeUrl: '/sites/qm/RedirectExport/url-mapping (1).csv', timeLastModified: '' }, 'url-mapping_2026-11-10.csv');

    expect(recorded.calls).toEqual([
      'add /sites/qm/RedirectExport/url-mapping.csv overwrite=true',
      'move /sites/qm/RedirectExport/url-mapping (1).csv -> /sites/qm/RedirectExport/Archiv/url-mapping_2026-11-10.csv overwrite=false'
    ]);
  });

  it('lists the root files with name, path and last modification', async () => {
    const { library } = setup();

    expect(await library.listRootFiles()).toEqual([
      { name: 'url-mapping.csv', serverRelativeUrl: '/sites/qm/RedirectExport/url-mapping.csv', timeLastModified: '2026-11-17T05:00:00Z' }
    ]);
  });
});
