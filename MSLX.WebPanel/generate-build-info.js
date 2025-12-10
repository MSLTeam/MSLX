/* eslint-disable */
const fs = require('fs');
const path = require('path');
const { execSync } = require('child_process');

function getGitInfo() {
  try {
    // 辅助函数：去除字符串两端的空格和各种引号 (包括中文引号 “ ”)
    const clean = (str) => {
      if (!str) return 'unknown';
      // 移除首尾的空白、双引号、单引号、中文引号
      return str.toString().trim().replace(/^["'“”]+|["'“”]+$/g, '');
    };

    const commitId = clean(execSync('git rev-parse HEAD'));
    const commitMsg = clean(execSync('git log -1 --pretty=format:"%s"'));
    const commitAuthor = clean(execSync('git log -1 --pretty=format:"%an"'));

    // 格式: Hash|Date|Author|Message
    // 注意：这里为了防止 Author 或 Message 里有 | 符号破坏结构，其实最好用其他分隔符，但简单场景 | 够用了
    const logOutput = execSync('git log -50 --pretty=format:"%H|%cd|%an|%s" --date=iso').toString().trim();

    const history = logOutput.split('\n').map(line => {
      const parts = line.split('|');
      // 确保至少有4个部分 (Hash, Date, Author, Msg)
      if (parts.length < 4) return null;

      return {
        commitId: parts[0],
        commitTime: parts[1],
        commitAuthor: clean(parts[2]), // 这里也清洗一下名字
        commitMsg: clean(parts.slice(3).join('|')) // 防止消息里有 | 符号被切断
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

    console.log(`[${buildTime}] 构建成功!`);
    console.log(`- Version: ${buildInfo.version}`);
    console.log(`- Commit: ${gitInfo.commitId.substring(0, 7)} by ${gitInfo.commitAuthor}`);
    console.log(`- Output: ${buildJsonPath}`);

  } catch (error) {
    console.error('构建错误:', error);
    process.exit(1);
  }
}

generateBuildFiles();
