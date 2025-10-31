import { computed, toRaw, unref } from 'vue';
import uniqBy from 'lodash/uniqBy';
import { useRouter } from 'vue-router';
import type { MenuRoute } from '@/types/interface';

export function useFrameKeepAlive() {
  const router = useRouter();
  const { currentRoute } = router;


  const getFramePages = computed(() => {

    return getAllFramePages(toRaw(router.getRoutes()) as unknown as MenuRoute[]) || [];
  });


  function getAllFramePages(routes: MenuRoute[]): MenuRoute[] {
    let res: MenuRoute[] = [];
    for (const route of routes) {
      const { meta: { frameSrc, frameBlank } = {}, children } = route;
      if (frameSrc && !frameBlank) {
        res.push(route);
      }
      if (children && children.length) {
        res.push(...getAllFramePages(children));
      }
    }
    res = uniqBy(res, 'name');
    return res;
  }

  function showIframe(item: MenuRoute) {
    return item.name === unref(currentRoute).name;
  }

  function hasRenderFrame(name: string) {
    return router.currentRoute.value.name === name;
  }

  return { hasRenderFrame, getFramePages, showIframe, getAllFramePages };
}
