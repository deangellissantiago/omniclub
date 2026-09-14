import { useEffect, useRef, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { getSubscriptionStatus, openBillingPortal, startCheckout } from "../api/billing";
import type { SubscriptionStatusInfo } from "../api/types";
import { extractErrorMessage } from "../api/client";
import { useAuth } from "../context/AuthContext";
import { BrandLockup } from "../components/ui";

const POLL_ATTEMPTS = 8;
const POLL_INTERVAL_MS = 2000;

export function SubscribePage() {
  const { auth, logout } = useAuth();
  const [params] = useSearchParams();
  const cameFromCheckout = params.get("status"); // "sucesso" | "cancelado" | null

  const [status, setStatus] = useState<SubscriptionStatusInfo | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [confirming, setConfirming] = useState(cameFromCheckout === "sucesso");
  const pollCount = useRef(0);

  async function refreshStatus(): Promise<SubscriptionStatusInfo | null> {
    try {
      const result = await getSubscriptionStatus();
      setStatus(result);
      return result;
    } catch (err) {
      setError(extractErrorMessage(err));
      return null;
    }
  }

  useEffect(() => {
    refreshStatus();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    if (!confirming) return;
    if (pollCount.current >= POLL_ATTEMPTS) {
      setConfirming(false);
      return;
    }
    const timer = setTimeout(async () => {
      pollCount.current += 1;
      const result = await refreshStatus();
      if (result?.status === "Active") {
        setConfirming(false);
        window.location.href = "/";
      }
    }, POLL_INTERVAL_MS);
    return () => clearTimeout(timer);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [confirming, status]);

  async function handleSubscribe() {
    setError(null);
    setLoading(true);
    try {
      const { url } = await startCheckout();
      window.location.href = url;
    } catch (err) {
      setError(extractErrorMessage(err));
      setLoading(false);
    }
  }

  async function handleManage() {
    setError(null);
    setLoading(true);
    try {
      const { url } = await openBillingPortal();
      window.location.href = url;
    } catch (err) {
      setError(extractErrorMessage(err));
      setLoading(false);
    }
  }

  const isActive = status?.status === "Active";

  return (
    <div className="login-screen">
      <div className="login-form-col">
        <BrandLockup logoSize={36} onLight />

        <div className="login-form-wrap">
          {confirming ? (
            <>
              <h1 className="login-heading">Confirmando pagamento...</h1>
              <p className="subtitle">
                Já recebemos seu checkout do Stripe. Assim que o pagamento for confirmado (alguns segundos), você é
                redirecionado automaticamente.
              </p>
            </>
          ) : isActive ? (
            <>
              <h1 className="login-heading">Assinatura ativa</h1>
              <p className="subtitle">
                {auth?.tenantName ?? "Sua escola"} está com a assinatura em dia
                {status?.currentPeriodEnd && (
                  <> — renova em {new Date(status.currentPeriodEnd).toLocaleDateString("pt-BR")}</>
                )}
                .
              </p>
              {error && <p className="error-text">{error}</p>}
              <button type="button" className="btn-primary" onClick={handleManage} disabled={loading}>
                {loading ? "Abrindo..." : "Gerenciar assinatura"}
              </button>
              <button type="button" className="btn-ghost" style={{ marginTop: "0.5rem" }} onClick={() => (window.location.href = "/")}>
                Voltar pro painel
              </button>
            </>
          ) : (
            <>
              <h1 className="login-heading">
                {cameFromCheckout === "cancelado" ? "Checkout cancelado" : "Assine para continuar"}
              </h1>
              <p className="subtitle">
                {cameFromCheckout === "cancelado"
                  ? "Nenhum problema — você pode tentar de novo quando quiser."
                  : `${auth?.tenantName ?? "Sua conta"} ainda não tem uma assinatura ativa. Assine o plano mensal pra liberar o sistema.`}
              </p>
              {error && <p className="error-text">{error}</p>}
              <button type="button" className="btn-primary" onClick={handleSubscribe} disabled={loading}>
                {loading ? "Abrindo checkout..." : "Assinar agora"}
              </button>
            </>
          )}

          <p className="login-footer" style={{ marginTop: "1rem" }}>
            <button type="button" className="btn-link" onClick={logout}>Sair</button>
          </p>
        </div>

        <p className="login-footer">© {new Date().getFullYear()} OmniClub · Automação de check-ins Wellhub &amp; TotalPass</p>
      </div>

      <div className="login-hero">
        <img className="hero-bg" src="/images/login-hero.jpg" alt="" />
        <div className="login-hero-copy">
          <h2>
            Check-ins de benefícios aprovados <em>automaticamente</em>
          </h2>
          <p>Assinatura mensal única, sem taxa por unidade ou por aluno.</p>
        </div>
        <span className="photo-credit">
          Foto: <a href="https://unsplash.com/@lukechesser" target="_blank" rel="noreferrer">Luke Chesser</a> / Unsplash
        </span>
      </div>
    </div>
  );
}
