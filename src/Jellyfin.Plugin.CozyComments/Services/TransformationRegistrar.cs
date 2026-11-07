using System.Reflection;
using System.Runtime.Loader;
using Jellyfin.Plugin.CozyComments.Api;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.CozyComments.Services;

/// <summary>Payload the File Transformation plugin hands to our callback (deserialised from {"contents": ...}).</summary>
public sealed class TransformPayload
{
    /// <summary>Gets or sets the file contents.</summary>
    public string Contents { get; set; } = string.Empty;
}

/// <summary>In-memory edit of the served <c>/web/index.html</c>: adds the plugin's stylesheet and script tags.</summary>
public static class IndexTransformer
{
    /// <summary>Marker that makes the transformation idempotent.</summary>
    public const string Marker = "<!-- cozycomments-plugin -->";

    /// <summary>Called by File Transformation for every served index.html.</summary>
    /// <param name="payload">The original file.</param>
    /// <returns>The file with our tags inserted before &lt;/body&gt;.</returns>
    public static string TransformIndex(TransformPayload payload)
    {
        var html = payload.Contents ?? string.Empty;
        if (html.Contains(Marker, StringComparison.Ordinal))
        {
            return html;
        }

        var v = CommentsController.AssetVersion();
        var tags = $"{Marker}<link rel=\"stylesheet\" href=\"../CozyComments/client.css?v={v}\"><script defer src=\"../CozyComments/client.js?v={v}\"></script>";
        var at = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        return at < 0 ? html + tags : html.Insert(at, tags);
    }
}

/// <summary>
/// Registers <see cref="IndexTransformer"/> with the File Transformation plugin at startup
/// (in-process callback through reflection, as File Transformation documents).
/// </summary>
public sealed class TransformationRegistrar : IHostedService
{
    private static readonly Guid TransformId = Guid.Parse("7f3c2a51-0b8e-4f0e-9a51-2c7d1e0b6c11");
    private readonly ILogger<TransformationRegistrar> _logger;

    /// <summary>Initializes a new instance of the <see cref="TransformationRegistrar"/> class.</summary>
    /// <param name="logger">Logger.</param>
    public TransformationRegistrar(ILogger<TransformationRegistrar> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var ft = AssemblyLoadContext.All
                .SelectMany(c => c.Assemblies)
                .FirstOrDefault(a => a.GetName().Name == "Jellyfin.Plugin.FileTransformation");
            var iface = ft?.GetType("Jellyfin.Plugin.FileTransformation.PluginInterface");
            var register = iface?.GetMethod("RegisterTransformation", BindingFlags.Public | BindingFlags.Static);
            if (register is null)
            {
                _logger.LogWarning("[Cozy Comments] The 'File Transformation' plugin is not installed or not active. Install it, then restart Jellyfin; until then the web client will not show comments.");
                return Task.CompletedTask;
            }

            var payload = new JObject
            {
                ["id"] = TransformId.ToString(),
                ["fileNamePattern"] = "index.html",
                ["callbackAssembly"] = typeof(IndexTransformer).Assembly.FullName,
                ["callbackClass"] = typeof(IndexTransformer).FullName,
                ["callbackMethod"] = nameof(IndexTransformer.TransformIndex),
            };
            register.Invoke(null, new object?[] { payload });
            _logger.LogInformation("[Cozy Comments] Registered the index.html transformation with File Transformation");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Cozy Comments] Could not register the index.html transformation");
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
