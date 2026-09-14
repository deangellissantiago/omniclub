// Teste de ponta a ponta (smoke) do frontend do OmniClub, dirigido via Playwright.
//
// Cobre o fluxo de admin logado: login -> dashboard -> CRUD de aluno -> pontos de check-in ->
// check-ins (confere os status Approved/Rejected vindos da integração Wellhub) -> relatórios
// (3 abas) -> logout. Grava um vídeo (.webm) da sessão inteira, screenshots de cada passo, e
// falha (exit code 1) se algum passo travar, se o console do navegador logar um erro, ou se
// alguma resposta HTTP vier 5xx.
//
// Uso:
//   npm install
//   BASE_URL=http://localhost:5271 ADMIN_EMAIL=admin@escoladetenis.com ADMIN_PASSWORD='Trocar@123' \
//     npm test
//
// Requer um Chrome/Chromium instalado localmente (aponte CHROME_PATH se não for
// /usr/bin/google-chrome) — não baixa um browser via Playwright para manter a instalação leve
// (só playwright-core).

import { chromium } from 'playwright-core';
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));

const BASE_URL = process.env.BASE_URL || 'http://localhost:5271';
const ADMIN_EMAIL = process.env.ADMIN_EMAIL || 'admin@escoladetenis.com';
const ADMIN_PASSWORD = process.env.ADMIN_PASSWORD || 'Trocar@123';
const CHROME_PATH = process.env.CHROME_PATH || '/usr/bin/google-chrome';
const OUT_DIR = process.env.OUT_DIR || path.join(__dirname, 'last-run');
const VIDEO_DIR = path.join(OUT_DIR, 'video');
const SHOT_DIR = path.join(OUT_DIR, 'screenshots');

fs.rmSync(OUT_DIR, { recursive: true, force: true });
fs.mkdirSync(VIDEO_DIR, { recursive: true });
fs.mkdirSync(SHOT_DIR, { recursive: true });

const consoleErrors = [];
let shotN = 0;
async function shot(page, name) {
  shotN += 1;
  const file = path.join(SHOT_DIR, `${String(shotN).padStart(2, '0')}_${name}.png`);
  await page.screenshot({ path: file, fullPage: true });
  console.log('shot:', file);
}

const browser = await chromium.launch({
  executablePath: CHROME_PATH,
  headless: true,
  args: ['--no-sandbox', '--disable-dev-shm-usage'],
});

const context = await browser.newContext({
  viewport: { width: 1360, height: 860 },
  recordVideo: { dir: VIDEO_DIR, size: { width: 1360, height: 860 } },
});

const page = await context.newPage();
page.on('console', (msg) => {
  if (msg.type() === 'error') consoleErrors.push(`[console] ${page.url()} :: ${msg.text()}`);
});
page.on('pageerror', (err) => consoleErrors.push(`[pageerror] ${page.url()} :: ${err.message}`));
page.on('requestfailed', (req) => consoleErrors.push(`[requestfailed] ${req.url()} :: ${req.failure()?.errorText}`));
page.on('response', (res) => {
  if (res.status() >= 500) consoleErrors.push(`[http5xx] ${res.request().method()} ${res.url()} -> ${res.status()}`);
});

let step = 'start';
try {
  // ---- Login ---------------------------------------------------------------------------
  step = 'login: nav';
  await page.goto(`${BASE_URL}/login`, { waitUntil: 'networkidle' });
  await shot(page, 'login_page');

  step = 'login: fill + submit';
  await page.fill('input[type=email]', ADMIN_EMAIL);
  await page.fill('input[type=password]', ADMIN_PASSWORD);
  await Promise.all([
    page.waitForURL(`${BASE_URL}/`, { timeout: 15000 }),
    page.click('button[type=submit]'),
  ]);
  await page.waitForSelector('text=Total de check-ins', { timeout: 15000 });
  await shot(page, 'dashboard');

  // ---- Alunos: CRUD round-trip ----------------------------------------------------------
  step = 'alunos: nav';
  await page.click('a:has-text("Alunos")');
  await page.waitForSelector('h1:has-text("Alunos")');
  await shot(page, 'alunos_list_before');

  const studentName = `Teste Playwright ${Date.now()}`;
  step = 'alunos: create';
  await page.fill('label:has-text("Nome *") input', studentName);
  await page.fill('label:has-text("E-mail") input', 'teste.playwright@example.com');
  await page.fill('label:has-text("Wellhub ID") input', `pw-${Date.now()}`);
  await page.click('button:has-text("Cadastrar aluno")');
  await page.waitForSelector(`td:has-text("${studentName}")`, { timeout: 10000 });
  await shot(page, 'alunos_created');

  step = 'alunos: edit';
  const row = page.locator('tr', { has: page.locator(`text=${studentName}`) });
  await row.locator('button:has-text("Editar")').click();
  await page.fill('label:has-text("Telefone") input', '31999990000');
  await page.click('button:has-text("Salvar alterações")');
  await page.waitForSelector('button:has-text("Cadastrar aluno")', { timeout: 10000 }); // form voltou ao modo "novo"
  await shot(page, 'alunos_edited');

  step = 'alunos: delete';
  page.once('dialog', (d) => d.accept());
  await row.locator('button:has-text("Remover")').click();
  await page.waitForSelector(`td:has-text("${studentName}")`, { state: 'detached', timeout: 10000 });
  await shot(page, 'alunos_deleted');

  // ---- Pontos de check-in ----------------------------------------------------------------
  step = 'pontos: nav';
  await page.click('a:has-text("Pontos de check-in")');
  await page.waitForSelector('h1:has-text("Pontos de check-in")');
  await page.waitForSelector('.data-table tbody tr', { timeout: 10000 });
  await shot(page, 'pontos_checkin_list');

  // ---- Check-ins: confere os registros recebidos via webhook do Wellhub ------------------
  step = 'checkins: nav';
  await page.click('a:has-text("Check-ins")');
  await page.waitForSelector('h1:has-text("Check-ins")');
  await shot(page, 'checkins_list');

  // ---- Dashboard de novo: confere números batendo -----------------------------------------
  step = 'dashboard: revisit';
  await page.click('a:has-text("Dashboard")');
  await page.waitForSelector('text=Total de check-ins');
  await shot(page, 'dashboard_revisit');

  // ---- Relatórios: as 3 abas --------------------------------------------------------------
  step = 'relatorios: nav';
  await page.click('a:has-text("Relatórios")');
  await page.waitForSelector('h1:has-text("Relatórios")');
  await page.waitForSelector('h2:has-text("Top alunos")', { timeout: 10000 }); // espera o fetch assíncrono resolver
  await shot(page, 'relatorios_geral');

  step = 'relatorios: aba aluno';
  await page.click('button.tab:has-text("Por aluno")');
  await page.waitForSelector('h2:has-text("Check-ins por aluno")');
  await shot(page, 'relatorios_por_aluno');

  step = 'relatorios: aba escola';
  await page.click('button.tab:has-text("Por escola")');
  await page.waitForSelector('h2:has-text("Check-ins por escola/unidade")');
  await shot(page, 'relatorios_por_escola');

  // ---- Logout -------------------------------------------------------------------------------
  step = 'logout';
  await page.click('button:has-text("Sair")');
  await page.waitForURL(`${BASE_URL}/login`, { timeout: 10000 });
  await shot(page, 'logged_out');

  step = 'done';
} catch (err) {
  console.error(`FALHOU no passo "${step}":`, err.message);
  await shot(page, `FAILURE_at_${step.replace(/[^a-z0-9]+/gi, '_')}`);
  await context.close();
  await browser.close();
  console.log('CONSOLE_ERRORS:', JSON.stringify(consoleErrors, null, 2));
  process.exit(1);
}

await context.close(); // só aqui o vídeo é finalizado em disco
await browser.close();

const video = fs.readdirSync(VIDEO_DIR).find((f) => f.endsWith('.webm'));
if (video) {
  const finalPath = path.join(OUT_DIR, 'smoke.webm');
  fs.renameSync(path.join(VIDEO_DIR, video), finalPath);
  fs.rmdirSync(VIDEO_DIR);
  console.log('VIDEO:', finalPath);
}

if (consoleErrors.length > 0) {
  console.error('Passos OK, mas houve erros de console/rede durante o teste:');
  console.error(JSON.stringify(consoleErrors, null, 2));
  process.exit(1);
}

console.log('OK: todos os passos concluídos sem erros de console/rede.');
