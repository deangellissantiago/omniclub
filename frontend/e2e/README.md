# E2E smoke test (Playwright)

Dirige um Chrome headless de verdade contra o frontend rodando (`BASE_URL`, default
`http://localhost:5271`) cobrindo: login → dashboard → CRUD de aluno → pontos de check-in →
check-ins (confere os status Approved/Rejected vindos da integração Wellhub) → relatórios (3
abas) → logout. Grava um vídeo (`.webm`) da sessão inteira + screenshot de cada passo, e falha
(exit code 1) se algum passo travar, se o console do navegador logar um erro, se alguma request
falhar, ou se o backend responder 5xx.

## Rodar

```bash
# suba o app primeiro (docker compose up, na raiz do repo, ou frontend/backend via dotnet run + vite dev)
cd frontend/e2e
npm install
npm test
```

Variáveis opcionais: `BASE_URL`, `ADMIN_EMAIL`, `ADMIN_PASSWORD`, `CHROME_PATH` (default
`/usr/bin/google-chrome` — não baixa um Chromium próprio, usa o do sistema).

## `npm run test:billing`

Cobre cadastro autosserviço (`/cadastro`) → confere que o `SubscriptionGateMiddleware` bloqueia o
resto do sistema (402) pra um tenant recém-criado → clica em "Assinar agora" e confere que chega
de verdade no Checkout hospedado do Stripe (modo teste). Não completa o pagamento — isso exige
`Billing:WebhookSecret` configurado com uma URL pública (ver README do repo, "Cobrança / Stripe").
Screenshots saem em `last-run/billing-*.png`.

## Resultado

Sai em `last-run/` (git-ignorado, é sempre limpo a cada execução):
- `smoke.webm` — vídeo da sessão inteira
- `screenshots/NN_*.png` — uma captura por passo

## Última execução

2026-09-10, contra o `docker compose` local (frontend na porta 5271, backend na 5280): todos os
passos passaram, sem erros de console/rede. Único ponto de atenção — não é bug, é limitação
conhecida do Sandbox Wellhub (ver README do repo): os check-ins que aparecem na tela vêm todos
como "Rejeitado", porque os testes de webhook usaram um `gympass_id` fictício que o Wellhub não
reconhece como tendo feito check-in de verdade. A UI trata "Aprovado"/"Rejeitado"/"Pendente"
corretamente (badges, cores, contagens) — só falta um check-in real do Sandbox pra ver
"Aprovado" na tela também.
