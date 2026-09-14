# OmniClub — Automação de check-ins Wellhub / TotalPass

Automação e dados para academias e escolas de tênis que aceitam aplicativos de benefício:
o OmniClub recebe cada check-in feito via Wellhub (e, futuramente, TotalPass), valida e
aprova automaticamente, e transforma tudo em dados — totais por app, por aluno e por
unidade, em tempo real.

## Identidade visual

O foco da marca é **automação e dados dos benefícios** — não o esporte em si.

- **Paleta** (Adobe Color, `AdobeColor-Meu tema de cores.jpeg`): azul royal `#3059F0`
  (ação primária), roxo profundo `#381F59` (sidebar/fundos escuros), azul claro `#89B1F2`,
  amarelo `#FFFE78` (acentos, ex.: o check do logo) e rosa `#FFAEE4` (reservado ao TotalPass).
- **Logo**: anel "O" com um check amarelo — o check-in aprovado automaticamente
  (`frontend/public/favicon.svg` e componente `Logo` em `ui.tsx`).
- **Tipografia** (Google Fonts): [Sora](https://fonts.google.com/specimen/Sora) para títulos
  e números de destaque, [Inter](https://fonts.google.com/specimen/Inter) para a interface.
- **Imagens**: fotos do Unsplash com tema de dados/automação em `frontend/public/images/`
  (créditos em `frontend/public/images/CREDITS.md`).
- Todo o design system fica em `frontend/src/index.css` (tokens CSS em `:root`); logo e
  componentes visuais compartilhados em `frontend/src/components/ui.tsx`.

## Stack

- **Backend**: .NET 8, arquitetura hexagonal (Domain → Application → Infrastructure → Api)
- **Frontend**: React + Vite + TypeScript
- **Banco de dados**: MongoDB
- **Docker**: `docker-compose.yml` sobe Mongo + API + Frontend

## Arquitetura (hexagonal)

```
backend/src/
  Checkin.Domain/                        entidades e enums puros, sem dependências externas
  Checkin.Application/                   casos de uso + portas (interfaces) que o domínio usa
    Ports/Repositories, Ports/Security, Ports/Integrations
    UseCases/{Auth,Students,CheckinPoints,Checkins,Dashboard,Reports}
  Checkin.Infrastructure.Persistence.Mongo/   adapter de saída: repositórios MongoDB
  Checkin.Infrastructure.Wellhub/             adapter de saída: gateway HTTP para o Wellhub
  Checkin.Api/                                adapter de entrada: controllers REST, JWT, DI, seed
```

Regra de dependência: `Api` e os `Infrastructure.*` dependem de `Application`, que depende de
`Domain`. `Domain` e `Application` não conhecem MongoDB, ASP.NET Core ou HTTP — só interfaces
("portas"). Trocar de banco ou de integração significa escrever um novo adapter, sem tocar nos
casos de uso.

Multi-tenant: cada `AdminUser` pertence a um `Tenant`; o JWT carrega o `tenantId` e todo
repositório filtra por ele automaticamente (`ICurrentTenantContext`).

## Como rodar (Docker — recomendado)

```bash
cp .env.example .env   # ajuste os segredos antes de ir para produção
docker compose up --build
```

- Frontend: http://localhost:5271
- API: http://localhost:5280/api (Swagger em http://localhost:5280/swagger, ambiente Development)
- MongoDB: só na rede interna do compose (`mongo:27017`), sem porta publicada no host por
  padrão — esta máquina já tinha outros serviços usando 27017/27018. Para acessar com um
  cliente Mongo local, adicione `ports: ["27099:27017"]` ao serviço `mongo` no
  `docker-compose.yml`.

> Nota: esta máquina de desenvolvimento já tinha outros projetos rodando via Docker (inclusive
> um `tenis-api`/`tenis-site` nas portas 5000/3001, sem relação com este repositório) — por isso
> as portas 5280/5271 foram escolhidas para o Checkin, em vez das mais óbvias 5080/5173.

Login inicial (criado automaticamente na primeira execução, junto com o tenant
"Escola de Tênis" e os 4 pontos de check-in Wellhub informados):

- **E-mail**: `admin@escoladetenis.com`
- **Senha**: `Trocar@123`

> Troque essa senha assim que possível — não há tela de "esqueci minha senha" ainda; para
> resetar, atualize o hash diretamente na coleção `admin_users` do Mongo ou apague o tenant e
> deixe o seed rodar de novo.

## Como rodar sem Docker (desenvolvimento)

Backend:
```bash
cd backend
dotnet run --project src/Checkin.Api   # usa appsettings.Development.json -> mongodb://localhost:27017
```
(suba um MongoDB local antes, ex.: `docker run -p 27017:27017 mongo:7`)

Frontend:
```bash
cd frontend
cp .env.example .env.local   # ajuste VITE_API_URL para a porta do dotnet run (ex.: 5081)
npm install
npm run dev
```

## Testes

- **Backend**: `cd backend && dotnet test` — 60 testes (unit) cobrindo os casos de uso de
  Check-in/Booking/Billing/Auth, os gateways Wellhub/Stripe e o `SimulationController` (ver
  "Integração Wellhub"/"Cobrança / Stripe" abaixo).
- **Backend (E2E do Swagger)**: `cd backend/e2e && npm install && npm test` — Playwright dirige o
  Swagger de verdade (login → Authorize → simular um check-in via "Try it out") e grava vídeo.
  Detalhes em [`backend/e2e/README.md`](backend/e2e/README.md).
- **Frontend (E2E)**: `cd frontend/e2e && npm install && npm test` — Playwright headless dirige o
  app de ponta a ponta (login, CRUD de aluno, pontos de check-in, check-ins, relatórios, logout)
  contra o app rodando, grava vídeo (`.webm`) e screenshots de cada passo, e falha se houver erro
  de console/rede. `npm run test:billing` cobre cadastro → gate de assinatura → checkout real do
  Stripe (ver "Cobrança / Stripe" abaixo). Detalhes em [`frontend/e2e/README.md`](frontend/e2e/README.md).
- **Swagger + simulação manual**: <http://localhost:5280/swagger> — sempre disponível (não só em
  Development; considere restringir isso antes de um go-live de verdade). Faça login em
  `POST /api/auth/login`, clique em "Authorize" e cole `Bearer {token}`. O grupo **Simulation**
  (`POST /api/simulate/wellhub/checkin`, `.../booking-requested`, `.../booking-canceled`) dispara
  os mesmos casos de uso que o webhook real do Wellhub dispararia — sem precisar montar
  payload/assinatura na mão — útil pra testar o pipeline (matching de aluno/vaga, chamada real
  `/access/v1/validate`, gravação do resultado) direto pela UI. Importante: isso simula a
  **notificação**, não o check-in em si — o Wellhub só responde `Approved` para um check-in que
  ele registrou de verdade do lado dele (ver "Pendências" na Access Control API).

## Funcionalidades implementadas

- **Alunos**: CRUD completo (sem login, como pedido), com campo `wellhubMemberId` usado para
  casar os check-ins recebidos com o aluno certo. **Pré-registro automático**: se chega um
  check-in de um `unique_token` sem aluno cadastrado, o sistema cadastra o aluno na hora usando os
  dados que o próprio Wellhub manda no webhook (nome, e-mail, telefone) — conforme a opção
  descrita no e-mail do Wellhub Technical Sales ("que pode optar por fazer um pré-registro do
  usuário"). Testado ao vivo em 2026-09-10 (`CheckinService.RegisterWellhubCheckinAsync`,
  coberto por teste).
- **Pontos de check-in**: CRUD por tenant. Já vem semeado com os 4 pontos Wellhub informados
  (Dynamis Santa Lúcia, Pilar, Buritis e Vila da Serra). Cada ponto tem um botão **"Buscar
  produtos"** que consulta `GET /setup/v1/gyms/:gym_id/products` no Wellhub e vincula os produtos
  (planos/tipos de acesso) reais da unidade ao cadastro, pra ficarem visíveis na lista
  (`CheckinPointService.SyncProductsAsync` / `POST /api/checkin-points/{id}/sync-products`) — são
  os `productId`s usados depois pra criar categorias de aula (ver Booking API).
- **Check-ins**: aprovação sempre automática (não existe fluxo de aprovação manual).
- **Dashboard**: total de check-ins por app, total de alunos cadastrados por app, últimos
  check-ins.
- **Relatórios** (todos com filtro de data `startDate`/`endDate`):
  - por aluno (`/api/reports/by-student`)
  - por escola/unidade (`/api/reports/by-school`)
  - geral (`/api/reports/general`)

## Integração Wellhub — Access Control API v1.0 (✅ confirmada de ponta a ponta, `Approved` de verdade)

Estado atual (2026-09-10): **o fluxo completo funciona contra o Sandbox real, com `Approved` de
verdade** — não só `Rejected`. Temos credenciais de Sandbox (`gym_id: 609` + `api_key` JWT, ver
arquivo `email` — não versionado) e confirmamos cada etapa com chamadas reais:

1. Simulamos um check-in de verdade no lado do Wellhub:
   `POST https://apitesting.partners.gympass.com/helper/v1/gyms/609/simulate/checkins`
   com `{"gympass_user_id": "<id>", "product_id": <id>}` — endpoint real do Sandbox, achado na
   collection do Postman do parceiro (ver nota abaixo). Retorna o payload exato de webhook que o
   Wellhub mandaria pra gente.
2. Assinamos esse payload com `WellhubSignature.Compute` e mandamos pro nosso
   `POST /api/integrations/wellhub/checkins` — o webhook real, com validação de assinatura.
3. Nosso sistema chamou `POST /access/v1/validate` de verdade e recebeu `200` com
   `"validated_at"` preenchido — **check-in gravado como `Approved`**.

Isto fecha 100% o "caminho feliz" que ficava pendente nas rodadas anteriores de teste (que só
tinham conseguido `Rejected`, porque simulavam a notificação sem o Wellhub ter processado um
check-in de verdade do lado dele).

> **De onde veio a confirmação**: o portal público (`developers.wellhub.com`/`developers.gympass.com`)
> segue fora do ar (SPA só mostra o shell, ver histórico deste arquivo) e o link do Postman do
> e-mail é só uma página de texto sem itens de requisição — mas o Deangellis encontrou o arquivo
> **`Old  - Gympass Quick Start Guide - Booking & Access Control API Copy.postman_collection.json`**
> (a collection de verdade, exportada, na raiz do repo) com todos os requests reais: paths,
> headers, bodies de exemplo, e uma pasta inteira de **"Webhook Simulations"** — endpoints
> `/helper/v1/...` que existem só no Sandbox pra simular check-ins/reservas sem precisar de URL
> pública nem esperar o Wellhub disparar nada. Tudo abaixo foi re-confirmado a partir dela +
> chamadas reais, não é mais extrapolação.

### 1) Check-in Webhook (entrada) → `POST /access/v1/validate` (saída) → aprova/rejeita

Conforme o fluxo confirmado por e-mail com o Wellhub Technical Sales — "nosso sistema envia uma
notificação via Webhook [...] seu sistema chama o endpoint /validate [...] com a resposta
positiva, seu sistema libera o acesso físico" —, o check-in **não** é aprovado automaticamente
só por ter chegado o webhook; ele passa por dois passos, ambos implementados:

**a) Webhook recebe a notificação** (`WellhubWebhookController` → `POST /api/integrations/wellhub/checkins`):

```
POST <nossa-url-pública>/api/integrations/wellhub/checkins
Content-Type: application/json
X-Gympass-Signature: <HMAC-SHA1(corpo, secret) em hex, maiúsculo>

{
  "event_type": "checkin",
  "event_data": {
    "user": { "unique_token": "0123456789012", "first_name": "...", "last_name": "...", "email": "...", "phone_number": "..." },
    "location": { "lat": 51.49, "lon": 0.06 },
    "gym": { "id": 500977, "title": "Dynamis Santa Lúcia", "product": { "id": 1, "description": "..." } },
    "timestamp": 1666629613
  }
}
```

- `user.unique_token` é o **Wellhub ID** (`gympass_id`) → casado com `Student.wellhubMemberId`.
- `gym.id` → casado com `CheckinPoint.externalId` (os códigos 500977/804587/500205/547050 em
  produção; `609` é o gym_id do Sandbox).
- Assinatura: HMAC-SHA1 do corpo cru com o Secret, hex, maiúsculo — `WellhubSignature.Compute`
  (`Checkin.Infrastructure.Wellhub`), coberta por teste em `WellhubSignatureTests` com vetor
  calculado fora do .NET (`openssl dgst -sha1 -hmac`), para não validar a implementação contra
  ela mesma. **Diferente do webhook secret, este NÃO é emitido pelo Wellhub**: somos nós que
  escolhemos um valor forte (`openssl rand -hex 32`, já gerado em `.env`) e o enviamos de volta
  ao Wellhub Technical Sales junto da URL pública (ver "Pendências").
- Retry: o Wellhub espera resposta em 1s e tenta de novo até 3x se não responder — por isso o
  check-in é gravado com um id sintético (`unique_token:gym.id:timestamp`) para não duplicar em
  reenvios, e reenvios não chamam `/validate` de novo (`ICheckinRecordRepository.FindByExternalCheckinIdAsync`,
  coberto em `CheckinServiceTests.Is_idempotent_for_repeated_webhook_retries`).

**b) `CheckinService.RegisterWellhubCheckinAsync` chama `IWellhubGateway.ValidateAccessAsync`**
(`POST /access/v1/validate`, headers `Authorization: Bearer {Wellhub:ApiKey}` + `X-Gym-Id: {gym.id}`,
body `{ "gympass_id", "custom_code" }`) e só marca o check-in como `Approved` se a Wellhub
confirmar; resposta negativa (400/401/404) grava `Rejected`. Sem `Wellhub:ApiKey` configurado
(dev local sem credenciais), cai no fallback antigo de aprovar direto.

### 2) Access Control API — demais chamadas de saída (prontas, não usadas pelo webhook)

| Método | Endpoint | Uso |
|---|---|---|
| `POST` | `/access/v1/validate` | Usado pelo fluxo acima — **confirmado, retorna `Approved` de verdade** |
| `POST` | `/access/v1/code/:wellhub_id` | Cria um código de acesso (PIN/QR) do aluno para a academia (catraca/leitor próprio) |
| `PUT` | `/access/v1/code/:wellhub_id` | Atualiza esse código |
| `DELETE` | `/access/v1/code/:wellhub_id` | Remove esse código |

Implementado em `IWellhubGateway` / `WellhubGatewayAdapter`, testado contra um `HttpMessageHandler`
fake em `WellhubGatewayAdapterTests` (confirma path, headers e parsing da resposta sem precisar de
rede) **e ao vivo contra o Sandbox** (ver acima). Os 3 métodos de código de acesso continuam sem
uso por nenhum caso de uso (o cenário atual é o aluno abrir o app Wellhub, não catraca própria) —
prontos pra quando precisar, mas não testados ao vivo.

### Base URL de Sandbox: `https://apitesting.partners.gympass.com`

Confirmado (produção `api.partners.gympass.com` responde 401 pra essa `api_key` — é uma credencial
específica de teste). Já configurado em `WELLHUB_BASE_URL` no `.env`.

### Como reproduzir o "caminho feliz" (gera um check-in `Approved` de verdade no Sandbox)

```bash
API_KEY=$(grep '^WELLHUB_API_KEY=' .env | cut -d= -f2-)
WEBHOOK_SECRET=$(grep '^WELLHUB_WEBHOOK_SECRET=' .env | cut -d= -f2-)

# 1) Simula um check-in de verdade no Wellhub (endpoint real de Sandbox — não existe em produção)
PAYLOAD=$(curl -s -X POST 'https://apitesting.partners.gympass.com/helper/v1/gyms/609/simulate/checkins' \
  -H "Authorization: Bearer $API_KEY" -H 'Content-Type: application/json' \
  -d '{"gympass_user_id": "1000000000001", "product_id": 1217}')
# product_id precisa ser um produto real da unidade — liste com:
#   curl -H "Authorization: Bearer $API_KEY" https://apitesting.partners.gympass.com/setup/v1/gyms/609/products
# (609 no Sandbox tem 1217 "Outdoor Training" e 1218 "Virtual Class")

# 2) Assina e manda pro nosso webhook de verdade
SIGNATURE=$(printf '%s' "$PAYLOAD" | openssl dgst -sha1 -hmac "$WEBHOOK_SECRET" -hex | awk '{print toupper($2)}')
curl -X POST http://localhost:5280/api/integrations/wellhub/checkins \
  -H "X-Gympass-Signature: $SIGNATURE" -H 'Content-Type: application/json' -d "$PAYLOAD"
# -> {"status":"Approved", ...}
```

Os "test users" fixos do guia do Postman (`1000000000001`–`1000000000010`) funcionam como
`gympass_user_id` nesse fluxo — só não servem como atalho pra pular o passo 1 (chamar
`/access/v1/validate` direto pra um desses ids, sem antes simular o check-in, dá 404 "Check-In
not found in database" — testamos os 10, nenhum tem check-in pré-registrado).

## Integração Wellhub — Booking API v1.0 (✅ confirmada de ponta a ponta)

Implementado e testado ao vivo contra o Sandbox 609 em 2026-09-10, do zero até o fim: **listar
produtos → criar categoria → criar aula/slot → simular reserva → confirmar automaticamente →
sincronizar vaga → cancelar**. Todas as chamadas reais retornaram sucesso (200/201/204).

| Etapa | Chamada nossa | Confirmado ao vivo |
| --- | --- | --- |
| Listar produtos da unidade | `GET /api/classes/products/{checkinPointId}` → `GET /setup/v1/gyms/:gym_id/products` | ✅ retornou os 2 produtos reais da 609 |
| Criar categoria | `POST /api/classes` → `POST /booking/v1/gyms/:gym_id/classes` | ✅ criou a categoria `16401`/`16402` de verdade |
| Criar aula/slot | `POST /api/classes/{id}/slots` → `POST /booking/v1/gyms/:gym_id/classes/:class_id/slots` | ✅ criou o slot `304912`–`304914` |
| Reserva confirmada | webhook `booking-requested` → `BookingService` → `PATCH /booking/v1/gyms/:gym_id/bookings/:booking_number` (`status: 2`) | ✅ `204`, status final `Confirmed` |
| Reserva rejeitada (sem vaga) | idem, com `status: 3` (palpite, não documentado — mas aceito com `204` pelo Sandbox) | ✅ `204`, status final `Rejected` |
| Atualização de vagas | `PATCH .../slots/:slot_id` (`total_capacity`, `total_booked`) | ✅ `204` |
| Cancelamento | webhook `booking-canceled` → libera a vaga | ✅ status final `Canceled` |

Como reproduzir (mesma lógica do check-in, usando os helpers de simulação de booking):

```bash
# cria categoria+aula pela nossa API (ver acima "Como rodar os testes" pra pegar $TOKEN)
# simula uma reserva de verdade no Wellhub:
curl -X POST "https://apitesting.partners.gympass.com/helper/v1/gyms/609/simulate/bookings" \
  -H "Authorization: Bearer $API_KEY" -H 'Content-Type: application/json' \
  -d '{"gympass_user_id": "1000000000001", "slot_id": <externalId do slot>, "class_id": <externalId da categoria>}'
# assina o payload retornado e manda pro nosso webhook, igual ao check-in
```

### O que foi corrigido em relação à v1 desta integração

A primeira tentativa (antes de acharmos a collection real) tinha chutado o contrato a partir do
padrão da Access Control API, e errado em vários pontos — todos corrigidos:

- Path errado: `/booking/v1/classes` → certo é `/booking/v1/gyms/:gym_id/classes` (o `gym_id` vai
  na URL, **não** no header `X-Gym-Id` como na Access Control API).
- Campo errado de vaga: `available_spots` → o Wellhub usa `total_capacity`/`total_booked`.
- `product_id` é **obrigatório** pra criar categoria/aula (não sabíamos disso) — agora
  `WellhubClass.ProductId` guarda isso, e `GET /api/classes/products/{checkinPointId}` lista os
  produtos válidos da unidade antes de criar.
- `description` da categoria é obrigatório e não pode ser vazio pro Wellhub (mesmo sendo opcional
  pra nós) — `WellhubBookingGatewayAdapter` cai pro nome da categoria quando não informado.
- Payload do webhook de booking: o campo certo é `slot.booking_number` (string, ex. `"BK_A1B2C3"`),
  não um `booking.id` separado como a v1 tinha chutado.
- Confirmar/rejeitar reserva exige `class_id` no corpo do PATCH, não só o `booking_number` na URL.

### Como rodar os testes

```bash
cd backend
dotnet test                                   # 41 testes: Checkin/Booking services, WellhubSignature, gateways
./scripts/simulate-wellhub-checkin.sh         # end-to-end local: assina e envia um check-in de teste
                                               # para a API rodando (docker compose ou dotnet run),
                                               # cadastra o CheckinPoint 609 se faltar
```

`simulate-wellhub-checkin.sh` precisa de `WELLHUB_WEBHOOK_SECRET` (o mesmo valor de
`Wellhub:WebhookSecret`); as demais variáveis têm default (ver comentários no script). Esse script
gera um check-in `Rejected` (não passa pelo helper de simulação do Wellhub) — pra um `Approved` de
verdade, use a receita em "Como reproduzir o 'caminho feliz'" acima. Pra testar sem terminal, use
o Swagger (`/api/simulate/wellhub/*` — ver seção "Testes" no topo do README).

### Pendências (fora do escopo de código)

1. **Expor a URL do webhook publicamente** — hoje só roda em `localhost`/rede interna do Docker.
   Você decidiu cuidar do deploy/domínio depois; quando tiver, é só registrar
   `<domínio>/api/integrations/wellhub/checkins` no Wellhub. Não bloqueia mais os testes — os
   helpers de simulação (`/helper/v1/gyms/:gym_id/simulate/...`) resolvem isso enquanto não temos
   URL pública, tanto pra check-in quanto pra booking.
2. **Enviar de volta ao Wellhub Technical Sales** (conforme e-mail, "Envio de credenciais de
   sandbox"): a URL pública do item 1 + o `Wellhub:WebhookSecret` que já geramos em `.env`.
3. **Confirmar o código de status "rejeitado" do PATCH de reserva** — usamos `3` (o Sandbox aceita
   com `204`, mas o único exemplo real da collection mostra só o de confirmação, `2`); vale
   confirmar com o Wellhub antes de depender disso em produção.
4. Integration Setup API (3ª parte do e-mail) não foi iniciada — só é necessária se vocês
   decidirem usar URL/secret diferentes por parceiro; caso contrário (mesma URL/secret pra
   todo mundo, que é o que já temos), o e-mail diz que dá pra pular e só pedir a integração
   pelo portal do parceiro.
5. **Limpar dados de teste**: os testes ao vivo desta sessão criaram categorias/aulas/reservas de
   teste de verdade no Sandbox 609 (categorias `16401`/`16402`, slots `304912`–`304914`) e vários
   registros no nosso Mongo local (checkin point "Sandbox Wellhub (609)", alunos e check-ins de
   teste, e tenants de teste do cadastro autosserviço tipo "Academia Playwright") — não custa nada
   real (é Sandbox/Stripe teste), mas avise se quiser que eu limpe o banco local.

## Cobrança / Stripe (✅ cadastro + checkout confirmados de ponta a ponta, webhook pendente)

Autosserviço com cobrança: quem visita `/cadastro` cria o próprio tenant (escola) — sem depender
de ninguém criar a conta manualmente — mas ele nasce com `SubscriptionStatus.Inactive`. Enquanto
não tiver assinatura ativa, o `SubscriptionGateMiddleware` bloqueia **toda rota autenticada**
(alunos, pontos de check-in, check-ins, relatórios, simulação — tudo) com `402 Payment Required`,
exceto login/cadastro e a própria tela de cobrança (senão ninguém conseguiria assinar pra sair do
bloqueio). O tenant seedado original ("Escola de Tênis") continua `Active` — não foi afetado pelo
gate (ver `Tenant.SubscriptionStatus`, comentário sobre o default de migração).

**Fluxo**: `POST /api/auth/register` (cria tenant + admin, já loga) → front manda pra
`/assinatura` → `POST /api/billing/checkout` cria um Customer no Stripe (se ainda não tiver) e uma
Checkout Session (assinatura mensal, valor de `Billing:PriceAmountCents`/`Billing:Currency`) →
admin paga no Checkout hospedado do Stripe → Stripe manda `checkout.session.completed` +
`customer.subscription.*` pro nosso `POST /api/billing/webhook` → `BillingService` marca o tenant
`Active` (ou `PastDue`/`Canceled` conforme o status real da assinatura). `GET /api/billing/status`
e `POST /api/billing/portal` (Stripe Billing Portal, pra trocar cartão/cancelar) completam o
básico. Um plano único mensal — sem multi-plano por enquanto (decisão do Deangellis, dá pra
evoluir depois).

**Testado ao vivo em 2026-09-10** contra a conta Stripe real (modo teste, `sk_test_...`):

- Cadastro cria o tenant `Inactive` de verdade.
- Gate bloqueia: acessar `/api/students` sem assinatura retorna `402` limpo.
- `POST /api/billing/checkout` cria o Customer e retorna uma URL real do Stripe Checkout
  (`checkout.stripe.com/c/pay/cs_test_...`), com o preço (R$ 99,00/mês — valor de exemplo, ainda
  não decidido) e e-mail já preenchidos.
- Vídeo do fluxo completo (cadastro → gate → checkout): `frontend/e2e/billing.mjs`
  (`npm run test:billing` dentro de `frontend/e2e`).

**Não testado ainda**: o webhook em si (`checkout.session.completed`/`customer.subscription.*`
→ tenant vira `Active`) — o Stripe só entrega webhooks numa URL pública, e assim como o Wellhub,
isso ainda não está exposto. `STRIPE_WEBHOOK_SECRET` está vazio no `.env` por isso. Assim que
tiver a URL pública (mesmo item pendente do Wellhub — ver seção acima), cadastre o endpoint
`<URL pública>/api/billing/webhook` em <https://dashboard.stripe.com/test/webhooks>, copie o
Signing Secret (`whsec_...`) pro `.env`, e o fluxo fecha 100% (dá pra testar com o cartão de teste
padrão do Stripe, `4242 4242 4242 4242`, sem custo nenhum — é modo teste).

## TotalPass

O domínio já suporta múltiplos apps (`IntegrationApp.Wellhub` / `IntegrationApp.TotalPass`,
campo `totalPassMemberId` no aluno, CRUD de pontos de check-in por app). Adicionar TotalPass no
futuro é: 1) criar `Checkin.Infrastructure.TotalPass` com seu adapter, 2) adicionar um endpoint
de webhook equivalente, 3) permitir `App: TotalPass` no formulário de pontos de check-in do
frontend — sem alterar Domain, Application, dashboard ou relatórios.
