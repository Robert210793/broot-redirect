export type RedirectRuleStatus = 'vorbereitet' | 'IIS-Umleitung offen' | 'Testing' | 'Fehlerhaft' | 'aktiv';

export interface RedirectRuleEntry {
  id: number;
  uniqueId: string;
  etag: string;
  area: string;
  sourceUrl: string;
  targetUrl: string;
  sourceKey: string;
  status: string;
}

export interface ExportEntry {
  id: number;
  uniqueId: string;
  area: string;
  sourceUrl: string;
  targetUrl: string;
  status: string;
  modified: string;
  modifiedBy: string;
}

export interface RedirectRuleComment {
  id: string;
  text: string;
  authorName: string;
  created: string;
}

export type CommentsLoadResult =
  | { kind: 'loaded'; comments: RedirectRuleComment[] }
  | { kind: 'disabled' }
  | { kind: 'forbidden' }
  | { kind: 'failed' };

export type AddCommentResult = 'added' | 'disabled' | 'forbidden' | 'failed';

export interface AreaSnapshot {
  area: string;
  targetUrl: string;
  entries: RedirectRuleEntry[];
}

export interface AreaOption {
  key: string;
  name: string;
}

export interface AreaEntryReference {
  id: number;
  area: string;
}

export interface SourceUrlRow {
  rowId: string;
  itemId?: number;
  sourceUrl: string;
  removed: boolean;
  status?: RedirectRuleStatus;
}

export interface PreparedRow extends SourceUrlRow {
  sourceKey: string;
}

export interface RedirectRuleFields {
  brtqmArea: string;
  brtqmSourceUrl: string;
  brtqmTargetUrl: string;
  brtqmSourceKey: string;
  brtqmStatus?: RedirectRuleStatus;
}

export interface CreateOperation {
  kind: 'create';
  rowId: string;
  fields: RedirectRuleFields;
}

export interface UpdateOperation {
  kind: 'update';
  rowId: string;
  itemId: number;
  etag: string;
  fields: Partial<RedirectRuleFields>;
}

export interface RecycleOperation {
  kind: 'recycle';
  rowId: string;
  itemId: number;
}

export type SaveOperation = CreateOperation | UpdateOperation | RecycleOperation;

export type SaveFailureReason = 'conflict' | 'forbidden' | 'notFound' | 'error';

export interface SaveOperationResult {
  operation: SaveOperation;
  succeeded: boolean;
  failure?: SaveFailureReason;
  entry?: RedirectRuleEntry;
}

export type RowIssue =
  | { kind: 'required' }
  | { kind: 'invalidSourceUrl' }
  | { kind: 'duplicateInSave' }
  | { kind: 'duplicateInList'; area: string }
  | { kind: 'notSaved'; reason: SaveFailureReason };

export type TargetUrlIssue = 'required' | 'invalidTargetUrl';

export interface AreaInputValidation {
  areaNameMissing: boolean;
  targetUrlIssue?: TargetUrlIssue;
  noSourceUrls: boolean;
  rowIssues: Record<string, RowIssue>;
}

export type SaveResult =
  | { kind: 'invalid'; validation: AreaInputValidation }
  | { kind: 'nothingToSave' }
  | { kind: 'forbidden' }
  | { kind: 'concurrentChange' }
  | { kind: 'areaNameTaken'; existingArea: string }
  | { kind: 'duplicates'; rowIssues: Record<string, RowIssue> }
  | { kind: 'completed'; snapshot: AreaSnapshot; rows: SourceUrlRow[]; rowIssues: Record<string, RowIssue> };
