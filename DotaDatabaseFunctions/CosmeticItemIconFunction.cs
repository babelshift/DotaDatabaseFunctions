using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SourceSchemaParser;
using Steam.Models.DOTA2;
using SteamWebAPI2.Interfaces;
using SteamWebAPI2.Utilities;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace DotaDatabaseFunctions
{
    public class CosmeticItemIconFunction
    {
        private const string schemaStorageContainerName = "schemas";
        private const string cosmeticItemIconStorageContainerName = "cosmeticitemicons";
        private const string schemaFileName = "items_game.vdf";
        private const string steamCDNBaseUrl = "https://steamcdn-a.akamaihd.net/apps/570";

        private readonly ISchemaParser schemaParser;

        private ILogger log;
        private DOTA2Econ dota2Econ;
        private BlobServiceClient blobServiceClient;

        public CosmeticItemIconFunction(ISchemaParser schemaParser)
        {
            this.schemaParser = schemaParser;
        }

        [FunctionName("CosmeticItemIconFunction")]
        public async Task Run(
            [TimerTrigger("0 * * * * Sun", RunOnStartup = true)]TimerInfo myTimer,
            ExecutionContext context,
            ILogger log)
        {
            this.log = log;

            log.LogInformation($"Starting {nameof(CosmeticItemIconFunction)} function.");

            var config = new ConfigurationBuilder()
                .SetBasePath(context.FunctionAppDirectory)
                .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true) // <- This gives you access to your application settings in your local development environment
                .AddEnvironmentVariables() // <- This is what actually gets you the application settings in Azure
                .Build();

            string blobStorageConnectionString = config["BlobStorageConnectionString"];
            blobServiceClient = new BlobServiceClient(blobStorageConnectionString);

            string steamWebApiKey = config["SteamWebApiKey"];
            var steamWebInterfaceFactory = new SteamWebInterfaceFactory(steamWebApiKey);
            dota2Econ = steamWebInterfaceFactory.CreateSteamWebInterface<DOTA2Econ>();

            log.LogInformation("Downloading latest schema file from blob storage.");

            // Get latest Dota 2 game schema (cosmetic items)
            var schema = await GetSchemaAsync(blobStorageConnectionString);

            log.LogInformation($"Starting to process {schema.Items.Count} items.");

            // For each item in schema
            HttpClient httpClient = new HttpClient();
            foreach (var item in schema.Items)
            {
                // Get item icon from Steam Web API
                if (!string.IsNullOrWhiteSpace(item.ImageInventoryPath))
                {
                    log.LogInformation($"Starting to process {item.Name}");

                    string uploadFileName = $"{item.DefIndex}.jpg";
                    var blobContainerClient = blobServiceClient.GetBlobContainerClient(cosmeticItemIconStorageContainerName);
                    var blobClient = blobContainerClient.GetBlockBlobClient(uploadFileName);
                    try
                    {
                        if (!await blobClient.ExistsAsync())
                        {
                            log.LogInformation($"Calling {nameof(GetItemIconFromSteamAsync)} for {item.ImageInventoryPath}.");
                            var iconPng = await GetItemIconFromSteamAsync(httpClient, item.ImageInventoryPath);

                            log.LogInformation($"Converting from JPG --> PNG.");
                            var iconJpg = ConvertPngToJpg(iconPng);

                            log.LogInformation($"Calling {nameof(UploadItemIconToBlobStorageAsync)} for {uploadFileName}.");
                            await UploadItemIconToBlobStorageAsync(uploadFileName, iconJpg);

                            await Task.Delay(TimeSpan.FromSeconds(5));
                        }
                        else
                        {
                            log.LogInformation($"{uploadFileName} already exists in blob storage. Skipping.");
                            await Task.Delay(TimeSpan.FromMilliseconds(500));
                        }
                    }
                    catch(Exception ex)
                    {
                        log.LogError(ex, ex.Message);
                        await Task.Delay(TimeSpan.FromSeconds(5));
                    }
                }
            }

            log.LogInformation("Done.");
        }

        private async Task<byte[]> GetItemIconFromSteamAsync(HttpClient httpClient, string itemImageInventoryPath)
        {
            var indexOfLastSlash = itemImageInventoryPath.LastIndexOf('/');
            var itemName = itemImageInventoryPath.Substring(indexOfLastSlash + 1);
            var response = await dota2Econ.GetItemIconPathAsync(itemName);
            var itemIconPath = response.Data;
            var itemIconUrl = $"{steamCDNBaseUrl}/{itemIconPath}";
            return await httpClient.GetByteArrayAsync(itemIconUrl);
        }

        private async Task UploadItemIconToBlobStorageAsync(string fileName, byte[] iconJpg)
        {
            var blobContainerClient = blobServiceClient.GetBlobContainerClient(cosmeticItemIconStorageContainerName);
            var blobClient = blobContainerClient.GetBlockBlobClient(fileName);
            await blobClient.UploadAsync(new MemoryStream(iconJpg), new BlobHttpHeaders()
            {
                ContentType = "image/jpg"
            });
        }

        private static byte[] ConvertPngToJpg(byte[] sourceImage)
        {
            using (MemoryStream source = new MemoryStream(sourceImage))
            {
                Image destination = Image.FromStream(source);
                using (var b = new Bitmap(destination.Width, destination.Height))
                {
                    b.SetResolution(destination.HorizontalResolution, destination.VerticalResolution);
                    using (var g = Graphics.FromImage(b))
                    {
                        g.Clear(Color.Black);
                        g.DrawImageUnscaled(destination, 0, 0);
                    }

                    using (MemoryStream jpg = new MemoryStream())
                    {
                        b.Save(jpg, ImageFormat.Jpeg);
                        return jpg.ToArray();
                    }
                }
            }
        }

        private async Task<SchemaModel> GetSchemaAsync(string blobStorageConnectionString)
        {
            var blobServiceClient = new BlobServiceClient(blobStorageConnectionString);
            var blobContainerClient = blobServiceClient.GetBlobContainerClient(schemaStorageContainerName);
            var blobClient = blobContainerClient.GetBlobClient(schemaFileName);
            var download = await blobClient.DownloadAsync();

            var schemaContents = new List<string>();
            using (StreamReader sr = new StreamReader(download.Value.Content))
            {
                while (!sr.EndOfStream)
                {
                    string line = await sr.ReadLineAsync();
                    schemaContents.Add(line);
                }
            }

            return schemaParser.GetDotaSchema(schemaContents);
        }
    }
}