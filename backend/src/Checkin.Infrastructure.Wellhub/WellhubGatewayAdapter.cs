using System.Net.Http.Headers;
using System.Net.Http.Json;
using Checkin.Application.Ports.Integrations;
using Checkin.Infrastructure.Wellhub.Dtos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Checkin.Infrastructure.Wellhub;

/// <summary>
/// Adapter de saída para IWellhubGateway, implementado contra o contrato confirmado em
/// https://developers.wellhub.com/product/access-control-api/1.0/endpoints
/// (host: https://api.partners.gympass.com).
///
/// Só falta uma coisa para funcionar de verdade: <see cref="WellhubOptions.ApiKey"/>. Esse
/// Bearer token é emitido pelo Wellhub Technical Sales depois que a escola assina o contrato de
/// parceria e é homologada como CMS (ver README). Enquanto ele não existir, os métodos abaixo
/// retornam "não configurado" em vez de tentar uma chamada que sempre falharia com 403.
/// </summary>
public class WellhubGatewayAdapter : IWellhubGateway
{
    private readonly HttpClient _httpClient;
    private readonly WellhubOptions _options;
    private readonly ILogger<WellhubGatewayAdapter> _logger;

    public WellhubGatewayAdapter(HttpClient httpClient, IOptions<WellhubOptions> options, ILogger<WellhubGatewayAdapter> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        if (Uri.TryCreate(_options.BaseUrl, UriKind.Absolute, out var baseUri))
        {
            _httpClient.BaseAddress = baseUri;
        }
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey);

    public async Task<WellhubValidationResult?> ValidateAccessAsync(
        string gympassId, string? customCode, string gymExternalId, CancellationToken ct = default)
    {
        if (!TryPrepareRequest(gymExternalId, out var reason))
        {
            _logger.LogInformation("Wellhub /access/v1/validate não chamado: {Reason}", reason);
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "/access/v1/validate")
        {
            Content = JsonContent.Create(new ValidateAccessRequest(gympassId, customCode))
        };
        ApplyHeaders(request, gymExternalId);

        using var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var reasonDetail = await TryReadErrorReasonAsync(response, ct);
            _logger.LogWarning(
                "Wellhub /access/v1/validate retornou {Status} para gympass_id {GympassId}{ReasonDetail}",
                response.StatusCode, gympassId, reasonDetail is null ? "" : $" ({reasonDetail})");
            return null;
        }

        // Alguns modos de operação retornam 200 sem corpo; nesse caso consideramos validado.
        var body = await response.Content.ReadFromJsonAsync<ValidateAccessResponse>(cancellationToken: ct);
        var user = body?.Results?.User?.GympassId ?? gympassId;
        var gymId = body?.Results?.Gym?.Id ?? (long.TryParse(gymExternalId, out var parsedGymId) ? parsedGymId : 0);

        return new WellhubValidationResult(
            user, gymId,
            body?.Results?.Gym?.Product?.Id, body?.Results?.Gym?.Product?.Description,
            body?.Results?.ValidatedAt);
    }

    public Task<bool> CreateCustomCodeAsync(string wellhubId, string customCode, string gymExternalId, CancellationToken ct = default) =>
        SendCustomCodeRequestAsync(HttpMethod.Post, wellhubId, customCode, gymExternalId, ct);

    public Task<bool> UpdateCustomCodeAsync(string wellhubId, string customCode, string gymExternalId, CancellationToken ct = default) =>
        SendCustomCodeRequestAsync(HttpMethod.Put, wellhubId, customCode, gymExternalId, ct);

    public async Task<bool> DeleteCustomCodeAsync(string wellhubId, string gymExternalId, CancellationToken ct = default)
    {
        if (!TryPrepareRequest(gymExternalId, out var reason))
        {
            _logger.LogInformation("Wellhub DELETE /access/v1/code não chamado: {Reason}", reason);
            return false;
        }

        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/access/v1/code/{Uri.EscapeDataString(wellhubId)}");
        ApplyHeaders(request, gymExternalId);

        using var response = await _httpClient.SendAsync(request, ct);
        return response.IsSuccessStatusCode;
    }

    private async Task<bool> SendCustomCodeRequestAsync(HttpMethod method, string wellhubId, string customCode, string gymExternalId, CancellationToken ct)
    {
        if (!TryPrepareRequest(gymExternalId, out var reason))
        {
            _logger.LogInformation("Wellhub {Method} /access/v1/code não chamado: {Reason}", method, reason);
            return false;
        }

        using var request = new HttpRequestMessage(method, $"/access/v1/code/{Uri.EscapeDataString(wellhubId)}")
        {
            Content = JsonContent.Create(new CustomCodeRequest(customCode))
        };
        ApplyHeaders(request, gymExternalId);

        using var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Wellhub {Method} /access/v1/code/{WellhubId} retornou {Status}", method, wellhubId, response.StatusCode);
        }
        return response.IsSuccessStatusCode;
    }

    private bool TryPrepareRequest(string gymExternalId, out string reason)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            reason = "Wellhub:ApiKey não configurado (aguardando homologação como CMS junto ao Wellhub Technical Sales).";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private void ApplyHeaders(HttpRequestMessage request, string gymExternalId)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        request.Headers.Add("X-Gym-Id", gymExternalId);
    }

    /// <summary>
    /// Extrai a primeira mensagem de erro do corpo (formato confirmado ao vivo contra o Sandbox:
    /// <c>{"errors":[{"message":"...","key":"..."}]}</c>), só para deixar o log mais útil. Se o
    /// corpo não vier nesse formato (ex.: 401 sem corpo JSON), retorna null silenciosamente.
    /// </summary>
    private static async Task<string?> TryReadErrorReasonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadFromJsonAsync<ValidateAccessResponse>(cancellationToken: ct);
            var error = body?.Errors?.FirstOrDefault();
            return error is null ? null : $"{error.Key}: {error.Message}";
        }
        catch
        {
            return null;
        }
    }
}
