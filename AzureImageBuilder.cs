using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.EventGrid;
using Microsoft.Extensions.Logging;
using Azure.Messaging.EventGrid;
using System.Text.Json;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using Microsoft.Net.Http.Headers;
using System.Net.Http.Headers;
using System.Net.Mime;

namespace AzureImageBuilderFunctionApp
{
    public static class AzureImageBuilder
    {
        [FunctionName(nameof(AzureImageBuilder))]
        public static async Task Run([EventGridTrigger] EventGridEvent eventGridEvent, ILogger log)
        {
            log.LogInformation(eventGridEvent.Data.ToString());
            using var document = await JsonDocument.ParseAsync(eventGridEvent.Data.ToStream());
            if (Predicates.All(predicate => predicate(document.RootElement)))
            {
                document.RootElement.TryGetProperty("resourceUri", out var resourceUri);
                log.LogInformation(1000, "Image version {resourceUri} was published to the gallery. Triggering Azure DevOps Pipeline...", resourceUri);
                await TriggerPipelineAsync();
                log.LogInformation(1001, "Done");
            }
        }

        private static readonly Predicate<JsonElement> ResourceProviderPredicate = element =>
            element.TryGetProperty("resourceProvider", out var value) && value.ValueEquals("Microsoft.Compute");

        private static readonly Predicate<JsonElement> OperationNamePredicate = element =>
            element.TryGetProperty("operationName", out var value) && value.ValueEquals("Microsoft.Compute/galleries/images/versions/write");

        private static readonly Predicate<JsonElement> StatusPredicate = element =>
            element.TryGetProperty("status", out var value) && value.ValueEquals("Succeeded");

        private static readonly IEnumerable<Predicate<JsonElement>> Predicates = new[] { ResourceProviderPredicate, OperationNamePredicate, StatusPredicate };

        private static readonly string AzureDevOpsAuthorizationHeaderValue = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{Environment.GetEnvironmentVariable("ADO_PAT")}"));

        private static readonly string AzureDevOpsOrganizationalProject = Environment.GetEnvironmentVariable("ADO_ORG_PRJ");

        private static readonly string AzureDevOpsPipeline = Environment.GetEnvironmentVariable("ADO_PIPELINE");

        private static readonly HttpContent RequestPayload = new StringContent("{}", new UTF8Encoding(), MediaTypeNames.Application.Json);

        private static async Task TriggerPipelineAsync()
        {
            
            using var client = new HttpClient { BaseAddress = new Uri("https://dev.azure.com") };
            client.DefaultRequestHeaders.Remove(HeaderNames.Authorization);
            client.DefaultRequestHeaders.Add(HeaderNames.Authorization, new AuthenticationHeaderValue("Basic", AzureDevOpsAuthorizationHeaderValue).ToString());
            using var response = await client.PostAsync($"{AzureDevOpsOrganizationalProject}/_apis/pipelines/{AzureDevOpsPipeline}/runs?api-version=6.1-preview.1", RequestPayload);
            response.EnsureSuccessStatusCode();
        }
    }
}
