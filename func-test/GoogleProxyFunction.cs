using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;

using Azure;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Azure.Data.Tables;
using Microsoft.Azure.Functions.Worker.Extensions.Timer;
using System.Text.Json;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Azure.Core;


public class GoogleProxyFunction
{
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;

    public GoogleProxyFunction(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    {
        _httpClient = httpClientFactory.CreateClient();
        _logger = loggerFactory.CreateLogger<GoogleProxyFunction>();
    }

    [Function("GoogleProxy")]
    public async Task RunAsync([TimerTrigger("0 */1 * * * *")] TimerInfo timer)

        {
            _logger.LogInformation("Sync started at {time}", DateTime.UtcNow);

            var credential = new DefaultAzureCredential();
            var token = await credential.GetTokenAsync(
                new TokenRequestContext(new[] { "https://storage.azure.com/.default" }));

            _logger.LogInformation("Storage token acquired, expires {time}", token.ExpiresOn);

            // ----- Key Vault -----
            var kvName = Environment.GetEnvironmentVariable("KEYVAULT_NAME");
            var secretClient = new SecretClient(
                new Uri($"https://{kvName}.vault.azure.net"),
                credential);

            var apiKey = (await secretClient.GetSecretAsync("nasa-key")).Value.Value;

            // ----- Table Storage -----
            var storageAccount = Environment.GetEnvironmentVariable("STORAGE_ACCOUNT");
            var tableService = new TableServiceClient(
                Environment.GetEnvironmentVariable("AzureWebJobsStorage"));


            var controlTable = tableService.GetTableClient("ControlTable");
            var dataTable = tableService.GetTableClient("DataTable");

            
            // ----- Read lastSync -----
            string lastSync;
            try
            {
                var entity = await controlTable.GetEntityAsync<TableEntity>(
                    "SYNC", "API");

                lastSync = entity.Value.GetString("LastSync");
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                lastSync = "1970-01-01T00:00:00Z";
            }

            // ----- Call external API -----
            var apiUrl = Environment.GetEnvironmentVariable("API_BASE_URL");
            var requestUrl = $"{apiUrl}?api_key={apiKey}&since={Uri.EscapeDataString(lastSync)}";

            var response = await _httpClient.GetAsync(requestUrl);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            // Get the "element_count" field
            int elementCount = root.GetProperty("element_count").GetInt32();

            // ----- Save result (outbox) -----
            var dataEntity = new TableEntity("API_DATA", Guid.NewGuid().ToString())
            {
                { "ElementCount", elementCount },
                { "CreatedAt", DateTime.UtcNow }
            };

            await dataTable.UpsertEntityAsync(dataEntity);

            // ----- Update control table -----
            var newSync = DateTime.UtcNow.ToString("O");

            await controlTable.UpsertEntityAsync(new TableEntity("SYNC", "API")
            {
                { "LastSync", newSync }
            });

            _logger.LogInformation("Sync completed at {time}", newSync);
        }
    }

    public class ApiItem
    {
        public string Id { get; set; }
        // add other fields as needed
    }

