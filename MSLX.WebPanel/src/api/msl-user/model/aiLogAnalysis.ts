export interface AIServiceUsageModel {
  code: number;
  data: {
    uid: number;
    today_usage: number;
    extra_tokens: number;
    max_per_day: number;
    last_use_time: number;
  };
}

export interface AIAnalysisResultModel {
  code: number;
  data: {
    content: string;
    tokens: number;
    rate: number;
  };
}

export interface AIAnalysisModelsListModel {
  code: number;
  data: ModelInfoModel[];
}

export interface ModelInfoModel{
  name: string;
  rate: number;
}
