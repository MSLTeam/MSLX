export interface DockerEnvStatusModel {
  available: boolean;
  clientVersion?: string;
  serverVersion?: string;
  osType?: string;
  inContainer: boolean;
  sockMounted: boolean;
  errorType?: 'notInstalled' | 'daemonUnreachable' | 'sockNotMounted' | 'permissionDenied' | 'unknown';
  errorMessage?: string;
}

export interface DockerImageUsageModel {
  instanceId: number;
  instanceName: string;
  configuredImage: string;
}

export interface DockerImageModel {
  repository: string;
  tag: string;
  reference: string;
  imageId: string;
  shortId: string;
  digest?: string;
  size: string;
  sizeBytes?: number;
  createdAt: string;
  isDangling: boolean;
  isMslxRuntime: boolean;
  usedBy: DockerImageUsageModel[];
}

export interface DockerPresetImageModel {
  pseudo: string;
  image: string;
  label: string;
  exists: boolean;
  size?: string;
}

export interface DockerImageDetailModel {
  imageId: string;
  repoTags: string[];
  repoDigests: string[];
  created?: string;
  architecture?: string;
  os?: string;
  size: number;
  workingDir?: string;
  env: string[];
  entrypoint: string[];
  cmd: string[];
  exposedPorts: string[];
  volumes: string[];
  labels: Record<string, string>;
  layers: string[];
  raw?: string;
}

export interface DockerPullTaskModel {
  taskId: string;
  status: 'pending' | 'processing' | 'success' | 'error';
  progress: number;
  message: string;
  image: string;
  logs: string[];
}

export interface DockerOperationResultModel {
  success: boolean;
  message: string;
  output?: string;
}

export interface DockerPullRequestModel {
  image: string;
  platform?: string;
}

export interface DockerImageDeleteRequestModel {
  reference: string;
  force?: boolean;
  noPrune?: boolean;
}

export interface DockerImageCheckUpdateItemModel {
  reference: string;
  hasUpdate: boolean;
  localDigest?: string;
  remoteDigest?: string;
  status: 'upToDate' | 'hasUpdate' | 'error';
  message?: string;
}
