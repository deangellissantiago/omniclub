/** Helpers de dinheiro (centavos <-> reais) — usados no cadastro de pontos de check-in
 * (valor/check-in) e no relatório de repasse (Fase 2 do roadmap). Backend sempre trabalha em
 * centavos (mesma convenção do Stripe/Billing), formulário e exibição usam reais.
 */

export function formatMoneyCents(cents?: number | null): string {
  if (cents == null) return "—";
  return (cents / 100).toLocaleString("pt-BR", { style: "currency", currency: "BRL" });
}

/** Converte o valor digitado em reais (string do input) para centavos, ou `null` se vazio/inválido. */
export function reaisInputToCents(value: string): number | null {
  if (value.trim() === "") return null;
  const reais = Number(value.replace(",", "."));
  return Number.isFinite(reais) ? Math.round(reais * 100) : null;
}

export function centsToReaisInput(cents?: number | null): string {
  return cents == null ? "" : (cents / 100).toFixed(2);
}
