import { useState, type FormEvent } from "react";
import { Navigate, Link } from "react-router-dom";
import { useAuth } from "../context/AuthContext";
import { extractErrorMessage } from "../api/client";
import { BrandLockup } from "../components/ui";

export function LoginPage() {
  const { isAuthenticated, login } = useAuth();
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
      await login(email, password);
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
          <h1 className="login-heading">Bem-vindo de volta</h1>
          <p className="subtitle">Acesse o painel do administrador para acompanhar os check-ins da sua unidade.</p>

          <label>
            E-mail
            <input
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              placeholder="voce@suaacademia.com"
              required
              autoFocus
            />
          </label>
          <label>
            Senha
            <input
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              placeholder="••••••••"
              required
            />
          </label>

          {error && <p className="error-text">{error}</p>}

          <button type="submit" className="btn-primary" disabled={loading}>
            {loading ? "Entrando..." : "Entrar"}
          </button>

          <p className="login-footer" style={{ marginTop: "0.75rem" }}>
            Ainda não tem conta? <Link to="/cadastro">Cadastre sua escola</Link>
          </p>
        </form>

        <p className="login-footer">© {new Date().getFullYear()} OmniClub · Automação de check-ins Wellhub &amp; TotalPass</p>
      </div>

      <div className="login-hero">
        <img className="hero-bg" src="/images/login-hero.jpg" alt="" />
        <div className="login-hero-copy">
          <h2>
            Check-ins de benefícios aprovados <em>automaticamente</em>
          </h2>
          <p>
            O OmniClub recebe cada check-in do Wellhub e do TotalPass, valida, aprova e transforma
            tudo em dados: totais por aplicativo, por aluno e por unidade — em tempo real, sem
            planilhas e sem trabalho manual.
          </p>
          <div className="login-hero-badges">
            <span className="pill"><span className="dot" />Aprovação automática via webhook</span>
            <span className="pill"><span className="dot" />Dados em tempo real</span>
            <span className="pill"><span className="dot" />Relatórios por aluno e unidade</span>
          </div>
        </div>
        <span className="photo-credit">
          Foto: <a href="https://unsplash.com/@lukechesser" target="_blank" rel="noreferrer">Luke Chesser</a> / Unsplash
        </span>
      </div>
    </div>
  );
}
