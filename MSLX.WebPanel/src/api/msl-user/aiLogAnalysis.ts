import { request } from '@/utils/request';
import { AIAnalysisResultModel, AIServiceUsageModel } from '@/api/msl-user/model/aiLogAnalysis';

export async function getAIServiceUsage(token: string) {
  return await request.get<AIServiceUsageModel>({
    url: `/api/tools/ai/usage`,
    baseURL: 'https://user.mslmc.net',
    headers: { Authorization: `Bearer ${token}` }
  });
}


export async function postAIAnalysis(token:string,mods:string,plugins:string,core:string,logs:string,model:string){
  if (mods === '') mods = null;
  if (plugins === '') plugins = null;
  return await request.post<AIAnalysisResultModel>({
    url: `/api/tools/ai/analysis`,
    baseURL: 'https://user.mslmc.net',
    headers: { Authorization: `Bearer ${token}` },
    data: { mods, plugins, core, logs, model,usemd:true },
    timeout: 300 * 1000,
  });
}
