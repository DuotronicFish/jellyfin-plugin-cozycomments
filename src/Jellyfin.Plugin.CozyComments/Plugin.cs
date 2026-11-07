using Jellyfin.Plugin.CozyComments.Configuration;
using Jellyfin.Plugin.CozyComments.Data;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.CozyComments;

/// <summary>Cozy Comments: a comment and rating system for shared users on Jellyfin. Per-item comments with optional star ratings, spoilers and replies.</summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>The plugin GUID (also used in the repository manifest).</summary>
    public static readonly Guid PluginId = Guid.Parse("dc9c7d6f-2de4-4ee4-9187-6a87ca998510");

    /// <summary>Initializes a new instance of the <see cref="Plugin"/> class.</summary>
    /// <param name="applicationPaths">Jellyfin application paths.</param>
    /// <param name="xmlSerializer">Jellyfin XML serializer.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;

        // Carry over the settings saved under the plugin's old name (Jellyfin.Plugin.Comments.xml), once.
        var legacy = LegacyMigration.LegacySettings(applicationPaths, xmlSerializer, ConfigurationFilePath);
        if (legacy != null)
        {
            UpdateConfiguration(legacy);
        }
    }

    /// <summary>Gets the running plugin instance.</summary>
    public static Plugin? Instance { get; private set; }

    /// <summary>Gets the current configuration, or defaults if the plugin is not initialised yet.</summary>
    public static PluginConfiguration Settings => Instance?.Configuration ?? new PluginConfiguration();

    /// <inheritdoc />
    public override Guid Id => PluginId;

    /// <inheritdoc />
    public override string Name => "Cozy Comments";

    /// <inheritdoc />
    public override string Description => "Comments, optional 1-5 star ratings, spoilers and replies on movie, series and episode pages, plus a recent-comments page.";

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = Name,
            EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html",
        };
    }
}
