const tagThemes = ['primary', 'success', 'warning', 'danger'] as const;

export function getTagTheme(tag: string) {
  let hash = 0;
  for (let i = 0; i < tag.length; i++) {
    hash = (hash << 5) - hash + tag.charCodeAt(i);
  }
  return tagThemes[Math.abs(hash) % tagThemes.length];
}
