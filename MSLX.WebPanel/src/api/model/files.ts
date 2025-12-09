export interface UploadInitResponse {
  uploadId: string;
}

export interface UploadFinishResponse {
  uploadId: string;
}

export interface UploadPackageCheckJarResponse {
  count: number;
  jars: string[];
  detectedRoot: string;
}

export interface FilesListModel{
  name: string;
  size: number;
  type: string;
  lastModified: string;
  permission: string;
}
