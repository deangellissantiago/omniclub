# Roadmap — métricas de dia a dia da academia

Inspirado no que sistemas de gestão de academia como Pacto e NextFit já entregam, adaptado ao
que o OmniClub tem de exclusivo: dados de check-in vindos direto dos apps de benefício
(Wellhub/TotalPass), não um sistema de matrícula/cobrança de aluno.

## Fase 1 — dado já existia no schema, só faltava agregar (✅ implementado)

| Métrica | Backend | Frontend |
|---|---|---|
| Alunos "sumidos" (sem check-in há 14/30+ dias) | `ReportService.EngagementAsync` → `GET /api/reports/engagement` | Aba **Engajamento** em Relatórios + painel "Alunos sumidos" no Dashboard |
| Frequência média por aluno (check-ins/semana, 30 dias) | idem (`AvgCheckinsPerWeek`) | idem, coluna "Frequência/semana" |
| Heatmap de horário de pico (dia × hora) | `ReportService.PeakHoursAsync` → `GET /api/reports/peak-hours` | Aba **Horários de pico** |
| Taxa de comparecimento em aulas (ocupação/no-show de `Booking`) | `ReportService.AttendanceAsync` → `GET /api/reports/attendance` | Aba **Aulas** |
| Novos alunos por período (curva de crescimento) | `ReportService.GrowthAsync` → `GET /api/reports/growth` | Aba **Crescimento** |
| Exportar CSV/Excel | `GET /api/reports/{by-student,by-school,engagement}/export` (`CsvExporter`, `;` + BOM UTF-8) | Botão "Exportar CSV" nas abas Por aluno, Por escola e Engajamento |

Testado: 67 testes de backend (`ReportServiceTests`), build + lint do frontend limpos, e
validado ao vivo contra o Mongo do ambiente de dev (login real, chamada aos 4 endpoints novos,
export CSV) + suíte E2E (`frontend/e2e/smoke.mjs`) sem erros de console/rede.

## Fase 2 — diferencial exclusivo do OmniClub (✅ implementado)

| Métrica | Backend | Frontend |
|---|---|---|
| Conciliação de repasse Wellhub/TotalPass | `CheckinPoint.PricePerCheckinCents` (configurável por ponto) + `ReportService.RevenueAsync` → `GET /api/reports/revenue` | Campo "Valor por check-in (R$)" no cadastro de Pontos de check-in + aba **Repasse** em Relatórios |
| Penetração de cada app na base | `ReportService.AppPenetrationAsync` → `GET /api/reports/app-penetration` | Painel "Penetração por app" (aba Repasse) |
| Ranking de unidades com variação período a período | `ReportService.SchoolRankingAsync` (compara com período anterior de mesma duração) → `GET /api/reports/school-ranking` | Painel "Ranking de unidades" (aba Repasse), badge ▲/▼/"Novo" |

Modelo de repasse: valor fixo em centavos por check-in *aprovado*, configurável por ponto de
check-in (unidades diferentes podem ter tabelas diferentes no contrato). Nunca estima em cima de
um ponto sem valor configurado — mostra "—" e conta em "pontos sem valor configurado" em vez de
inventar um número.

Testado: 75 testes de backend (8 novos: `RevenueAsync`, `AppPenetrationAsync`,
`SchoolRankingAsync` e persistência do preço em `CheckinPointService`), build + lint do frontend
limpos, validado ao vivo contra o Mongo do ambiente de dev (preço configurado via API, os 3
endpoints novos + export CSV conferidos) e suíte E2E sem erros de console/rede.

## Fase 3 — retenção proativa / régua de relacionamento (não implementado)

- Alerta automático (e-mail/WhatsApp) pro aluno que sumiu há X dias — depende de e-mail
  transacional existir no produto primeiro.
- Aviso pro admin quando o movimento de um ponto cai muito semana a semana.
- Aniversário do aluno na régua — precisa adicionar data de nascimento ao cadastro de `Student`
  (campo não existe hoje).

## Fase 4 — funil leve / CRM (não implementado)

- Funil pré-registro automático → virou recorrente (≥2 check-ins) → engajado (check-in semanal).
