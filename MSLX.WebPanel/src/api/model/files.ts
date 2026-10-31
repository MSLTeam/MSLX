export interface UploadInitResponse {
  uploadId: string;
  resumed?: boolean;
  receivedChunks?: number[];
}

export interface UploadFinishResponse {
  uploadId: string;
  size?: number;
  blake3?: string;
}

export interface UploadStatusResponse {
  uploadId: string;
  receivedChunks: number[];
  receivedCount: number;
  status: 'uploading' | string;
}

export interface UploadPackageCheckJarResponse {
  count: number;
  jars: string[];
  scripts?: string[];
  detectedRoot: string;
  metadata?: Record<string, any>;
  format?: 'zip' | 'mrpack' | string;
}

export interface FilesListModel{
  name: string;
  size: number;
  type: string;
  lastModified: string;
  permission: string;
}

export interface FilesListResponse {
  total: number;
  items: FilesListModel[];
}

export interface PluginsAndModsListModel{
  totalCount: number;
  activeCount: number;
  disableCount: number;
  jarFiles: string[];
  disableJarFiles: string[];
  clientJarFiles: string[];
}

export interface HostFileItem {
  name: string;
  path: string;
  type: 'folder' | 'file';
  size: number;
  lastModified: string;
}

export interface HostFsResponse {
  currentPath: string;
  parentPath: string | null;
  items: HostFileItem[];
}

export interface HostDriveItem {
  name: string;
  path: string;
  type: 'drive';
  volumeLabel: string;
  totalSize: number;
  availableFreeSpace: number;
}
