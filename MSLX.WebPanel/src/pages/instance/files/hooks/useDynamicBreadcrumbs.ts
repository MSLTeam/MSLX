import { computed, ref, type Ref } from 'vue';
import { useElementSize } from '@vueuse/core';

export interface BreadcrumbItem {
  name: string;
  path: string;
}

export interface DynamicBreadcrumbsResult {
  isFolded: boolean;
  headCrumbs: BreadcrumbItem[];
  foldedCrumbs: BreadcrumbItem[];
  tailCrumbs: BreadcrumbItem[];
}

let measurementCanvas: HTMLCanvasElement | null = null;
let measurementCtx: CanvasRenderingContext2D | null = null;

function getTextWidth(
  text: string,
  font = '14px -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif',
): number {
  if (typeof document === 'undefined') return text.length * 8;
  if (!measurementCanvas) {
    measurementCanvas = document.createElement('canvas');
    measurementCtx = measurementCanvas.getContext('2d');
  }
  if (measurementCtx) {
    measurementCtx.font = font;
    return measurementCtx.measureText(text).width;
  }
  return text.length * 8;
}

const estimateCrumbWidth = (
  crumb: BreadcrumbItem,
  isRoot: boolean,
  isLast: boolean,
  maxItemWidth: number,
): number => {
  if (isRoot) {
    // 根目录：图标(16) + 间距(4) + 文字 "根目录"(约42) + 分隔符(24) = 86px
    return isLast ? 62 : 86;
  }
  const rawTextWidth = getTextWidth(crumb.name);
  const textWidth = Math.min(rawTextWidth, maxItemWidth);
  const separatorWidth = isLast ? 0 : 24;
  return textWidth + 8 + separatorWidth;
};

const ELLIPSIS_WIDTH = 44; // 横向省略号图标(~20px) + 后续分隔符(~24px)

export function useDynamicBreadcrumbs(
  currentPath: Ref<string>,
  isMobile: Ref<boolean>,
  screenWidth: Ref<number>,
) {
  const breadcrumbContainerRef = ref<HTMLElement | null>(null);
  const { width: containerWidth } = useElementSize(breadcrumbContainerRef);

  const fullBreadcrumbs = computed<BreadcrumbItem[]>(() => {
    const parts = currentPath.value.split('/').filter((p) => p);
    const crumbs: BreadcrumbItem[] = [{ name: '根目录', path: '' }];
    let accumPath = '';
    parts.forEach((part) => {
      accumPath = accumPath ? `${accumPath}/${part}` : part;
      crumbs.push({ name: part, path: accumPath });
    });
    return crumbs;
  });

  const dynamicBreadcrumbs = computed<DynamicBreadcrumbsResult>(() => {
    const crumbs = fullBreadcrumbs.value;
    const n = crumbs.length;
    if (n <= 2) {
      return { isFolded: false, headCrumbs: crumbs, foldedCrumbs: [], tailCrumbs: [] };
    }

    const maxItemWidth = isMobile.value ? 80 : 150;
    // 容器实时可用宽度；挂载前若尚未测得宽度则依据屏幕宽度预估
    const availWidth =
      containerWidth.value > 0
        ? containerWidth.value
        : screenWidth.value - (isMobile.value ? 450 : 750);

    // 1. 检查是否能容纳全部路径（宽屏空间充裕时直接全展开）
    let totalWidth = 0;
    for (let i = 0; i < n; i++) {
      totalWidth += estimateCrumbWidth(crumbs[i], i === 0, i === n - 1, maxItemWidth);
    }
    if (totalWidth <= availWidth) {
      return { isFolded: false, headCrumbs: crumbs, foldedCrumbs: [], tailCrumbs: [] };
    }

    // 2. 空间受限，动态计算折叠配额
    // 基础保底项：根目录 + 省略号 + 当前目录
    let remainingWidth =
      availWidth -
      (estimateCrumbWidth(crumbs[0], true, false, maxItemWidth) +
        ELLIPSIS_WIDTH +
        estimateCrumbWidth(crumbs[n - 1], false, true, maxItemWidth));

    let tailStartIndex = n - 1;
    let headEndIndex = 0;

    // 优先级 1：直接父级目录 (crumbs[n - 2])
    if (tailStartIndex - 1 > headEndIndex) {
      const parentWidth = estimateCrumbWidth(crumbs[tailStartIndex - 1], false, false, maxItemWidth);
      if (remainingWidth >= parentWidth) {
        tailStartIndex--;
        remainingWidth -= parentWidth;
      }
    }

    // 优先级 2：一级主目录 (crumbs[1])
    if (headEndIndex + 1 < tailStartIndex) {
      const topFolderWidth = estimateCrumbWidth(crumbs[headEndIndex + 1], false, false, maxItemWidth);
      if (remainingWidth >= topFolderWidth) {
        headEndIndex++;
        remainingWidth -= topFolderWidth;
      }
    }

    // 优先级 3：空间宽裕时自尾部向前继续扩充
    while (tailStartIndex - 1 > headEndIndex) {
      const prevWidth = estimateCrumbWidth(crumbs[tailStartIndex - 1], false, false, maxItemWidth);
      if (remainingWidth >= prevWidth) {
        tailStartIndex--;
        remainingWidth -= prevWidth;
      } else {
        break;
      }
    }

    // 若计算得出的折叠区间为空（例如所有项都能塞下），则无需折叠
    if (headEndIndex + 1 >= tailStartIndex) {
      return { isFolded: false, headCrumbs: crumbs, foldedCrumbs: [], tailCrumbs: [] };
    }

    return {
      isFolded: true,
      headCrumbs: crumbs.slice(0, headEndIndex + 1),
      foldedCrumbs: crumbs.slice(headEndIndex + 1, tailStartIndex),
      tailCrumbs: crumbs.slice(tailStartIndex),
    };
  });

  return {
    breadcrumbContainerRef,
    dynamicBreadcrumbs,
    breadcrumbs: fullBreadcrumbs,
  };
}
