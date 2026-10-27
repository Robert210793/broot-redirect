import * as React from 'react';
import { ActionButton, IconButton, Label, Stack, Text, TextField } from '@fluentui/react';
import * as strings from 'BrtqmRedirectRulesWebPartStrings';
import { RowIssue, SourceUrlRow } from '../types';
import { rowIssueText } from './text';
import styles from './RedirectRulesEditor.module.scss';

export interface ISourceUrlTableProps {
  rows: SourceUrlRow[];
  rowIssues: Record<string, RowIssue>;
  disabled: boolean;
  onChange: (rowId: string, sourceUrl: string) => void;
  onRemove: (rowId: string) => void;
  onRestore: (rowId: string) => void;
  onAdd: () => void;
}

export const SourceUrlTable: React.FC<ISourceUrlTableProps> = (props) => (
  <Stack tokens={{ childrenGap: 4 }}>
    <Label>{strings.SourceUrlsLabel}</Label>
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
