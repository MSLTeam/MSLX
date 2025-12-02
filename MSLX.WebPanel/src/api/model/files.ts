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
