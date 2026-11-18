import * as React from 'react';
import { ActionButton } from '@fluentui/react';
import * as strings from 'BrtqmRedirectRulesWebPartStrings';
import { format } from './text';

export interface ICommentButtonProps {
  count: number | undefined;
  onClick: () => void;
}

export const CommentButton: React.FC<ICommentButtonProps> = ({ count, onClick }) => {
  const label = count !== undefined ? format(strings.CommentsWithCount, String(count)) : strings.Comments;

  return (
    <ActionButton iconProps={{ iconName: 'Comment' }} title={label} ariaLabel={label} onClick={onClick}>
      {count !== undefined ? String(count) : ''}
    </ActionButton>
  );
};
