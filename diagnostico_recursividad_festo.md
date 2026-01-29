
# Diagnóstico y Estrategia de Solución para Scraping Recursivo de Festo

**Fecha:** 29 de enero de 2026
**Autor:** Manus AI

## 1. Resumen del Problema

Se ha reportado que la extracción de productos de Festo únicamente tiene éxito cuando se utilizan URLs de productos directas. La extracción recursiva, que debería navegar por las categorías del sitio, no está encontrando ni procesando ningún producto. El análisis de los logs de prueba confirma este comportamiento: el scraper inicia en una página de categoría, pero no logra identificar enlaces a subcategorías o familias de productos, finalizando la ejecución con cero resultados.

## 2. Diagnóstico de la Causa Raíz

Tras una revisión detallada del flujo de ejecución en `PlaywrightScrapingService.cs` y los archivos de configuración y pruebas, se ha identificado una **bifurcación de lógica y una dependencia no cumplida** como la causa principal del fallo.

El servicio de scraping contiene dos implementaciones distintas para manejar la estructura de Festo:

1.  **El "Gancho Mejorado" (`NavigateAndCollectFromSubcategoriesAsync`):** Una lógica de scraping recursivo muy avanzada y robusta, diseñada específicamente para la navegación compleja de Festo. Esta es la implementación superior.
2.  **El "Modo Families" (`TryScrapeFamiliesModeAsync`):** Una implementación más antigua y simple, que depende de la configuración `scrapingMode: "families"` en el archivo JSON.

El problema reside en el flujo de control que decide cuál de estas dos lógicas ejecutar:

-   El código primero intenta ejecutar el **"Gancho Mejorado"**. Sin embargo, este gancho tiene una condición de entrada crítica: **solo se activa si el archivo `festo_example_urls.json` existe y contiene URLs**. En las pruebas actuales, este archivo no se utiliza, por lo que el gancho intenta una única ejecución recursiva desde la URL base y, al fallar en ese primer intento, termina sin productos.
-   Debido a que el "Gancho Mejorado" no lanza una excepción, sino que simplemente devuelve una lista vacía, el control pasa a la siguiente sección del código.
-   A continuación, se ejecuta la lógica del **"Modo Families"**, ya que el archivo `festo_config_families_mode.json` así lo especifica. Sin embargo, como se observa en los logs, esta implementación más antigua falla porque su selector (`a[class*='similar-products-link']`) ya no es válido para encontrar los enlaces de las familias de productos en las páginas de categorías actuales de Festo.

En resumen, el scraper intenta primero la estrategia correcta pero con una dependencia no cumplida (las URLs de ejemplo), y al fallar, recurre a una estrategia obsoleta que también falla. El éxito de las URLs directas se debe a que estas evitan por completo la lógica de navegación por categorías.

## 3. Estrategia de Solución Propuesta

El objetivo es asegurar que el sistema utilice **siempre y únicamente la lógica del "Gancho Mejorado" (`NavigateAndCollectFromSubcategoriesAsync`)** para la navegación de categorías de Festo, ya que es la implementación más completa y robusta. La estrategia se basa en la **unificación y simplificación** del flujo de ejecución.

### Paso 1: Eliminar la Bifurcación de Lógica

Se debe modificar el método `ScrapeAsync` en `PlaywrightScrapingService.cs` para eliminar la dualidad de estrategias.

**Acción:**

1.  Localizar el bloque `// --- GANCHO ESPECÍFICO PARA FESTO ---` (aproximadamente en la línea 437).
2.  Modificar la condición `if (site.Name.Contains("Festo", ...))` para que, si se cumple, **toda la lógica de scraping para Festo se ejecute dentro de este bloque y finalice allí**, ya sea con éxito o con un error controlado.
3.  Eliminar por completo la lógica de fallback que ejecuta el `TryScrapeFamiliesModeAsync` para Festo. El "Gancho Mejorado" debe ser la única vía.

### Paso 2: Independizar el "Gancho Mejorado" de las URLs de Ejemplo

El "Gancho Mejorado" no debe depender del archivo `festo_example_urls.json` para iniciar la navegación recursiva. Debe ser capaz de comenzar desde cualquier URL de categoría proporcionada.

**Acción:**

1.  Dentro del "Gancho Mejorado", modificar la lógica para que, si `exampleUrls` está vacío, no se rinda. En su lugar, debe tomar la `site.BaseUrl` (que en el contexto de la prueba es la URL de la categoría) como punto de partida para la recursión.
2.  La llamada inicial a la función recursiva debe ser: `await NavigateAndCollectFromSubcategoriesAsync(page, site.Id, selectors, festoProducts, seenProducts, maxProducts, new List<string>(), cancellationToken);`, utilizando la página actual que ya ha sido navegada a la `BaseUrl`.

### Paso 3: Consolidar y Mejorar los Selectores de Navegación

La función `NavigateAndCollectFromSubcategoriesAsync` ya contiene un conjunto de selectores de respaldo para encontrar subcategorías. La función `CollectFamilyLinksAsync` (parte del modo obsoleto) también tiene una lista de selectores. Se deben consolidar los mejores selectores de ambas en un único lugar.

**Acción:**

1.  Revisar los selectores de `CollectFamilyLinksAsync` y fusionar los que sean útiles y robustos en la lista de selectores de `NavigateAndCollectFromSubcategoriesAsync`.
2.  **Recomendación Clave:** Externalizar esta lista de selectores al archivo de configuración `festo_config.json`. Esto permitirá ajustar los selectores de navegación sin necesidad de recompilar el código, aumentando drásticamente la mantenibilidad.

    *Ejemplo de cómo podría verse en `festo_config.json`:*

    ```json
    "navigationSelectors": [
        "a[data-testid='category-tile']",
        "a[class*='category-card--']",
        "a[class*='product-family--']",
        "div[class*='category-list--'] a"
    ]
    ```

### Paso 4: Actualizar el Programa de Prueba

El programa de prueba (`TestFestoScraper/Program.cs`) debe ser ajustado para reflejar la nueva estrategia unificada.

**Acción:**

1.  Eliminar la dependencia del archivo `festo_config_families_mode.json`.
2.  Utilizar un único archivo de configuración principal (ej. `festo_config.json`) que contenga todos los selectores necesarios, incluyendo los de navegación.
3.  Asegurarse de que el `SiteProfile` se construya con las URLs de categoría que se desean probar en el `BaseUrl` para cada ejecución del bucle.

## 4. Plan de Implementación y Validación

1.  **Implementar Cambios en `PlaywrightScrapingService.cs`:** Aplicar los pasos 1, 2 y 3 para unificar el flujo de control y consolidar los selectores.
2.  **Actualizar Configuración JSON:** Mover los selectores de navegación al archivo `festo_config.json`.
3.  **Modificar `TestFestoScraper`:** Ajustar el programa de prueba para que utilice la nueva configuración unificada.
4.  **Ejecutar Prueba Incremental:**
    -   Ejecutar la prueba con una única URL de categoría de alto nivel.
    -   Verificar en los logs que el "Gancho Mejorado" se activa correctamente.
    -   Confirmar que la función `NavigateAndCollectFromSubcategoriesAsync` identifica y navega a las subcategorías.
    -   Validar que se extraen productos de las páginas de detalle o de las tablas de variantes encontradas durante la recursión.
5.  **Validación Final:** Ejecutar la prueba con múltiples URLs de categoría para asegurar que el proceso es robusto y funciona en diferentes secciones del sitio.

Al seguir esta estrategia, se eliminará la confusión entre las dos implementaciones, se centralizará la lógica en el método más avanzado y se hará que el sistema sea más mantenible y predecible, solucionando de raíz el fallo en la extracción recursiva.
