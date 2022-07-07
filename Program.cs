using ManifestOffsetRepro;
using Microsoft.Azure.Management.Media;
using Microsoft.Azure.Management.Media.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Xml.Linq;

var baseConfig = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

AzureConfigWrapper config = new(baseConfig);

using IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((_, services) =>
        services
            .AddSingleton<IAzureMediaFactory, AzureMediaFactory>())
    .Build();

using IServiceScope serviceScope = host.Services.CreateScope();
IServiceProvider provider = serviceScope.ServiceProvider;
var mediaFactory = provider.GetRequiredService<IAzureMediaFactory>();

var assetName = "inputAsset-98fa6162ff5e4a9dbfc28fac1a3a0ebe";
var streamingLocatorName = "streamingLocator-98fa6162ff5e4a9dbfc28fac1a3a0ebe";
var offsetInSeconds = 100;
var streamingPolicyName = PredefinedStreamingPolicy.ClearStreamingOnly;

var firstResult = await ProcessVideoOffset(mediaFactory, config, assetName, offsetInSeconds, streamingLocatorName, streamingPolicyName);
Console.WriteLine($"First Offset Result: {firstResult}");

// Wait for a minute
Thread.Sleep(1000);

var secondResult = await ProcessVideoOffset(mediaFactory, config, assetName, offsetInSeconds, streamingLocatorName, streamingPolicyName);
Console.WriteLine($"Second Offset Result: {secondResult}");

if (firstResult != secondResult)
{
    Console.WriteLine("VALUES ARE NOT THE SAME");
}
else
{
    Console.WriteLine("Match");
}

/// <summary>
/// Process the video offset.
/// </summary>
/// <exception cref="ArgumentNullException">    Thrown when one or more required arguments are
///                                             null. </exception>
/// <param name="assetName">                Name of the asset. </param>
/// <param name="videoOffsetInSeconds">     The video offset in seconds. </param>
/// <param name="streamingLocatorName">     Name of the streaming locator. </param>
/// <param name="streamingPolicyName">      Name of the streaming policy. </param>
/// <param name="assetFilterName">          (Optional) Name of the asset filter. </param>
/// <param name="streamingEndPointName">    (Optional) Name of the streaming end point. </param>
/// <param name="token">                    (Optional) A token that allows processing to be
///                                         cancelled. </param>
/// <returns>
/// A long.
/// </returns>
async Task<long> ProcessVideoOffset(IAzureMediaFactory mediaFactory, AzureConfigWrapper configuration, string assetName, int videoOffsetInSeconds, string streamingLocatorName, string streamingPolicyName, string assetFilterName = "offsetFilter", string streamingEndPointName = "default", CancellationToken token = default)
{
    long offsetResult = 0L;
    
    if (videoOffsetInSeconds < 0)
    {
        videoOffsetInSeconds = 0;
    }

    var client = await mediaFactory.CreateMediaServicesClientAsync(configuration, token);

    var videoAsset = await client.Assets.GetAsync(configuration.ResourceGroup, configuration.AccountName, assetName, token);

    StreamingLocator? streamingLocator = null;
    try
    {
        streamingLocator = await client.StreamingLocators.GetAsync(configuration.ResourceGroup, configuration.AccountName, streamingLocatorName, token);
    }
    catch (Exception ex)
    {
        throw;
    }

    if (streamingLocator != null)
    {
        var streamingEndpoint = await client.StreamingEndpoints.GetAsync(configuration.ResourceGroup, configuration.AccountName, streamingEndPointName, cancellationToken: token);
        var hostname = streamingEndpoint.HostName;
        var scheme = "https";

        bool recreate = false;

        if (videoOffsetInSeconds <= 0)
        {
            try
            {
                var filter = await client.AssetFilters.GetAsync(
                   configuration.ResourceGroup, configuration.AccountName, assetName,
                   assetFilterName);
                await client.AssetFilters.DeleteAsync(configuration.ResourceGroup, configuration.AccountName, assetName,
                   assetFilterName);
                recreate = streamingLocator.Filters.Any();
            }
            catch
            {
                // Filter doesn't exist - nothing to do
                recreate = !streamingLocator.Filters.Any(a => a != null && a.Equals(assetFilterName, StringComparison.InvariantCultureIgnoreCase));
            }
        }
        else
        {
            var timescale = 10000000L;
            var playbackOffset = 0L;

            // Get the Timescale from the Manifest URL!
            var existingPaths = await client.StreamingLocators.ListPathsAsync(configuration.ResourceGroup, configuration.AccountName, streamingLocatorName);
            string? manifestUrl = null;
            foreach (var path in existingPaths.StreamingPaths)
            {
                if (path.Paths.Any())
                {
                    var updatedPath = path.Paths[0];
                    if (!updatedPath.StartsWith("/"))
                    {
                        updatedPath = "/" + updatedPath;
                    }
                    var manifestBase = $"{scheme}://{hostname}{updatedPath}";
                    manifestUrl = $"{manifestBase.Substring(0, manifestBase.LastIndexOf("/"))}/manifest";
                    try
                    {
                        var ts = GetTimeScaleAndFirstOffsetMarker(manifestUrl);
                        timescale = ts.timeScale;
                        playbackOffset = ts.firstOffsetMarker;
                    }
                    catch { }
                    break;
                }
            }

            var timestampOffset = (videoOffsetInSeconds * timescale) + playbackOffset;

            offsetResult = timestampOffset;
            
            var assetFilter = await client.AssetFilters.CreateOrUpdateAsync(
               configuration.ResourceGroup, configuration.AccountName, assetName,
               assetFilterName,
               new AssetFilter(
                   presentationTimeRange: new PresentationTimeRange(
                       startTimestamp: timestampOffset,
                       timescale: timescale
                   ))
               );
            
            recreate = !streamingLocator.Filters.Any(a => a != null && a.Equals(assetFilterName, StringComparison.InvariantCultureIgnoreCase));
        }

        if (recreate)
        {
            var streamingLocatorId = streamingLocator.StreamingLocatorId;
            await client.StreamingLocators.DeleteAsync(configuration.ResourceGroup, configuration.AccountName, streamingLocatorName); // Delete the old locator.
            var locator = await client.StreamingLocators.CreateAsync(
                    configuration.ResourceGroup, configuration.AccountName, streamingLocatorName,
                    new StreamingLocator
                    {
                        AssetName = assetName,
                        StreamingPolicyName = streamingPolicyName,
                        Filters = videoOffsetInSeconds <= 0 ? new List<string>() : new List<string> { assetFilterName },
                        StreamingLocatorId = streamingLocatorId
                    });
        }        
    }
    return offsetResult;
}

/// <summary>
/// Gets time scale and first offset marker.
/// </summary>
/// <param name="manifestUrl">          URL of the manifest. </param>
/// <param name="timeScale">            (Optional) The time scale. </param>
/// <param name="firstOffsetMarker">    (Optional) The first offset marker. </param>
/// <returns>
/// The time scale and first offset marker.
/// </returns>
(long timeScale, long firstOffsetMarker) GetTimeScaleAndFirstOffsetMarker(string manifestUrl, long timeScale = 10000000L, long firstOffsetMarker = 0L)
{
    var manifestDocument = XDocument.Load(manifestUrl);
    if (manifestDocument != null && manifestDocument.Root != null)
    {
        var videoIndex = manifestDocument.Root.Elements("StreamIndex").FirstOrDefault(a => a.Attribute("Type") != null && a.Attribute("Type")!.Value.ToLower() == "video");
        if (videoIndex != null)
        {
            var timeScaleElement = videoIndex.Attribute("TimeScale")?.Value;
            if (timeScaleElement != null && Int64.TryParse(timeScaleElement, out var actualTimescale))
            {
                timeScale = actualTimescale;
            }
            var timeIndex = videoIndex.Elements("c").FirstOrDefault();
            if (timeIndex != null)
            {
                var firstOffsetMarkerAttribute = timeIndex.Attribute("t");
                if (firstOffsetMarkerAttribute != null && Int64.TryParse(firstOffsetMarkerAttribute.Value, out var actualOffset))
                {
                    firstOffsetMarker = actualOffset;
                }
            }
        }
    }
    return (timeScale, firstOffsetMarker);
}