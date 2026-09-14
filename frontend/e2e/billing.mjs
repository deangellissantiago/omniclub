// E2E do cadastro + assinatura (Stripe): cria um tenant novo, confirma que o gate de assinatura
// (SubscriptionGateMiddleware, 402) bloqueia o resto do sistema, e confirma que "Assinar agora"
// chega de verdade no Checkout hospedado do Stripe (modo teste — sk_test_...). Não completa o
// pagamento (precisaria do STRIPE_WEBHOOK_SECRET configurado com uma URL pública pra confirmar
// a ativação — ver README, "Cobrança / Stripe").
import { chromium } from 'playwright-core';

const browser = await chromium.launch({ executablePath: '/usr/bin/google-chrome', headless: true, args: ['--no-sandbox','--disable-dev-shm-usage'] });
const page = await browser.newPage({ viewport: { width: 1400, height: 900 } });
const consoleErrors = [];
page.on('console', (m) => { if (m.type() === 'error') consoleErrors.push(m.text()); });
page.on('response', (r) => { if (r.status() >= 500) consoleErrors.push(`${r.status()} ${r.url()}`); });

const email = `teste.billing.${Date.now()}@example.com`;

await page.goto('http://localhost:5271/cadastro', { waitUntil: 'networkidle' });
await page.screenshot({ path: '/home/deangellis-santiago/checkin/frontend/e2e/last-run/billing-01-cadastro.png', fullPage: true });

await page.fill('input[placeholder="Ex.: Academia Vida Ativa"]', 'Academia Playwright');
await page.fill('input[placeholder="Seu nome completo"]', 'Teste Playwright');
await page.fill('input[type=email]', email);
await page.fill('input[type=password]', 'SenhaForte@123');
await page.click('button:has-text("Criar conta")');

await page.waitForURL('**/assinatura**', { timeout: 15000 });
await page.waitForSelector('text=Assine para continuar', { timeout: 10000 });
await page.screenshot({ path: '/home/deangellis-santiago/checkin/frontend/e2e/last-run/billing-02-assinatura.png', fullPage: true });

// tenta acessar o dashboard direto (deve voltar pra /assinatura por causa do 402)
await page.goto('http://localhost:5271/', { waitUntil: 'networkidle' });
await page.waitForURL('**/assinatura**', { timeout: 10000 });
console.log('gate funcionou: tentativa de acessar / voltou pra', page.url());
await page.screenshot({ path: '/home/deangellis-santiago/checkin/frontend/e2e/last-run/billing-03-gate.png', fullPage: true });

// clica em "Assinar agora" -> deve redirecionar pro Stripe de verdade
await page.click('button:has-text("Assinar agora")');
await page.waitForURL('**checkout.stripe.com**', { timeout: 15000 });
console.log('redirecionou pro Stripe:', page.url());
await page.waitForTimeout(1500);
await page.screenshot({ path: '/home/deangellis-santiago/checkin/frontend/e2e/last-run/billing-04-stripe-checkout.png', fullPage: true });

console.log('CONSOLE_ERRORS:', JSON.stringify(consoleErrors, null, 2));
await browser.close();
