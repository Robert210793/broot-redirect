declare interface IBrtqmRedirectRulesWebPartStrings {
  PropertyPaneDescription: string;
  Title: string;
  Loading: string;
  ListMissing: string;
  LoadFailed: string;
  AreaLabel: string;
  AreaPlaceholder: string;
  NewAreaOption: string;
  AreaNameLabel: string;
  AreaNameDescription: string;
  TargetUrlLabel: string;
  TargetUrlDescription: string;
  SourceUrlsLabel: string;
  SourceUrlPlaceholder: string;
  AddSourceUrl: string;
  RemoveSourceUrl: string;
  RestoreSourceUrl: string;
  RemovedHint: string;
  Save: string;
  Reload: string;
  Saving: string;
  AreaNameRequired: string;
  TargetUrlRequired: string;
  TargetUrlInvalid: string;
  SourceUrlsRequired: string;
  SourceUrlRequired: string;
  SourceUrlInvalid: string;
  SourceUrlDuplicateInSave: string;
  SourceUrlDuplicateInList: string;
  NotSavedConflict: string;
  NotSavedForbidden: string;
  NotSavedNotFound: string;
  NotSavedError: string;
  InvalidInput: string;
  NothingToSave: string;
  Forbidden: string;
  ConcurrentChange: string;
  AreaNameTaken: string;
  Duplicates: string;
  Saved: string;
  AreaRemoved: string;
  PartiallySaved: string;
  AreaNoLongerExists: string;
  UnexpectedError: string;
  ExportCsv: string;
  Exporting: string;
  Exported: string;
}

declare module 'BrtqmRedirectRulesWebPartStrings' {
  const strings: IBrtqmRedirectRulesWebPartStrings;
  export = strings;
}
