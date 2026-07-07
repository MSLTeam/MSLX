# Server Properties Path Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let each instance configure a relative `server.properties` path that is used consistently by instance info display and the Server.properties editor.

**Architecture:** Store the path on the existing instance model, validate it as a relative in-instance path, and resolve it through one backend helper before reading files. The frontend adds one settings field near file encoding and passes the configured path to the existing file content APIs.

**Tech Stack:** ASP.NET Core 10, C# 13-style project conventions, Vue 3, TypeScript, TDesign Vue Next, Vite, xUnit for focused path validation tests.

---

## File Structure

Create:

- `MSLX.Daemon/Utils/ServerPropertiesPathUtils.cs` - owns defaulting, normalization, validation, and full-path resolution for the configured `server.properties` path.
- `MSLX.Tests/MSLX.Tests.csproj` - small test project for path validation behavior.
- `MSLX.Tests/ServerPropertiesPathUtilsTests.cs` - unit tests for accepted and rejected path values.

Modify:

- `MSLX.SDK/Models/MCServerInfo.cs` - add persisted `ServerPropertiesPath` to instance config.
- `MSLX.SDK/Models/Instance/UpdateServerRequest.cs` - accept `ServerPropertiesPath` in settings saves and validate the request-level relative path rules.
- `MSLX.Daemon/Utils/FileUtils.cs` - tighten `GetSafePath` root-boundary checks so `C:\base2` cannot match `C:\base` by prefix.
- `MSLX.Daemon/Controllers/InstanceControllers/InstanceSettingsController.cs` - normalize and save `ServerPropertiesPath`.
- `MSLX.Daemon/Controllers/InstanceControllers/InstanceInfoController.cs` - use the configured path and keep instance info successful when the file is missing or unreadable.
- `MSLX.WebPanel/src/api/model/instance.ts` - add `serverPropertiesPath` to instance settings and optional instance info config.
- `MSLX.WebPanel/src/pages/instance/console/components/settingsComponents/GeneralSettings.vue` - add the settings UI row and frontend validation.
- `MSLX.WebPanel/src/pages/instance/console/components/settingsComponents/ServerProperties.vue` - read and write the configured path instead of hard-coded `server.properties`.

Do not commit unless the user explicitly authorizes commits during execution. The commit steps below are checkpoints for authorized commit workflows only.

---

### Task 1: Add Backend Path Utility and Unit Tests

**Files:**

- Create: `MSLX.Daemon/Utils/ServerPropertiesPathUtils.cs`
- Modify: `MSLX.Daemon/Utils/FileUtils.cs:36-57`
- Create: `MSLX.Tests/MSLX.Tests.csproj`
- Create: `MSLX.Tests/ServerPropertiesPathUtilsTests.cs`
- Modify: `MSLX.sln`

- [ ] **Step 1: Create the test project**

Run:

```bash
dotnet new xunit -n MSLX.Tests -f net10.0
dotnet sln MSLX.sln add MSLX.Tests/MSLX.Tests.csproj
dotnet add MSLX.Tests/MSLX.Tests.csproj reference MSLX.Daemon/MSLX.Daemon.csproj
```

Expected:

- `MSLX.Tests/MSLX.Tests.csproj` exists.
- `MSLX.sln` includes `MSLX.Tests`.
- Restore succeeds. If NuGet restore fails because the network is unavailable, stop and report the restore error.

- [ ] **Step 2: Replace the generated test with failing path validation tests**

Delete the generated `UnitTest1.cs` if the template created it.

Create `MSLX.Tests/ServerPropertiesPathUtilsTests.cs`:

```csharp
using MSLX.Daemon.Utils;
using MSLX.SDK.Models;

namespace MSLX.Tests;

public class ServerPropertiesPathUtilsTests
{
    [Theory]
    [InlineData(null, "server.properties")]
    [InlineData("", "server.properties")]
    [InlineData("   ", "server.properties")]
    [InlineData("server.properties", "server.properties")]
    [InlineData("config/server.properties", "config/server.properties")]
    [InlineData(@"config\server.properties", "config/server.properties")]
    public void NormalizeRelativePath_accepts_safe_relative_paths(string? input, string expected)
    {
        var actual = ServerPropertiesPathUtils.NormalizeRelativePath(input);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("../server.properties")]
    [InlineData("config/../server.properties")]
    [InlineData("/tmp/server.properties")]
    [InlineData(@"C:\temp\server.properties")]
    public void NormalizeRelativePath_rejects_paths_outside_instance(string input)
    {
        var ex = Assert.Throws<ArgumentException>(() => ServerPropertiesPathUtils.NormalizeRelativePath(input));

        Assert.Equal(ServerPropertiesPathUtils.InvalidPathMessage, ex.Message);
    }

    [Fact]
    public void ResolveFullPath_combines_instance_base_and_normalized_relative_path()
    {
        var basePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(basePath);
        try
        {
            var server = new McServerInfo.ServerInfo
            {
                ID = 1,
                Name = "Test",
                Base = basePath,
                Java = "java",
                Core = "server.jar",
                ServerPropertiesPath = "config/server.properties"
            };

            var actual = ServerPropertiesPathUtils.ResolveFullPath(server);
            var expected = Path.GetFullPath(Path.Combine(basePath, "config", "server.properties"));

            Assert.Equal(expected, actual);
        }
        finally
        {
            Directory.Delete(basePath, true);
        }
    }
}
```

- [ ] **Step 3: Run the new tests and verify they fail because the utility does not exist**

Run:

```bash
dotnet test MSLX.Tests/MSLX.Tests.csproj --filter ServerPropertiesPathUtilsTests
```

Expected: FAIL with errors mentioning `ServerPropertiesPathUtils` is missing.

- [ ] **Step 4: Add the path utility**

Create `MSLX.Daemon/Utils/ServerPropertiesPathUtils.cs`:

```csharp
using MSLX.SDK.Models;

namespace MSLX.Daemon.Utils;

public static class ServerPropertiesPathUtils
{
    public const string DefaultServerPropertiesPath = "server.properties";
    public const string InvalidPathMessage = "server.properties 路径必须是实例目录内的相对路径";

    public static string NormalizeRelativePath(string? path)
    {
        var value = string.IsNullOrWhiteSpace(path)
            ? DefaultServerPropertiesPath
            : path.Trim().Replace('\\', '/');

        if (Path.IsPathRooted(value))
        {
            throw new ArgumentException(InvalidPathMessage);
        }

        var segments = value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return DefaultServerPropertiesPath;
        }

        if (segments.Any(segment => segment is "." or ".."))
        {
            throw new ArgumentException(InvalidPathMessage);
        }

        if (segments.Any(segment => segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
        {
            throw new ArgumentException(InvalidPathMessage);
        }

        return string.Join('/', segments);
    }

    public static string ResolveFullPath(McServerInfo.ServerInfo server)
    {
        var relativePath = NormalizeRelativePath(server.ServerPropertiesPath);
        var check = FileUtils.GetSafePath(server.Base, relativePath);

        if (!check.IsSafe)
        {
            throw new ArgumentException(InvalidPathMessage);
        }

        return check.FullPath;
    }
}
```

- [ ] **Step 5: Tighten `FileUtils.GetSafePath` root matching**

In `MSLX.Daemon/Utils/FileUtils.cs`, replace the body of `GetSafePath` with:

```csharp
public static (bool IsSafe, string FullPath, string Message) GetSafePath(string rootBase, string? relativePath)
{
    if (string.IsNullOrEmpty(rootBase) || !Directory.Exists(rootBase))
    {
        return (false, string.Empty, "服务端根目录未配置或不存在");
    }

    try
    {
        var rootPath = Path.GetFullPath(rootBase)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var reqPath = relativePath ?? string.Empty;
        var targetPath = Path.GetFullPath(Path.Combine(rootPath, reqPath));
        var rootPrefix = rootPath + Path.DirectorySeparatorChar;

        if (!targetPath.Equals(rootPath, StringComparison.OrdinalIgnoreCase) &&
            !targetPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return (false, string.Empty, "禁止访问实例目录以外的资源");
        }

        return (true, targetPath, string.Empty);
    }
    catch (Exception ex)
    {
        return (false, string.Empty, $"路径解析错误: {ex.Message}");
    }
}
```

- [ ] **Step 6: Run path utility tests**

Run:

```bash
dotnet test MSLX.Tests/MSLX.Tests.csproj --filter ServerPropertiesPathUtilsTests
```

Expected: PASS for all tests in `ServerPropertiesPathUtilsTests`.

- [ ] **Step 7: Authorized commit checkpoint**

Only run if the user has explicitly authorized commits:

```bash
git add MSLX.Daemon/Utils/ServerPropertiesPathUtils.cs MSLX.Daemon/Utils/FileUtils.cs MSLX.Tests/ServerPropertiesPathUtilsTests.cs MSLX.Tests/MSLX.Tests.csproj MSLX.sln
git commit -m "test(daemon): cover server properties path validation"
```

---

### Task 2: Persist `ServerPropertiesPath` Through Instance Settings

**Files:**

- Modify: `MSLX.SDK/Models/MCServerInfo.cs`
- Modify: `MSLX.SDK/Models/Instance/UpdateServerRequest.cs`
- Modify: `MSLX.Daemon/Controllers/InstanceControllers/InstanceSettingsController.cs`

- [ ] **Step 1: Add failing request validation tests**

Append these tests to `MSLX.Tests/ServerPropertiesPathUtilsTests.cs`:

```csharp
[Fact]
public void UpdateServerRequest_accepts_blank_server_properties_path_as_default()
{
    var request = CreateValidRequest();
    request.ServerPropertiesPath = "";

    var results = Validate(request);

    Assert.DoesNotContain(results, result => result.MemberNames.Contains(nameof(request.ServerPropertiesPath)));
}

[Theory]
[InlineData("config/server.properties")]
[InlineData(@"config\server.properties")]
public void UpdateServerRequest_accepts_safe_relative_server_properties_path(string path)
{
    var request = CreateValidRequest();
    request.ServerPropertiesPath = path;

    var results = Validate(request);

    Assert.DoesNotContain(results, result => result.MemberNames.Contains(nameof(request.ServerPropertiesPath)));
}

[Theory]
[InlineData("../server.properties")]
[InlineData("config/../server.properties")]
[InlineData("/tmp/server.properties")]
[InlineData(@"C:\temp\server.properties")]
public void UpdateServerRequest_rejects_unsafe_server_properties_path(string path)
{
    var request = CreateValidRequest();
    request.ServerPropertiesPath = path;

    var results = Validate(request);

    Assert.Contains(results, result =>
        result.MemberNames.Contains(nameof(request.ServerPropertiesPath)) &&
        result.ErrorMessage == ServerPropertiesPathUtils.InvalidPathMessage);
}

private static UpdateServerRequest CreateValidRequest()
{
    return new UpdateServerRequest
    {
        ID = 1,
        Name = "Test",
        Base = "C:/Servers/Test",
        Java = "java",
        Core = "server.jar",
        MinM = 1024,
        MaxM = 2048
    };
}

private static List<System.ComponentModel.DataAnnotations.ValidationResult> Validate(UpdateServerRequest request)
{
    var context = new System.ComponentModel.DataAnnotations.ValidationContext(request);
    var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
    System.ComponentModel.DataAnnotations.Validator.TryValidateObject(request, context, results, true);
    return results;
}
```

Add these usings at the top of the test file:

```csharp
using MSLX.SDK.Models.Instance;
```

- [ ] **Step 2: Run tests and verify they fail because the request property is missing**

Run:

```bash
dotnet test MSLX.Tests/MSLX.Tests.csproj --filter ServerPropertiesPathUtilsTests
```

Expected: FAIL with errors mentioning `UpdateServerRequest.ServerPropertiesPath` is missing.

- [ ] **Step 3: Add the persisted field to `ServerInfo`**

In `MSLX.SDK/Models/MCServerInfo.cs`, add this property after `FileEncoding`:

```csharp
public string ServerPropertiesPath { get; set; } = "server.properties";
```

- [ ] **Step 4: Add request field and validation**

In `MSLX.SDK/Models/Instance/UpdateServerRequest.cs`, add this property after `FileEncoding`:

```csharp
public string ServerPropertiesPath { get; set; } = "server.properties";
```

Inside `Validate`, after the memory comparison block, add:

```csharp
var serverPropertiesPath = string.IsNullOrWhiteSpace(ServerPropertiesPath)
    ? "server.properties"
    : ServerPropertiesPath.Trim().Replace('\\', '/');

if (Path.IsPathRooted(serverPropertiesPath) ||
    serverPropertiesPath
        .Split('/', StringSplitOptions.RemoveEmptyEntries)
        .Any(segment => segment is "." or ".." || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
{
    yield return new ValidationResult(
        "server.properties 路径必须是实例目录内的相对路径",
        new[] { nameof(ServerPropertiesPath) }
    );
}
```

- [ ] **Step 5: Save normalized path in settings update**

In `MSLX.Daemon/Controllers/InstanceControllers/InstanceSettingsController.cs`, inside the direct-save branch after `server.FileEncoding = request.FileEncoding;`, add:

```csharp
try
{
    server.ServerPropertiesPath = ServerPropertiesPathUtils.NormalizeRelativePath(request.ServerPropertiesPath);
}
catch (ArgumentException ex)
{
    return BadRequest(new ApiResponse<object>
    {
        Code = 400,
        Message = ex.Message,
    });
}
```

Ensure the file already imports `MSLX.Daemon.Utils;`; it currently does, so no new using is needed.

- [ ] **Step 6: Run tests**

Run:

```bash
dotnet test MSLX.Tests/MSLX.Tests.csproj --filter ServerPropertiesPathUtilsTests
```

Expected: PASS.

- [ ] **Step 7: Build backend projects**

Run:

```bash
dotnet build MSLX.sln
```

Expected: Build succeeds. Existing warnings are acceptable; new errors are not.

- [ ] **Step 8: Authorized commit checkpoint**

Only run if the user has explicitly authorized commits:

```bash
git add MSLX.SDK/Models/MCServerInfo.cs MSLX.SDK/Models/Instance/UpdateServerRequest.cs MSLX.Daemon/Controllers/InstanceControllers/InstanceSettingsController.cs MSLX.Tests/ServerPropertiesPathUtilsTests.cs
git commit -m "feat(daemon): persist server properties path setting"
```

---

### Task 3: Use the Configured Path in Instance Info

**Files:**

- Modify: `MSLX.Daemon/Controllers/InstanceControllers/InstanceInfoController.cs`

- [ ] **Step 1: Add a helper method in `InstanceInfoController`**

Inside `InstanceInfoController`, before the final class closing brace, add:

```csharp
private object BuildUnknownMcConfig(McServerInfo.ServerInfo server, bool exists = false)
{
    return new
    {
        difficulty = "未知",
        gamemode = "未知",
        levelName = "未知",
        serverPort = "未知",
        onlineMode = "未知",
        serverPropertiesPath = ServerPropertiesPathUtils.NormalizeRelativePath(server.ServerPropertiesPath),
        serverPropertiesExists = exists,
    };
}
```

- [ ] **Step 2: Replace the hard-coded `server.properties` branch**

In `GetInstanceInfo`, replace this condition and load path:

```csharp
if (System.IO.File.Exists(Path.Combine(server.Base, "server.properties")))
{
    dynamic config = ServerPropertiesLoader.Load(Path.Combine(server.Base, "server.properties"),FileUtils.GetFileEncodingByString(server.FileEncoding));
```

with:

```csharp
var serverPropertiesPath = ServerPropertiesPathUtils.ResolveFullPath(server);
var serverPropertiesRelativePath = ServerPropertiesPathUtils.NormalizeRelativePath(server.ServerPropertiesPath);

if (System.IO.File.Exists(serverPropertiesPath))
{
    dynamic config;
    try
    {
        config = ServerPropertiesLoader.Load(serverPropertiesPath, FileUtils.GetFileEncodingByString(server.FileEncoding));
    }
    catch (Exception ex)
    {
        Console.WriteLine($"读取 server.properties 失败: {serverPropertiesPath}, {ex.Message}");
        config = null!;
    }

    if (config != null)
    {
```

Then close the new `if (config != null)` block just before the existing fallback `return Ok(...)` that handles missing `server.properties`.

- [ ] **Step 3: Include path metadata in successful `mcConfig`**

In the successful `mcConfig = new { ... }`, add:

```csharp
serverPropertiesPath = serverPropertiesRelativePath,
serverPropertiesExists = true,
```

The full `mcConfig` block should be:

```csharp
mcConfig = new
{
    difficulty,
    gamemode,
    levelName = config.level_name,
    serverPort = config.server_port,
    onlineMode = config.online_mode,
    serverPropertiesPath = serverPropertiesRelativePath,
    serverPropertiesExists = true,
}
```

- [ ] **Step 4: Replace missing-file fallback `mcConfig`**

In the fallback response where `mcConfig` currently hard-codes all fields to `未知`, replace that `mcConfig = new { ... }` block with:

```csharp
mcConfig = BuildUnknownMcConfig(server)
```

- [ ] **Step 5: Run backend build**

Run:

```bash
dotnet build MSLX.Daemon/MSLX.Daemon.csproj
```

Expected: Build succeeds.

- [ ] **Step 6: Run tests**

Run:

```bash
dotnet test MSLX.Tests/MSLX.Tests.csproj --filter ServerPropertiesPathUtilsTests
```

Expected: PASS.

- [ ] **Step 7: Authorized commit checkpoint**

Only run if the user has explicitly authorized commits:

```bash
git add MSLX.Daemon/Controllers/InstanceControllers/InstanceInfoController.cs
git commit -m "feat(daemon): read instance info from configured properties path"
```

---

### Task 4: Add Frontend Model Field and Settings UI

**Files:**

- Modify: `MSLX.WebPanel/src/api/model/instance.ts`
- Modify: `MSLX.WebPanel/src/pages/instance/console/components/settingsComponents/GeneralSettings.vue`

- [ ] **Step 1: Extend TypeScript models**

In `InstanceInfoModel.mcConfig`, add:

```ts
serverPropertiesPath?: string;
serverPropertiesExists?: boolean;
```

In `UpdateInstanceModel`, add after `fileEncoding`:

```ts
serverPropertiesPath?: string;
```

In `InstanceSettingsModel`, add after `outputEncoding`:

```ts
fileEncoding?: 'utf-8' | 'utf-8-bom' | 'gbk';
serverPropertiesPath?: string;
```

This also fixes the existing mismatch where `InstanceSettingsModel` omits `fileEncoding` while the settings component already uses it.

- [ ] **Step 2: Add frontend path validator helper in `GeneralSettings.vue`**

In the `<script setup>` block, after `fileEncodingOptions`, add:

```ts
const normalizeServerPropertiesPath = (value?: string) => {
  const normalized = (value || 'server.properties').trim().replace(/\\/g, '/');
  if (!normalized) return 'server.properties';
  return normalized.replace(/\/+/g, '/').replace(/^\.\//, '');
};

const isSafeServerPropertiesPath = (value?: string) => {
  const normalized = normalizeServerPropertiesPath(value);
  if (/^[a-zA-Z]:\//.test(normalized) || normalized.startsWith('/')) return false;
  return normalized.split('/').every((segment) => segment && segment !== '.' && segment !== '..');
};
```

- [ ] **Step 3: Add default form field**

In the `formData` initializer, after `fileEncoding: 'utf-8',`, add:

```ts
serverPropertiesPath: 'server.properties',
```

- [ ] **Step 4: Add form validation rule**

In both branches of the `rules` computed value, add:

```ts
serverPropertiesPath: [
  {
    validator: (val: string) => isSafeServerPropertiesPath(val),
    message: 'server.properties 路径必须是实例目录内的相对路径',
    trigger: 'blur',
  },
],
```

For the `javaType.value === 'none'` branch, the returned object should include `name`, `base`, `args`, and `serverPropertiesPath`.

For the normal branch, the returned object should include existing rules plus `serverPropertiesPath`.

- [ ] **Step 5: Normalize before submit**

In `onSubmit`, after validation succeeds and before custom-mode handling, add:

```ts
formData.value.serverPropertiesPath = normalizeServerPropertiesPath(formData.value.serverPropertiesPath);
```

- [ ] **Step 6: Add the UI row near file encoding**

In the template, immediately after the existing `文件编码` row and before the sticky save button, insert:

```vue
<div
  class="flex flex-col md:flex-row md:items-start justify-between p-3 md:p-4 border-b border-dashed border-zinc-100 dark:border-zinc-800/60 hover:bg-zinc-50/50 dark:hover:bg-zinc-800/20 transition-colors rounded-xl"
>
  <div class="flex-1 pr-0 md:pr-8 mb-3 md:mb-0 min-w-[200px]">
    <div class="text-sm font-medium text-[var(--td-text-color-primary)] leading-snug">Server.properties 路径</div>
    <div class="text-xs text-[var(--td-text-color-secondary)] mt-1 leading-relaxed">
      相对实例路径，用于读取端口、难度、游戏模式等服务端配置
    </div>
  </div>
  <div class="w-full md:w-[340px] shrink-0 flex">
    <t-input
      v-model="formData.serverPropertiesPath"
      placeholder="server.properties 或 config/server.properties"
      class="w-full"
    />
  </div>
</div>
```

- [ ] **Step 7: Run frontend typecheck/build command**

Run:

```bash
cd MSLX.WebPanel && npm run build
```

Expected:

- `vue-tsc --noEmit` passes.
- Vite build passes.
- `generate-build-info.js` copies `dist` into `MSLX.Daemon/Frontend`.

If dependencies are not installed, run the package manager used by the lockfile:

```bash
cd MSLX.WebPanel && pnpm install
```

Then retry `npm run build`.

- [ ] **Step 8: Authorized commit checkpoint**

Only run if the user has explicitly authorized commits:

```bash
git add MSLX.WebPanel/src/api/model/instance.ts MSLX.WebPanel/src/pages/instance/console/components/settingsComponents/GeneralSettings.vue MSLX.Daemon/Frontend
git commit -m "feat(webpanel): configure server properties path in settings"
```

---

### Task 5: Make Server.properties Editor Use the Configured Path

**Files:**

- Modify: `MSLX.WebPanel/src/pages/instance/console/components/settingsComponents/ServerProperties.vue`

- [ ] **Step 1: Import instance settings API**

At the top of the script, add:

```ts
import { getInstanceSettings } from '@/api/instance';
```

- [ ] **Step 2: Add configured path state and normalizer**

After `rawFileContent`, add:

```ts
const serverPropertiesPath = ref('server.properties');
const fileMissing = ref(false);

const normalizeServerPropertiesPath = (value?: string) => {
  const normalized = (value || 'server.properties').trim().replace(/\\/g, '/');
  if (!normalized) return 'server.properties';
  return normalized.replace(/\/+/g, '/').replace(/^\.\//, '');
};
```

- [ ] **Step 3: Load the configured path before reading content**

Add this function above `loadData`:

```ts
const loadServerPropertiesPath = async () => {
  const settings = await getInstanceSettings(instanceId.value);
  serverPropertiesPath.value = normalizeServerPropertiesPath(settings.serverPropertiesPath);
};
```

Replace the start of `loadData` with:

```ts
const loadData = async () => {
  if (!instanceId.value) return;
  loading.value = true;
  fileMissing.value = false;
  try {
    await loadServerPropertiesPath();
    const res = await getFileContent(instanceId.value, serverPropertiesPath.value);
    if (res) {
      rawFileContent.value = res;
      propertiesMap.value = parseProperties(res);
    }
  } catch (e: any) {
    console.error(`读取配置文件失败: ${e.message}`);
    rawFileContent.value = '';
    propertiesMap.value = {};
    fileMissing.value = true;
  } finally {
    loading.value = false;
  }
};
```

- [ ] **Step 4: Save to the configured path**

In `handleSave`, replace:

```ts
await saveFileContent(instanceId.value, 'server.properties', content);
```

with:

```ts
await saveFileContent(instanceId.value, serverPropertiesPath.value, content);
```

- [ ] **Step 5: Add editor path hint and missing-file warning**

In the template, inside the header area after the `h2`, add this under the title block:

```vue
<div class="text-xs text-[var(--td-text-color-secondary)] font-mono mt-1">
  {{ serverPropertiesPath }}
</div>
```

Inside `<t-loading>`, before the main white editor container, add:

```vue
<t-alert
  v-if="fileMissing"
  theme="warning"
  class="!mb-4 !rounded-lg"
  message="未找到当前配置的 server.properties 文件，保存后会在该路径创建文件。"
/>
```

- [ ] **Step 6: Run frontend build**

Run:

```bash
cd MSLX.WebPanel && npm run build
```

Expected: build succeeds and frontend assets are copied into `MSLX.Daemon/Frontend`.

- [ ] **Step 7: Authorized commit checkpoint**

Only run if the user has explicitly authorized commits:

```bash
git add MSLX.WebPanel/src/pages/instance/console/components/settingsComponents/ServerProperties.vue MSLX.Daemon/Frontend
git commit -m "feat(webpanel): edit configured server properties file"
```

---

### Task 6: End-to-End Verification

**Files:**

- No source files should be edited in this task unless verification exposes a bug.

- [ ] **Step 1: Run backend tests**

Run:

```bash
dotnet test MSLX.Tests/MSLX.Tests.csproj
```

Expected: PASS.

- [ ] **Step 2: Build solution**

Run:

```bash
dotnet build MSLX.sln
```

Expected: Build succeeds.

- [ ] **Step 3: Build frontend**

Run:

```bash
cd MSLX.WebPanel && npm run build
```

Expected: `vue-tsc` and Vite build succeed, then build output copies to `MSLX.Daemon/Frontend`.

- [ ] **Step 4: Run the daemon for manual verification**

Run:

```bash
dotnet run --project MSLX.Daemon -- --nobrowser true
```

Expected:

- Daemon starts on the configured host and port, normally `http://localhost:1027`.
- Logs show `MSLX 守护进程服务已就绪`.

- [ ] **Step 5: Manual UI verification**

Open the WebPanel and verify:

1. Go to an instance settings page.
2. Confirm `Server.properties 路径` appears near `文件编码`.
3. Set it to `config/server.properties` and save.
4. Refresh settings and confirm the value persists.
5. Ensure `{instance base}/config/server.properties` exists with values such as:

```properties
server-port=25566
difficulty=hard
gamemode=creative
level-name=world
online-mode=false
```

6. Open the instance console and confirm:

- 运行端口 shows `25566`.
- 游戏难度 shows `困难`.
- 游戏模式 shows `创造`.
- 游戏地图 shows `world`.
- 正版验证 shows `关闭`.

7. Open the Server.properties editor and confirm it displays `config/server.properties` and edits that file.

- [ ] **Step 6: Manual invalid-path verification**

In instance settings, try each value and save:

```text
../server.properties
config/../server.properties
C:\temp\server.properties
/tmp/server.properties
```

Expected: save fails with `server.properties 路径必须是实例目录内的相对路径`.

- [ ] **Step 7: Manual missing-file verification**

Set the path to:

```text
missing/server.properties
```

Expected:

- Save succeeds.
- Instance console derived config values show `未知`.
- Server.properties editor shows the missing-file warning.

- [ ] **Step 8: Final status check**

Run:

```bash
git status --short
```

Expected:

- Source changes match this feature.
- `.superpowers/` remains untracked unless the user chooses to keep visual brainstorming artifacts.
- No unrelated files are changed.

- [ ] **Step 9: Authorized final commit checkpoint**

Only run if the user has explicitly authorized commits and previous task commits were skipped:

```bash
git add MSLX.Daemon MSLX.SDK MSLX.WebPanel MSLX.Tests MSLX.sln
git commit -m "feat(instance): support custom server properties path"
```

---

## Self-Review Notes

Spec coverage:

- Per-instance setting: Task 2 and Task 4.
- Relative-only validation: Task 1, Task 2, Task 4, Task 6.
- Instance info uses configured path: Task 3.
- UI near file encoding: Task 4.
- Server.properties editor uses same path: Task 5.
- Missing-file behavior: Task 3, Task 5, Task 6.
- Existing instance compatibility: Task 1 and Task 2 default behavior.

Type consistency:

- Backend persisted property: `ServerPropertiesPath`.
- Frontend JSON property: `serverPropertiesPath`.
- Helper names used consistently: `NormalizeRelativePath`, `ResolveFullPath`.

Execution note:

- Commit commands are included only as authorized checkpoints. Do not run them unless the user explicitly asks for commits.
