import { request } from '@/utils/request';
import type { ResourceSearchFilter, ResourceVersionModel, ResourceSearchResult, ResourceModel } from '@/api/model/resourceCenter';

export * from '@/api/model/resourceCenter';

export async function searchResources(filter: ResourceSearchFilter) {
  return await request.post<ResourceSearchResult>({
    url: '/api/resource/search',
    data: filter,
    timeout: 30000
  });
}

export async function getResourceDetail(providerType: number, id: string, useMirror: boolean = true) {
  return await request.get<ResourceModel>({
    url: `/api/resource/${providerType}/${id}`,
    params: { useMirror }
  });
}

export async function getResourceVersions(providerType: number, id: string, gameVersion?: string, loader?: string, useMirror: boolean = true) {
  return await request.get<ResourceVersionModel[]>({
    url: `/api/resource/${providerType}/${id}/versions`,
    params: { gameVersion, loader, useMirror }
  });
}
