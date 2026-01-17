export interface UpdateLogResponseModel {
  code: number;
  message: string;
  data: UpdateLogDetailModel[];
}

export interface UpdateLogDetailModel {
  changes: string;
  version: string;
  time: string;
}
