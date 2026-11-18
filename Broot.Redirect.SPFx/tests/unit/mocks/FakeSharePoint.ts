import { SPFI } from '@pnp/sp';

export interface FakeItem {
  Id: number;
  UniqueId: string;
  etagVersion: number;
  brtqmArea: string;
  brtqmSourceUrl: string;
  brtqmTargetUrl: string;
  brtqmSourceKey: string;
  brtqmStatus: string;
  Modified: string;
  Editor: { Title: string };
}

export interface FakeComment {
  id: string;
  text: string;
  createdDate: string;
  author: { name: string };
  mentions?: { name: string }[] | null;
}

export interface FakeError {
  status: number;
  message: string;
}

export interface WriteRequest {
  kind: 'add' | 'update' | 'recycle';
  itemId?: number;
  fields?: Record<string, unknown>;
}

type Clause = (item: FakeItem) => boolean;

function httpError(status: number, message: string = `HTTP ${status}`): FakeError {
  return { status, message: `Error making HttpClient request in queryable [${status}] ::> ${message}` };
}

function parseClause(clause: string): Clause {
  const match = /^(\w+) (eq|gt|ge|lt|le) (.+)$/.exec(clause.trim());

  if (!match) {
    throw new Error(`Unsupported filter clause: ${clause}`);
  }

  const [, field, operator, rawValue] = match;
  const isString = rawValue.charAt(0) === "'";
  const value = isString ? rawValue.slice(1, -1).replace(/''/g, "'") : Number(rawValue);

  return (item: FakeItem) => {
    const actual = (item as unknown as Record<string, string | number>)[field];

    if (isString) {
      return operator === 'eq' && String(actual).toLowerCase() === String(value).toLowerCase();
    }

    const numeric = Number(actual);
    const expected = Number(value);
    switch (operator) {
      case 'eq': return numeric === expected;
      case 'gt': return numeric > expected;
      case 'ge': return numeric >= expected;
      case 'lt': return numeric < expected;
      default: return numeric <= expected;
    }
  };
}

export class FakeSharePoint {
  public items: FakeItem[] = [];
  public listMissing: boolean = false;
  public permissions: number[] = [2, 3, 4];
  public areaQueryThrottleLimit: number = Number.MAX_SAFE_INTEGER;
  public failWrite: (request: WriteRequest) => FakeError | undefined = () => undefined;
  public batchSizes: number[] = [];
  public writes: WriteRequest[] = [];
  public queries: string[] = [];
  public expands: string[] = [];
  public comments: Record<number, FakeComment[]> = {};
  public commentingDisabled: boolean | undefined = false;
  public listSettingsFailure: FakeError | undefined = undefined;
  public commentFailure: FakeError | undefined = undefined;
  public commentCountFailure: boolean = false;
  public commentQueries: { itemId: number; top: number; skip: number }[] = [];
  private nextId: number = 1;

  public seed(values: Partial<FakeItem>[]): FakeItem[] {
    return values.map((value) => {
      const item: FakeItem = {
        Id: value.Id || this.nextId,
        UniqueId: `guid-${value.Id || this.nextId}`,
        etagVersion: 1,
        brtqmArea: '',
        brtqmSourceUrl: '',
        brtqmTargetUrl: '',
        brtqmSourceKey: '',
        brtqmStatus: '',
        Modified: '2026-10-29T07:00:00Z',
        Editor: { Title: 'Test Benutzer' },
        ...value
      };
      this.nextId = Math.max(this.nextId, item.Id) + 1;
      this.items.push(item);
      return item;
    });
  }

  public modify(id: number, changes: Partial<FakeItem>): void {
    const item = this.find(id);
    Object.assign(item, changes, { etagVersion: item.etagVersion + 1 });
  }

  public asSpfi(): SPFI {
    return this.createSp(undefined) as unknown as SPFI;
  }

  private find(id: number): FakeItem {
    const item = this.items.filter((candidate) => candidate.Id === id)[0];
    if (!item) {
      throw httpError(404);
    }
    return item;
  }

  private toRaw(item: FakeItem, fields: string[]): Record<string, unknown> {
    const raw: Record<string, unknown> = { 'odata.etag': `"${item.etagVersion}"` };
    const source = item as unknown as Record<string, unknown>;
    (fields.length > 0 ? fields : Object.keys(item)).forEach((field) => {
      const name = field.split('/')[0];
      if (name === '_CommentCount') {
        if (this.commentCountFailure) {
          throw httpError(400, 'The field _CommentCount is not supported');
        }
        raw.OData__CommentCount = (this.comments[item.Id] || []).length || null;
        return;
      }
      raw[name] = source[name];
    });
    return raw;
  }

  private run<T>(gate: Promise<void> | undefined, work: () => T): Promise<T> {
    const start = gate || Promise.resolve();
    return start.then(() => {
      if (this.listMissing) {
        throw httpError(404);
      }
      return work();
    });
  }

  private createSp(gate: Promise<void> | undefined): unknown {
    return {
      batched: () => {
        let open: () => void = () => undefined;
        const batchGate = new Promise<void>((resolve) => { open = resolve; });
        const batch = { size: 0 };
        const batchedSp = {
          web: {
            getList: () => this.createList(batchGate, () => { batch.size++; })
          }
        };
        return [batchedSp, async () => {
          this.batchSizes.push(batch.size);
          open();
          await batchGate;
        }];
      },
      web: {
        getList: () => this.createList(gate, () => undefined)
      }
    };
  }

  private createList(gate: Promise<void> | undefined, count: () => void): unknown {
    const list = (): Promise<unknown> => this.run(gate, () => ({ Id: 'list-id' }));
    const settings = (): Promise<unknown> => this.run(gate, () => {
      if (this.listSettingsFailure) {
        throw this.listSettingsFailure;
      }
      return this.commentingDisabled === undefined ? {} : { DisableCommenting: this.commentingDisabled };
    });
    const selectable = Object.assign(list, {
      select: (...fields: string[]) => (fields.indexOf('DisableCommenting') >= 0 ? settings : list),
      currentUserHasPermissions: (permission: number) => this.run(gate, () => this.permissions.indexOf(permission) >= 0),
      items: this.createItems(gate, count)
    });
    return selectable;
  }

  private createItems(gate: Promise<void> | undefined, count: () => void): unknown {
    const query = { fields: [] as string[], filter: '', ascending: true, top: 100, orderBy: '' };

    const execute = (): Promise<unknown> => {
      count();
      const snapshot = { ...query, fields: query.fields.slice() };
      this.queries.push(snapshot.filter);
      return this.run(gate, () => {
        const clauses = snapshot.filter ? snapshot.filter.split(' and ').map(parseClause) : [];
        let matches = this.items.filter((item) => clauses.every((clause) => clause(item)));

        if (/^brtqmArea eq /.test(snapshot.filter) && matches.length > this.areaQueryThrottleLimit) {
          throw httpError(500, 'Microsoft.SharePoint.SPQueryThrottledException: The attempted operation is prohibited because it exceeds the list view threshold.');
        }

        matches = matches.slice().sort((left, right) => (snapshot.ascending ? left.Id - right.Id : right.Id - left.Id));
        return matches.slice(0, snapshot.top).map((item) => this.toRaw(item, snapshot.fields));
      });
    };

    const builder = Object.assign(execute, {
      select: (...fields: string[]) => { query.fields = fields; return builder; },
      expand: (...expanded: string[]) => { this.expands.push(...expanded); return builder; },
      filter: (filter: string) => { query.filter = filter; return builder; },
      orderBy: (field: string, ascending: boolean = true) => { query.orderBy = field; query.ascending = ascending; return builder; },
      top: (top: number) => { query.top = top; return builder; },
      add: (fields: Record<string, unknown>) => {
        count();
        return this.run(gate, () => this.write({ kind: 'add', fields }, () => {
          const [item] = this.seed([{ brtqmStatus: 'vorbereitet', ...fields } as Partial<FakeItem>]);
          return this.toRaw(item, ['Id']);
        }));
      },
      getById: (itemId: number) => {
        const fields: string[] = [];
        const read = (): Promise<unknown> => {
          count();
          return this.run(gate, () => this.toRaw(this.find(itemId), fields));
        };
        return Object.assign(read, {
          select: (...selected: string[]) => { fields.push(...selected); return read; },
          update: (changes: Record<string, unknown>, etag: string) => {
            count();
            return this.run(gate, () => this.write({ kind: 'update', itemId, fields: changes }, () => {
              const item = this.find(itemId);
              if (etag !== '*' && etag !== `"${item.etagVersion}"`) {
                throw httpError(412);
              }
              Object.assign(item, changes, { etagVersion: item.etagVersion + 1 });
              return { etag: `"${item.etagVersion}"` };
            }));
          },
          comments: this.createComments(gate, itemId),
          recycle: () => {
            count();
            return this.run(gate, () => this.write({ kind: 'recycle', itemId }, () => {
              const item = this.find(itemId);
              this.items = this.items.filter((candidate) => candidate !== item);
              return 'recycle-id';
            }));
          }
        });
      }
    });

    return builder;
  }

  private createComments(gate: Promise<void> | undefined, itemId: number): unknown {
    const page = { top: 30, skip: 0 };
    const read = (): Promise<unknown> => {
      const snapshot = { ...page };
      this.commentQueries.push({ itemId, ...snapshot });
      return this.run(gate, () => {
        if (this.commentFailure) {
          throw this.commentFailure;
        }
        this.find(itemId);
        return (this.comments[itemId] || []).slice(snapshot.skip, snapshot.skip + snapshot.top);
      });
    };
    const builder = Object.assign(read, {
      top: (top: number) => { page.top = top; return builder; },
      skip: (skip: number) => { page.skip = skip; return builder; },
      add: (text: string) => this.run(gate, () => {
        if (this.commentFailure) {
          throw this.commentFailure;
        }
        const item = this.find(itemId);
        const existing = this.comments[itemId] || [];
        const comment: FakeComment = {
          id: String(existing.length + 1),
          text,
          createdDate: '2026-11-18T09:15:00Z',
          author: { name: 'Test Benutzer' }
        };
        this.comments[itemId] = existing.concat(comment);
        item.etagVersion += 1;
        return comment;
      })
    });
    return builder;
  }

  private write<T>(request: WriteRequest, work: () => T): T {
    this.writes.push(request);
    const failure = this.failWrite(request);
    if (failure) {
      throw failure;
    }
    return work();
  }
}

export { httpError };
