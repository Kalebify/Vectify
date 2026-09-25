using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Vectify.Api.Checking;
using Vectify.Api.Clients;
using Vectify.Api.Tests.TestSupport;

namespace Vectify.Api.Tests.Clients;

/// <summary>
/// Pruebas unitarias de PythonCheckClient contra un HttpMessageHandler stub
/// (sin red real): cubren los estados exigidos por el contrato -- éxito, SVG
/// de entrada inválido/demasiado grande, demasiados subpaths, parámetros
/// inválidos, timeout, no disponible, respuesta inválida, error HTTP, y la
/// validación defensiva adicional sobre respuestas 200 con contenido
/// sospechoso (issues con forma incoherente, resumen que no coincide con la
/// cantidad real de issues, tolerancias efectivas fuera de rango) -- sin
/// excepciones sin controlar escapando del cliente. Mismo criterio que
/// PythonVectorizeClientTests/PythonSimplifyClientTests.
/// </summary>
public sealed class PythonCheckClientTests
{
    private static PythonCheckClient CreateClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handlerFunc,
        TimeSpan? timeout = null)
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(handlerFunc))
        {
            BaseAddress = new Uri("http://python-engine.test"),
            Timeout = timeout ?? TimeSpan.FromSeconds(5),
        };

        return new PythonCheckClient(httpClient, NullLogger<PythonCheckClient>.Instance);
    }

    private static Task<PythonCheckResult> InvokeAsync(PythonCheckClient client) =>
        client.CheckAsync(
            new MemoryStream([1, 2, 3, 4]), "image/svg+xml", "vector.svg",
            new CheckParameters(0.005, 0.002, CheckSourceKind.Vector));

    [Fact]
    public async Task CheckAsync_WhenResponseIsValid_ReturnsSuccessWithIssues()
    {
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, CheckPayloads.SuccessBody())));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonCheckState.Success, result.State);
        Assert.Equal(0, result.SkippedPathCount);
        Assert.Equal(2, result.Issues!.Count);
        Assert.Contains(result.Issues, i => i is CheckIssue.OpenPath);
        Assert.Contains(result.Issues, i => i is CheckIssue.DuplicatePath);
    }

    [Fact]
    public async Task CheckAsync_WhenPythonReportsInvalidInputSvg_ReturnsInvalidInputSvg()
    {
        var client = CreateClient((_, _) => Task.FromResult(
            JsonResponse(HttpStatusCode.BadRequest, CheckPayloads.ErrorBody("invalid_input_svg", "SVG corrupto"))));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonCheckState.InvalidInputSvg, result.State);
    }

    [Fact]
    public async Task CheckAsync_WhenPythonReportsTooManySubpaths_ReturnsTooManySubpaths()
    {
        var client = CreateClient((_, _) => Task.FromResult(
            JsonResponse(HttpStatusCode.RequestEntityTooLarge, CheckPayloads.ErrorBody("too_many_subpaths", "demasiados subpaths"))));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonCheckState.TooManySubpaths, result.State);
    }

    [Fact]
    public async Task CheckAsync_WhenPythonReportsTimeout_ReturnsTimeout()
    {
        var client = CreateClient((_, _) => Task.FromResult(
            JsonResponse((HttpStatusCode)504, CheckPayloads.ErrorBody("check_timeout", "tardó demasiado"))));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonCheckState.Timeout, result.State);
    }

    [Fact]
    public async Task CheckAsync_WhenConnectionRefused_ReturnsUnavailable()
    {
        var client = CreateClient((_, _) => throw new HttpRequestException("Connection refused"));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonCheckState.Unavailable, result.State);
    }

    [Fact]
    public async Task CheckAsync_WhenRequestExceedsTimeout_ReturnsTimeout()
    {
        var client = CreateClient(
            async (_, ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
                return JsonResponse(HttpStatusCode.OK, CheckPayloads.SuccessBody());
            },
            timeout: TimeSpan.FromMilliseconds(50));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonCheckState.Timeout, result.State);
    }

    [Fact]
    public async Task CheckAsync_WhenBodyIsNotJson_ReturnsInvalidResponse()
    {
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, "<html>not json</html>")));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonCheckState.InvalidResponse, result.State);
    }

    [Fact]
    public async Task CheckAsync_WhenRequiredFieldsAreMissing_ReturnsInvalidResponse()
    {
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, """{"skipped_path_count": 0}""")));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonCheckState.InvalidResponse, result.State);
    }

    [Fact]
    public async Task CheckAsync_WhenHttpStatusIsUnmappedError_ReturnsHttpError()
    {
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.InternalServerError, "")));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonCheckState.HttpError, result.State);
    }

    [Fact]
    public async Task CheckAsync_WhenSummaryDoesNotMatchIssueCounts_ReturnsInvalidSvg()
    {
        var body = CheckPayloads.SuccessBody(openPathCount: 5);
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, body)));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonCheckState.InvalidSvg, result.State);
    }

    [Fact]
    public async Task CheckAsync_WhenIssueTypeIsUnknown_ReturnsInvalidSvg()
    {
        var body = CheckPayloads.SuccessBody(issuesJson: """[{"type": "unknown_kind", "id": "x", "severity": "error"}]""");
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, body)));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonCheckState.InvalidSvg, result.State);
    }

    [Fact]
    public async Task CheckAsync_WhenIssueSeverityIsUnknown_ReturnsInvalidSvg()
    {
        var body = CheckPayloads.SuccessBody(issuesJson: $"[{CheckPayloads.OpenPathIssueJson(severity: "critical")}]", duplicateGroupCount: 0);
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, body)));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonCheckState.InvalidSvg, result.State);
    }

    [Fact]
    public async Task CheckAsync_WhenOpenPathBoundsAreIncoherent_MaxLessThanMin_ReturnsInvalidSvg()
    {
        var body = CheckPayloads.SuccessBody(issuesJson: $"[{CheckPayloads.OpenPathIssueJson(minX: 8, maxX: 2)}]", duplicateGroupCount: 0);
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, body)));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonCheckState.InvalidSvg, result.State);
    }

    [Fact]
    public async Task CheckAsync_WhenDuplicateGroupHasFewerThanTwoMembers_ReturnsInvalidSvg()
    {
        var body = CheckPayloads.SuccessBody(
            issuesJson: $"[{CheckPayloads.DuplicatePathIssueJson(memberCount: 1)}]", openPathCount: 0);
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, body)));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonCheckState.InvalidSvg, result.State);
    }

    [Fact]
    public async Task CheckAsync_WhenEffectiveToleranceIsOutOfRange_ReturnsInvalidSvg()
    {
        var body = CheckPayloads.SuccessBody(closeGapRatio: 0.6);
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, body)));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonCheckState.InvalidSvg, result.State);
    }

    [Fact]
    public async Task CheckAsync_WhenSkippedPathCountIsNegative_ReturnsInvalidSvg()
    {
        var body = CheckPayloads.SuccessBody(skippedPathCount: -1);
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, body)));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonCheckState.InvalidSvg, result.State);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string body) => new(statusCode)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handlerFunc) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) => handlerFunc(request, cancellationToken);
    }
}
