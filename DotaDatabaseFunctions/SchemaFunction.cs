using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SteamWebAPI2.Interfaces;
using SteamWebAPI2.Utilities;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace DotaDatabaseFunctions
{
    public class SchemaFunction
    {
        private const string schemaStorageContainerName = "schemas";
        private const string schemaFileName = "items_game.vdf";

        private EconItems econItems;

        [FunctionName("SchemaFunction")]
        public async Task Run([TimerTrigger("0 * * * * Sat")]TimerInfo myTimer,
            ExecutionContext context,
            ILogger log)
        {
            log.LogInformation($"Starting {nameof(SchemaFunction)} function.");

            var config = new ConfigurationBuilder()
                .SetBasePath(context.FunctionAppDirectory)
                .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true) // <- This gives you access to your application settings in your local development environment
                .AddEnvironmentVariables() // <- This is what actually gets you the application settings in Azure
                .Build();

            string steamWebApiKey = config["SteamWebApiKey"];
            string blobStorageConnectionString = config["BlobStorageConnectionString"];
            var steamWebInterfaceFactory = new SteamWebInterfaceFactory(steamWebApiKey);
            econItems = steamWebInterfaceFactory.CreateSteamWebInterface<EconItems>(AppId.Dota2);

            log.LogInformation("Downloading latest schema file from Steam Web API.");

            var schema = await GetSchemaFromSteamAsync();

            log.LogInformation("Uploading latest schema file to Azure Blob Storage.");

            await UploadSchemaToBlobStorageAsync(blobStorageConnectionString, schema);

            log.LogInformation("Done.");
        }

        private static async Task UploadSchemaToBlobStorageAsync(string blobStorageConnectionString, byte[] schema)
        {
            var blobServiceClient = new BlobServiceClient(blobStorageConnectionString);
            var blobContainerClient = blobServiceClient.GetBlobContainerClient(schemaStorageContainerName);
            var blobClient = blobContainerClient.GetBlockBlobClient(schemaFileName);
            await blobClient.UploadAsync(new MemoryStream(schema), new BlobHttpHeaders()
            {
                ContentType = "text/plain"
            });
        }

        private async Task<byte[]> GetSchemaFromSteamAsync()
        {
            var schemaUrl = await econItems.GetSchemaUrlAsync();
            HttpClient httpClient = new HttpClient();
            var schema = await httpClient.GetByteArrayAsync(schemaUrl.Data);
            return schema;
        }
    }
}