import { inject, onScopeDispose, type InjectionKey } from 'vue';
import { getPageNodeId } from '@/utils/nodeTarget';

export interface InstanceSettingsTarget {
  instanceId: number;
  nodeId: string;
  isCurrent: () => boolean;
}

export const instanceSettingsTargetKey: InjectionKey<() => InstanceSettingsTarget> = Symbol('instanceSettingsTarget');

export function useInstanceSettingsTarget() {
  const target = inject(instanceSettingsTargetKey)!();
  let active = true;
  onScopeDispose(() => { active = false; });
  return { ...target, isCurrent: () => active && target.isCurrent() && getPageNodeId() === target.nodeId };
}
