import { useState, type FormEvent } from "react";
import { Navigate, useNavigate, Link } from "react-router-dom";
import { useAuth } from "../context/AuthContext";
import { extractErrorMessage } from "../api/client";
import { BrandLockup } from "../components/ui";

export function RegisterPage() {
  const { isAuthenticated, register } = useAuth();
  const navigate = useNavigate();
  const [tenantName, setTenantName] = useState("");
  const [adminName, setAdminName] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  if (isAuthenticated) return <Navigate to="/" replace />;

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    setLoading(true);
    try {
      await register(tenantName, adminName, email, password);
      // Cadastro nasce sem assinatura ativa — manda direto pra tela de assinatura, não pro
      // dashboard (que daria 402 no primeiro fetch e redirecionaria de qualquer forma).
      navigate("/assinatura", { replace: true });
    } catch (err) {
      setError(extractErrorMessage(err));
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="login-screen">
      <div className="login-form-col">
        <BrandLockup logoSize={36} onLight />

        <form className="login-form-wrap" onSubmit={handleSubmit}>
          <h1 className="login-heading">Crie sua conta</h1>
          <p className="subtitle">Cadastre sua escola/academia e comece a automatizar os check-ins.</p>

          <label>
            Nome da escola/academia
            <input
              value={tenantName}
              onChange={(e) => setTenantName(e.target.value)}
              placeholder="Ex.: Academia Vida Ativa"
              required
              autoFocus
            />
          </label>
          <label>
            Seu nome
            <input value={adminName} onChange={(e) => setAdminName(e.target.value)} placeholder="Seu nome completo" required />
          </label>
          <label>
            E-mail
            <input
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              placeholder="voce@suaacademia.com"
              required
            />
          </label>
          <label>
            Senha
            <input
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              placeholder="••••••••"
              minLength={8}
              required
            />
          </label>

          {error && <p className="error-text">{error}</p>}

          <button type="submit" className="btn-primary" disabled={loading}>
            {loading ? "Criando conta..." : "Criar conta"}
          </button>

          <p className="login-footer" style={{ marginTop: "0.75rem" }}>
            Já tem conta? <Link to="/login">Entrar</Link>
          </p>
        </form>

        <p className="login-footer">© {new Date().getFullYear()} OmniClub · Automação de check-ins Wellhub &amp; TotalPass</p>
      </div>

      <div className="login-hero">
        <img className="hero-bg" src="/images/login-hero.jpg" alt="" />
        <div className="login-hero-copy">
          <h2>
            Comece a validar check-ins <em>hoje mesmo</em>
          </h2>
          <p>
            Cadastre sua escola, assine o plano mensal e conecte o Wellhub — em poucos minutos seu
            clube já está recebendo e aprovando check-ins automaticamente.
          </p>
          <div className="login-hero-badges">
            <span className="pill"><span className="dot" />Setup em minutos</span>
            <span className="pill"><span className="dot" />Sem contrato de fidelidade</span>
          </div>
        </div>
        <span className="photo-credit">
          Foto: <a href="https://unsplash.com/@lukechesser" target="_blank" rel="noreferrer">Luke Chesser</a> / Unsplash
        </span>
      </div>
    </div>
  );
}
