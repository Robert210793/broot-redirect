import { RedirectRuleComment } from '../types';
import { formatDateTime } from './RedirectRulesCsv';

export interface CommentMention {
  name?: string;
}

const MENTION_PLACEHOLDER: RegExp = /@mention\{(\d+)\}/g;

export function commentDisplayText(text: string, mentions: CommentMention[] | undefined): string {
  const resolved = mentions || [];

  return (text || '').replace(MENTION_PLACEHOLDER, (placeholder: string, index: string) => {
    const mention = resolved[Number(index)];
    return mention && mention.name ? `@${mention.name}` : placeholder;
  });
}

export function commentTimestamp(comment: RedirectRuleComment): string {
  return formatDateTime(comment.created);
}
