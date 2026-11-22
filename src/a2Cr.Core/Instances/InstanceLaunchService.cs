using a2Cr.Core.Errors;
using a2Cr.Core.Launch;
using a2Cr.Core.Meta;
using a2Cr.Core.Path;
using a2Cr.Core.Pool;
using a2Cr.Core.Settings;

namespace a2Cr.Core.Instances;

/// <summary>一次启动的显式覆盖；CLI 参数和 GUI 表单都落到这里。</summary>
public sealed record LaunchOverrides(
    string? Memory = null,
    string? JvmArgs = null,
    int? JavaMajor = null,
    string PlayerName = "a2Cr");

public sealed record EffectiveLaunchSettings(string MaxMemory, string JvmArgs, int? JavaMajor, string PlayerName)
{
    /// <summary>优先级：调用方显式值 &gt; 实例覆盖 &gt; 全局设置。</summary>
    public static EffectiveLaunchSettings Resolve(LaunchOverrides overrides, InstanceMetadata metadata,
        LauncherSettings settings)
    {
        var memory = MemoryValue.IsValid(overrides.Memory)
            ? MemoryValue.Normalize(overrides.Memory)
            : metadata.MaxMemoryOverride ?? settings.MaxMemory;
        var jvmArgs = overrides.JvmArgs ?? metadata.JvmArgsOverride ?? settings.JvmArgs;
        var javaMajor = overrides.JavaMajor ?? metadata.JavaMajorOverride;
        return new EffectiveLaunchSettings(memory, jvmArgs, javaMajor, overrides.PlayerName);
    }
}

public sealed record InstanceLaunchPlan(
    InstanceMetadata Metadata,
    LaunchPlan Plan,
    string GameDirectory,
    ReconcileReport Reconcile);

/// <summary>实例启动的完整准备过程；CLI 和 GUI 共用，避免两边各拼一套参数。</summary>
public sealed class InstanceLaunchService
{
    private readonly LauncherSettings _settings;
    private readonly InstanceManager _instances;
    private readonly PoolReconciler _reconciler;
    private readonly VersionCatalog _catalog;
    private readonly VersionMetaMerger _merger;
    private readonly LaunchArgumentBuilder _builder;
    private readonly GameProcessLauncher _processLauncher;

    public InstanceLaunchService(LauncherPaths paths, LauncherSettings settings,
        InstanceManager? instances = null, PoolReconciler? reconciler = null,
        VersionCatalog? catalog = null, LaunchArgumentBuilder? builder = null,
        GameProcessLauncher? processLauncher = null)
    {
        _settings = settings;
        _instances = instances ?? new InstanceManager(paths, settings);
        _reconciler = reconciler ?? new PoolReconciler(paths);
        _catalog = catalog ?? new VersionCatalog(paths);
        _merger = new VersionMetaMerger(paths);
        _builder = builder ?? new LaunchArgumentBuilder(paths);
        _processLauncher = processLauncher ?? new GameProcessLauncher();
    }

    public async Task<InstanceLaunchPlan> PrepareAsync(string folder, LaunchOverrides overrides,
        CancellationToken cancellationToken)
    {
        var metadata = await _instances.GetAsync(folder, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(metadata.VersionId))
        {
            throw new InstanceException("instance_broken", $"实例 {folder} 没有记录 version_id。",
                "编辑 instance.json 补上版本，或重新创建实例。");
        }

        await _catalog.GetAsync(metadata.VersionId, false, cancellationToken).ConfigureAwait(false);
        var reconcile = await _reconciler.ReconcileAsync(folder, metadata, cancellationToken)
            .ConfigureAwait(false);
        var meta = await _merger.ResolveAsync(metadata.VersionId, cancellationToken).ConfigureAwait(false);
        var effective = EffectiveLaunchSettings.Resolve(overrides, metadata, _settings);
        var gameDirectory = _instances.InstanceDirectory(folder);
        var plan = _builder.Build(meta, metadata.VersionId, new LaunchOptions(
            gameDirectory,
            PlayerName: effective.PlayerName,
            MaxMemory: effective.MaxMemory,
            JvmArgs: effective.JvmArgs,
            JavaMajor: effective.JavaMajor,
            JavaOverrides: _settings.JavaOverrides,
            JavaExtraPaths: _settings.ExtraJavaPaths));
        return new InstanceLaunchPlan(metadata, plan, gameDirectory, reconcile);
    }

    /// <summary>
    /// 进程创建成功后才写 last_played。用 TCS 和 RunAsync 赛跑，
    /// 避免进程启动失败时污染排序，也避免在回调里同步阻塞写文件。
    /// </summary>
    public async Task<LaunchResult> LaunchAsync(string folder, LaunchOverrides overrides,
        Action<string>? onLog, CancellationToken cancellationToken, bool detach = false)
    {
        var prepared = await PrepareAsync(folder, overrides, cancellationToken).ConfigureAwait(false);
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = _processLauncher.RunAsync(prepared.Plan, prepared.GameDirectory,
            new LaunchCallbacks(OnProcess: _ => started.TrySetResult(true), OnLog: onLog), cancellationToken,
            detach);
        await Task.WhenAny(started.Task, run).ConfigureAwait(false);
        if (started.Task.IsCompleted)
        {
            try
            {
                await _instances.MarkPlayedAsync(folder, CancellationToken.None).ConfigureAwait(false);
            }
            catch (LauncherException)
            {
                // 元数据写失败不影响已经启动的游戏。
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return await run.ConfigureAwait(false);
    }
}
