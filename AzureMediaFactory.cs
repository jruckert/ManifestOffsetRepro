using Microsoft.Azure.Management.Media;
using Microsoft.Identity.Client;
using Microsoft.Rest;
using Newtonsoft.Json;

namespace ManifestOffsetRepro
{
    /// <summary>
    /// An azure media factory.
    /// </summary>
    public class AzureMediaFactory : IAzureMediaFactory
    {
        /// <summary>
        /// (Immutable) type of the token.
        /// </summary>
        private static readonly string TokenType = "Bearer";
        
        /// <summary>
        /// Creates media services client asynchronous.
        /// </summary>
        /// <param name="configuration"> The configuration.</param>
        /// <param name="token"> (Optional) A token that allows processing to be cancelled.</param>
        /// <returns>
        /// The create media services client.
        /// </returns>
        public async Task<IAzureMediaServicesClient> CreateMediaServicesClientAsync(AzureConfigWrapper configuration, CancellationToken token = default)
        {
            var credentials = await GetCredentialsAsync(configuration, token);
            return new AzureMediaServicesClient(configuration.ArmEndpoint, credentials)
            {
                SubscriptionId = configuration.SubscriptionId
            };
        }
        
        /// <summary>
        /// Gets credentials asynchronous.
        /// </summary>
        /// <param name="config"> The configuration.</param>
        /// <param name="token"> (Optional) A token that allows processing to be cancelled.</param>
        /// <returns>
        /// The credentials.
        /// </returns>
        private async Task<ServiceClientCredentials> GetCredentialsAsync(AzureConfigWrapper config, CancellationToken token = default)
        {
            var scopes = new[] { config.ArmAadAudience + "/.default" };

            var app = ConfidentialClientApplicationBuilder.Create(config.AadClientId)
                .WithClientSecret(config.AadSecret)
                .WithAuthority(AzureCloudInstance.AzurePublic, config.AadTenantId)
                .Build();

            var authResult = await app.AcquireTokenForClient(scopes)
                                                     .ExecuteAsync(token)
                                                     .ConfigureAwait(false);

            return new TokenCredentials(authResult.AccessToken, TokenType);
        }
    }
}
