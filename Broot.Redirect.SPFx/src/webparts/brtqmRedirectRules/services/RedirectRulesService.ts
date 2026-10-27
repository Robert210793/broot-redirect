import { SPFI } from '@pnp/sp';
import '@pnp/sp/webs';
import '@pnp/sp/lists';
import '@pnp/sp/items';
import '@pnp/sp/batching';
import '@pnp/sp/security';
import { IList } from '@pnp/sp/lists';
import { PermissionKind } from '@pnp/sp/security';
import { groupAreas } from '../logic/AreaName';
import {
  AreaEntryReference,
  AreaOption,
  RedirectRuleEntry,
  SaveFailureReason,
  SaveOperation,
  SaveOperationResult
} from '../types';

export const LIST_RELATIVE_URL: string = 'Lists/brtqmRedirectRules';
export const PAGE_SIZE: number = 5000;
export const BATCH_SIZE: number = 100;

const ENTRY_FIELDS: string[] = ['Id', 'UniqueId', 'brtqmArea', 'brtqmSourceUrl', 'brtqmTargetUrl', 'brtqmSourceKey'];
const THROTTLING_MESSAGE: RegExp = /SPQueryThrottledException|list view threshold|Schwellenwert/i;

interface RawItem {
  Id: number;
  UniqueId?: string;
  brtqmArea?: string;
  brtqmSourceUrl?: string;
  brtqmTargetUrl?: string;
  brtqmSourceKey?: string;
  'odata.etag'?: string;
  __metadata?: { etag?: string };
}

interface SentOperation {
  operation: SaveOperation;
  succeeded: boolean;
  failure?: SaveFailureReason;
  itemId?: number;
}

interface RequestError {
  status?: number;
  message?: string;
}

export interface IRedirectRulesService {
  listExists(): Promise<boolean>;
  getAreas(): Promise<AreaOption[]>;
  getAreaEntries(areaName: string): Promise<RedirectRuleEntry[]>;
  findAreaEntry(areaName: string): Promise<AreaEntryReference | undefined>;
  findEntriesBySourceKeys(sourceKeys: string[]): Promise<Record<string, AreaEntryReference[]>>;
  canExecute(operations: SaveOperation[]): Promise<boolean>;
  execute(operations: SaveOperation[]): Promise<SaveOperationResult[]>;
}

export function odataString(value: string): string {
  return `'${value.replace(/'/g, "''")}'`;
}

export function listUrlForSite(siteServerRelativeUrl: string): string {
  return `${siteServerRelativeUrl.replace(/\/+$/, '')}/${LIST_RELATIVE_URL}`;
}

function chunk<T>(values: T[], size: number): T[][] {
  const chunks: T[][] = [];

  for (let index = 0; index < values.length; index += size) {
    chunks.push(values.slice(index, index + size));
  }

  return chunks;
}

function toEntry(item: RawItem): RedirectRuleEntry {
  return {
    id: item.Id,
    uniqueId: item.UniqueId || '',
    etag: item['odata.etag'] || (item.__metadata && item.__metadata.etag) || '',
    area: item.brtqmArea || '',
    sourceUrl: item.brtqmSourceUrl || '',
    targetUrl: item.brtqmTargetUrl || '',
    sourceKey: item.brtqmSourceKey || ''
  };
}

function statusOf(error: unknown): number | undefined {
  return error && typeof error === 'object' ? (error as RequestError).status : undefined;
}

function isThrottled(error: unknown): boolean {
  const message = error && typeof error === 'object' ? (error as RequestError).message : undefined;
  return THROTTLING_MESSAGE.test(message || '');
}

function failureReason(error: unknown): SaveFailureReason {
  switch (statusOf(error)) {
    case 412:
      return 'conflict';
    case 401:
    case 403:
      return 'forbidden';
    case 404:
      return 'notFound';
    default:
      return 'error';
  }
}

function requiredPermission(operation: SaveOperation): PermissionKind {
  switch (operation.kind) {
    case 'create':
      return PermissionKind.AddListItems;
    case 'update':
      return PermissionKind.EditListItems;
    default:
      return PermissionKind.DeleteListItems;
  }
}

export class RedirectRulesService implements IRedirectRulesService {
  private readonly sp: SPFI;
  private readonly listUrl: string;

  constructor(sp: SPFI, listUrl: string) {
    this.sp = sp;
    this.listUrl = listUrl;
  }

  public async listExists(): Promise<boolean> {
    try {
      await this.list().select('Id')();
      return true;
    } catch (error) {
      if (statusOf(error) === 404) {
        return false;
      }
      throw error;
    }
  }

  public async getAreas(): Promise<AreaOption[]> {
    const references: AreaEntryReference[] = [];
    let lastId = 0;

    for (;;) {
      const page: RawItem[] = await this.list().items
        .select('Id', 'brtqmArea')
        .filter(`Id gt ${lastId}`)
        .orderBy('Id', true)
        .top(PAGE_SIZE)();

      page.forEach((item) => references.push({ id: item.Id, area: item.brtqmArea || '' }));

      if (page.length < PAGE_SIZE) {
        return groupAreas(references);
      }
      lastId = page[page.length - 1].Id;
    }
  }

  public async getAreaEntries(areaName: string): Promise<RedirectRuleEntry[]> {
    try {
      return await this.readAreaEntriesByIndex(areaName);
    } catch (error) {
      if (!isThrottled(error)) {
        throw error;
      }
      return this.readAreaEntriesByIdWindows(areaName);
    }
  }

  public async findAreaEntry(areaName: string): Promise<AreaEntryReference | undefined> {
    const items: RawItem[] = await this.list().items
      .select('Id', 'brtqmArea')
      .filter(`brtqmArea eq ${odataString(areaName.trim())}`)
      .top(1)();

    return items.length > 0 ? { id: items[0].Id, area: items[0].brtqmArea || '' } : undefined;
  }

  public async findEntriesBySourceKeys(sourceKeys: string[]): Promise<Record<string, AreaEntryReference[]>> {
    const matches: Record<string, AreaEntryReference[]> = {};

    for (const keys of chunk(sourceKeys, BATCH_SIZE)) {
      const [batchedSp, execute] = this.sp.batched();
      const batchedList = batchedSp.web.getList(this.listUrl);
      const requests = keys.map((key) => batchedList.items
        .select('Id', 'brtqmArea')
        .filter(`brtqmSourceKey eq ${odataString(key)}`)
        .top(10)() as Promise<RawItem[]>);

      await execute();
      const results = await Promise.all(requests);

      keys.forEach((key, index) => {
        matches[key] = results[index].map((item) => ({ id: item.Id, area: item.brtqmArea || '' }));
      });
    }

    return matches;
  }

  public async canExecute(operations: SaveOperation[]): Promise<boolean> {
    const permissions: PermissionKind[] = [];

    operations.map(requiredPermission).forEach((permission) => {
      if (permissions.indexOf(permission) < 0) {
        permissions.push(permission);
      }
    });

    const granted = await Promise.all(permissions.map((permission) => this.list().currentUserHasPermissions(permission)));
    return granted.every((value) => value);
  }

  public async execute(operations: SaveOperation[]): Promise<SaveOperationResult[]> {
    const results: SaveOperationResult[] = [];

    for (const operationChunk of chunk(operations, BATCH_SIZE)) {
      const [batchedSp, execute] = this.sp.batched();
      const batchedList = batchedSp.web.getList(this.listUrl);
      const pending = operationChunk.map((operation) => this.send(batchedList, operation)
        .then((itemId): SentOperation => ({ operation, succeeded: true, itemId }))
        .catch((error: unknown): SentOperation => ({ operation, succeeded: false, failure: failureReason(error) })));

      await execute();
      const sent = await Promise.all(pending);
      const savedEntries = await this.readEntriesById(sent
        .map((result) => result.itemId)
        .filter((itemId): itemId is number => itemId !== undefined));

      sent.forEach((result) => results.push({
        operation: result.operation,
        succeeded: result.succeeded,
        failure: result.failure,
        entry: result.itemId !== undefined ? savedEntries[result.itemId] : undefined
      }));
    }

    return results;
  }

  private list(): IList {
    return this.sp.web.getList(this.listUrl);
  }

  private async send(list: IList, operation: SaveOperation): Promise<number | undefined> {
    switch (operation.kind) {
      case 'create': {
        const created: RawItem = await list.items.add(operation.fields);
        return created.Id;
      }
      case 'update':
        await list.items.getById(operation.itemId).update(operation.fields, operation.etag);
        return operation.itemId;
      default:
        await list.items.getById(operation.itemId).recycle();
        return undefined;
    }
  }

  private async readEntriesById(itemIds: number[]): Promise<Record<number, RedirectRuleEntry>> {
    const entries: Record<number, RedirectRuleEntry> = {};

    if (itemIds.length === 0) {
      return entries;
    }

    const [batchedSp, execute] = this.sp.batched();
    const batchedList = batchedSp.web.getList(this.listUrl);
    const requests = itemIds.map((itemId) => (batchedList.items.getById(itemId).select(...ENTRY_FIELDS)() as Promise<RawItem>)
      .then((item) => { entries[itemId] = toEntry(item); })
      .catch(() => undefined));

    await execute();
    await Promise.all(requests);

    return entries;
  }

  private async readAreaEntriesByIndex(areaName: string): Promise<RedirectRuleEntry[]> {
    const entries: RedirectRuleEntry[] = [];
    let lastId = 0;

    for (;;) {
      const page: RawItem[] = await this.list().items
        .select(...ENTRY_FIELDS)
        .filter(`brtqmArea eq ${odataString(areaName)} and Id gt ${lastId}`)
        .orderBy('Id', true)
        .top(PAGE_SIZE)();

      page.forEach((item) => entries.push(toEntry(item)));

      if (page.length < PAGE_SIZE) {
        return entries;
      }
      lastId = page[page.length - 1].Id;
    }
  }

  private async readAreaEntriesByIdWindows(areaName: string): Promise<RedirectRuleEntry[]> {
    const newest: RawItem[] = await this.list().items.select('Id').orderBy('Id', false).top(1)();
    const maxId = newest.length > 0 ? newest[0].Id : 0;
    const entries: RedirectRuleEntry[] = [];

    for (let windowStart = 0; windowStart < maxId; windowStart += PAGE_SIZE) {
      const page: RawItem[] = await this.list().items
        .select(...ENTRY_FIELDS)
        .filter(`Id gt ${windowStart} and Id le ${windowStart + PAGE_SIZE} and brtqmArea eq ${odataString(areaName)}`)
        .orderBy('Id', true)
        .top(PAGE_SIZE)();

      page.forEach((item) => entries.push(toEntry(item)));
    }

    return entries;
  }
}
