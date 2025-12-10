export interface CommitHistoryItem {
  commitId: string;
  commitTime: string;
  commitAuthor: string;
  commitMsg: string;
}

export interface BuildInfoModel {
  version: string;
  buildTime: string;
  commitId: string;
  commitMsg: string;
  commitAuthor: string;
  dependencies: Record<string, string>;
  devDependencies: Record<string, string>;
  history: CommitHistoryItem[];
}
