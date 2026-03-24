// ============================================================
// ScrapSAE Extension - Human Behavior Simulator
// Simula movimientos de ratón, scroll y pausas aleatorias
// para evitar la detección de bots.
// ============================================================

import { CONFIG } from '../shared/config';

const SIM = CONFIG.HUMAN_SIM;

// ============================================================
// Utilidades de Aleatoriedad
// ============================================================

/**
 * Genera un número aleatorio entre min y max (inclusive).
 */
function randomBetween(min: number, max: number): number {
  return Math.floor(Math.random() * (max - min + 1)) + min;
}

/**
 * Pausa aleatoria con un mínimo de 1 segundo.
 * Cumple con el requisito de pausas >= 1s entre interacciones.
 */
export function delay(minMs?: number, maxMs?: number): Promise<void> {
  const min = Math.max(minMs ?? SIM.MIN_DELAY, 1000);
  const max = Math.max(maxMs ?? SIM.MAX_DELAY, min + 500);
  const ms = randomBetween(min, max);
  return new Promise((resolve) => setTimeout(resolve, ms));
}

/**
 * Pausa corta para micro-interacciones (no cuenta como interacción principal).
 */
function microDelay(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, randomBetween(50, 200)));
}

// ============================================================
// Curvas de Bézier para Movimiento de Ratón
// ============================================================

interface Point {
  x: number;
  y: number;
}

/**
 * Calcula un punto en una curva de Bézier cúbica para t en [0, 1].
 */
function cubicBezier(p0: Point, p1: Point, p2: Point, p3: Point, t: number): Point {
  const u = 1 - t;
  const tt = t * t;
  const uu = u * u;
  const uuu = uu * u;
  const ttt = tt * t;

  return {
    x: uuu * p0.x + 3 * uu * t * p1.x + 3 * u * tt * p2.x + ttt * p3.x,
    y: uuu * p0.y + 3 * uu * t * p1.y + 3 * u * tt * p2.y + ttt * p3.y,
  };
}

/**
 * Genera puntos de control aleatorios para una curva de Bézier
 * que conecta el punto de inicio con el punto final.
 */
function generateControlPoints(start: Point, end: Point): [Point, Point] {
  const dx = end.x - start.x;
  const dy = end.y - start.y;
  const dist = Math.sqrt(dx * dx + dy * dy);

  // Desviación lateral proporcional a la distancia
  const deviation = dist * 0.3;

  const cp1: Point = {
    x: start.x + dx * 0.25 + (Math.random() - 0.5) * deviation,
    y: start.y + dy * 0.25 + (Math.random() - 0.5) * deviation,
  };

  const cp2: Point = {
    x: start.x + dx * 0.75 + (Math.random() - 0.5) * deviation,
    y: start.y + dy * 0.75 + (Math.random() - 0.5) * deviation,
  };

  return [cp1, cp2];
}

/**
 * Genera la trayectoria completa del ratón usando una curva de Bézier.
 */
function generateMousePath(start: Point, end: Point, steps: number): Point[] {
  const [cp1, cp2] = generateControlPoints(start, end);
  const path: Point[] = [];

  for (let i = 0; i <= steps; i++) {
    const t = i / steps;
    // Aplicar easing para velocidad variable (más lento al inicio y final)
    const easedT = t < 0.5 ? 2 * t * t : 1 - Math.pow(-2 * t + 2, 2) / 2;
    path.push(cubicBezier(start, cp1, cp2, end, easedT));
  }

  return path;
}

// ============================================================
// Simulación de Eventos del Ratón
// ============================================================

let currentMousePos: Point = { x: 0, y: 0 };

/**
 * Despacha un evento de ratón en las coordenadas dadas.
 */
function dispatchMouseEvent(type: string, x: number, y: number, target?: Element): void {
  const element = target ?? document.elementFromPoint(x, y) ?? document.body;
  const event = new MouseEvent(type, {
    bubbles: true,
    cancelable: true,
    view: window,
    clientX: x,
    clientY: y,
    screenX: x + window.screenX,
    screenY: y + window.screenY,
  });
  element.dispatchEvent(event);
}

/**
 * Mueve el ratón desde la posición actual hasta el elemento objetivo
 * siguiendo una curva de Bézier natural.
 */
export async function moveMouseTo(element: Element): Promise<void> {
  const rect = element.getBoundingClientRect();
  // Apuntar a un punto aleatorio dentro del elemento (no siempre al centro)
  const targetX = rect.left + rect.width * (0.3 + Math.random() * 0.4);
  const targetY = rect.top + rect.height * (0.3 + Math.random() * 0.4);

  const target: Point = { x: targetX, y: targetY };
  const steps = SIM.MOUSE_MOVE_STEPS + randomBetween(-5, 5);
  const path = generateMousePath(currentMousePos, target, steps);

  const stepDelay = SIM.MOUSE_MOVE_DURATION / steps;

  for (const point of path) {
    dispatchMouseEvent('mousemove', point.x, point.y);
    await new Promise((resolve) => setTimeout(resolve, stepDelay + randomBetween(-2, 5)));
  }

  currentMousePos = target;
}

/**
 * Simula un clic humano: mueve el ratón, hover, mousedown, mouseup, click.
 */
export async function humanClick(element: Element): Promise<void> {
  await moveMouseTo(element);
  await microDelay();

  const rect = element.getBoundingClientRect();
  const x = currentMousePos.x;
  const y = currentMousePos.y;

  dispatchMouseEvent('mouseenter', x, y, element);
  dispatchMouseEvent('mouseover', x, y, element);
  await microDelay();

  dispatchMouseEvent('mousedown', x, y, element);
  await new Promise((resolve) => setTimeout(resolve, randomBetween(50, 150)));
  dispatchMouseEvent('mouseup', x, y, element);
  dispatchMouseEvent('click', x, y, element);

  // Pausa post-clic (mínimo 1 segundo)
  await delay();
}

// ============================================================
// Simulación de Scroll
// ============================================================

/**
 * Realiza scroll progresivo hacia abajo, simulando lectura humana.
 * Avanza en incrementos variables con pausas intermedias.
 */
export async function humanScrollDown(pixels?: number): Promise<void> {
  const totalScroll = pixels ?? window.innerHeight * 0.7;
  let scrolled = 0;

  while (scrolled < totalScroll) {
    const step = randomBetween(SIM.SCROLL_STEP_MIN, SIM.SCROLL_STEP_MAX);
    const actualStep = Math.min(step, totalScroll - scrolled);

    window.scrollBy({ top: actualStep, behavior: 'auto' });
    scrolled += actualStep;

    // Pausa entre pasos de scroll
    await new Promise((resolve) =>
      setTimeout(resolve, randomBetween(SIM.SCROLL_PAUSE_MIN, SIM.SCROLL_PAUSE_MAX))
    );
  }
}

/**
 * Hace scroll hasta que un elemento sea visible en el viewport.
 */
export async function scrollToElement(element: Element): Promise<void> {
  const rect = element.getBoundingClientRect();

  if (rect.top >= 0 && rect.bottom <= window.innerHeight) {
    return; // Ya es visible
  }

  // Scroll progresivo hasta que el elemento esté visible
  const targetY = window.scrollY + rect.top - window.innerHeight * 0.3;
  const currentY = window.scrollY;
  const distance = targetY - currentY;
  const steps = Math.max(5, Math.abs(Math.floor(distance / 200)));

  for (let i = 1; i <= steps; i++) {
    const progress = i / steps;
    const eased = progress < 0.5 ? 2 * progress * progress : 1 - Math.pow(-2 * progress + 2, 2) / 2;
    window.scrollTo({ top: currentY + distance * eased, behavior: 'auto' });
    await new Promise((resolve) =>
      setTimeout(resolve, randomBetween(SIM.SCROLL_PAUSE_MIN, SIM.SCROLL_PAUSE_MAX))
    );
  }

  // Pausa después de llegar al destino
  await delay(800, 1500);
}

/**
 * Scroll infinito: hace scroll hasta el final de la página
 * esperando que se cargue nuevo contenido.
 */
export async function infiniteScrollLoad(maxScrolls = 10): Promise<number> {
  let previousHeight = document.body.scrollHeight;
  let scrollCount = 0;

  while (scrollCount < maxScrolls) {
    await humanScrollDown(window.innerHeight * 0.8);
    await delay(1500, 3000); // Esperar carga de contenido

    const newHeight = document.body.scrollHeight;
    if (newHeight === previousHeight) {
      // No se cargó nuevo contenido, intentar una vez más
      await delay(2000, 4000);
      if (document.body.scrollHeight === previousHeight) {
        break; // Definitivamente no hay más contenido
      }
    }

    previousHeight = document.body.scrollHeight;
    scrollCount++;
  }

  return scrollCount;
}

// ============================================================
// Simulación de Escritura
// ============================================================

/**
 * Escribe texto en un input simulando la velocidad de escritura humana.
 */
export async function humanType(element: HTMLInputElement | HTMLTextAreaElement, text: string): Promise<void> {
  element.focus();
  element.value = '';
  element.dispatchEvent(new Event('focus', { bubbles: true }));

  for (const char of text) {
    element.value += char;
    element.dispatchEvent(new Event('input', { bubbles: true }));
    element.dispatchEvent(new KeyboardEvent('keydown', { key: char, bubbles: true }));
    element.dispatchEvent(new KeyboardEvent('keypress', { key: char, bubbles: true }));
    element.dispatchEvent(new KeyboardEvent('keyup', { key: char, bubbles: true }));

    // Velocidad de escritura variable (40-120ms por carácter)
    await new Promise((resolve) => setTimeout(resolve, randomBetween(40, 120)));
  }

  element.dispatchEvent(new Event('change', { bubbles: true }));
  await delay(500, 1000);
}
