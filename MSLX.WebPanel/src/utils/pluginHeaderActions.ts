import { markRaw } from 'vue';
import type { Component } from 'vue';
import type { RouteLocationRaw } from 'vue-router';
import type { createPluginNodeContext } from './pluginNodeContext';

export const HEADER_ACTION_SLOT = 'header-action';

export type HeaderActionIcon = string | Component;
export type HeaderActionRole = 'admin' | 'user';

interface HeaderActionBase {
  id: string;
  label: string;
  icon?: HeaderActionIcon;
  roleCode?: HeaderActionRole[];
}

type HeaderPanel = { type: 'drawer' | 'floating'; component: Component };
type HeaderRoute = { type: 'route'; to: RouteLocationRaw };
type HeaderLink = { type: 'link'; href: string; target?: '_blank' | '_self' };
type HeaderCallback = { type: 'action'; run: (context: ReturnType<typeof createPluginNodeContext>) => void | Promise<void> };

export type HeaderActionLeaf = HeaderActionBase & (HeaderPanel | HeaderRoute | HeaderLink | HeaderCallback);

export type HeaderActionExtension = { slot: typeof HEADER_ACTION_SLOT; placement?: 'header' | 'more' } &
  (HeaderActionLeaf | (HeaderActionBase & { type: 'dropdown'; items: HeaderActionLeaf[] }));

interface HeaderRegistration {
  key: string;
  pluginId: string;
  nodeId: string;
}

export type RegisteredHeaderLeaf = HeaderActionLeaf & HeaderRegistration;
export type RegisteredHeaderPanel = HeaderActionBase & HeaderRegistration & HeaderPanel;
type RegisteredHeaderDropdown = HeaderActionBase & HeaderRegistration & { type: 'dropdown'; items: RegisteredHeaderLeaf[] };
export type RegisteredHeaderAction = (RegisteredHeaderLeaf | RegisteredHeaderDropdown) & { placement?: 'header' | 'more' };

export function isInternalHeaderPath(path: unknown): path is string {
  return typeof path === 'string' && path.startsWith('/') && !path.startsWith('//') && !/[\\\t\r\n]/.test(path);
}

function isInternalTarget(to: unknown): to is RouteLocationRaw {
  if (typeof to !== 'object' || !to) return isInternalHeaderPath(to);
  if ('path' in to) return isInternalHeaderPath(to.path);
  return 'name' in to && (typeof to.name === 'string' || typeof to.name === 'symbol');
}

function externalHref(value: unknown) {
  if (typeof value !== 'string' || !/^https?:\/\//i.test(value)) return;
  try {
    const url = new URL(value);
    if (url.hostname) return url.href;
  } catch {
    // A malformed link is skipped along with other invalid entries.
  }
}

function isComponent(value: unknown): value is Component {
  return typeof value === 'function' || (typeof value === 'object' && value !== null && !Array.isArray(value));
}

export function registerHeaderActions(
  extensions: unknown[], pluginId: string, nodeId: string, bind: (component: Component) => Component,
): RegisteredHeaderAction[] {
  function parse(value: unknown, parentKey?: string): RegisteredHeaderLeaf | RegisteredHeaderDropdown | undefined {
    if (typeof value !== 'object' || !value) return;
    const raw = value as Record<string, unknown>;
    const { id, label, icon, roleCode, type } = raw;
    if (typeof id !== 'string' || !id.trim() || typeof label !== 'string' || !label.trim()) return;
    if (roleCode !== undefined && (!Array.isArray(roleCode) || !roleCode.every(role => role === 'admin' || role === 'user'))) return;
    const key = JSON.stringify([parentKey ?? pluginId, id]);
    const base = { id, label, key, pluginId, nodeId, roleCode: roleCode as HeaderActionRole[] | undefined,
      icon: typeof icon === 'string' ? icon : isComponent(icon) ? markRaw(icon) : undefined };

    switch (type) {
      case 'drawer':
      case 'floating':
        if (isComponent(raw.component)) return { ...base, type, component: markRaw(bind(raw.component)) };
        break;
      case 'route':
        if (isInternalTarget(raw.to)) return { ...base, type, to: raw.to };
        break;
      case 'link': {
        const href = externalHref(raw.href);
        if (href) return { ...base, type, href, target: raw.target === '_self' ? '_self' : '_blank' };
        break;
      }
      case 'action':
        if (typeof raw.run === 'function') return { ...base, type, run: raw.run as HeaderCallback['run'] };
        break;
      case 'dropdown': {
        if (parentKey || !Array.isArray(raw.items)) break;
        const items = unique(raw.items.map(item => parse(item, key)))
          .filter((item): item is RegisteredHeaderLeaf => item.type !== 'dropdown');
        if (items.length) return { ...base, type, items };
        break;
      }
      default:
        break;
    }
  }

  function unique<T extends HeaderRegistration>(entries: (T | undefined)[]): T[] {
    const seen = new Set<string>();
    return entries.filter((entry): entry is T => {
      if (!entry || seen.has(entry.key)) return false;
      seen.add(entry.key);
      return true;
    });
  }

  return unique(extensions.map(value => {
    if (typeof value !== 'object' || !value || !('slot' in value) || value.slot !== HEADER_ACTION_SLOT) return;
    const item = parse(value);
    if (item && 'placement' in value && value.placement === 'more') return { ...item, placement: 'more' as const };
    return item;
  }));
}
