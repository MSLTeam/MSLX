import {
  DockerEnvStatusModel,
  DockerImageDeleteRequestModel,
  DockerImageDetailModel,
  DockerImageModel,
  DockerOperationResultModel,
  DockerPresetImageModel,
  DockerPullRequestModel,
  DockerPullTaskModel,
} from '@/api/model/docker';
import { request } from '@/utils/request';

// Docker 环境探测
export async function getDockerStatus(refresh = false) {
  return await request.get<DockerEnvStatusModel>({
    url: '/api/docker/status',
    params: { refresh },
    timeout: 30 * 1000,
  });
}

// 本地镜像列表
export async function getDockerImages(dangling = true) {
  return await request.get<DockerImageModel[]>({
    url: '/api/docker/images',
    params: { dangling },
    timeout: 60 * 1000, // 镜像多时 docker 响应会偏慢
  });
}

// 内置运行时镜像清单
export async function getDockerPresetImages() {
  return await request.get<DockerPresetImageModel[]>({
    url: '/api/docker/presets',
    timeout: 60 * 1000,
  });
}

// 镜像详情
export async function getDockerImageDetail(reference: string) {
  return await request.get<DockerImageDetailModel>({
    url: '/api/docker/images/inspect',
    params: { reference },
    timeout: 30 * 1000,
  });
}

// 提交拉取任务
export async function postPullDockerImage(data: DockerPullRequestModel) {
  return await request.post<{ taskId: string }>({
    url: '/api/docker/images/pull',
    data,
    timeout: 30 * 1000,
  });
}

// 查询拉取进度
export async function getDockerPullTask(taskId: string) {
  return await request.get<DockerPullTaskModel>({
    url: `/api/docker/task/pull/${taskId}`,
  });
}

// 删除镜像
export async function postDeleteDockerImage(data: DockerImageDeleteRequestModel) {
  return await request.post<DockerOperationResultModel>({
    url: '/api/docker/images/delete',
    data,
    timeout: 120 * 1000,
  });
}

// 清理悬空镜像
export async function postPruneDockerImages() {
  return await request.post<DockerOperationResultModel>({
    url: '/api/docker/images/prune',
    timeout: 5 * 60 * 1000,
  });
}

// 添加标签
export async function postTagDockerImage(source: string, target: string) {
  return await request.post<DockerOperationResultModel>({
    url: '/api/docker/images/tag',
    data: { source, target },
    timeout: 30 * 1000,
  });
}
