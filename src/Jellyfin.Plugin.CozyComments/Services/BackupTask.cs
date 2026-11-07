using Jellyfin.Plugin.CozyComments.Data;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.CozyComments.Services;

/// <summary>Daily online backup of comments.db (Dashboard → Scheduled Tasks → Cozy Comments).</summary>
public sealed class BackupTask : IScheduledTask
{
    private readonly CommentStore _store;
    private readonly ILogger<BackupTask> _logger;

    /// <summary>Initializes a new instance of the <see cref="BackupTask"/> class.</summary>
    /// <param name="store">Comment store.</param>
    /// <param name="logger">Logger.</param>
    public BackupTask(CommentStore store, ILogger<BackupTask> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Back up comments database";

    /// <inheritdoc />
    public string Key => "CozyCommentsBackup";

    /// <inheritdoc />
    public string Description => "Copies comments.db to backups/comments-YYYY-MM-DD.db next to it and keeps the newest N (plugin setting).";

    /// <inheritdoc />
    public string Category => "Cozy Comments";

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var keep = Plugin.Settings.BackupsToKeep;
        if (keep <= 0)
        {
            _logger.LogInformation("[Cozy Comments] Backups are turned off (BackupsToKeep = 0)");
            progress.Report(100);
            return;
        }

        var path = await _store.BackupAsync(keep).ConfigureAwait(false);
        _logger.LogInformation("[Cozy Comments] Backup written to {Path}", path);
        progress.Report(100);
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        // Daily at 03:30 plus once at every server start.
        yield return new TaskTriggerInfo { Type = TaskTriggerInfoType.DailyTrigger, TimeOfDayTicks = TimeSpan.FromMinutes(210).Ticks };
        yield return new TaskTriggerInfo { Type = TaskTriggerInfoType.StartupTrigger };
    }
}
