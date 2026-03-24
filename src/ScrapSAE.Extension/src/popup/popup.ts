// ============================================================
// ScrapSAE Extension - Popup Controller
// ============================================================

import type {
  ExtensionMessage,
  ScrapingProgressPayload,
  StartScrapingPayload,
  UserLayout,
  UserProfile,
} from '../shared/types';
import {
  signInWithEmail,
  signUpWithEmail,
  signInWithGoogle,
  signOut,
  getSession,
  getUserProfile,
  getLayouts,
} from '../shared/supabase_client';
import { CONFIG } from '../shared/config';

// ============================================================
// DOM References
// ============================================================

const $ = (id: string) => document.getElementById(id)!;

const authScreen = $('auth-screen');
const mainScreen = $('main-screen');
const loginForm = $('login-form');
const registerForm = $('register-form');
const authError = $('auth-error');

const userEmail = $('user-email');
const userPlan = $('user-plan');
const layoutSelect = $('layout-select') as HTMLSelectElement;
const toggleHumanSim = $('toggle-human-sim');
const maxPagesInput = $('max-pages') as HTMLInputElement;

const actionIdle = $('action-idle');
const actionRunning = $('action-running');
const actionComplete = $('action-complete');
const progressFill = $('progress-fill');
const progressText = $('progress-text');
const progressCount = $('progress-count');
const completeSummary = $('complete-summary');

const btnStart = $('btn-start') as HTMLButtonElement;
const btnStop = $('btn-stop') as HTMLButtonElement;

// ============================================================
// State
// ============================================================

let currentLayouts: UserLayout[] = [];
let currentProfile: UserProfile | null = null;

// ============================================================
// Auth Logic
// ============================================================

async function checkAuth(): Promise<void> {
  const session = await getSession();
  if (session) {
    currentProfile = await getUserProfile();
    showMainScreen();
  } else {
    showAuthScreen();
  }
}

function showAuthScreen(): void {
  authScreen.classList.remove('hidden');
  mainScreen.classList.add('hidden');
}

async function showMainScreen(): Promise<void> {
  authScreen.classList.add('hidden');
  mainScreen.classList.remove('hidden');

  // Mostrar info del usuario
  userEmail.textContent = currentProfile?.email ?? '';
  const plan = currentProfile?.planType ?? 'free';
  userPlan.textContent = plan.charAt(0).toUpperCase() + plan.slice(1);
  userPlan.className = `badge badge-${plan}`;

  // Cargar layouts
  await loadLayouts();
}

async function handleLogin(): Promise<void> {
  const email = ($('login-email') as HTMLInputElement).value.trim();
  const password = ($('login-password') as HTMLInputElement).value;

  if (!email || !password) {
    showAuthError('Ingresa tu correo y contraseña.');
    return;
  }

  const { user, error } = await signInWithEmail(email, password);
  if (error) {
    showAuthError(error);
    return;
  }

  currentProfile = await getUserProfile();
  showMainScreen();
}

async function handleRegister(): Promise<void> {
  const email = ($('register-email') as HTMLInputElement).value.trim();
  const password = ($('register-password') as HTMLInputElement).value;

  if (!email || !password) {
    showAuthError('Ingresa tu correo y contraseña.');
    return;
  }

  if (password.length < 6) {
    showAuthError('La contraseña debe tener al menos 6 caracteres.');
    return;
  }

  const { user, error } = await signUpWithEmail(email, password);
  if (error) {
    showAuthError(error);
    return;
  }

  showAuthError(''); // Clear
  authError.textContent = 'Cuenta creada. Revisa tu correo para confirmar.';
  authError.style.color = 'var(--success)';
  authError.classList.remove('hidden');
}

async function handleLogout(): Promise<void> {
  await signOut();
  currentProfile = null;
  currentLayouts = [];
  showAuthScreen();
}

function showAuthError(msg: string): void {
  if (!msg) {
    authError.classList.add('hidden');
    return;
  }
  authError.textContent = msg;
  authError.style.color = 'var(--danger)';
  authError.classList.remove('hidden');
}

// ============================================================
// Layouts
// ============================================================

async function loadLayouts(): Promise<void> {
  currentLayouts = await getLayouts();

  // Limpiar y poblar el select
  layoutSelect.innerHTML = '<option value="">-- Seleccionar layout --</option>';

  currentLayouts.forEach((layout) => {
    const option = document.createElement('option');
    option.value = layout.id;
    option.textContent = layout.name + (layout.isDefault ? ' (Default)' : '');
    layoutSelect.appendChild(option);
  });

  // Restaurar último layout usado
  const stored = await chrome.storage.local.get(CONFIG.STORAGE_KEYS.LAST_LAYOUT);
  if (stored[CONFIG.STORAGE_KEYS.LAST_LAYOUT]) {
    layoutSelect.value = stored[CONFIG.STORAGE_KEYS.LAST_LAYOUT];
  }

  updateStartButton();
}

function getSelectedLayout(): UserLayout | null {
  const id = layoutSelect.value;
  return currentLayouts.find((l) => l.id === id) ?? null;
}

function updateStartButton(): void {
  btnStart.disabled = !layoutSelect.value;
}

// ============================================================
// Scraping Control
// ============================================================

async function handleStartScraping(): Promise<void> {
  const layout = getSelectedLayout();
  if (!layout) return;

  const useHumanSim = toggleHumanSim.getAttribute('data-active') === 'true';
  const maxPages = parseInt(maxPagesInput.value) || 5;

  // Guardar último layout usado
  await chrome.storage.local.set({
    [CONFIG.STORAGE_KEYS.LAST_LAYOUT]: layout.id,
  });

  const payload: StartScrapingPayload = {
    layoutId: layout.id,
    selectors: layout.selectors,
    columnMapping: layout.columnMapping,
    maxPages,
    useHumanSimulation: useHumanSim,
  };

  // Cambiar a vista de progreso
  actionIdle.classList.add('hidden');
  actionComplete.classList.add('hidden');
  actionRunning.classList.remove('hidden');
  progressFill.style.width = '0%';
  progressText.textContent = 'Iniciando extracción...';
  progressCount.textContent = '0 productos encontrados';

  // Enviar al Service Worker
  chrome.runtime.sendMessage({
    action: 'START_SCRAPING',
    payload,
  } as ExtensionMessage, (response) => {
    if (!response?.success) {
      showError(response?.error ?? 'Error al iniciar el scraping.');
    }
  });
}

function handleStopScraping(): void {
  chrome.runtime.sendMessage({
    action: 'STOP_SCRAPING',
  } as ExtensionMessage);

  resetToIdle();
}

function resetToIdle(): void {
  actionRunning.classList.add('hidden');
  actionComplete.classList.add('hidden');
  actionIdle.classList.remove('hidden');
}

function showError(msg: string): void {
  actionRunning.classList.add('hidden');
  actionComplete.classList.remove('hidden');
  completeSummary.textContent = msg;
  completeSummary.parentElement!.style.borderColor = 'var(--danger)';
  completeSummary.parentElement!.style.background = 'var(--danger-light)';
  (completeSummary.previousElementSibling as HTMLElement).style.color = 'var(--danger)';
  (completeSummary.previousElementSibling as HTMLElement).textContent = 'Error';
}

// ============================================================
// Message Listener (from Service Worker)
// ============================================================

chrome.runtime.onMessage.addListener((message: ExtensionMessage) => {
  switch (message.action) {
    case 'SCRAPING_PROGRESS': {
      const progress = message.payload as ScrapingProgressPayload;
      const pct = progress.totalPages > 0
        ? Math.round((progress.currentPage / progress.totalPages) * 100)
        : 0;
      progressFill.style.width = `${pct}%`;
      progressText.textContent = progress.status;
      progressCount.textContent = `${progress.productsFound} productos encontrados`;
      break;
    }

    case 'SCRAPING_COMPLETE': {
      const result = message.payload as {
        productsFound: number;
        productsExported: number;
        fileName: string;
      };
      actionRunning.classList.add('hidden');
      actionComplete.classList.remove('hidden');
      completeSummary.textContent =
        `${result.productsExported} productos exportados a ${result.fileName}`;
      completeSummary.parentElement!.style.borderColor = 'var(--success)';
      completeSummary.parentElement!.style.background = 'var(--success-light)';
      (completeSummary.previousElementSibling as HTMLElement).style.color = 'var(--success)';
      (completeSummary.previousElementSibling as HTMLElement).textContent = 'Extracción completada';
      break;
    }

    case 'SCRAPING_ERROR': {
      const { error } = message.payload as { error: string };
      showError(error);
      break;
    }
  }
});

// ============================================================
// Event Listeners
// ============================================================

$('btn-login').addEventListener('click', handleLogin);
$('btn-register').addEventListener('click', handleRegister);
$('btn-google').addEventListener('click', () => signInWithGoogle());
$('btn-logout').addEventListener('click', handleLogout);

$('btn-show-register').addEventListener('click', (e) => {
  e.preventDefault();
  loginForm.classList.add('hidden');
  registerForm.classList.remove('hidden');
  authError.classList.add('hidden');
});

$('btn-show-login').addEventListener('click', (e) => {
  e.preventDefault();
  registerForm.classList.add('hidden');
  loginForm.classList.remove('hidden');
  authError.classList.add('hidden');
});

layoutSelect.addEventListener('change', updateStartButton);

toggleHumanSim.addEventListener('click', () => {
  const isActive = toggleHumanSim.getAttribute('data-active') === 'true';
  toggleHumanSim.setAttribute('data-active', String(!isActive));
  toggleHumanSim.classList.toggle('active');
});

btnStart.addEventListener('click', handleStartScraping);
btnStop.addEventListener('click', handleStopScraping);
$('btn-new-scraping').addEventListener('click', resetToIdle);

$('btn-open-sidepanel').addEventListener('click', () => {
  chrome.runtime.sendMessage({ action: 'OPEN_SIDEPANEL' } as ExtensionMessage);
});

$('btn-upgrade').addEventListener('click', (e) => {
  e.preventDefault();
  chrome.tabs.create({ url: `${CONFIG.WEB_URL}/pricing` });
});

// ============================================================
// Init
// ============================================================

checkAuth();
