# Custom Region Path Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让“世界渲染图”的 region 文件夹支持实例级自定义，默认仍读取 `{WorldPath}/region`。

**Architecture:** 在实例配置中新增 `RegionPath`，含义为“相对世界目录的 region 数据目录”，默认值为 `region`。`WorldPath` 继续负责定位 `level.dat` 所在的世界目录；地图瓦片接口改为读取 `{WorldPath}/{RegionPath}/r.x.z.mca`，并复用现有安全相对路径校验，防止绝对路径或 `..` 跳出实例目录。

**Tech Stack:** ASP.NET Core 10, C#, xUnit, Vue 3, TypeScript, TDesign Vue Next, Vite.

---

## File Structure

- Modify: `MSLX.SDK/Models/MCServerInfo.cs`
  - Add persisted instance field `RegionPath` with default `region`.
- Modify: `MSLX.SDK/Models/Instance/UpdateServerRequest.cs`
  - Add request field `RegionPath` and validate it as a safe relative path under the world directory.
- Modify: `MSLX.Daemon/Controllers/InstanceControllers/InstanceSettingsController.cs`
  - Save `RegionPath` when settings are saved directly.
- Modify: `MSLX.Daemon/Services/DeployServerService/ServerUpdateService.cs`
  - Save `RegionPath` when settings are saved through the background update task.
- Modify: `MSLX.Daemon/Controllers/InstanceControllers/MapRenderController.cs`
  - Replace hard-coded `{WorldPath}/region` lookup with `{WorldPath}/{RegionPath}` lookup.
- Modify: `MSLX.Tests/ServerPropertiesPathUtilsTests.cs`
  - Add request validation tests for `RegionPath`.
- Modify: `MSLX.WebPanel/src/api/model/instance.ts`
  - Add `regionPath?: string` to instance settings/update models.
- Modify: `MSLX.WebPanel/src/pages/instance/console/components/settingsComponents/GeneralSettings.vue`
  - Add `regionPath` default, validation, initialization, submit normalization, and UI input next to “地图目录路径”.
- Build output: `MSLX.Daemon/Frontend/**`
  - Running `npm --prefix MSLX.WebPanel run build` will regenerate embedded frontend assets.

---

### Task 1: Add and Test Backend RegionPath Model Validation

**Files:**
- Modify: `MSLX.SDK/Models/MCServerInfo.cs:32-36`
- Modify: `MSLX.SDK/Models/Instance/UpdateServerRequest.cs:64-114`
- Modify: `MSLX.Tests/ServerPropertiesPathUtilsTests.cs`

- [ ] **Step 1: Add failing tests for RegionPath defaults and validation**

In `MSLX.Tests/ServerPropertiesPathUtilsTests.cs`, inside `ServerPropertiesPathUtilsRequestTests`, insert these tests after `UpdateServerRequest_rejects_unsafe_world_path` and before `CreateValidRequest()`:

```csharp
    [Fact]
    public void UpdateServerRequest_accepts_blank_region_path_as_default()
    {
        var request = CreateValidRequest();
        request.RegionPath = "";

        var results = Validate(request);

        Assert.DoesNotContain(results, result => result.MemberNames.Contains(nameof(request.RegionPath)));
    }

    [Theory]
    [InlineData("region")]
    [InlineData("DIM-1/region")]
    [InlineData("DIM1/region")]
    [InlineData(@"dimensions\minecraft\overworld\region")]
    public void UpdateServerRequest_accepts_safe_relative_region_path(string path)
    {
        var request = CreateValidRequest();
        request.RegionPath = path;

        var results = Validate(request);

        Assert.DoesNotContain(results, result => result.MemberNames.Contains(nameof(request.RegionPath)));
    }

    [Theory]
    [InlineData("../region")]
    [InlineData("DIM-1/../region")]
    [InlineData("/tmp/region")]
    [InlineData(@"C:\temp\region")]
    public void UpdateServerRequest_rejects_unsafe_region_path(string path)
    {
        var request = CreateValidRequest();
        request.RegionPath = path;

        var results = Validate(request);

        Assert.Contains(results, result =>
            result.MemberNames.Contains(nameof(request.RegionPath)) &&
            result.ErrorMessage == "Region 目录路径必须是地图目录内的相对路径");
    }
```

- [ ] **Step 2: Run tests to verify the new tests fail**

Run:

```powershell
dotnet test MSLX.Tests/MSLX.Tests.csproj --filter RegionPath
```

Expected: FAIL, because `UpdateServerRequest` does not yet contain `RegionPath`.

- [ ] **Step 3: Add `RegionPath` to persisted server model**

In `MSLX.SDK/Models/MCServerInfo.cs`, change the path field block to:

```csharp
            public string ServerPropertiesPath { get; set; } = "server.properties";
            public string PluginsPath { get; set; } = "plugins";
            public string ModsPath { get; set; } = "mods";
            public string WorldPath { get; set; } = "world";
            public string RegionPath { get; set; } = "region";
```

- [ ] **Step 4: Add `RegionPath` to update request model and validation**

In `MSLX.SDK/Models/Instance/UpdateServerRequest.cs`, change the path fields to:

```csharp
    public string ServerPropertiesPath { get; set; } = "server.properties";
    public string PluginsPath { get; set; } = "plugins";
    public string ModsPath { get; set; } = "mods";
    public string WorldPath { get; set; } = "world";
    public string RegionPath { get; set; } = "region";
```

Then in `Validate()`, after the existing `WorldPath` validation, add:

```csharp
        foreach (var result in ValidateRelativeInstancePath(RegionPath, "region", "Region 目录路径必须是地图目录内的相对路径", nameof(RegionPath)))
            yield return result;
```

The full path validation block should become:

```csharp
        foreach (var result in ValidateRelativeInstancePath(ServerPropertiesPath, "server.properties", "server.properties 路径必须是实例目录内的相对路径", nameof(ServerPropertiesPath)))
            yield return result;
        foreach (var result in ValidateRelativeInstancePath(PluginsPath, "plugins", "插件目录路径必须是实例目录内的相对路径", nameof(PluginsPath)))
            yield return result;
        foreach (var result in ValidateRelativeInstancePath(ModsPath, "mods", "模组目录路径必须是实例目录内的相对路径", nameof(ModsPath)))
            yield return result;
        foreach (var result in ValidateRelativeInstancePath(WorldPath, "world", "地图目录路径必须是实例目录内的相对路径", nameof(WorldPath)))
            yield return result;
        foreach (var result in ValidateRelativeInstancePath(RegionPath, "region", "Region 目录路径必须是地图目录内的相对路径", nameof(RegionPath)))
            yield return result;
```

- [ ] **Step 5: Run targeted backend tests**

Run:

```powershell
dotnet test MSLX.Tests/MSLX.Tests.csproj --filter RegionPath
```

Expected: PASS.

- [ ] **Step 6: Commit Task 1**

```bash
git add MSLX.SDK/Models/MCServerInfo.cs MSLX.SDK/Models/Instance/UpdateServerRequest.cs MSLX.Tests/ServerPropertiesPathUtilsTests.cs
git commit -m "feat: add configurable region path model"
```

---

### Task 2: Persist RegionPath From Instance Settings

**Files:**
- Modify: `MSLX.Daemon/Controllers/InstanceControllers/InstanceSettingsController.cs:126-131`
- Modify: `MSLX.Daemon/Services/DeployServerService/ServerUpdateService.cs:115-119`

- [ ] **Step 1: Save `RegionPath` in direct settings updates**

In `MSLX.Daemon/Controllers/InstanceControllers/InstanceSettingsController.cs`, update the path normalization block to:

```csharp
            try
            {
                server.ServerPropertiesPath = ServerPropertiesPathUtils.NormalizeRelativePath(request.ServerPropertiesPath);
                server.PluginsPath = ServerPropertiesPathUtils.NormalizeRelativePath(request.PluginsPath, "plugins", "插件目录路径必须是实例目录内的相对路径");
                server.ModsPath = ServerPropertiesPathUtils.NormalizeRelativePath(request.ModsPath, "mods", "模组目录路径必须是实例目录内的相对路径");
                server.WorldPath = ServerPropertiesPathUtils.NormalizeRelativePath(request.WorldPath, "world", "地图目录路径必须是实例目录内的相对路径");
                server.RegionPath = ServerPropertiesPathUtils.NormalizeRelativePath(request.RegionPath, "region", "Region 目录路径必须是地图目录内的相对路径");
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

- [ ] **Step 2: Save `RegionPath` in update-task settings updates**

In `MSLX.Daemon/Services/DeployServerService/ServerUpdateService.cs`, update the path assignment block to:

```csharp
            server.ServerPropertiesPath = ServerPropertiesPathUtils.NormalizeRelativePath(req.ServerPropertiesPath);
            server.PluginsPath = ServerPropertiesPathUtils.NormalizeRelativePath(req.PluginsPath, "plugins", "插件目录路径必须是实例目录内的相对路径");
            server.ModsPath = ServerPropertiesPathUtils.NormalizeRelativePath(req.ModsPath, "mods", "模组目录路径必须是实例目录内的相对路径");
            server.WorldPath = ServerPropertiesPathUtils.NormalizeRelativePath(req.WorldPath, "world", "地图目录路径必须是实例目录内的相对路径");
            server.RegionPath = ServerPropertiesPathUtils.NormalizeRelativePath(req.RegionPath, "region", "Region 目录路径必须是地图目录内的相对路径");
            IConfigBase.ServerList.UpdateServer(server);
```

- [ ] **Step 3: Build backend**

Run:

```powershell
dotnet build MSLX.Daemon/MSLX.Daemon.csproj
```

Expected: build succeeds with 0 errors. Existing warnings are acceptable.

- [ ] **Step 4: Commit Task 2**

```bash
git add MSLX.Daemon/Controllers/InstanceControllers/InstanceSettingsController.cs MSLX.Daemon/Services/DeployServerService/ServerUpdateService.cs
git commit -m "feat: persist configurable region path"
```

---

### Task 3: Use RegionPath in Map Rendering

**Files:**
- Modify: `MSLX.Daemon/Controllers/InstanceControllers/MapRenderController.cs:122-293`

- [ ] **Step 1: Replace `GetWorldPath` with helper methods that also resolve RegionPath**

In `MSLX.Daemon/Controllers/InstanceControllers/MapRenderController.cs`, replace the current `GetWorldPath(uint id)` method with this block:

```csharp
    private McServerInfo.ServerInfo GetServer(uint id)
    {
        return IConfigBase.ServerList.GetServer(id) ?? throw new Exception("实例不存在");
    }

    private string GetWorldPath(McServerInfo.ServerInfo server)
    {
        var relativePath = ServerPropertiesPathUtils.NormalizeRelativePath(
            server.WorldPath,
            "world",
            "地图目录路径必须是实例目录内的相对路径"
        );
        var check = FileUtils.GetSafePath(server.Base, relativePath);
        if (!check.IsSafe) throw new ArgumentException(check.Message);
        return check.FullPath;
    }

    private string GetRegionDirectoryPath(McServerInfo.ServerInfo server)
    {
        var worldPath = GetWorldPath(server);
        var relativePath = ServerPropertiesPathUtils.NormalizeRelativePath(
            server.RegionPath,
            "region",
            "Region 目录路径必须是地图目录内的相对路径"
        );
        var check = FileUtils.GetSafePath(worldPath, relativePath);
        if (!check.IsSafe) throw new ArgumentException(check.Message);
        return check.FullPath;
    }
```

- [ ] **Step 2: Update spawn endpoint to use the new server helper**

In `GetWorldSpawn(uint id)`, replace:

```csharp
            var worldPath = GetWorldPath(id);
            var levelDatPath = Path.Combine(worldPath, "level.dat");
```

with:

```csharp
            var server = GetServer(id);
            var worldPath = GetWorldPath(server);
            var levelDatPath = Path.Combine(worldPath, "level.dat");
```

- [ ] **Step 3: Update region endpoint to use RegionPath**

In `GetRegionMap(uint id, int regionX = 0, int regionZ = 0)`, replace:

```csharp
            var worldPath = GetWorldPath(id);
            var mcaFilePath = Path.Combine(worldPath, "region", $"r.{regionX}.{regionZ}.mca");
```

with:

```csharp
            var server = GetServer(id);
            var regionDirectoryPath = GetRegionDirectoryPath(server);
            var mcaFilePath = Path.Combine(regionDirectoryPath, $"r.{regionX}.{regionZ}.mca");
```

- [ ] **Step 4: Build backend**

Run:

```powershell
dotnet build MSLX.Daemon/MSLX.Daemon.csproj
```

Expected: build succeeds with 0 errors. Existing warnings are acceptable.

- [ ] **Step 5: Commit Task 3**

```bash
git add MSLX.Daemon/Controllers/InstanceControllers/MapRenderController.cs
git commit -m "feat: render maps from configurable region path"
```

---

### Task 4: Add RegionPath to Frontend Types and Settings Form

**Files:**
- Modify: `MSLX.WebPanel/src/api/model/instance.ts:52-116`
- Modify: `MSLX.WebPanel/src/pages/instance/console/components/settingsComponents/GeneralSettings.vue`

- [ ] **Step 1: Add `regionPath` to frontend API models**

In `MSLX.WebPanel/src/api/model/instance.ts`, update `UpdateInstanceModel` path fields to:

```ts
  serverPropertiesPath?: string;
  pluginsPath?: string;
  modsPath?: string;
  worldPath?: string;
  regionPath?: string;
```

Then update `InstanceSettingsModel` path fields to:

```ts
  serverPropertiesPath?: string;
  pluginsPath?: string;
  modsPath?: string;
  worldPath?: string;
  regionPath?: string;
```

- [ ] **Step 2: Add RegionPath validation rules**

In `MSLX.WebPanel/src/pages/instance/console/components/settingsComponents/GeneralSettings.vue`, after `worldPathRules`, add:

```ts
const regionPathRules: FormRules[string] = [
  {
    validator: (val: string) => isSafeRelativeInstancePath(val, 'region'),
    message: 'Region 目录路径必须是地图目录内的相对路径',
    trigger: 'blur',
  },
];
```

- [ ] **Step 3: Add RegionPath default form value**

In the `formData` default object, change the path block to:

```ts
  serverPropertiesPath: 'server.properties',
  pluginsPath: 'plugins',
  modsPath: 'mods',
  worldPath: 'world',
  regionPath: 'region',
  coreUrl: '',
```

- [ ] **Step 4: Add RegionPath to validation rules for custom mode**

In the `if (javaType.value === 'none')` return object, change the path rule block to:

```ts
      serverPropertiesPath: serverPropertiesPathRules,
      pluginsPath: pluginsPathRules,
      modsPath: modsPathRules,
      worldPath: worldPathRules,
      regionPath: regionPathRules,
```

- [ ] **Step 5: Add RegionPath to validation rules for normal mode**

In the normal `return` object from `rules`, change the path rule block to:

```ts
    serverPropertiesPath: serverPropertiesPathRules,
    pluginsPath: pluginsPathRules,
    modsPath: modsPathRules,
    worldPath: worldPathRules,
    regionPath: regionPathRules,
```

- [ ] **Step 6: Initialize RegionPath from backend settings**

In `initData()`, change the path normalization block to:

```ts
      serverPropertiesPath: normalizeRelativeInstancePath(res.serverPropertiesPath, 'server.properties'),
      pluginsPath: normalizeRelativeInstancePath(res.pluginsPath, 'plugins'),
      modsPath: normalizeRelativeInstancePath(res.modsPath, 'mods'),
      worldPath: normalizeRelativeInstancePath(res.worldPath, 'world'),
      regionPath: normalizeRelativeInstancePath(res.regionPath, 'region'),
```

- [ ] **Step 7: Normalize RegionPath before submit**

In `onSubmit()`, change the path normalization block to:

```ts
  formData.value.serverPropertiesPath = normalizeRelativeInstancePath(formData.value.serverPropertiesPath, 'server.properties');
  formData.value.pluginsPath = normalizeRelativeInstancePath(formData.value.pluginsPath, 'plugins');
  formData.value.modsPath = normalizeRelativeInstancePath(formData.value.modsPath, 'mods');
  formData.value.worldPath = normalizeRelativeInstancePath(formData.value.worldPath, 'world');
  formData.value.regionPath = normalizeRelativeInstancePath(formData.value.regionPath, 'region');
```

- [ ] **Step 8: Add RegionPath input to the settings UI**

In the template, after the existing “地图目录路径” block and before the sticky save button, insert:

```vue
        <div
          class="flex flex-col md:flex-row md:items-start justify-between p-3 md:p-4 border-b border-dashed border-zinc-100 dark:border-zinc-800/60 hover:bg-zinc-50/50 dark:hover:bg-zinc-800/20 transition-colors rounded-xl"
        >
          <div class="flex-1 pr-0 md:pr-8 mb-3 md:mb-0 min-w-[200px]">
            <div class="text-sm font-medium text-[var(--td-text-color-primary)] leading-snug">Region 目录路径</div>
            <div class="text-xs text-[var(--td-text-color-secondary)] mt-1 leading-relaxed">
              相对地图目录路径，用于世界渲染图读取 r.x.z.mca 文件。默认 region；下界可填 DIM-1/region，末地可填 DIM1/region
            </div>
          </div>
          <div class="w-full md:w-[340px] shrink-0 flex">
            <t-input v-model="formData.regionPath" placeholder="region 或 DIM-1/region" class="w-full" />
          </div>
        </div>
```

- [ ] **Step 9: Run frontend type check/build**

Run:

```powershell
npm --prefix MSLX.WebPanel run build
```

Expected: `vue-tsc` passes, Vite build succeeds, and `MSLX.Daemon/Frontend` assets are regenerated.

- [ ] **Step 10: Commit Task 4**

```bash
git add MSLX.WebPanel/src/api/model/instance.ts MSLX.WebPanel/src/pages/instance/console/components/settingsComponents/GeneralSettings.vue MSLX.Daemon/Frontend MSLX.Daemon/Frontend/build.json
git commit -m "feat: expose configurable region path in settings"
```

---

### Task 5: End-to-End Verification

**Files:**
- Verify only; no source file changes expected.

- [ ] **Step 1: Run targeted tests**

Run:

```powershell
dotnet test MSLX.Tests/MSLX.Tests.csproj --filter "WorldPath|RegionPath"
```

Expected: PASS.

- [ ] **Step 2: Build backend**

Run:

```powershell
dotnet build MSLX.Daemon/MSLX.Daemon.csproj
```

Expected: build succeeds with 0 errors. Existing warnings are acceptable.

- [ ] **Step 3: Run frontend build**

Run:

```powershell
npm --prefix MSLX.WebPanel run build
```

Expected: `vue-tsc` passes, Vite build succeeds, and frontend assets are copied to `MSLX.Daemon/Frontend`.

- [ ] **Step 4: Manually verify default behavior remains unchanged**

Use an instance whose world is still laid out as:

```text
{instance base}/world/level.dat
{instance base}/world/region/r.0.0.mca
```

Set:

```text
地图目录路径: world
Region 目录路径: region
```

Open the instance console, click the existing “游戏地图” entry, and confirm that the map still loads around the spawn point.

- [ ] **Step 5: Manually verify custom RegionPath**

Use an instance with this layout:

```text
{instance base}/world/level.dat
{instance base}/world/DIM-1/region/r.0.0.mca
```

Set:

```text
地图目录路径: world
Region 目录路径: DIM-1/region
```

Open the world map viewer and confirm the requested image URL still uses the existing endpoint shape:

```text
/api/instance/map/{id}/0/0
```

Expected behavior: backend reads the file from:

```text
{instance base}/world/DIM-1/region/r.0.0.mca
```

- [ ] **Step 6: Manually verify path rejection**

Try to save each invalid Region path in the settings form:

```text
../region
/region
C:/temp/region
DIM-1/../region
```

Expected: the form or backend rejects the save with:

```text
Region 目录路径必须是地图目录内的相对路径
```

- [ ] **Step 7: Commit verification-only changes if frontend assets changed after final build**

If `npm --prefix MSLX.WebPanel run build` regenerated assets after Task 4, commit them:

```bash
git add MSLX.Daemon/Frontend MSLX.Daemon/Frontend/build.json
git commit -m "build: refresh embedded frontend assets"
```

If there are no new changes, skip this commit.

---

## Self-Review Notes

- Spec coverage: The plan implements custom region folder support in backend persistence, backend rendering, frontend settings, request models, and tests.
- Backward compatibility: Default `RegionPath = "region"` preserves existing `{WorldPath}/region/r.x.z.mca` behavior.
- Security: `RegionPath` is validated as a relative path and resolved below the resolved world directory with `FileUtils.GetSafePath`.
- Type consistency: Backend uses `RegionPath`; frontend uses camelCase `regionPath`, matching existing JSON serialization conventions for `WorldPath`/`worldPath`.
- YAGNI: The public map image endpoint shape is unchanged; no new API route is introduced.
