# Guía de Publicación - ScrapSAE Extension

## Chrome Web Store y Firefox Add-ons

---

## 1. Resumen Ejecutivo

Este documento detalla todos los pasos, requisitos, assets y configuraciones necesarias para publicar la extensión ScrapSAE en Chrome Web Store y Firefox Add-ons. La guía cubre desde la preparación de la cuenta de desarrollador hasta la aprobación final, incluyendo las justificaciones de permisos que los revisores solicitan durante el proceso de revisión.

---

## 2. Requisitos Previos

Antes de iniciar el proceso de publicación, es necesario contar con los siguientes elementos preparados y verificados.

| Requisito | Chrome Web Store | Firefox Add-ons |
|---|---|---|
| Cuenta de desarrollador | Google Developer Account | Firefox Developer Hub Account |
| Costo de registro | $5 USD (pago único) | Gratuito |
| Verificación de identidad | Sí (verificación de Google) | No |
| Política de privacidad | Obligatoria (URL pública) | Obligatoria (URL pública) |
| Sitio web del desarrollador | Recomendado | Recomendado |
| Correo de soporte | Obligatorio | Obligatorio |

---

## 3. Assets Gráficos Requeridos

La publicación en ambas tiendas requiere un conjunto de imágenes con especificaciones estrictas. Todas las imágenes deben ser en formato PNG con fondo transparente o sólido, sin bordes redondeados (la tienda los aplica automáticamente).

### 3.1 Iconos de la Extensión

Los iconos se incluyen en el paquete de la extensión dentro del directorio `public/icons/`. Se requieren las siguientes resoluciones.

| Tamaño | Uso | Archivo |
|---|---|---|
| 16x16 px | Favicon y barra de pestañas | `icon-16.png` |
| 32x32 px | Barra de herramientas de Windows | `icon-32.png` |
| 48x48 px | Página de extensiones del navegador | `icon-48.png` |
| 128x128 px | Chrome Web Store y detalle de extensión | `icon-128.png` |

### 3.2 Assets para Chrome Web Store

Estos assets se suben directamente al Developer Dashboard de Chrome, no se incluyen en el paquete `.zip`.

| Asset | Dimensiones | Formato | Descripción |
|---|---|---|---|
| Icono de la tienda | 128x128 px | PNG | Icono principal mostrado en resultados de búsqueda |
| Captura de pantalla 1 | 1280x800 px | PNG/JPEG | Vista del Popup con layout configurado |
| Captura de pantalla 2 | 1280x800 px | PNG/JPEG | Vista del SidePanel con historial de ejecuciones |
| Captura de pantalla 3 | 1280x800 px | PNG/JPEG | Proceso de scraping en ejecución |
| Captura de pantalla 4 | 1280x800 px | PNG/JPEG | Archivo Excel descargado con datos extraídos |
| Captura de pantalla 5 | 1280x800 px | PNG/JPEG | Configuración de selectores CSS |
| Imagen promocional pequeña | 440x280 px | PNG | Banner para listados destacados |
| Imagen promocional marquee | 1400x560 px | PNG | Banner para la parte superior de la tienda |

### 3.3 Assets para Firefox Add-ons

| Asset | Dimensiones | Formato | Descripción |
|---|---|---|---|
| Icono | 128x128 px | PNG | Mismo icono de la extensión |
| Capturas de pantalla | Mínimo 1, máximo 5 | PNG/JPEG | Mismas que Chrome, redimensionadas si es necesario |

---

## 4. Contenido de la Ficha de Publicación

### 4.1 Información General

El siguiente contenido se utiliza para ambas tiendas, adaptando el formato según los requisitos de cada plataforma.

**Nombre de la extensión:** ScrapSAE - Web Scraper to Excel

**Resumen corto (132 caracteres máximo):**
Extrae datos de cualquier sitio web y exporta a Excel. Layouts configurables, simulación humana e IA.

**Descripción completa:**

ScrapSAE es una extensión profesional de web scraping que permite extraer datos estructurados de cualquier sitio web directamente desde la pestaña activa del navegador y exportarlos a un archivo Excel (.xlsx) con un solo clic.

Funciones principales:

- Layouts de extracción configurables con selectores CSS personalizados.
- Exportación directa a Excel (.xlsx) con columnas y formato configurable.
- Simulación de comportamiento humano para evitar detección de bots.
- Procesamiento con inteligencia artificial para enriquecer los datos extraídos.
- Historial completo de ejecuciones sincronizado en la nube.
- Gestión de múltiples layouts por usuario.
- Panel lateral (SidePanel) para gestión sin salir de la página.

Ideal para:
- Equipos de compras que necesitan comparar precios de proveedores.
- Analistas de datos que requieren información estructurada de la web.
- Investigadores de mercado que monitorean productos y precios.
- Desarrolladores que necesitan extraer datos para sus proyectos.

Planes disponibles:
- Free: 50 extracciones/día, 1 layout, 3 páginas por scraping.
- Pro ($19/mes): 1,000 extracciones/día, 20 layouts, procesamiento con IA.
- Enterprise ($79/mes): Ilimitado, soporte dedicado.

Privacidad: Los datos extraídos se procesan localmente en tu navegador y se descargan directamente a tu computadora. Nunca almacenamos ni compartimos tus datos.

**Categoría:** Productividad (Chrome) / Data Management (Firefox)

**Idioma principal:** Español

**Idiomas adicionales:** Inglés

### 4.2 URLs Requeridas

| Campo | URL |
|---|---|
| Sitio web | https://scrapsae.com |
| Política de privacidad | https://scrapsae.com/privacy.html |
| Términos de servicio | https://scrapsae.com/terms.html |
| Soporte | https://scrapsae.com/support.html |
| Correo de soporte | soporte@scrapsae.com |

---

## 5. Justificación de Permisos

Los revisores de Chrome Web Store son particularmente estrictos con los permisos solicitados. Cada permiso debe estar justificado de forma clara y específica. A continuación se presenta la justificación para cada permiso declarado en el `manifest.json`.

### 5.1 Permisos Declarados

| Permiso | Justificación para el Revisor |
|---|---|
| `activeTab` | La extensión necesita acceder al contenido DOM de la pestaña activa cuando el usuario hace clic en el icono de la extensión o en el botón "Iniciar Scraping" del popup. Este acceso se utiliza exclusivamente para leer los elementos HTML según los selectores CSS configurados por el usuario y extraer los datos de productos (nombre, precio, SKU, etc.). El acceso se solicita solo bajo demanda del usuario, nunca de forma automática. |
| `storage` | Se utiliza para almacenar las preferencias del usuario (tema, idioma), el token de sesión de autenticación y una caché local de los layouts configurados. Esto permite que la extensión funcione sin necesidad de consultar el servidor en cada apertura. Los datos almacenados son exclusivamente configuraciones del usuario, nunca datos de navegación. |
| `downloads` | La función principal de la extensión es generar un archivo Excel (.xlsx) con los datos extraídos y descargarlo al computador del usuario. El permiso `downloads` es necesario para invocar `chrome.downloads.download()` y guardar el archivo generado. Sin este permiso, la extensión no podría entregar los resultados al usuario. |
| `scripting` | Se requiere para inyectar el Content Script de extracción en la pestaña activa cuando el usuario inicia un scraping. El script inyectado lee los elementos DOM según los selectores configurados y envía los datos al Service Worker para su procesamiento. La inyección se realiza únicamente bajo acción explícita del usuario. |
| `sidePanel` | La extensión utiliza el panel lateral (Side Panel) como interfaz principal para la gestión de layouts, visualización del historial de ejecuciones y configuración de la cuenta del usuario. El Side Panel permite al usuario interactuar con la extensión sin abandonar la página web que está consultando. |

### 5.2 Host Permissions

| Patrón | Justificación |
|---|---|
| `https://api.scrapsae.com/*` | La extensión envía los datos extraídos al API de ScrapSAE para procesamiento con inteligencia artificial (categorización, normalización de nombres, detección de especificaciones técnicas). También se comunica con el API para autenticación, gestión de layouts y registro del historial de ejecuciones. |
| `https://*.supabase.co/*` | Se utiliza para la autenticación del usuario (OAuth con Google, login con email) y para la sincronización de layouts y historial entre dispositivos. Supabase es el proveedor de backend-as-a-service utilizado por ScrapSAE. |

### 5.3 Notas Importantes para la Revisión

El formulario de revisión de Chrome Web Store incluye preguntas específicas que deben responderse con precisión.

**"¿Por qué necesita acceso a datos del usuario?"**
ScrapSAE necesita leer el contenido DOM de la pestaña activa para extraer datos de productos (nombre, precio, SKU, imágenes) según los selectores CSS que el usuario ha configurado previamente. Los datos se procesan localmente y se descargan como archivo Excel. No se recopila información personal del usuario ni datos de navegación.

**"¿Qué datos se envían a servidores externos?"**
Los datos de productos extraídos (nombre, precio, SKU, categoría) se envían opcionalmente al API de ScrapSAE para procesamiento con IA (enriquecimiento y categorización). El usuario puede desactivar esta función y procesar los datos exclusivamente de forma local. Los datos de autenticación (token JWT) se envían a Supabase para gestión de sesiones.

**"¿La extensión recopila datos personales?"**
No. La extensión recopila exclusivamente datos de productos de sitios web comerciales (nombre, precio, SKU, descripción, imágenes). No recopila datos personales del usuario más allá del correo electrónico proporcionado voluntariamente durante el registro.

---

## 6. Proceso de Publicación en Chrome Web Store

### 6.1 Paso a Paso

El proceso de publicación en Chrome Web Store sigue una secuencia específica que debe completarse en orden.

**Paso 1: Registrar cuenta de desarrollador.** Acceder a https://chrome.google.com/webstore/devconsole y completar el registro pagando la tarifa de $5 USD. La verificación de identidad puede tomar entre 1 y 3 días hábiles.

**Paso 2: Preparar el paquete.** El workflow de GitHub Actions genera automáticamente el archivo `scrapsae-chrome-{sha}.zip` en cada push a la rama `main`. Descargar el artifact más reciente desde la pestaña Actions del repositorio.

**Paso 3: Subir el paquete.** En el Developer Dashboard, hacer clic en "New Item" y subir el archivo `.zip`. El sistema validará automáticamente el `manifest.json` y los permisos declarados.

**Paso 4: Completar la ficha.** Llenar todos los campos de la ficha de publicación con la información detallada en la sección 4 de este documento. Subir todos los assets gráficos listados en la sección 3.2.

**Paso 5: Declarar permisos.** En la sección "Privacy practices", responder las preguntas sobre permisos utilizando las justificaciones de la sección 5. Marcar las casillas correspondientes sobre recopilación de datos.

**Paso 6: Configurar distribución.** Seleccionar "Public" para distribución pública. Opcionalmente, iniciar con "Unlisted" para pruebas beta antes del lanzamiento público.

**Paso 7: Enviar a revisión.** Hacer clic en "Submit for Review". El proceso de revisión toma entre 1 y 3 días hábiles para extensiones nuevas. Las actualizaciones posteriores suelen revisarse en menos de 24 horas.

### 6.2 Razones Comunes de Rechazo

La siguiente tabla lista las razones más frecuentes de rechazo y cómo evitarlas.

| Razón de Rechazo | Cómo Evitarlo |
|---|---|
| Permisos excesivos | Solicitar solo los permisos estrictamente necesarios. No usar `<all_urls>` si no es imprescindible. |
| Falta de política de privacidad | Publicar la política en https://scrapsae.com/privacy.html antes de enviar a revisión. |
| Descripción engañosa | No prometer funciones que no existan. Ser preciso en las capacidades. |
| Código ofuscado | No ofuscar el código JavaScript. La minificación es aceptable pero la ofuscación no. |
| Funcionalidad insuficiente | La extensión debe funcionar correctamente en el momento de la revisión. |
| Violación de marca | No usar logos o nombres de otras empresas en los assets gráficos. |

---

## 7. Proceso de Publicación en Firefox Add-ons

### 7.1 Paso a Paso

**Paso 1: Registrar cuenta.** Crear una cuenta en https://addons.mozilla.org/developers/. El registro es gratuito y no requiere verificación de identidad adicional.

**Paso 2: Preparar el paquete.** El workflow de GitHub Actions genera el archivo `scrapsae-firefox-{sha}.zip` con el manifest ajustado para Firefox (incluye `browser_specific_settings` con el ID de gecko y reemplaza `side_panel` por `sidebar_action`).

**Paso 3: Subir el add-on.** En el Developer Hub, hacer clic en "Submit a New Add-on" y subir el archivo `.zip`. Firefox validará automáticamente la estructura y los permisos.

**Paso 4: Elegir tipo de revisión.** Seleccionar "Listed" para publicación en la tienda pública. Firefox ofrece dos canales: "Listed" (revisión completa) y "Self-distributed" (sin revisión de tienda).

**Paso 5: Proporcionar código fuente.** Si el código está minificado o compilado (como en nuestro caso con TypeScript), Firefox requiere acceso al código fuente original. Proporcionar un enlace al repositorio de GitHub o subir un archivo con el código fuente.

**Paso 6: Completar la ficha.** Llenar la información del add-on con los datos de la sección 4. Subir las capturas de pantalla.

**Paso 7: Enviar a revisión.** El proceso de revisión de Firefox es generalmente más rápido que el de Chrome, tomando menos de 24 horas para extensiones nuevas.

### 7.2 Diferencias Técnicas con Chrome

La extensión requiere ajustes menores para funcionar en Firefox. Estos ajustes se aplican automáticamente en el workflow de CI/CD.

| Aspecto | Chrome | Firefox |
|---|---|---|
| Manifest | Manifest V3 estándar | Manifest V3 + `browser_specific_settings` |
| Panel lateral | `chrome.sidePanel` API | `browser.sidebarAction` API |
| Permiso sidePanel | `"sidePanel"` en permissions | No necesario (sidebar_action es declarativo) |
| Service Worker | Background Service Worker | Background Service Worker (soporte desde Firefox 109) |
| ID de extensión | Asignado por Chrome Web Store | Definido en `browser_specific_settings.gecko.id` |

---

## 8. Checklist Pre-Publicación

La siguiente lista debe verificarse completamente antes de enviar la extensión a revisión en cualquiera de las dos tiendas.

| Verificación | Estado |
|---|---|
| Extensión compilada sin errores de TypeScript | Pendiente |
| Manifest.json válido y con versión correcta | Pendiente |
| Todos los iconos generados (16, 32, 48, 128 px) | Pendiente |
| Popup se abre correctamente y muestra login/dashboard | Pendiente |
| SidePanel se abre y lista layouts del usuario | Pendiente |
| Scraping funciona en al menos 3 sitios de prueba | Pendiente |
| Excel se genera y descarga correctamente | Pendiente |
| Simulación humana funciona (movimientos visibles) | Pendiente |
| Login con Google OAuth funciona | Pendiente |
| Login con email/password funciona | Pendiente |
| Layouts se guardan y cargan desde Supabase | Pendiente |
| Historial de ejecuciones se registra | Pendiente |
| Límites por plan se aplican correctamente | Pendiente |
| Política de privacidad publicada y accesible | Pendiente |
| Términos de servicio publicados y accesibles | Pendiente |
| Landing page desplegada y funcional | Pendiente |
| Stripe checkout funciona en modo test | Pendiente |
| Capturas de pantalla generadas (1280x800) | Pendiente |
| Imágenes promocionales generadas | Pendiente |
| Descripción de la tienda redactada y revisada | Pendiente |
| Justificaciones de permisos preparadas | Pendiente |
| Workflow de CI/CD genera .zip correctamente | Pendiente |
| Paquete Firefox con manifest ajustado | Pendiente |

---

## 9. Post-Publicación

Una vez aprobada la extensión en ambas tiendas, se deben completar las siguientes tareas.

**Actualizar la Landing Page.** Reemplazar los enlaces placeholder de los botones "Instalar en Chrome" e "Instalar en Firefox" con las URLs reales de las tiendas.

**Configurar analytics.** Implementar seguimiento de instalaciones y uso para medir la adopción. Chrome Web Store proporciona estadísticas básicas en el Developer Dashboard.

**Monitorear reviews.** Configurar alertas para nuevas reseñas en ambas tiendas. Responder a las reseñas negativas de forma profesional y oportuna.

**Planificar actualizaciones.** Establecer un ciclo de actualizaciones regular (quincenal o mensual) para mantener la extensión activa y mejorar la posición en las búsquedas de la tienda.

**Verificar webhooks de Stripe.** Confirmar que los webhooks de producción están configurados correctamente y que las suscripciones se activan y desactivan automáticamente.
