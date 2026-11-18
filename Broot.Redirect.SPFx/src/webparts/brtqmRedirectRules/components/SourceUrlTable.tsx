import * as React from 'react';
import { ActionButton, IconButton, Label, Stack, Text, TextField } from '@fluentui/react';
import * as strings from 'BrtqmRedirectRulesWebPartStrings';
import { commonStatus, effectiveStatus } from '../logic/RedirectRuleStatus';
import { RedirectRuleStatus, RowIssue, SourceUrlRow } from '../types';
import { CommentButton } from './CommentButton';
import { StatusDropdown } from './StatusDropdown';
import { rowIssueText } from './text';
import styles from './RedirectRulesEditor.module.scss';

export interface ISourceUrlTableProps {
  rows: SourceUrlRow[];
  rowIssues: Record<string, RowIssue>;
  storedStatuses: Record<number, string>;
  commentCounts: Record<number, number>;
  disabled: boolean;
  onChange: (rowId: string, sourceUrl: string) => void;
  onStatusChange: (rowId: string, status: RedirectRuleStatus) => void;
  onStatusForAll: (status: RedirectRuleStatus) => void;
  onOpenComments: (row: SourceUrlRow) => void;
  onRemove: (rowId: string) => void;
  onRestore: (rowId: string) => void;
  onAdd: () => void;
}

export const SourceUrlTable: React.FC<ISourceUrlTableProps> = (props) => {
  const storedStatus = (row: SourceUrlRow): string | undefined =>
    (row.itemId !== undefined ? props.storedStatuses[row.itemId] : undefined);
  const displayedStatus = (row: SourceUrlRow): RedirectRuleStatus =>
    effectiveStatus(row.status !== undefined ? row.status : storedStatus(row));
  const activeRows = props.rows.filter((row) => !row.removed);

  return (
    <Stack tokens={{ childrenGap: 4 }}>
      <Label>{strings.SourceUrlsLabel}</Label>
      {activeRows.length > 0 && (
        <StatusDropdown
          label={strings.StatusForAllLabel}
          ariaLabel={strings.StatusForAllLabel}
          selected={commonStatus(props.rows, storedStatus)}
          placeholder={strings.StatusForAllPlaceholder}
          disabled={props.disabled}
          className={styles.statusForAll}
          onChange={props.onStatusForAll}
        />
      )}
      {props.rows.map((row) => {
        const issue = rowIssueText(props.rowIssues[row.rowId]);

        return row.removed ? (
          <Stack key={row.rowId} horizontal verticalAlign="center" tokens={{ childrenGap: 8 }} className={styles.row}>
            <Stack.Item grow className={styles.removed}>
              <Text>{row.sourceUrl}</Text>
              <Text variant="small" block>{issue || strings.RemovedHint}</Text>
            </Stack.Item>
            <IconButton
              iconProps={{ iconName: 'Undo' }}
              title={strings.RestoreSourceUrl}
              ariaLabel={strings.RestoreSourceUrl}
              disabled={props.disabled}
              onClick={() => props.onRestore(row.rowId)}
            />
          </Stack>
        ) : (
          <Stack key={row.rowId} horizontal verticalAlign="start" tokens={{ childrenGap: 8 }} className={styles.row}>
            <Stack.Item grow>
              <TextField
                value={row.sourceUrl}
                placeholder={strings.SourceUrlPlaceholder}
                ariaLabel={strings.SourceUrlsLabel}
                errorMessage={issue}
                disabled={props.disabled}
                onChange={(_, value) => props.onChange(row.rowId, value || '')}
              />
            </Stack.Item>
            <StatusDropdown
              ariaLabel={strings.StatusLabel}
              selected={displayedStatus(row)}
              disabled={props.disabled}
              className={styles.status}
              onChange={(status) => props.onStatusChange(row.rowId, status)}
            />
            {row.itemId !== undefined && (
              <CommentButton count={props.commentCounts[row.itemId]} onClick={() => props.onOpenComments(row)} />
            )}
            <IconButton
              iconProps={{ iconName: 'Delete' }}
              title={strings.RemoveSourceUrl}
              ariaLabel={strings.RemoveSourceUrl}
              disabled={props.disabled}
              onClick={() => props.onRemove(row.rowId)}
            />
          </Stack>
        );
      })}
      <ActionButton iconProps={{ iconName: 'Add' }} disabled={props.disabled} onClick={props.onAdd}>
        {strings.AddSourceUrl}
      </ActionButton>
    </Stack>
  );
};
