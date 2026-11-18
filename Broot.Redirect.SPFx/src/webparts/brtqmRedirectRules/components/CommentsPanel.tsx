import * as React from 'react';
import {
  MessageBar,
  MessageBarType,
  Panel,
  PanelType,
  PrimaryButton,
  Spinner,
  SpinnerSize,
  Stack,
  Text,
  TextField
} from '@fluentui/react';
import * as strings from 'BrtqmRedirectRulesWebPartStrings';
import { commentTimestamp } from '../logic/CommentText';
import { IRedirectRulesService } from '../services/RedirectRulesService';
import { CommentsLoadResult } from '../types';
import styles from './RedirectRulesEditor.module.scss';

export interface ICommentsPanelProps {
  service: IRedirectRulesService;
  itemId: number;
  sourceUrl: string;
  onDismiss: () => void;
  onCommentsLoaded: (itemId: number, count: number) => void;
  onCommentAdded: (itemId: number) => void;
}

function loadFailureText(result: CommentsLoadResult): string | undefined {
  switch (result.kind) {
    case 'disabled':
      return strings.CommentsDisabled;
    case 'forbidden':
      return strings.CommentsForbidden;
    case 'failed':
      return strings.CommentsLoadFailed;
    default:
      return undefined;
  }
}

export const CommentsPanel: React.FC<ICommentsPanelProps> = (props) => {
  const { service, itemId, onCommentsLoaded } = props;
  const [result, setResult] = React.useState<CommentsLoadResult | undefined>(undefined);
  const [text, setText] = React.useState<string>('');
  const [sending, setSending] = React.useState<boolean>(false);
  const [sendError, setSendError] = React.useState<string | undefined>(undefined);

  const load = React.useCallback(async (): Promise<void> => {
    const loaded = await service.getComments(itemId);
    setResult(loaded);
    if (loaded.kind === 'loaded') {
      onCommentsLoaded(itemId, loaded.comments.length);
    }
  }, [service, itemId, onCommentsLoaded]);

  React.useEffect(() => {
    setResult(undefined);
    load().catch(() => setResult({ kind: 'failed' }));
  }, [load]);

  const onSend = (): void => {
    const trimmed = text.trim();
    if (trimmed.length === 0) {
      return;
    }

    setSending(true);
    setSendError(undefined);
    service.addComment(itemId, trimmed)
      .then(async (outcome) => {
        if (outcome === 'disabled') {
          setResult({ kind: 'disabled' });
          return;
        }
        if (outcome !== 'added') {
          setSendError(outcome === 'forbidden' ? strings.CommentsForbidden : strings.CommentSendFailed);
          return;
        }
        setText('');
        props.onCommentAdded(itemId);
        await load();
      })
      .catch(() => setSendError(strings.CommentSendFailed))
      .then(() => setSending(false), () => setSending(false));
  };

  const failure = result ? loadFailureText(result) : undefined;

  return (
    <Panel
      isOpen
      type={PanelType.medium}
      headerText={strings.CommentsPanelTitle}
      closeButtonAriaLabel={strings.CommentsPanelTitle}
      onDismiss={props.onDismiss}
    >
      <Stack tokens={{ childrenGap: 12 }}>
        <Text className={styles.panelSourceUrl}>{props.sourceUrl}</Text>
        {!result && <Spinner size={SpinnerSize.medium} label={strings.CommentsLoading} />}
        {failure && (
          <MessageBar messageBarType={result && result.kind === 'disabled' ? MessageBarType.info : MessageBarType.error} isMultiline>
            {failure}
          </MessageBar>
        )}
        {result && result.kind === 'loaded' && (
          <>
            {result.comments.length === 0 && <Text>{strings.CommentsEmpty}</Text>}
            {result.comments.map((comment) => (
              <Stack key={comment.id} className={styles.comment} tokens={{ childrenGap: 2 }}>
                <Text variant="small">
                  <strong>{comment.authorName}</strong> · {commentTimestamp(comment)}
                </Text>
                <Text className={styles.commentText}>{comment.text}</Text>
              </Stack>
            ))}
            {sendError && (
              <MessageBar messageBarType={MessageBarType.error} onDismiss={() => setSendError(undefined)}>
                {sendError}
              </MessageBar>
            )}
            <TextField
              label={strings.CommentInputLabel}
              multiline
              autoAdjustHeight
              value={text}
              disabled={sending}
              onChange={(_, value) => setText(value || '')}
            />
            <Stack horizontal verticalAlign="center" tokens={{ childrenGap: 8 }}>
              <PrimaryButton text={strings.CommentSend} disabled={sending || text.trim().length === 0} onClick={onSend} />
              {sending && <Spinner size={SpinnerSize.small} />}
            </Stack>
          </>
        )}
      </Stack>
    </Panel>
  );
};
