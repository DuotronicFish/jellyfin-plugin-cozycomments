using Jellyfin.Plugin.CozyComments.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.CozyComments.Data;

/// <summary>
/// One-time carry-over from the plugin's old name. Up to 1.1.0.0 the assembly was <c>Jellyfin.Plugin.Comments</c>,
/// so its data folder and settings file were named after that. If the new locations don't exist yet but the old
/// ones do, they are copied (never moved: the old folder stays as a backup and can be deleted by hand later).
/// </summary>
public static class LegacyMigration
{
    /// <summary>The plugin's former assembly name.</summary>
    public const string LegacyName = "Jellyfin.Plugin.Comments";

    /// <summary>The current assembly name, which names the data folder.</summary>
    public const string CurrentName = "Jellyfin.Plugin.CozyComments";

    private static readonly object s_lock = new();
    private static bool s_dataDone;

    /// <summary>Gets the data folder (database, backups, cover image), migrating the old one first if needed.</summary>
    /// <param name="paths">Jellyfin application paths.</param>
    /// <param name="logger">Logger.</param>
    /// <returns>The data folder path.</returns>
    public static string DataDirectory(IApplicationPaths paths, ILogger logger)
    {
        var current = Path.Combine(paths.PluginConfigurationsPath, CurrentName);
        lock (s_lock)
        {
            if (s_dataDone)
            {
                return current;
            }

            s_dataDone = true;
            var legacy = Path.Combine(paths.PluginConfigurationsPath, LegacyName);
            if (Directory.Exists(current) || !Directory.Exists(legacy))
            {
                return current;
            }

            try
            {
                CopyDirectory(legacy, current);
                logger.LogInformation("[Cozy Comments] Copied data from {Old} to {New}. The old folder was kept and can be deleted.", legacy, current);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[Cozy Comments] Could not copy old data from {Old} to {New}", legacy, current);
                try
                {
                    // Leave no half-copied folder behind, so the next start tries again.
                    if (Directory.Exists(current))
                    {
                        Directory.Delete(current, true);
                    }
                }
                catch (Exception cleanupEx)
                {
                    logger.LogError(cleanupEx, "[Cozy Comments] Could not remove the partial copy at {New}", current);
                }

                s_dataDone = false;
            }
        }

        return current;
    }

    /// <summary>
    /// Returns the old settings if the plugin has no settings file under its new name yet but has one under the old name.
    /// </summary>
    /// <param name="paths">Jellyfin application paths.</param>
    /// <param name="xmlSerializer">Jellyfin XML serializer.</param>
    /// <param name="newConfigPath">The settings file path under the new name.</param>
    /// <returns>The old settings, or null if there is nothing to migrate.</returns>
    public static PluginConfiguration? LegacySettings(IApplicationPaths paths, IXmlSerializer xmlSerializer, string newConfigPath)
    {
        var legacy = Path.Combine(paths.PluginConfigurationsPath, LegacyName + ".xml");
        if (File.Exists(newConfigPath) || !File.Exists(legacy))
        {
            return null;
        }

        try
        {
            return xmlSerializer.DeserializeFromFile(typeof(PluginConfiguration), legacy) as PluginConfiguration;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from))
        {
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        }

        foreach (var dir in Directory.GetDirectories(from))
        {
            CopyDirectory(dir, Path.Combine(to, Path.GetFileName(dir)));
        }
    }
}
