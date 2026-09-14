# E2E: Simulação de check-in via Swagger (Playwright)

Dirige o Swagger de verdade (`/swagger`) exatamente como o Deangellis vai usar: login pela UI →
Authorize com o token → `POST /api/simulate/wellhub/checkin` via "Try it out" → confere que a
resposta é 200 com um `CheckinDto` válido. Grava vídeo (`.webm`) da sessão + screenshot de cada
passo.

## Rodar

```bash
# suba o backend primeiro (docker compose up, na raiz do repo, ou dotnet run)
cd backend/e2e
npm install
npm test
```

Variáveis opcionais: `BASE_URL` (default `http://localhost:5280`), `ADMIN_EMAIL`,
`ADMIN_PASSWORD`, `GYMPASS_ID`, `GYM_EXTERNAL_ID` (default `609`), `CHROME_PATH`.

## Resultado

Sai em `last-run/` (git-ignorado, sempre limpo a cada execução):
- `simulate-swagger.webm` — vídeo da sessão inteira
- `screenshots/NN_*.png` — uma captura por passo

## Sobre os testes do endpoint em si

A lógica de negócio por trás do `/api/simulate/wellhub/*` (matching de aluno, idempotência,
gym/slot desconhecido, respostas 400/401/404) está coberta por testes automatizados em
`backend/tests/Checkin.Tests/SimulationControllerTests.cs` (`dotnet test`, 37 testes no total) —
essa bateria foi rodada manualmente por curl em 2026-09-10 antes de virar teste automatizado,
cobrindo: caminho feliz, gym_id inexistente (404), corpo vazio (400 de validação automática),
sem token (401), matching de aluno por `wellhubMemberId`, idempotência com `occurredAt`
explícito, e JSON malformado (400, não 500). Nenhum bug encontrado.

**Lembrete de negócio** (não é bug): o `status` do check-in simulado sempre volta `Rejected`,
porque o Wellhub só confirma (`Approved`) um check-in que ele mesmo registrou do lado dele — ver
README do repo, seção "Integração Wellhub".
