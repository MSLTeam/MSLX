import { deleteUpload } from '@/api/files';
import {
  deleteUploadResumeRecord,
  getUploadResumeRecords,
  type UploadResumeRecord,
} from '@/utils/uploadResume';
import { getPageNodeId } from '@/utils/nodeTarget';

export const CREATE_DRAFT_TTL_MS = 2 * 60 * 60 * 1000;

export type CreateInstanceDraftMode = 'quick' | 'package' | 'bedrock' | 'mcdr' | 'custom';

export interface StoredCreateInstanceDraft<T = Record<string, unknown>> {
  mode: CreateInstanceDraftMode;
  updatedAt: number;
  state?: T;
  [key: string]: unknown;
}

export function getCreateInstanceDraftKey(mode: CreateInstanceDraftMode, nodeId = getPageNodeId()): string {
  // 保留整合包模式已有的键，避免丢失现有的刷新恢复数据。
  return mode === 'package' ? `mslx-create-package-draft:${nodeId}` : `mslx-create-${mode}-draft:${nodeId}`;
}

export function readCreateInstanceDraft<T = Record<string, unknown>>(
  mode: CreateInstanceDraftMode,
  nodeId = getPageNodeId(),
): StoredCreateInstanceDraft<T> | null {
  try {
    const raw = localStorage.getItem(getCreateInstanceDraftKey(mode, nodeId));
    if (!raw) return null;
    const draft = JSON.parse(raw) as StoredCreateInstanceDraft<T>;
    if (draft.mode !== mode || typeof draft.updatedAt !== 'number') return null;
    if (Date.now() - draft.updatedAt > CREATE_DRAFT_TTL_MS) {
      localStorage.removeItem(getCreateInstanceDraftKey(mode, nodeId));
      return null;
    }
    return draft;
  } catch {
    return null;
  }
}

export function saveCreateInstanceDraft(
  mode: CreateInstanceDraftMode,
  state: Record<string, unknown>,
  nodeId = getPageNodeId(),
): void {
  try {
    localStorage.setItem(
      getCreateInstanceDraftKey(mode, nodeId),
      JSON.stringify({ mode, updatedAt: Date.now(), state }),
    );
  } catch {
    // 本地存储不可用时不影响创建流程。
  }
}

export function removeCreateInstanceDraft(mode: CreateInstanceDraftMode, nodeId = getPageNodeId()): void {
  try {
    localStorage.removeItem(getCreateInstanceDraftKey(mode, nodeId));
  } catch {
    // 忽略本地存储清理失败。
  }
}

export function clearCreateInstanceDrafts(nodeId = getPageNodeId()): void {
  for (const mode of ['quick', 'package', 'bedrock', 'mcdr', 'custom'] as CreateInstanceDraftMode[]) {
    removeCreateInstanceDraft(mode, nodeId);
  }
}

export async function clearCreateInstanceUploads(
  nodeId = getPageNodeId(),
  kind?: 'package' | 'core',
): Promise<void> {
  const records = await getUploadResumeRecords(nodeId).catch(() => [] as UploadResumeRecord[]);
  await Promise.all(
    records
      .filter(
        (record) =>
          kind
            ? record.kind
              ? record.kind === kind
              : kind === 'package'
                ? /\.(?:zip|mrpack)$/i.test(record.fileName)
                : /\.jar$/i.test(record.fileName)
            : record.kind === 'package' ||
              record.kind === 'core' ||
              /\.(?:zip|mrpack|jar)$/i.test(record.fileName),
      )
      .map(async (record) => {
        await deleteUpload(record.uploadId, nodeId).catch(() => {});
        await deleteUploadResumeRecord(record.key).catch(() => {});
      }),
  );
}
