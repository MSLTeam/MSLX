export interface CreateInstanceQucikModeModel {
  name: string;
  path: string;
  java: string;
  core: string;
  packageFileKey: string;
  coreFileKey: string;
  coreUrl: string;
  coreSha256: string;
  minM: number;
  maxM: number;
  args: string;
}

export interface InstanceListModel{
  id:number;
  name:string;
  basePath:string;
  java:string;
  core:string;
  icon:string;
  status:boolean;
}
