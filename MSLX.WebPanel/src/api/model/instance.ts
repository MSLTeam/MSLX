export interface CreateInstanceQucikModeModel {
  name: string;
  path: string;
  java: string;
  core: string;
  coreFileKey: string;
  coreUrl: string;
  coreSha256: string;
  minM: number;
  maxM: number;
  args: string;
}
