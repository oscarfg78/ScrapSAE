# Arquitectura de Ejecución de ScrapSAE

Este documento describe el flujo de ejecución unificado (alineado con los métodos descubiertos en el Wizard) para realizar la extracción de productos.

## Diagrama de Capas de Ejecución

El objetivo de esta arquitectura es asegurar que las mismas estrategias de extracción probadas y validadas visualmente por el usuario en el Wizard de ScrapSAE (Desktop) sean ejecutadas de manera idéntica por el worker de fondo (`ScrapSAE.Api`).

1. **Configuración en el Wizard (Desktop)**
   - El usuario explora y selecciona visualmente elementos clave en el navegador.
   - El modelo de IA local analiza y valida los selectores CSS (`"productContainer"`, `"productCard"`, `"sku"`, `"name"`, `"price"`, `"image"`, etc.).
   - Se construye el objeto `SiteProfile`, definiendo:
     - `StrategyType` (Ej. `"Generic"`, `"Shopify"`)
     - `Strategies` activas y en orden (Ej. `Direct`, `List`, `Families`).
     - Diccionario de `Selectors` exacto a ser consumido por las estrategias.

2. **Invocación (API Endpoint)**
   - El usuario (o un cron) lanza el scrape a través de `POST /api/scraping/run/{siteId}`.
   - A través de los Query Parameters se construye y populariza el contexto inmutable de la ejecución: `ScrapeExecutionContext`. Esto elimina la dependencia de variables de entorno y permite ejecución *thread-safe* (múltiples scrapes simultáneos con distintas configuraciones).

3. **Enrutamiento (ScrapingRunner)**
   - El Runner lee la **única fuente de la verdad** para enrutamiento: `SiteProfile.StrategyType`.
   - Si es `"Shopify"`, enruta directamente a `ShopifyApiStrategy` (sin levantar un navegador Playwright).
   - Si es `"Generic"`, inyecta el `IScrapingService` base (basado en Playwright) que prepara el terreno de navegación.

4. **Setup del Navegador (PlaywrightScrapingService)**
   - Se encarga exclusivamente de las etapas complejas del inicio:
     - Anti-bot evasión y *Stealth injection*.
     - Manejo y carga de estado (Cookies persistentes).
     - Ejecución de Login automatizado y su fallback a Login Manual interactivo.
     - Navegación inicial a la URL.
   - Una vez la página está estabilizada en la URL principal (con sesión iniciada de requerirse), *DELEGA* la extracción a la capa orquestadora.

5. **Orquestación de Estrategias (StrategyOrchestrator)**
   - Lee `SiteProfile.Strategies` (o asume el fallback `Direct` -> `List` -> `Families`).
   - Itera por las estrategias habilitadas ordenadas por prioridad.
   - Detiene su ejecución apenas la primera estrategia retorna una lista de productos válidos.

6. **Estrategias Finales (IScrapingStrategy)**
   - Las estrategias base (`DirectExtractionStrategy`, `ListExtractionStrategy`, `FamiliesExtractionStrategy`) usan la abstracción para consumir las llaves exactas definidas en la fase 1 (Wizard) del diccionario `SiteProfile.Selectors`.

## Beneficios
- **Thread-Safety**: Todo estado de la ejecución vive en memoria aislada (`ScrapeExecutionContext`) evitando corrupciones por sobreescritura de variables globales.
- **Predictibilidad**: Lo que el usuario configura y aprueba en el UI Desktop (Wizard) mapea 1-1 con el engine de ejecución en el API.
- **Mantenibilidad**: La clase monolítica `PlaywrightScrapingService` reduce sus responsabilidades delegando el *extracción/scraping* real a módulos más pequeños.
