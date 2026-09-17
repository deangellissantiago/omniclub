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

## Fase 3 — retenção proativa / régua de relacionamento (✅ implementado, canal: WhatsApp)

| Alerta | Backend | Regra |
|---|---|---|
| Aluno sumido | `RetentionAlertService.SendInactivityAlertsAsync` | 14+ dias sem check-in, cooldown de 14 dias |
| Aniversário | `RetentionAlertService.SendBirthdayAlertsAsync` | `Student.BirthDate` bate com hoje, uma vez por ano |
| Queda de movimento | `RetentionAlertService.SendPointDropAlertsAsync` | últimos 7 dias caíram 30%+ vs. os 7 anteriores, cooldown de 7 dias |

Rodado uma vez por dia por `RetentionAlertHostedService` (`Checkin.Api/BackgroundJobs`).

**Decisão de arquitetura**: o envio de WhatsApp virou um serviço standalone separado —
**[whatsapp-service](https://github.com/deangellissantiago/whatsapp-service)** (Meta WhatsApp
Cloud API), fora deste repositório, pra poder ser reaproveitado por outros projetos. O OmniClub
só fala com ele por HTTP (`IWhatsAppSender` / `Checkin.Infrastructure.WhatsApp`) — ver seção
"Régua de relacionamento (WhatsApp)" no README para como ligar os dois em dev local
(`docker-compose.whatsapp.yml`, rede `whatsapp_shared`) e o que falta configurar na conta Meta
pra alertas saírem de verdade.

Também entrou: `Student.BirthDate` (campo novo, com tela de cadastro), `Tenant.AlertsWhatsAppPhone`
(sem tela ainda — mesmo caminho do reset de senha, edita direto no Mongo).

Testado: 100 testes de backend no OmniClub (25 novos: `RetentionAlertServiceTests` cobrindo os
três alertas + cooldowns + casos de borda, `WhatsAppServiceClientTests` cobrindo o contrato HTTP
com o whatsapp-service) + 18 testes no whatsapp-service (contrato com a Graph API da Meta contra
handler fake, e os endpoints HTTP via `WebApplicationFactory`). Verificado ao vivo: os dois
serviços buildam e rodam em Docker, a rede compartilhada resolve `whatsapp-service:8080` de
dentro do container do backend (testado com um container `curl` avulso na mesma rede), e o job
loga corretamente "pulado" quando `WhatsApp:BaseUrl` não está configurado. **Não verificado**: o
envio de ponta a ponta contra credenciais reais da Meta — isso exige conta no Business Manager e
templates aprovados, que só o Deangellis pode configurar (ver "Pendências" no README).

## Fase 4 — funil leve / CRM (não implementado)

- Funil pré-registro automático → virou recorrente (≥2 check-ins) → engajado (check-in semanal).
