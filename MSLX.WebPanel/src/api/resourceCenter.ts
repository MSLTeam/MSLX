import { request } from '@/utils/request';
import type { ResourceSearchFilter, ResourceVersionModel, ResourceSearchResult, ResourceModel } from '@/api/model/resourceCenter';

export * from '@/api/model/resourceCenter';

export async function searchResources(filter: ResourceSearchFilter) {
  return await request.post<ResourceSearchResult>({
    url: '/api/resource/search',
    data: filter
  });
}

export async function getResourceDetail(providerType: number, id: string) {
  return await request.get<ResourceModel>({
    url: `/api/resource/${providerType}/${id}`
  });
}

export async function getResourceVersions(providerType: number, id: string, gameVersion?: string, loader?: string) {
  return await request.get<ResourceVersionModel[]>({
    url: `/api/resource/${providerType}/${id}/versions`,
    params: { gameVersion, loader }
  });
}
