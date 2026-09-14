# Deploy em produção — omniclub.run

VPS: `72.61.221.130` — a mesma que já roda `assembleia` (votocondominio.com.br)
e `clubedotenis`, com um Caddy próprio (`caddy-proxy`) como único ponto de
entrada público da VPS (portas 80/443), renovando certificado TLS
automaticamente via Let's Encrypt. O omniclub **não sobe um segundo Caddy**
— usaria as mesmas portas 80/443 e o container falharia ao subir. Em vez
disso, só o `frontend` entra na rede `caddy_edge` (já existe nesta VPS,
criada no deploy do clubedotenis), e o roteamento de `omniclub.run` vira
mais um bloco no Caddyfile do assembleia (bloco pronto no `Caddyfile` deste
repo — ver seção 3).

O backend continua isolado, inalcançável de fora da rede docker deste
projeto: o nginx do próprio container do frontend já proxya `/api/` para o
serviço `backend` internamente (ver `frontend/nginx.conf`).

Em produção o backend se conecta direto no MongoDB Atlas via connection
string (`MONGODB_URI`, seção 4) — igual ao assembleia/clubedotenis. O
serviço `mongo` do `docker-compose.yml` base continua existindo só para o
dev local; na VPS ele nunca é iniciado (seção 6 sobe só `backend frontend`
explicitamente).

## 1. DNS

No provedor onde `omniclub.run` está registrado, crie os registros tipo
**A** apontando para o IP da VPS:

| Tipo | Nome | Valor |
|---|---|---|
| A | `@` (raiz) | `72.61.221.130` |
| A | `www` | `72.61.221.130` |

Confirme a propagação antes de seguir:
```bash
dig +short omniclub.run
dig +short www.omniclub.run
```
Ambos devem devolver `72.61.221.130`.

## 2. Preparar a pasta de deploy na VPS

As imagens (backend/frontend) são buildadas pelo GitHub Actions e publicadas
no GHCR — a VPS nunca precisa do código-fonte, só destes arquivos de
compose:

```bash
mkdir -p /opt/omniclub
scp docker-compose.yml docker-compose.prod.yml SEU_USUARIO@72.61.221.130:/opt/omniclub/
```

(o `Caddyfile` deste repo não vai para `/opt/omniclub` — ele é só a
referência do bloco a colar no Caddy compartilhado, seção 3).

## 3. Integrar com o Caddy compartilhado (fora deste repo)

Quem expõe 80/443 nesta VPS é o `caddy-proxy` do projeto `assembleia`,
rodando em algo como `~/assembleia` ou `/opt/assembleia`.

1. A rede `caddy_edge` já existe nesta VPS (criada no deploy do
   clubedotenis) — não precisa recriar. Se for verificar:
   ```bash
   docker network ls | grep caddy_edge
   ```
2. Adicione o bloco do `Caddyfile` deste repo ao final do Caddyfile do
   assembleia (mesmo arquivo que já tem os blocos de
   `votocondominio.com.br` e `clubedotenis.app`/`app.clubedotenis.app`):
   ```
   omniclub.run, www.omniclub.run {
       reverse_proxy omniclub-frontend:80
   }
   ```
3. Suba de novo o `caddy-proxy` para ele pegar o Caddyfile atualizado:
   ```bash
   cd ~/assembleia   # ou onde estiver o compose do caddy-proxy
   docker compose up -d caddy
   docker compose logs -f caddy
   ```
   Espere aparecer algo como `certificate obtained successfully` para
   `omniclub.run`/`www.omniclub.run` antes de testar.

## 4. Configurar o `.env` de produção na VPS

`.env` não vai por git nem pelo workflow — crie
`/opt/omniclub/.env` manualmente na VPS com os mesmos segredos do seu `.env`
local, ajustando o que muda em produção:

```bash
JWT_SECRET=...
WELLHUB_BASE_URL=https://api.partners.gympass.com
WELLHUB_API_KEY=...
WELLHUB_WEBHOOK_SECRET=...
STRIPE_SECRET_KEY=...
STRIPE_WEBHOOK_SECRET=...
STRIPE_PRICE_AMOUNT_CENTS=9900
STRIPE_CURRENCY=brl

FRONTEND_URL=https://omniclub.run
MONGODB_URI=mongodb+srv://usuario:senha@cluster.mongodb.net/?retryWrites=true&w=majority

SEED_ADMIN_EMAIL=admin@seudominio.com
SEED_ADMIN_PASSWORD=escolha-uma-senha-forte-aqui
```

`FRONTEND_URL`, `MONGODB_URI`, `SEED_ADMIN_EMAIL` e `SEED_ADMIN_PASSWORD`
são obrigatórios no `docker-compose.prod.yml` — sem algum deles o
`docker compose up` recusa subir o `backend` pedindo a variável que falta
(`FRONTEND_URL` vira `Cors__AllowedOrigins__0`/`Billing__FrontendBaseUrl`;
`MONGODB_URI` vira `MongoDb__ConnectionString`; os dois últimos viram
`Seed__AdminEmail`/`Seed__AdminPassword` — ver seção 7).

`MONGODB_URI` é a connection string do cluster no MongoDB Atlas — o nome do
banco (`checkin_db`) não precisa estar na URI, quem define isso é
`MongoDb__DatabaseName` (já fixo em `checkin_db` no `docker-compose.yml`
base).

Note que `VITE_API_URL` **não** entra neste `.env`: diferente do
clubedotenis, o frontend deste projeto não tem substituição em runtime —
`VITE_API_URL` já vem inlinada na imagem publicada pelo CI (padrão
`https://omniclub.run/api`, ver `.github/workflows/build-images.yml`). Se
precisar mudar isso, é preciso rebuildar a imagem (variável de repositório
`VITE_API_URL` no GitHub, não `.env` da VPS).

## 5. Firewall

80, 443 (Caddy compartilhado) e 22 (SSH) já devem estar liberados pelos
projetos que subiram o `caddy-proxy` primeiro — nada novo a abrir para o
omniclub. Não abra nenhuma porta do backend/frontend/mongo — eles não
precisam disso, o `caddy-proxy` compartilhado já alcança o frontend pela
rede docker (`caddy_edge`).

## 6. Subir (e atualizar depois)

O workflow só builda e publica as imagens no GHCR — não faz deploy sozinho.
Subir e atualizar a VPS é sempre manual, rodando isto lá:

```bash
cd /opt/omniclub
docker compose -f docker-compose.yml -f docker-compose.prod.yml pull backend frontend
docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d backend frontend
```

Liste `backend frontend` explicitamente (sem `mongo`) — o serviço `mongo` do
`docker-compose.yml` base é só para dev local; nesta VPS o banco é o Atlas
(`MONGODB_URI`), então ele nunca deve subir aqui.

`pull` só traz algo novo depois que existir pelo menos um push na `main`
tocando `backend/**` ou `frontend/**` (as imagens têm que existir no
ghcr.io primeiro). Repita esses dois comandos sempre que quiser atualizar
para a versão mais recente publicada.

Se a seção 3 ainda não foi feita, o `up -d` recusa subir o `frontend`
reclamando que a rede `caddy_edge` (externa) não existe.

## 7. Primeiro login

O backend popula o banco sozinho na primeira vez que sobe contra um banco
vazio (`DataSeeder`, roda uma única vez — checa se já existe algum tenant e,
se sim, não faz nada): cria o tenant "Escola de Tênis" já `Active` (sem
precisar passar pelo checkout do Stripe), os 4 pontos de check-in Wellhub já
combinados com o cliente, e um admin com o e-mail/senha de
`SEED_ADMIN_EMAIL`/`SEED_ADMIN_PASSWORD` (seção 4). Se essas duas variáveis
não estiverem definidas no `.env`, o `up -d` nem sobe (seção 4) — isso é de
propósito, pra não nascer em produção com a senha placeholder que existe no
código-fonte (`admin@escoladetenis.com` / `Trocar@123`, usada só em dev).

Depois do primeiro `up -d`, entre em `https://omniclub.run` com o e-mail e a
senha que você definiu em `SEED_ADMIN_EMAIL`/`SEED_ADMIN_PASSWORD`.

**Importante**: hoje não existe tela de "trocar senha" no produto. Se
precisar trocar a senha desse admin depois (ou de qualquer outro usuário),
o único jeito é editar o hash direto na coleção `admin_users` do Atlas (o
hash é bcrypt — `BCrypt.Net.BCrypt.HashPassword(novaSenha)`) ou, contanto
que ainda não haja dado real em produção, apagar o tenant e reiniciar o
backend para o `DataSeeder` rodar de novo com um novo valor de
`SEED_ADMIN_PASSWORD`.

## 8. Verificar

```bash
curl -I https://omniclub.run
curl -I https://omniclub.run/api/auth/login
```

Teste também no navegador — login com o admin da seção 7 e navegação pela
aplicação.

## 9. Visibilidade das imagens no GHCR

Por padrão, pacotes recém-criados no GHCR costumam nascer **privados**. Se a
VPS não tiver `docker login ghcr.io` configurado, o `pull` da seção 6 falha.
Duas opções:
- Tornar os pacotes `omniclub-backend`/`omniclub-frontend` públicos em
  `github.com/deangellissantiago?tab=packages` após o primeiro push — a VPS
  puxa sem autenticação.
- Ou manter privado e rodar `docker login ghcr.io` na VPS com um Personal
  Access Token (`read:packages`) antes do `pull`.

## Depois de estar no ar

- **Webhook do Wellhub/Stripe**: atualize as URLs de webhook nos respectivos
  dashboards para apontar para `https://omniclub.run/api/...` (ver README
  para os paths exatos).
- **Backup do Mongo**: o banco vive no MongoDB Atlas, não num volume local —
  configure backup pelo próprio painel do Atlas (backups automáticos ou
  snapshots, conforme o tier do cluster).
- **Atualizar o deploy**: depois de um push/merge na `main`, o workflow
  builda e publica as imagens novas no GHCR sozinho — mas a VPS só atualiza
  quando você rodar de novo os dois comandos da seção 6 (`pull` + `up -d`).
