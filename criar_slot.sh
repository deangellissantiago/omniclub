#!/usr/bin/env bash
set -euo pipefail
BASE_URL="https://omniclub.run"
ADMIN_EMAIL="admin@omniclub.run"
ADMIN_PASSWORD='Trocar@123'
GYM_EXTERNAL_ID="609"

echo "==> 1) Login"
TOKEN=$(curl -sS -X POST "$BASE_URL/api/auth/login" -H 'Content-Type: application/json' \
  -d "{\"email\":\"$ADMIN_EMAIL\",\"password\":\"$ADMIN_PASSWORD\"}" \
  | python3 -c "import sys,json;print(json.load(sys.stdin)['token'])")

echo "==> 2) Achando (ou criando) o ponto de check-in do gym $GYM_EXTERNAL_ID"
POINT_ID=$(curl -sS "$BASE_URL/api/checkin-points" -H "Authorization: Bearer $TOKEN" \
  | python3 -c "
import json,sys
pts=json.load(sys.stdin)
m=[p for p in pts if p['app']=='Wellhub' and p['externalId']=='$GYM_EXTERNAL_ID']
print(m[0]['id'] if m else '')
")
if [ -z "$POINT_ID" ]; then
  echo "    não existe, criando..."
  POINT_ID=$(curl -sS -X POST "$BASE_URL/api/checkin-points" -H "Authorization: Bearer $TOKEN" \
    -H 'Content-Type: application/json' \
    -d "{\"app\":\"Wellhub\",\"externalId\":\"$GYM_EXTERNAL_ID\",\"name\":\"Sandbox Wellhub ($GYM_EXTERNAL_ID)\",\"active\":true}" \
    | python3 -c "import sys,json;print(json.load(sys.stdin)['id'])")
fi
echo "    checkinPointId=$POINT_ID"

echo "==> 3) Buscando produtos válidos da unidade (via Wellhub)"
PRODUCT_ID=$(curl -sS "$BASE_URL/api/classes/products/$POINT_ID" -H "Authorization: Bearer $TOKEN" \
  | python3 -c "import json,sys; p=json.load(sys.stdin); print(p[0]['productId'] if p else '')")
if [ -z "$PRODUCT_ID" ]; then
  echo "ERRO: nenhum produto retornado — confira se Wellhub:ApiKey está configurado e válido." >&2
  exit 1
fi
echo "    productId=$PRODUCT_ID"

echo "==> 4) Criando categoria de aula"
CLASS_ID=$(curl -sS -X POST "$BASE_URL/api/classes" -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' \
  -d "{\"checkinPointId\":\"$POINT_ID\",\"name\":\"Teste certificação Wellhub\",\"description\":\"Slot de teste para o Wellhub Technical Sales\",\"productId\":$PRODUCT_ID}" \
  | python3 -c "import sys,json;print(json.load(sys.stdin)['id'])")
echo "    classId=$CLASS_ID"

echo "==> 5) Criando o slot (amanhã, 1h de duração, 10 vagas)"
STARTS_AT=$(python3 -c "import datetime;print((datetime.datetime.utcnow()+datetime.timedelta(days=1)).strftime('%Y-%m-%dT%H:%M:%SZ'))")
ENDS_AT=$(python3 -c "import datetime;print((datetime.datetime.utcnow()+datetime.timedelta(days=1,hours=1)).strftime('%Y-%m-%dT%H:%M:%SZ'))")
SLOT=$(curl -sS -X POST "$BASE_URL/api/classes/$CLASS_ID/slots" -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' \
  -d "{\"startsAt\":\"$STARTS_AT\",\"endsAt\":\"$ENDS_AT\",\"capacity\":10}")
echo "$SLOT" | python3 -m json.tool

EXTERNAL_ID=$(echo "$SLOT" | python3 -c "import sys,json;print(json.load(sys.stdin).get('externalId') or '')")
echo
if [ -n "$EXTERNAL_ID" ]; then
  echo "✅ Slot criado no Wellhub de verdade — externalId (id que o Wellhub reconhece): $EXTERNAL_ID"
else
  echo "⚠️  Slot gravado localmente, mas SEM externalId — confira Wellhub:ApiKey/BaseUrl."
fi
