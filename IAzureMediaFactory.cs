using Microsoft.Azure.Management.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ManifestOffsetRepro
{
    /// <summary>
    /// Interface for azure media factory.
    /// </summary>
    public interface IAzureMediaFactory
    {
        /// <summary>
        /// Creates media services client asynchronous.
        /// </summary>
        /// <param name="configuration"> The configuration.</param>
        /// <param name="token"> (Optional) A token that allows processing to be cancelled.</param>
        /// <returns>
        /// The create media services client.
        /// </returns>
        public Task<IAzureMediaServicesClient> CreateMediaServicesClientAsync(AzureConfigWrapper configuration, CancellationToken token = default);        
    }
}
