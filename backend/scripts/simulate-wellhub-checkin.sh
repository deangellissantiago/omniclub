#!/usr/bin/env bash
# Simula o Check-in Webhook do Wellhub contra a API local, de ponta a ponta:
#   1. loga como admin seed e garante que existe um CheckinPoint Wellhub com o gym_id de sandbox
#   2. monta o payload exatamente como documentado (ver WellhubWebhookController)
#   3. assina com HMAC-SHA1 (mesmo algoritmo de WellhubSignature.Compute) usando WELLHUB_WEBHOOK_SECRET
#   4. faz o POST em /api/integrations/wellhub/checkins com X-Gympass-Signature
#   5. imprime a resposta (Approved = passou pelo /access/v1/validate de verdade; Rejected = o
#      Wellhub recusou o passe; erro de conexão = confira Wellhub:ApiKey/BaseUrl)
#
# Não expõe nada publicamente — é só para testar localmente a validação de assinatura + o fluxo
# de aprovação/rejeição antes de registrar a URL de verdade no Wellhub (ver README).
#
# Uso:
#   WELLHUB_WEBHOOK_SECRET=<o mesmo valor de Wellhub:WebhookSecret> \
#   GYM_EXTERNAL_ID=609 \
#   ADMIN_EMAIL=admin@escoladetenis.com ADMIN_PASSWORD='Trocar@123' \
#   BASE_URL=http://localhost:5280 \
#     ./simulate-wellhub-checkin.sh

set -euo pipefail

BASE_URL="${BASE_URL:-http://localhost:5280}"
ADMIN_EMAIL="${ADMIN_EMAIL:-admin@escoladetenis.com}"
ADMIN_PASSWORD="${ADMIN_PASSWORD:-Trocar@123}"
GYM_EXTERNAL_ID="${GYM_EXTERNAL_ID:-609}"          # gym_id de Sandbox informado pelo Wellhub
UNIQUE_TOKEN="${UNIQUE_TOKEN:-sandbox-user-0001}"  # user.unique_token (= gympass_id no /validate)
WEBHOOK_SECRET="${WELLHUB_WEBHOOK_SECRET:?defina WELLHUB_WEBHOOK_SECRET (o mesmo valor configurado em Wellhub:WebhookSecret)}"

need() { command -v "$1" >/dev/null 2>&1 || { echo "Faltando dependência: $1" >&2; exit 1; }; }
need curl; need openssl; need python3

echo "==> Login como $ADMIN_EMAIL em $BASE_URL"
TOKEN=$(curl -sS -X POST "$BASE_URL/api/auth/login" \
  -H 'Content-Type: application/json' \
  -d "{\"email\":\"$ADMIN_EMAIL\",\"password\":\"$ADMIN_PASSWORD\"}" | python3 -c 'import json,sys; print(json.load(sys.stdin)["token"])')

echo "==> Garantindo CheckinPoint Wellhub para gym_id=$GYM_EXTERNAL_ID"
EXISTING=$(curl -sS "$BASE_URL/api/checkin-points" -H "Authorization: Bearer $TOKEN" \
  | python3 -c "import json,sys; d=json.load(sys.stdin); print('yes' if any(p['externalId']=='$GYM_EXTERNAL_ID' and p['app']=='Wellhub' for p in d) else 'no')")

if [ "$EXISTING" = "no" ]; then
  curl -sS -X POST "$BASE_URL/api/checkin-points" \
    -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
    -d "{\"app\":\"Wellhub\",\"externalId\":\"$GYM_EXTERNAL_ID\",\"name\":\"Sandbox Wellhub ($GYM_EXTERNAL_ID)\",\"active\":true}" \
    > /dev/null
  echo "    criado."
else
  echo "    já existe."
fi

TIMESTAMP=$(date +%s)
BODY=$(python3 - "$UNIQUE_TOKEN" "$GYM_EXTERNAL_ID" "$TIMESTAMP" <<'PY'
import json, sys
unique_token, gym_id, ts = sys.argv[1], int(sys.argv[2]), int(sys.argv[3])
payload = {
    "event_type": "checkin",
    "event_data": {
        "user": {
            "unique_token": unique_token,
            "first_name": "Sandbox",
            "last_name": "Tester",
            "email": "sandbox.tester@example.com",
            "phone_number": None,
        },
        "location": {"lat": -19.9227, "lon": -43.9451},
        "gym": {"id": gym_id, "title": "Sandbox", "product": {"id": 1, "description": "Gym"}},
        "timestamp": ts,
    },
}
# separators sem espaço: precisa bater exatamente com o corpo cru que o servidor recebe e assina
print(json.dumps(payload, separators=(",", ":")))
PY
)

SIGNATURE=$(printf '%s' "$BODY" | openssl dgst -sha1 -hmac "$WEBHOOK_SECRET" -hex | awk '{print toupper($2)}')

echo "==> POST /api/integrations/wellhub/checkins (unique_token=$UNIQUE_TOKEN, gym.id=$GYM_EXTERNAL_ID)"
curl -sS -i -X POST "$BASE_URL/api/integrations/wellhub/checkins" \
  -H 'Content-Type: application/json' \
  -H "X-Gympass-Signature: $SIGNATURE" \
  -d "$BODY"
echo
