// Testa o grupo "Simulation" do Swagger pela UI de verdade (o jeito que o Deangellis vai usar):
// login -> Authorize -> POST /api/simulate/wellhub/checkin via "Try it out" -> confere a resposta.
// Grava vídeo (.webm) + screenshot de cada passo.

import { chromium } from 'playwright-core';
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const BASE_URL = process.env.BASE_URL || 'http://localhost:5280';
const ADMIN_EMAIL = process.env.ADMIN_EMAIL || 'admin@escoladetenis.com';
const ADMIN_PASSWORD = process.env.ADMIN_PASSWORD || 'Trocar@123';
const CHROME_PATH = process.env.CHROME_PATH || '/usr/bin/google-chrome';
const GYMPASS_ID = process.env.GYMPASS_ID || `swagger-demo-${Date.now()}`;
const GYM_EXTERNAL_ID = process.env.GYM_EXTERNAL_ID || '609';
const OUT_DIR = process.env.OUT_DIR || path.join(__dirname, 'last-run');
const VIDEO_DIR = path.join(OUT_DIR, 'video');
const SHOT_DIR = path.join(OUT_DIR, 'screenshots');

fs.rmSync(OUT_DIR, { recursive: true, force: true });
fs.mkdirSync(VIDEO_DIR, { recursive: true });
fs.mkdirSync(SHOT_DIR, { recursive: true });

let shotN = 0;
async function shot(page, name) {
  shotN += 1;
  const file = path.join(SHOT_DIR, `${String(shotN).padStart(2, '0')}_${name}.png`);
  await page.screenshot({ path: file, fullPage: true });
  console.log('shot:', file);
}

const browser = await chromium.launch({
  executablePath: CHROME_PATH, headless: true, args: ['--no-sandbox', '--disable-dev-shm-usage'],
});
const context = await browser.newContext({
  viewport: { width: 1400, height: 1000 },
  recordVideo: { dir: VIDEO_DIR, size: { width: 1400, height: 1000 } },
});
const page = await context.newPage();
const consoleErrors = [];
page.on('console', (m) => { if (m.type() === 'error') consoleErrors.push(m.text()); });
page.on('pageerror', (e) => consoleErrors.push(e.message));

let step = 'start';
try {
  step = 'nav swagger';
  await page.goto(`${BASE_URL}/swagger/index.html`, { waitUntil: 'networkidle' });
  await page.waitForSelector('text=Checkin API', { timeout: 15000 });
  await shot(page, 'swagger_home');

  // ---- Login via a própria UI (POST /api/auth/login) -------------------------------------
  step = 'expand auth/login';
  await page.click('#operations-Auth-post_api_auth_login');
  await page.waitForSelector('button:has-text("Try it out")');
  await shot(page, 'auth_login_expanded');

  step = 'try it out (login)';
  await page.click('#operations-Auth-post_api_auth_login button:has-text("Try it out")');
  const loginTextarea = page.locator('#operations-Auth-post_api_auth_login textarea.body-param__text');
  await loginTextarea.fill(JSON.stringify({ email: ADMIN_EMAIL, password: ADMIN_PASSWORD }, null, 2));
  await shot(page, 'auth_login_filled');

  step = 'execute (login)';
  await page.click('#operations-Auth-post_api_auth_login button:has-text("Execute")');
  const loginResponseBody = page.locator('#operations-Auth-post_api_auth_login table.live-responses-table pre.microlight').first();
  await loginResponseBody.waitFor({ timeout: 10000 });
  const loginRespText = await loginResponseBody.innerText();
  const token = JSON.parse(loginRespText).token;
  if (!token) throw new Error('Não consegui extrair o token da resposta do login: ' + loginRespText);
  console.log('token obtido via UI, tamanho:', token.length);
  await shot(page, 'auth_login_executed');

  // ---- Authorize (botão global) ------------------------------------------------------------
  step = 'authorize: abrir modal';
  await page.click('button.btn.authorize');
  await page.waitForSelector('.auth-container input[type=text]');
  await shot(page, 'authorize_modal_open');

  step = 'authorize: colar token';
  await page.fill('.auth-container input[type=text]', `Bearer ${token}`);
  await page.click('.auth-container button:has-text("Authorize")');
  await page.click('.dialog-ux .btn-done, button:has-text("Close")');
  await shot(page, 'authorize_done');

  // ---- Simulation: POST /api/simulate/wellhub/checkin --------------------------------------
  // (o grupo "Simulation" já vem expandido por padrão nesta UI, igual aos outros grupos —
  // só precisa abrir a operação em si.)
  step = 'expand simulate checkin operation';
  await page.locator('#operations-Simulation-post_api_simulate_wellhub_checkin').scrollIntoViewIfNeeded();
  await page.click('#operations-Simulation-post_api_simulate_wellhub_checkin');
  await page.waitForSelector('#operations-Simulation-post_api_simulate_wellhub_checkin button:has-text("Try it out")');
  await shot(page, 'simulate_checkin_expanded');

  step = 'try it out (simulate checkin)';
  await page.click('#operations-Simulation-post_api_simulate_wellhub_checkin button:has-text("Try it out")');
  const simTextarea = page.locator('#operations-Simulation-post_api_simulate_wellhub_checkin textarea.body-param__text');
  await simTextarea.fill(JSON.stringify({ gympassId: GYMPASS_ID, gymExternalId: GYM_EXTERNAL_ID }, null, 2));
  await shot(page, 'simulate_checkin_filled');

  step = 'execute (simulate checkin)';
  await page.click('#operations-Simulation-post_api_simulate_wellhub_checkin button:has-text("Execute")');
  const simResponseCode = page.locator('#operations-Simulation-post_api_simulate_wellhub_checkin table.live-responses-table tbody td.response-col_status');
  await simResponseCode.waitFor({ timeout: 10000 });
  const statusText = (await simResponseCode.innerText()).trim();
  const simResponseBody = page.locator('#operations-Simulation-post_api_simulate_wellhub_checkin table.live-responses-table pre.microlight').first();
  const bodyText = await simResponseBody.innerText();
  console.log('HTTP status:', statusText);
  console.log('response body:', bodyText);
  await shot(page, 'simulate_checkin_executed');

  if (!statusText.startsWith('200')) throw new Error(`Esperava 200, veio ${statusText}`);
  const parsed = JSON.parse(bodyText);
  if (!parsed.id || !parsed.status) throw new Error('Resposta não tem o formato esperado de CheckinDto: ' + bodyText);

  step = 'done';
} catch (err) {
  console.error(`FALHOU no passo "${step}":`, err.message);
  await shot(page, `FAILURE_at_${step.replace(/[^a-z0-9]+/gi, '_')}`);
  await context.close();
  await browser.close();
  process.exit(1);
}

await context.close();
await browser.close();

const video = fs.readdirSync(VIDEO_DIR).find((f) => f.endsWith('.webm'));
if (video) {
  const finalPath = path.join(OUT_DIR, 'simulate-swagger.webm');
  fs.renameSync(path.join(VIDEO_DIR, video), finalPath);
  fs.rmdirSync(VIDEO_DIR);
  console.log('VIDEO:', finalPath);
}

if (consoleErrors.length > 0) {
  console.error('CONSOLE_ERRORS:', JSON.stringify(consoleErrors, null, 2));
  process.exit(1);
}

console.log('OK: simulação de check-in executada com sucesso pela UI do Swagger.');
