/* eslint-disable */
const fs = require('fs');
const path = require('path');
const { execSync } = require('child_process');

function getGitInfo() {
  try {
    const clean = (str) => {
      if (!str) return 'unknown';
      return str.toString().trim().replace(/^["'“”]+|["'“”]+$/g, '');
    };

    const commitId = clean(execSync('git rev-parse HEAD'));
    const commitMsg = clean(execSync('git log -1 --pretty=format:"%s"'));
    const commitAuthor = clean(execSync('git log -1 --pretty=format:"%an"'));

    const logOutput = execSync('git log -50 --pretty=format:"%H|%cd|%an|%s" --date=iso').toString().trim();

    const history = logOutput.split('\n').map(line => {
      const parts = line.split('|');
      if (parts.length < 4) return null;
      return {
        commitId: parts[0],
        commitTime: parts[1],
        commitAuthor: clean(parts[2]),
        commitMsg: clean(parts.slice(3).join('|'))
      };
    }).filter(Boolean);

    return { commitId, commitMsg, commitAuthor, history };
  } catch (e) {
    return {
      commitId: 'unknown',
      commitMsg: 'unknown',
      commitAuthor: 'unknown',
      history: []
    };
  }
}

function copyToBackend(sourcePath) {
  try {
    const targetPath = path.resolve(__dirname, '../MSLX.Daemon/Frontend');
    const parentDir = path.dirname(targetPath);

    if (!fs.existsSync(parentDir)) {
      return;
    }

    if (fs.existsSync(targetPath)) {
      fs.rmSync(targetPath, { recursive: true, force: true });
      console.log(`Cleaned target directory: ${targetPath}`);
    }

    if (!fs.existsSync(targetPath)) {
      fs.mkdirSync(targetPath, { recursive: true });
    }

    // 执行复制
    fs.cpSync(sourcePath, targetPath, { recursive: true, force: true });
    console.log(`Files copied to backend: ${targetPath}`);
  } catch (e) {
    console.error(`Copy failed: ${e.message}`);
  }
}

function generateBuildFiles() {
  try {
    const packageJsonPath = path.resolve(__dirname, 'package.json');
    const packageJson = JSON.parse(fs.readFileSync(packageJsonPath, 'utf-8'));

    const now = new Date();
    const buildTime = now.toLocaleString('zh-CN', {
      timeZone: 'Asia/Shanghai',
      year: 'numeric', month: '2-digit', day: '2-digit',
      hour: '2-digit', minute: '2-digit', second: '2-digit',
      hour12: false
    }).replace(/\//g, '-');

    const gitInfo = getGitInfo();

    const buildInfo = {
      version: packageJson.version,
      buildTime: buildTime,
      dependencies: packageJson.dependencies,
      devDependencies: packageJson.devDependencies,
      ...gitInfo
    };

    const distDir = path.resolve(__dirname, 'dist');
    if (!fs.existsSync(distDir)) {
      fs.mkdirSync(distDir, { recursive: true });
    }

    const buildJsonPath = path.resolve(distDir, 'build.json');
    fs.writeFileSync(buildJsonPath, JSON.stringify(buildInfo, null, 2));

    const robotsTxtPath = path.resolve(distDir, 'robots.txt');
    fs.writeFileSync(robotsTxtPath, `User-agent: *\nDisallow: /api/`);

    console.log(`[${buildTime}] Build Metadata Generated.`);
    console.log(`- Version: ${buildInfo.version}`);

    copyToBackend(distDir);

  } catch (error) {
    console.error('Build Error:', error);
    process.exit(1);
  }
}

generateBuildFiles();
