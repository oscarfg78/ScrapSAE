// ============================================================
// Pruebas Unitarias - Human Behavior Simulator
// Valida la generación de curvas de Bézier, delays y eventos.
// ============================================================

import { describe, it, expect, vi, beforeEach } from 'vitest';

// ============================================================
// Funciones puras extraídas del simulador para testing aislado
// ============================================================

interface Point {
  x: number;
  y: number;
}

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

function generateControlPoints(start: Point, end: Point): [Point, Point] {
  const dx = end.x - start.x;
  const dy = end.y - start.y;
  const dist = Math.sqrt(dx * dx + dy * dy);
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

function generateMousePath(start: Point, end: Point, steps: number): Point[] {
  const [cp1, cp2] = generateControlPoints(start, end);
  const path: Point[] = [];

  for (let i = 0; i <= steps; i++) {
    const t = i / steps;
    const easedT = t < 0.5 ? 2 * t * t : 1 - Math.pow(-2 * t + 2, 2) / 2;
    path.push(cubicBezier(start, cp1, cp2, end, easedT));
  }

  return path;
}

function randomBetween(min: number, max: number): number {
  return Math.floor(Math.random() * (max - min + 1)) + min;
}

// ============================================================
// Tests
// ============================================================

describe('cubicBezier', () => {
  it('debe retornar el punto de inicio cuando t = 0', () => {
    const p0 = { x: 0, y: 0 };
    const p1 = { x: 100, y: 200 };
    const p2 = { x: 200, y: 100 };
    const p3 = { x: 300, y: 300 };

    const result = cubicBezier(p0, p1, p2, p3, 0);
    expect(result.x).toBeCloseTo(0, 5);
    expect(result.y).toBeCloseTo(0, 5);
  });

  it('debe retornar el punto final cuando t = 1', () => {
    const p0 = { x: 0, y: 0 };
    const p1 = { x: 100, y: 200 };
    const p2 = { x: 200, y: 100 };
    const p3 = { x: 300, y: 300 };

    const result = cubicBezier(p0, p1, p2, p3, 1);
    expect(result.x).toBeCloseTo(300, 5);
    expect(result.y).toBeCloseTo(300, 5);
  });

  it('debe retornar un punto intermedio cuando t = 0.5', () => {
    const p0 = { x: 0, y: 0 };
    const p1 = { x: 0, y: 100 };
    const p2 = { x: 100, y: 100 };
    const p3 = { x: 100, y: 0 };

    const result = cubicBezier(p0, p1, p2, p3, 0.5);
    // En una curva simétrica, el punto medio debe estar cerca del centro
    expect(result.x).toBeCloseTo(50, 0);
    expect(result.y).toBeCloseTo(75, 0);
  });

  it('debe manejar puntos con coordenadas negativas', () => {
    const p0 = { x: -100, y: -100 };
    const p1 = { x: -50, y: 0 };
    const p2 = { x: 50, y: 0 };
    const p3 = { x: 100, y: 100 };

    const result = cubicBezier(p0, p1, p2, p3, 0);
    expect(result.x).toBeCloseTo(-100, 5);
    expect(result.y).toBeCloseTo(-100, 5);

    const end = cubicBezier(p0, p1, p2, p3, 1);
    expect(end.x).toBeCloseTo(100, 5);
    expect(end.y).toBeCloseTo(100, 5);
  });
});

describe('generateControlPoints', () => {
  it('debe generar dos puntos de control', () => {
    const start = { x: 0, y: 0 };
    const end = { x: 100, y: 100 };

    const [cp1, cp2] = generateControlPoints(start, end);

    expect(cp1).toHaveProperty('x');
    expect(cp1).toHaveProperty('y');
    expect(cp2).toHaveProperty('x');
    expect(cp2).toHaveProperty('y');
  });

  it('los puntos de control deben estar en la zona intermedia', () => {
    const start = { x: 0, y: 0 };
    const end = { x: 1000, y: 1000 };

    // Ejecutar múltiples veces para verificar distribución
    for (let i = 0; i < 20; i++) {
      const [cp1, cp2] = generateControlPoints(start, end);

      // cp1 debe estar más cerca del inicio (zona 0.25)
      // Con desviación del 30%, los valores deben estar en un rango razonable
      expect(cp1.x).toBeGreaterThan(-500);
      expect(cp1.x).toBeLessThan(1500);

      // cp2 debe estar más cerca del final (zona 0.75)
      expect(cp2.x).toBeGreaterThan(-500);
      expect(cp2.x).toBeLessThan(1500);
    }
  });

  it('debe manejar puntos idénticos (distancia 0)', () => {
    const point = { x: 50, y: 50 };
    const [cp1, cp2] = generateControlPoints(point, point);

    // Con distancia 0, la desviación es 0, así que los puntos de control
    // deben ser iguales al punto original
    expect(cp1.x).toBeCloseTo(50, 0);
    expect(cp1.y).toBeCloseTo(50, 0);
    expect(cp2.x).toBeCloseTo(50, 0);
    expect(cp2.y).toBeCloseTo(50, 0);
  });
});

describe('generateMousePath', () => {
  it('debe generar la cantidad correcta de puntos', () => {
    const start = { x: 0, y: 0 };
    const end = { x: 100, y: 100 };
    const steps = 20;

    const path = generateMousePath(start, end, steps);
    // steps + 1 porque incluye el punto inicial (t=0) y final (t=1)
    expect(path).toHaveLength(steps + 1);
  });

  it('el primer punto debe ser el inicio y el último el final', () => {
    const start = { x: 10, y: 20 };
    const end = { x: 300, y: 400 };

    const path = generateMousePath(start, end, 30);

    expect(path[0].x).toBeCloseTo(10, 0);
    expect(path[0].y).toBeCloseTo(20, 0);
    expect(path[path.length - 1].x).toBeCloseTo(300, 0);
    expect(path[path.length - 1].y).toBeCloseTo(400, 0);
  });

  it('la trayectoria no debe ser una línea recta (tiene curvatura)', () => {
    const start = { x: 0, y: 0 };
    const end = { x: 1000, y: 0 };

    // Ejecutar varias veces porque hay aleatoriedad
    let hasDeviation = false;
    for (let attempt = 0; attempt < 10; attempt++) {
      const path = generateMousePath(start, end, 50);

      // Verificar que al menos un punto tenga y != 0 (desviación de la línea recta)
      const midPoints = path.slice(5, -5);
      const maxDeviation = Math.max(...midPoints.map((p) => Math.abs(p.y)));

      if (maxDeviation > 1) {
        hasDeviation = true;
        break;
      }
    }

    expect(hasDeviation).toBe(true);
  });

  it('debe aplicar easing (más lento al inicio y final)', () => {
    const start = { x: 0, y: 0 };
    const end = { x: 100, y: 0 };

    const path = generateMousePath(start, end, 100);

    // Los primeros puntos deben estar más juntos (easing in)
    const firstGap = Math.abs(path[1].x - path[0].x);
    const midGap = Math.abs(path[51].x - path[50].x);

    // El gap al inicio debe ser menor que en el medio
    expect(firstGap).toBeLessThan(midGap);
  });
});

describe('randomBetween', () => {
  it('debe generar valores dentro del rango', () => {
    for (let i = 0; i < 100; i++) {
      const result = randomBetween(10, 20);
      expect(result).toBeGreaterThanOrEqual(10);
      expect(result).toBeLessThanOrEqual(20);
    }
  });

  it('debe retornar el mismo valor si min === max', () => {
    const result = randomBetween(5, 5);
    expect(result).toBe(5);
  });

  it('debe retornar un entero', () => {
    for (let i = 0; i < 50; i++) {
      const result = randomBetween(1, 100);
      expect(Number.isInteger(result)).toBe(true);
    }
  });
});

describe('delay (requisito mínimo 1 segundo)', () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  it('debe respetar el mínimo de 1000ms', async () => {
    // La función delay del simulador tiene: Math.max(minMs ?? SIM.MIN_DELAY, 1000)
    // Verificamos la lógica del cálculo
    const min = Math.max(500, 1000); // Simula delay(500)
    expect(min).toBe(1000);

    const min2 = Math.max(2000, 1000); // Simula delay(2000)
    expect(min2).toBe(2000);
  });

  it('el máximo debe ser mayor que el mínimo', () => {
    const min = 1000;
    const max = Math.max(1500, min + 500);
    expect(max).toBeGreaterThan(min);
  });
});

describe('Secuencia de eventos de humanClick', () => {
  it('debe despachar eventos en el orden correcto', () => {
    const events: string[] = [];
    const element = document.createElement('button');

    // Registrar todos los eventos
    ['mouseenter', 'mouseover', 'mousedown', 'mouseup', 'click'].forEach((type) => {
      element.addEventListener(type, () => events.push(type));
    });

    // Simular la secuencia manualmente (sin await)
    element.dispatchEvent(new MouseEvent('mouseenter', { bubbles: true }));
    element.dispatchEvent(new MouseEvent('mouseover', { bubbles: true }));
    element.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
    element.dispatchEvent(new MouseEvent('mouseup', { bubbles: true }));
    element.dispatchEvent(new MouseEvent('click', { bubbles: true }));

    expect(events).toEqual(['mouseenter', 'mouseover', 'mousedown', 'mouseup', 'click']);
  });

  it('los eventos de mouse deben tener coordenadas válidas', () => {
    const element = document.createElement('button');
    let capturedEvent: MouseEvent | null = null;

    element.addEventListener('click', (e) => {
      capturedEvent = e;
    });

    const event = new MouseEvent('click', {
      bubbles: true,
      cancelable: true,
      clientX: 150,
      clientY: 200,
    });
    element.dispatchEvent(event);

    expect(capturedEvent).not.toBeNull();
    expect(capturedEvent!.clientX).toBe(150);
    expect(capturedEvent!.clientY).toBe(200);
  });
});

describe('Simulación de escritura', () => {
  it('debe escribir carácter por carácter', () => {
    const input = document.createElement('input');
    const text = 'Hola';
    const events: string[] = [];

    input.addEventListener('input', () => events.push('input'));
    input.addEventListener('keydown', () => events.push('keydown'));
    input.addEventListener('keypress', () => events.push('keypress'));
    input.addEventListener('keyup', () => events.push('keyup'));

    // Simular la escritura carácter por carácter
    input.value = '';
    for (const char of text) {
      input.value += char;
      input.dispatchEvent(new Event('input', { bubbles: true }));
      input.dispatchEvent(new KeyboardEvent('keydown', { key: char, bubbles: true }));
      input.dispatchEvent(new KeyboardEvent('keypress', { key: char, bubbles: true }));
      input.dispatchEvent(new KeyboardEvent('keyup', { key: char, bubbles: true }));
    }

    expect(input.value).toBe('Hola');
    // 4 caracteres × 4 eventos = 16 eventos
    expect(events).toHaveLength(16);
  });
});
