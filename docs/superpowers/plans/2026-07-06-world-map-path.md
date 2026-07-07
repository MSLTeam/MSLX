# Custom World Map Path Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让“世界渲染图”像 server.properties / 插件 / 模组路径一样支持实例级自定义地图目录。

**Architecture:** 在实例配置模型中新增 `WorldPath`，默认值为 `world`。后端地图渲染接口统一通过安全相对路径解析得到世界目录，再读取 `{WorldPath}/level.dat` 和 `{WorldPath}/region/r.x.z.mca`；前端通用设置页新增“地图目录路径”输入项并随实例设置保存。

**Tech Stack:** ASP.NET Core 10, C#, xUnit, Vue 3, TypeScript, TDesign Vue Next, Vite.

---

## File Structure

- Modify: `MSLX.SDK/Models/MCServerInfo.cs`
  - Add persisted instance field `WorldPath` with default `world`.
- Modify: `MSLX.SDK/Models/Instance/UpdateServerRequest.cs`
  - Add request field `WorldPath` and validate it as a safe relative instance path.
- Modify: `MSLX.Daemon/Controllers/InstanceControllers/InstanceSettingsController.cs`
  - Save `WorldPath` when settings are saved without update task.
- Modify: `MSLX.Daemon/Services/DeployServerService/ServerUpdateService.cs`
  - Save `WorldPath` when settings are saved through update task.
- Modify: `MSLX.Daemon/Controllers/InstanceControllers/MapRenderController.cs`
  - Replace hard-coded `world` path with normalized `server.WorldPath`.
- Modify: `MSLX.Tests/ServerPropertiesPathUtilsTests.cs`
  - Add tests covering world path normalization and request validation.
- Modify: `MSLX.WebPanel/src/api/model/instance.ts`
  - Add `worldPath?: string` to instance settings/update models.
- Modify: `MSLX.WebPanel/src/pages/instance/console/components/settingsComponents/GeneralSettings.vue`
  - Add `worldPath` default, validation, initialization, submit normalization, and UI input beside the existing path settings.
- Build output: `MSLX.Daemon/Frontend/**`
  - Running `npm --prefix MSLX.WebPanel run build` will regenerate embedded frontend assets.

---

### Task 1: Add and Test Backend Model Validation

**Files:**
- Modify: `MSLX.SDK/Models/MCServerInfo.cs:32-35`
- Modify: `MSLX.SDK/Models/Instance/UpdateServerRequest.cs:64-111`
- Modify: `MSLX.Tests/ServerPropertiesPathUtilsTests.cs`

- [ ] **Step 1: Add failing tests for world path defaults and validation**

Append these tests to `ServerPropertiesPathUtilsRequestTests` in `MSLX.Tests/ServerPropertiesPathUtilsTests.cs`, before `CreateValidRequest()`:

```csharp
    [Fact]
    public void UpdateServerRequest_accepts_blank_world_path_as_default()
    {
        var request = CreateValidRequest();
        request.WorldPath = "";

        var results = Validate(request);

        Assert.DoesNotContain(results, result => result.MemberNames.Contains(nameof(request.WorldPath)));
    }

    [Theory]
    [InlineData("world")]
    [InlineData("survival_world")]
    [InlineData("saves/world")]
    [InlineData(@"saves\world")]
    public void UpdateServerRequest_accepts_safe_relative_world_path(string path)
    {
        var request = CreateValidRequest();
        request.WorldPath = path;

        var results = Validate(request);

        Assert.DoesNotContain(results, result => result.MemberNames.Contains(nameof(request.WorldPath)));
    }

    [Theory]
    [InlineData("../world")]
    [InlineData("saves/../world")]
    [InlineData("/tmp/world")]
    [InlineData(@"C:\temp\world")]
    public void UpdateServerRequest_rejects_unsafe_world_path(string path)
    {
        var request = CreateValidRequest();
        request.WorldPath = path;

        var results = Validate(request);

        Assert.Contains(results, result =>
            result.MemberNames.Contains(nameof(request.WorldPath)) &&
            result.ErrorMessage == "地图目录路径必须是实例目录内的相对路径");
    }
```

- [ ] **Step 2: Run tests to verify the new tests fail**

Run:

```powershell
dotnet test MSLX.Tests/MSLX.Tests.csproj --filter WorldPath
```

Expected: FAIL, because `UpdateServerRequest` does not yet contain `WorldPath`.

- [ ] **Step 3: Add `WorldPath` to persisted server model**

In `MSLX.SDK/Models/MCServerInfo.cs`, change the path field block to:

```csharp
            public string ServerPropertiesPath { get; set; } = "server.properties";
            public string PluginsPath { get; set; } = "plugins";
            public string ModsPath { get; set; } = "mods";
            public string WorldPath { get; set; } = "world";
```

- [ ] **Step 4: Add `WorldPath` to update request model and validation**

In `MSLX.SDK/Models/Instance/UpdateServerRequest.cs`, change the path fields to:

```csharp
    public string ServerPropertiesPath { get; set; } = "server.properties";
    public string PluginsPath { get; set; } = "plugins";
    public string ModsPath { get; set; } = "mods";
    public string WorldPath { get; set; } = "world";
```

Then in `Validate()` after the ModsPath validation block, add:

```csharp
        foreach (var result in ValidateRelativeInstancePath(WorldPath, "world", "地图目录路径必须是实例目录内的相对路径", nameof(WorldPath)))
            yield return result;
```

The validation block should be:

```csharp
        foreach (var result in ValidateRelativeInstancePath(ServerPropertiesPath, "server.properties", "server.properties 路径必须是实例目录内的相对路径", nameof(ServerPropertiesPath)))
            yield return result;
        foreach (var result in ValidateRelativeInstancePath(PluginsPath, "plugins", "插件目录路径必须是实例目录内的相对路径", nameof(PluginsPath)))
            yield return result;
        foreach (var result in ValidateRelativeInstancePath(ModsPath, "mods", "模组目录路径必须是实例目录内的相对路径", nameof(ModsPath)))
            yield return result;
        foreach (var result in ValidateRelativeInstancePath(WorldPath, "world", "地图目录路径必须是实例目录内的相对路径", nameof(WorldPath)))
            yield return result;
```

- [ ] **Step 5: Run targeted tests**

Run:

```powershell
dotnet test MSLX.Tests/MSLX.Tests.csproj --filter WorldPath
```

Expected: PASS.

- [ ] **Step 6: Commit Task 1**

```bash
git add MSLX.SDK/Models/MCServerInfo.cs MSLX.SDK/Models/Instance/UpdateServerRequest.cs MSLX.Tests/ServerPropertiesPathUtilsTests.cs
git commit -m "feat: add configurable world path model"
```

---

### Task 2: Persist WorldPath From Instance Settings

**Files:**
- Modify: `MSLX.Daemon/Controllers/InstanceControllers/InstanceSettingsController.cs:126-131`
- Modify: `MSLX.Daemon/Services/DeployServerService/ServerUpdateService.cs:115-118`

- [ ] **Step 1: Save `WorldPath` in direct settings updates**

In `MSLX.Daemon/Controllers/InstanceControllers/InstanceSettingsController.cs`, update the path normalization block to:

```csharp
            try
            {
                server.ServerPropertiesPath = ServerPropertiesPathUtils.NormalizeRelativePath(request.ServerPropertiesPath);
                server.PluginsPath = ServerPropertiesPathUtils.NormalizeRelativePath(request.PluginsPath, "plugins", "插件目录路径必须是实例目录内的相对路径");
                server.ModsPath = ServerPropertiesPathUtils.NormalizeRelativePath(request.ModsPath, "mods", "模组目录路径必须是实例目录内的相对路径");
                server.WorldPath = ServerPropertiesPathUtils.NormalizeRelativePath(request.WorldPath, "world", "地图目录路径必须是实例目录内的相对路径");
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

- [ ] **Step 2: Save `WorldPath` in update-task settings updates**

In `MSLX.Daemon/Services/DeployServerService/ServerUpdateService.cs`, update the path assignment block to:

```csharp
            server.ServerPropertiesPath = ServerPropertiesPathUtils.NormalizeRelativePath(req.ServerPropertiesPath);
            server.PluginsPath = ServerPropertiesPathUtils.NormalizeRelativePath(req.PluginsPath, "plugins", "插件目录路径必须是实例目录内的相对路径");
            server.ModsPath = ServerPropertiesPathUtils.NormalizeRelativePath(req.ModsPath, "mods", "模组目录路径必须是实例目录内的相对路径");
            server.WorldPath = ServerPropertiesPathUtils.NormalizeRelativePath(req.WorldPath, "world", "地图目录路径必须是实例目录内的相对路径");
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
git commit -m "feat: persist configurable world path"
```

---

### Task 3: Use WorldPath in Map Rendering

**Files:**
- Modify: `MSLX.Daemon/Controllers/InstanceControllers/MapRenderController.cs:122-166`

- [ ] **Step 1: Replace base-path helper with world-path helper**

In `MapRenderController.cs`, replace:

```csharp
    private string GetServerBasePath(uint id)
    {
        var server = IConfigBase.ServerList.GetServer(id) ?? throw new Exception("实例不存在");
        return server.Base;
    }
```

with:

```csharp
    private string GetWorldPath(uint id)
    {
        var server = IConfigBase.ServerList.GetServer(id) ?? throw new Exception("实例不存在");
        var relativePath = ServerPropertiesPathUtils.NormalizeRelativePath(
            server.WorldPath,
            "world",
            "地图目录路径必须是实例目录内的相对路径"
        );
        var check = FileUtils.GetSafePath(server.Base, relativePath);
        if (!check.IsSafe) throw new ArgumentException(check.Message);
        return check.FullPath;
    }
```

- [ ] **Step 2: Use world path for spawn lookup**

In `GetWorldSpawn`, replace:

```csharp
            var basePath = GetServerBasePath(id);
            var levelDatPath = Path.Combine(basePath, "world", "level.dat");
```

with:

```csharp
            var worldPath = GetWorldPath(id);
            var levelDatPath = Path.Combine(worldPath, "level.dat");
```

- [ ] **Step 3: Use world path for region lookup**

In `GetRegionMap`, replace:

```csharp
            var basePath = GetServerBasePath(id);
            var mcaFilePath = Path.Combine(basePath, "world", "region", $"r.{regionX}.{regionZ}.mca");
```

with:

```csharp
            var worldPath = GetWorldPath(id);
            var mcaFilePath = Path.Combine(worldPath, "region", $"r.{regionX}.{regionZ}.mca");
```

- [ ] **Step 4: Build backend**

Run:

```powershell
dotnet build MSLX.Daemon/MSLX.Daemon.csproj
```

Expected: build succeeds with 0 errors.

- [ ] **Step 5: Commit Task 3**

```bash
git add MSLX.Daemon/Controllers/InstanceControllers/MapRenderController.cs
git commit -m "feat: render maps from configurable world path"
```

---

### Task 4: Add WorldPath to WebPanel Types and UI

**Files:**
- Modify: `MSLX.WebPanel/src/api/model/instance.ts:79-82,112-115`
- Modify: `MSLX.WebPanel/src/pages/instance/console/components/settingsComponents/GeneralSettings.vue`

- [ ] **Step 1: Add `worldPath` to TypeScript models**

In `MSLX.WebPanel/src/api/model/instance.ts`, update both model interfaces so their path fields are:

```ts
  serverPropertiesPath?: string;
  pluginsPath?: string;
  modsPath?: string;
  worldPath?: string;
```

Apply this in both `UpdateInstanceModel` and `InstanceSettingsModel`.

- [ ] **Step 2: Add default value to form data**

In `GeneralSettings.vue`, update the form defaults near `serverPropertiesPath` to:

```ts
  serverPropertiesPath: 'server.properties',
  pluginsPath: 'plugins',
  modsPath: 'mods',
  worldPath: 'world',
```

- [ ] **Step 3: Add validation rule**

After `modsPathRules`, add:

```ts
const worldPathRules: FormRules[string] = [
  {
    validator: (val: string) => isSafeRelativeInstancePath(val, 'world'),
    message: '地图目录路径必须是实例目录内的相对路径',
    trigger: 'blur',
  },
];
```

Then in both branches of `rules`, include:

```ts
      worldPath: worldPathRules,
```

The non-custom branch should include:

```ts
    serverPropertiesPath: serverPropertiesPathRules,
    pluginsPath: pluginsPathRules,
    modsPath: modsPathRules,
    worldPath: worldPathRules,
```

- [ ] **Step 4: Load world path from backend**

In `initData()`, update the normalized path assignments to:

```ts
      serverPropertiesPath: normalizeRelativeInstancePath(res.serverPropertiesPath, 'server.properties'),
      pluginsPath: normalizeRelativeInstancePath(res.pluginsPath, 'plugins'),
      modsPath: normalizeRelativeInstancePath(res.modsPath, 'mods'),
      worldPath: normalizeRelativeInstancePath(res.worldPath, 'world'),
```

- [ ] **Step 5: Normalize world path before submit**

In `onSubmit()`, update the normalization block to:

```ts
  formData.value.serverPropertiesPath = normalizeRelativeInstancePath(formData.value.serverPropertiesPath, 'server.properties');
  formData.value.pluginsPath = normalizeRelativeInstancePath(formData.value.pluginsPath, 'plugins');
  formData.value.modsPath = normalizeRelativeInstancePath(formData.value.modsPath, 'mods');
  formData.value.worldPath = normalizeRelativeInstancePath(formData.value.worldPath, 'world');
```

- [ ] **Step 6: Add matching UI input beside the existing path settings**

In `GeneralSettings.vue`, insert this block after “模组目录路径” and before the save buttons:

```vue
        <div
          class="flex flex-col md:flex-row md:items-start justify-between p-3 md:p-4 border-b border-dashed border-zinc-100 dark:border-zinc-800/60 hover:bg-zinc-50/50 dark:hover:bg-zinc-800/20 transition-colors rounded-xl"
        >
          <div class="flex-1 pr-0 md:pr-8 mb-3 md:mb-0 min-w-[200px]">
            <div class="text-sm font-medium text-[var(--td-text-color-primary)] leading-snug">地图目录路径</div>
            <div class="text-xs text-[var(--td-text-color-secondary)] mt-1 leading-relaxed">
              相对实例路径，用于世界渲染图读取 level.dat 和 region 地图文件
            </div>
          </div>
          <div class="w-full md:w-[340px] shrink-0 flex">
            <t-input v-model="formData.worldPath" placeholder="world 或 saves/world" class="w-full" />
          </div>
        </div>
```

- [ ] **Step 7: Run frontend type/build check**

Run:

```powershell
npm --prefix MSLX.WebPanel run build
```

Expected: command exits 0. Existing Vite/Rollup/LightningCSS warnings are acceptable.

- [ ] **Step 8: Commit Task 4**

```bash
git add MSLX.WebPanel/src/api/model/instance.ts MSLX.WebPanel/src/pages/instance/console/components/settingsComponents/GeneralSettings.vue MSLX.Daemon/Frontend
git commit -m "feat: add world path setting to web panel"
```

---

### Task 5: End-to-End Verification

**Files:**
- No required source modifications unless verification finds a defect.

- [ ] **Step 1: Run backend tests**

Run:

```powershell
dotnet test MSLX.Tests/MSLX.Tests.csproj
```

Expected: all tests pass.

- [ ] **Step 2: Run backend build**

Run:

```powershell
dotnet build MSLX.Daemon/MSLX.Daemon.csproj
```

Expected: build succeeds with 0 errors.

- [ ] **Step 3: Run frontend build**

Run:

```powershell
npm --prefix MSLX.WebPanel run build
```

Expected: build succeeds with exit code 0.

- [ ] **Step 4: Manual runtime verification**

Run:

```powershell
dotnet run --project MSLX.Daemon
```

Then in the browser:

1. Open an instance.
2. Go to `实例设置`.
3. Confirm the path settings show:
   - `Server.properties 路径`
   - `插件目录路径`
   - `模组目录路径`
   - `地图目录路径`
4. Set `地图目录路径` to a safe relative path such as `world` or `saves/world`.
5. Save settings.
6. Open `更多功能 -> 世界渲染图`.
7. Confirm it requests tiles from `/api/instance/map/{id}/{regionX}/{regionZ}` and renders from the configured folder.

- [ ] **Step 5: Verify unsafe path rejection**

In `实例设置`, try setting `地图目录路径` to:

```text
../world
```

Expected: frontend validation rejects it or backend returns `地图目录路径必须是实例目录内的相对路径`.

- [ ] **Step 6: Final status check**

Run:

```powershell
git status --short
```

Expected: only intentional source files and regenerated frontend assets are modified. Confirm `.claude/` remains uncommitted unless explicitly intended.

- [ ] **Step 7: Commit verification updates if any generated assets changed**

```bash
git add MSLX.Daemon/Frontend MSLX.WebPanel/src/api/model/instance.ts MSLX.WebPanel/src/pages/instance/console/components/settingsComponents/GeneralSettings.vue
git commit -m "chore: rebuild web panel assets for world path setting"
```

Skip this commit if Task 4 already committed the generated assets and no files changed.

---

## Self-Review

**Spec coverage:** The plan adds an instance-level configurable world/map directory, persists it through both settings save paths, uses it in both spawn and region map endpoints, exposes it in WebPanel UI next to existing path settings, and verifies safe relative-path behavior.

**Placeholder scan:** No TBD/TODO/placeholders remain. All implementation steps include exact file paths and code snippets.

**Type consistency:** Property name is consistently `WorldPath` in C# and `worldPath` in TypeScript. Default is consistently `world`. Error message is consistently `地图目录路径必须是实例目录内的相对路径`.
