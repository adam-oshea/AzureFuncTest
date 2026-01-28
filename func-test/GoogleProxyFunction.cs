using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net.Http;

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
public async Task<HttpResponseData> Run(
    [HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestData req)
{
    try
    {
        _logger.LogInformation("Calling google.ie");

        var googleResponse = await _httpClient.GetAsync("https://www.google.ie");
        var content = await googleResponse.Content.ReadAsStringAsync();

        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "text/html; charset=utf-8");
        await response.WriteStringAsync(content);
        return response;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "GoogleProxy failed");

        var error = req.CreateResponse(HttpStatusCode.InternalServerError);
        await error.WriteStringAsync(ex.Message);
        return error;
    }
}

}
