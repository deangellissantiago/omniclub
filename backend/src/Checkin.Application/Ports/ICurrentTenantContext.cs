namespace Checkin.Application.Ports;

/// <summary>
/// Dá acesso ao tenant/admin autenticado na requisição atual, sem que os casos de uso
/// precisem conhecer HttpContext, JWT ou qualquer detalhe de transporte.
/// </summary>
public interface ICurrentTenantContext
{
    string TenantId { get; }
    string AdminId { get; }
}
