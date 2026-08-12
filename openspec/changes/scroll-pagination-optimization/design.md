## Context

Muchos sitios e-commerce (como Autonics o tiendas basadas en catálogos dinámicos) utilizan paginación por desplazamiento (infinite scroll o lazy loading). En ScrapSAE, para extraer la totalidad de los productos de un catálogo sin perder registros, se requiere un mecanismo que navegue progresivamente hasta el último producto visible y/o hasta el footer de la página, espere a que el motor AJAX/JavaScript renderice nuevos elementos en el DOM, y repita esta operación en bucle de forma efectiva.

## Goals / Non-Goals

**Goals:**
- Implementar un algoritmo de scroll interactivo (`ScrollToLastProductAndHydrateAsync`) en `PlaywrightScrapingService` / `DirectExtractionStrategy`.
- Hacer scroll específico hacia la última tarjeta de producto (`card.ScrollIntoViewIfNeededAsync()`) y posteriormente hacia el footer.
- Monitorear el conteo incremental de productos en el DOM tras cada scroll.
- Repetir la hidratación e inspección iterativamente hasta que no aparezcan productos nuevos tras varios intentos de espera o hasta alcanzar el límite máximo configurado (`MaxProductsPerScrape` / `MaxProductsOverride`).
- Emitir actualizaciones de log/progreso en consola en tiempo real al descubrir nuevos lotes de productos por scroll.

**Non-Goals:**
- Alterar la estrategia de paginación numérica tradicional (`?page=N` o botón "Siguiente"), la cual se mantendrá como alternativa independiente en `ListStrategy`.

## Decisions

### 1. Scroll Guiado por Elemento Objetivo y Footer
- **Decisión**: Para asegurar que los listeners de `IntersectionObserver` y eventos de scroll del navegador se activen correctamente, la función de scroll:
  1. Identificará la última tarjeta de producto en el DOM actual mediante los selectores configurados.
  2. Ejecutará `ScrollIntoViewIfNeededAsync()` sobre dicho elemento.
  3. Ejecutará un desplazamiento suave adicional hacia el fondo de la página o `footer`.
- **Razón**: Los scrolls estáticos al final del documento (`document.body.scrollHeight`) a menudo fallan en sitios que monitorean el scroll relativo a los contenedores de listas de productos.

### 2. Ciclo de Hidratación y Conteo Incremental
- **Decisión**: 
  - Registrar el conteo previo de tarjetas: `int previousCount = currentCards.Count`.
  - Aplicar scroll + retardo de hidratación dinámico (800ms - 1500ms).
  - Evaluar `int newCount = updatedCards.Count`.
  - Si `newCount > previousCount`, extraer inmediatamente los productos adicionales y continuar el ciclo.
  - Si `newCount == previousCount`, realizar hasta 2 reintentos de espera/scroll ligero antes de asumir que el catálogo ha llegado al final.

### 3. Evaluación Continua del Límite de Productos
- **Decisión**: Evaluar la condición `savedCount >= maxProducts` en cada iteración del bucle de scroll para detener inmediatamente la navegación en cuanto se satisfaga la cuota configurada.

## Risks / Trade-offs

- **[Risk] Sitios con carga infinita sin fin (infinite stream)**: Algunos sitios generan productos aleatorios o infinitos sin llegar nunca a un footer final.
  - *Mitigación*: Imponer un límite máximo de reintentos sin cambio (2 intentos) y respetar estrictamente el límite `MaxProductsPerScrape`.
- **[Risk] Retardos por red lenta**: El sitio puede tardar más de 1 segundo en responder a la solicitud AJAX de nuevos productos.
  - *Mitigación*: Incluir espera explícita `WaitForLoadStateAsync(LoadState.NetworkIdle)` o verificación de spinners/cargadores visuales si están presentes.

## Migration Plan

1. Actualizar `PlaywrightScrapingService.cs` incorporando `ScrollToLastProductAndHydrateAsync`.
2. Integrar el bucle de scroll e hidratación iterativo en `DirectExtractionStrategy.cs` y en la recolección de catálogos en `PlaywrightScrapingService`.
3. Verificar la extracción en tiempo real con sitios de prueba que usen scroll infinito.
