/* eslint-disable */
const fs = require('fs');
const path = require('path');

const propsPath = path.resolve(__dirname, '../Version.props');
const pkgPath = path.resolve(__dirname, './package.json');

try {
  if (!fs.existsSync(propsPath)) {
    console.warn(`[sync-version] 未找到 Version.props 文件: ${propsPath}`);
    process.exit(0);
  }

  const propsContent = fs.readFileSync(propsPath, 'utf-8');

  const match = propsContent.match(/<PanelBaseVersion>(.*?)<\/PanelBaseVersion>/i);

  if (match && match[1]) {
    const versionFromProps = match[1].trim();

    const pkg = JSON.parse(fs.readFileSync(pkgPath, 'utf-8'));

    if (pkg.version !== versionFromProps) {
      pkg.version = versionFromProps;
      fs.writeFileSync(pkgPath, JSON.stringify(pkg, null, 2) + '\n', 'utf-8');
      console.log(`\x1b[32m[sync-version] 成功将 package.json 版本同步为: ${versionFromProps}\x1b[0m`);
    } else {
      console.log(`[sync-version] package.json 版本已是最新: ${versionFromProps}`);
    }
  } else {
    console.warn('[sync-version] 在 Version.props 中未找到 <PanelBaseVersion> 节点');
  }
} catch (err) {
  console.error('[sync-version] 同步版本号失败:', err.message);
}
