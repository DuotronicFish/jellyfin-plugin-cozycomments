using System.Security.Cryptography;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.CozyComments.Data;

/// <summary>
/// The optional cover image of the home card, stored next to the database as
/// <c>cover.jpg</c> / <c>cover.png</c> / <c>cover.webp</c> / <c>cover.gif</c>.
/// </summary>
public sealed class CoverStore
{
    /// <summary>Largest accepted upload.</summary>
    public const int MaxBytes = 5 * 1024 * 1024;

    private static readonly (string Ext, string Type)[] s_types =
    {
        ("jpg", "image/jpeg"), ("png", "image/png"), ("webp", "image/webp"), ("gif", "image/gif"),
    };

    private readonly string _dir;
    private readonly ILogger<CoverStore> _logger;
    private readonly object _lock = new();
    private (string Path, DateTime Stamp, byte[] Bytes, string Type, string Tag)? _cache;

    /// <summary>Initializes a new instance of the <see cref="CoverStore"/> class.</summary>
    /// <param name="paths">Jellyfin application paths.</param>
    /// <param name="logger">Logger.</param>
    public CoverStore(IApplicationPaths paths, ILogger<CoverStore> logger)
    {
        _dir = LegacyMigration.DataDirectory(paths, logger);
        _logger = logger;
    }

    /// <summary>Returns the current cover, or null if none is set.</summary>
    /// <returns>Bytes, content type and a short content hash.</returns>
    public (byte[] Bytes, string Type, string Tag)? Get()
    {
        lock (_lock)
        {
            foreach (var (ext, type) in s_types)
            {
                var p = Path.Combine(_dir, "cover." + ext);
                if (!File.Exists(p))
                {
                    continue;
                }

                var stamp = File.GetLastWriteTimeUtc(p);
                if (_cache is { } c && c.Path == p && c.Stamp == stamp)
                {
                    return (c.Bytes, c.Type, c.Tag);
                }

                var bytes = File.ReadAllBytes(p);
                var tag = Convert.ToHexString(SHA256.HashData(bytes))[..12].ToLowerInvariant();
                _cache = (p, stamp, bytes, type, tag);
                return (bytes, type, tag);
            }

            _cache = null;
            return null;
        }
    }

    /// <summary>Detects the image type from its first bytes.</summary>
    /// <param name="b">Image bytes.</param>
    /// <returns>The file extension, or null if it isn't a JPEG/PNG/WebP/GIF.</returns>
    public static string? Sniff(byte[] b)
    {
        if (b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF)
        {
            return "jpg";
        }

        if (b.Length > 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47)
        {
            return "png";
        }

        if (b.Length > 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P')
        {
            return "webp";
        }

        if (b.Length > 6 && b[0] == 'G' && b[1] == 'I' && b[2] == 'F' && b[3] == '8')
        {
            return "gif";
        }

        return null;
    }

    /// <summary>Replaces the cover.</summary>
    /// <param name="bytes">Image bytes (already validated with <see cref="Sniff"/>).</param>
    /// <param name="ext">Extension from <see cref="Sniff"/>.</param>
    public void Save(byte[] bytes, string ext)
    {
        lock (_lock)
        {
            Directory.CreateDirectory(_dir);
            var target = Path.Combine(_dir, "cover." + ext);
            var tmp = target + ".tmp";
            File.WriteAllBytes(tmp, bytes);
            RemoveFiles();
            File.Move(tmp, target, overwrite: true);
            _cache = null;
            _logger.LogInformation("[Cozy Comments] Cover image updated ({Bytes} bytes, {Ext})", bytes.Length, ext);
        }
    }

    /// <summary>Removes the cover (the home card falls back to an icon).</summary>
    public void Delete()
    {
        lock (_lock)
        {
            RemoveFiles();
            _cache = null;
        }
    }

    private void RemoveFiles()
    {
        foreach (var (ext, _) in s_types)
        {
            var p = Path.Combine(_dir, "cover." + ext);
            if (File.Exists(p))
            {
                File.Delete(p);
            }
        }
    }
}
