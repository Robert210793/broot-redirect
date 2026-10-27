import * as React from 'react';
import {
  DefaultButton,
  MessageBar,
  MessageBarType,
  PrimaryButton,
  Spinner,
  SpinnerSize,
  Stack,
  Text,
  TextField
} from '@fluentui/react';
import * as strings from 'BrtqmRedirectRulesWebPartStrings';
import { areaCompareValue } from '../logic/AreaName';
import { buildAreaSnapshot } from '../logic/AreaSavePlanner';
import { AreaSaveCoordinator } from '../services/AreaSaveCoordinator';
import { IRedirectRulesService } from '../services/RedirectRulesService';
import { AreaInputValidation, AreaOption, AreaSnapshot, RowIssue, SaveResult, SourceUrlRow } from '../types';
import { AreaPicker, NEW_AREA_KEY } from './AreaPicker';
import { SourceUrlTable } from './SourceUrlTable';
import { format, targetUrlIssueText } from './text';
import styles from './RedirectRulesEditor.module.scss';

export interface IRedirectRulesEditorProps {
  service: IRedirectRulesService;
  coordinator: AreaSaveCoordinator;
}

type LoadState = 'loading' | 'listMissing' | 'failed' | 'ready';

interface Feedback {
  type: MessageBarType;
  text: string;
  details?: string[];
}

const NO_VALIDATION_ISSUES: AreaInputValidation = { areaNameMissing: false, noSourceUrls: false, rowIssues: {} };

let rowCounter: number = 0;

function newRow(): SourceUrlRow {
  rowCounter += 1;
  return { rowId: `new-${rowCounter}`, sourceUrl: '', removed: false };
}

function rowsFromSnapshot(snapshot: AreaSnapshot): SourceUrlRow[] {
  return snapshot.entries.map((entry) => ({ rowId: `item-${entry.id}`, itemId: entry.id, sourceUrl: entry.sourceUrl, removed: false }));
}

function errorText(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

function rejectionFeedback(result: SaveResult): Feedback {
  switch (result.kind) {
    case 'invalid':
      return {
        type: MessageBarType.error,
        text: strings.InvalidInput,
        details: result.validation.noSourceUrls ? [strings.SourceUrlsRequired] : undefined
      };
    case 'nothingToSave':
      return { type: MessageBarType.info, text: strings.NothingToSave };
    case 'forbidden':
      return { type: MessageBarType.error, text: strings.Forbidden };
    case 'concurrentChange':
      return { type: MessageBarType.error, text: strings.ConcurrentChange };
    case 'areaNameTaken':
      return { type: MessageBarType.error, text: format(strings.AreaNameTaken, result.existingArea) };
    default:
      return { type: MessageBarType.error, text: strings.Duplicates };
  }
}

export const RedirectRulesEditor: React.FC<IRedirectRulesEditorProps> = ({ service, coordinator }) => {
  const [loadState, setLoadState] = React.useState<LoadState>('loading');
  const [areas, setAreas] = React.useState<AreaOption[]>([]);
  const [selectedKey, setSelectedKey] = React.useState<string | undefined>(undefined);
  const [expected, setExpected] = React.useState<AreaSnapshot | undefined>(undefined);
  const [areaName, setAreaName] = React.useState<string>('');
  const [targetUrl, setTargetUrl] = React.useState<string>('');
  const [rows, setRows] = React.useState<SourceUrlRow[]>([]);
  const [validation, setValidation] = React.useState<AreaInputValidation>(NO_VALIDATION_ISSUES);
  const [rowIssues, setRowIssues] = React.useState<Record<string, RowIssue>>({});
  const [feedback, setFeedback] = React.useState<Feedback | undefined>(undefined);
  const [busy, setBusy] = React.useState<boolean>(false);

  const resetForm = (): void => {
    setExpected(undefined);
    setAreaName('');
    setTargetUrl('');
    setRows([]);
    setValidation(NO_VALIDATION_ISSUES);
    setRowIssues({});
  };

  const showArea = (snapshot: AreaSnapshot): void => {
    setExpected(snapshot);
    setAreaName(snapshot.area.trim());
    setTargetUrl(snapshot.targetUrl);
    setRows(rowsFromSnapshot(snapshot));
    setValidation(NO_VALIDATION_ISSUES);
    setRowIssues({});
  };

  const loadAreas = async (): Promise<AreaOption[]> => {
    const loaded = await service.getAreas();
    setAreas(loaded);
    return loaded;
  };

  const loadArea = async (key: string, options: AreaOption[]): Promise<void> => {
    const option = options.filter((candidate) => candidate.key === key)[0];
    const entries = option ? await service.getAreaEntries(option.name) : [];

    if (entries.length === 0) {
      setSelectedKey(undefined);
      resetForm();
      setFeedback({ type: MessageBarType.info, text: strings.AreaNoLongerExists });
      return;
    }

    setSelectedKey(key);
    showArea(buildAreaSnapshot(entries, option.name, ''));
  };

  const run = (work: () => Promise<void>): void => {
    setBusy(true);
    work().then(
      () => setBusy(false),
      (error: unknown) => {
        setFeedback({ type: MessageBarType.error, text: format(strings.UnexpectedError, errorText(error)) });
        setBusy(false);
      }
    );
  };

  React.useEffect(() => {
    let active = true;

    (async (): Promise<void> => {
      if (!(await service.listExists())) {
        if (active) {
          setLoadState('listMissing');
        }
        return;
      }
      const loaded = await service.getAreas();
      if (active) {
        setAreas(loaded);
        setLoadState('ready');
      }
    })().catch((error: unknown) => {
      if (active) {
        setFeedback({ type: MessageBarType.error, text: format(strings.UnexpectedError, errorText(error)) });
        setLoadState('failed');
      }
    });

    return () => { active = false; };
  }, [service]);

  const onSelect = (key: string): void => {
    setFeedback(undefined);

    if (key === NEW_AREA_KEY) {
      setSelectedKey(NEW_AREA_KEY);
      resetForm();
      setRows([newRow()]);
      return;
    }

    run(() => loadArea(key, areas));
  };

  const onReload = (): void => {
    setFeedback(undefined);
    run(async () => {
      const loaded = await loadAreas();

      if (selectedKey === undefined || (selectedKey === NEW_AREA_KEY && !expected)) {
        resetForm();
        if (selectedKey === NEW_AREA_KEY) {
          setRows([newRow()]);
        }
        return;
      }

      await loadArea(selectedKey, loaded);
    });
  };

  const onSave = (): void => {
    setFeedback(undefined);
    run(async () => {
      const result = await coordinator.save({ expected, areaName, targetUrl, rows });

      if (result.kind !== 'completed') {
        setValidation(result.kind === 'invalid' ? result.validation : NO_VALIDATION_ISSUES);
        setRowIssues(result.kind === 'invalid' ? result.validation.rowIssues : result.kind === 'duplicates' ? result.rowIssues : {});
        setFeedback(rejectionFeedback(result));
        return;
      }

      const failedRows = result.rows.filter((row) => result.rowIssues[row.rowId] !== undefined);
      setValidation(NO_VALIDATION_ISSUES);
      setRowIssues(result.rowIssues);
      setRows(result.rows);
      await loadAreas();

      if (result.snapshot.entries.length === 0) {
        setExpected(undefined);
        if (failedRows.length === 0) {
          setSelectedKey(undefined);
          resetForm();
          setFeedback({ type: MessageBarType.success, text: strings.AreaRemoved });
          return;
        }
      } else {
        setExpected(result.snapshot);
        setSelectedKey(areaCompareValue(areaName));
      }

      setFeedback(failedRows.length > 0
        ? { type: MessageBarType.warning, text: strings.PartiallySaved, details: failedRows.map((row) => row.sourceUrl) }
        : { type: MessageBarType.success, text: strings.Saved });
    });
  };

  const updateRows = (update: (current: SourceUrlRow[]) => SourceUrlRow[]): void => setRows(update);

  if (loadState === 'loading') {
    return <Spinner size={SpinnerSize.large} label={strings.Loading} />;
  }

  if (loadState === 'listMissing') {
    return <MessageBar messageBarType={MessageBarType.error}>{strings.ListMissing}</MessageBar>;
  }

  if (loadState === 'failed') {
    return <MessageBar messageBarType={MessageBarType.error}>{feedback ? feedback.text : strings.LoadFailed}</MessageBar>;
  }

  return (
    <Stack className={styles.editor} tokens={{ childrenGap: 12 }}>
      <Text variant="xLarge">{strings.Title}</Text>
      {feedback && (
        <MessageBar messageBarType={feedback.type} isMultiline onDismiss={() => setFeedback(undefined)}>
          {feedback.text}
          {feedback.details && (
            <ul className={styles.details}>
              {feedback.details.map((detail, index) => <li key={index}>{detail}</li>)}
            </ul>
          )}
        </MessageBar>
      )}
      <AreaPicker areas={areas} selectedKey={selectedKey} disabled={busy} onSelect={onSelect} />
      {selectedKey !== undefined && (
        <>
          <TextField
            label={strings.AreaNameLabel}
            required
            value={areaName}
            description={expected ? strings.AreaNameDescription : undefined}
            errorMessage={validation.areaNameMissing ? strings.AreaNameRequired : undefined}
            disabled={busy}
            onChange={(_, value) => setAreaName(value || '')}
          />
          <TextField
            label={strings.TargetUrlLabel}
            required
            value={targetUrl}
            description={strings.TargetUrlDescription}
            errorMessage={targetUrlIssueText(validation.targetUrlIssue)}
            disabled={busy}
            onChange={(_, value) => setTargetUrl(value || '')}
          />
          <SourceUrlTable
            rows={rows}
            rowIssues={rowIssues}
            disabled={busy}
            onChange={(rowId, sourceUrl) => updateRows((current) => current.map((row) => (row.rowId === rowId ? { ...row, sourceUrl } : row)))}
            onRemove={(rowId) => updateRows((current) => current
              .filter((row) => row.rowId !== rowId || row.itemId !== undefined)
              .map((row) => (row.rowId === rowId ? { ...row, removed: true } : row)))}
            onRestore={(rowId) => updateRows((current) => current.map((row) => (row.rowId === rowId ? { ...row, removed: false } : row)))}
            onAdd={() => updateRows((current) => current.concat(newRow()))}
          />
        </>
      )}
      <Stack horizontal verticalAlign="center" tokens={{ childrenGap: 8 }}>
        <PrimaryButton text={strings.Save} disabled={busy || selectedKey === undefined} onClick={onSave} />
        <DefaultButton text={strings.Reload} disabled={busy} onClick={onReload} />
        {busy && <Spinner size={SpinnerSize.small} label={strings.Saving} labelPosition="right" />}
      </Stack>
    </Stack>
  );
};
