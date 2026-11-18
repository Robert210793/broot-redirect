import * as strings from 'BrtqmRedirectRulesWebPartStrings';
import { LibraryFailure, RowIssue, SaveFailureReason, TargetUrlIssue, UrlMappingStep } from '../types';

export function libraryFailureText(reason: LibraryFailure): string {
  switch (reason) {
    case 'notFound':
      return strings.LibraryNotFound;
    case 'forbidden':
      return strings.LibraryForbidden;
    case 'conflict':
      return strings.LibraryConflict;
    default:
      return strings.LibraryFailed;
  }
}

export function urlMappingStepText(step: UrlMappingStep): string {
  switch (step) {
    case 'read':
      return strings.UrlMappingStepRead;
    case 'library':
      return strings.UrlMappingStepLibrary;
    case 'archive':
      return strings.UrlMappingStepArchive;
    default:
      return strings.UrlMappingStepReplace;
  }
}

export function format(template: string, ...values: string[]): string {
  return template.replace(/\{(\d+)\}/g, (placeholder: string, index: string) => {
    const value = values[Number(index)];
    return value !== undefined ? value : placeholder;
  });
}

function notSavedText(reason: SaveFailureReason): string {
  switch (reason) {
    case 'conflict':
      return strings.NotSavedConflict;
    case 'forbidden':
      return strings.NotSavedForbidden;
    case 'notFound':
      return strings.NotSavedNotFound;
    default:
      return strings.NotSavedError;
  }
}

export function rowIssueText(issue: RowIssue | undefined): string | undefined {
  if (!issue) {
    return undefined;
  }

  switch (issue.kind) {
    case 'required':
      return strings.SourceUrlRequired;
    case 'invalidSourceUrl':
      return strings.SourceUrlInvalid;
    case 'duplicateInSave':
      return strings.SourceUrlDuplicateInSave;
    case 'duplicateInList':
      return format(strings.SourceUrlDuplicateInList, issue.area);
    default:
      return notSavedText(issue.reason);
  }
}

export function targetUrlIssueText(issue: TargetUrlIssue | undefined): string | undefined {
  if (!issue) {
    return undefined;
  }

  return issue === 'required' ? strings.TargetUrlRequired : strings.TargetUrlInvalid;
}
