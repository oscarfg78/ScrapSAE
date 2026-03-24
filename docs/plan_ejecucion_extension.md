# Plan de Ejecución: ScrapSAE Chrome Extension & Web Platform

> **Proyecto:** ScrapSAE.Extension y ScrapSAE.Web
> **Rama:** `extension1`
> **Fecha:** Marzo 2026

Este documento detalla el plan de ejecución paso a paso para el desarrollo completo de la extensión de Chrome, la plataforma web con integración de pagos (Stripe) y los requisitos para la publicación en las tiendas de extensiones. El plan está estructurado en seis fases secuenciales que abarcan desde la configuración inicial hasta la publicación final.

---

## Fase 1: Configuración Inicial y Arquitectura Base (Semanas 1-2)

La primera fase se centra en establecer los cimientos del proyecto dentro de la solución existente y preparar la infraestructura de base de datos para soportar el nuevo modelo de negocio.

Se crearán dos nuevos proyectos en la solución `ScrapSAE.sln`. El primero es `ScrapSAE.Extension`, que contendrá el código de la extensión utilizando Vite y Vanilla JS/TS para asegurar un rendimiento óptimo y cumplir con las restricciones tecnológicas. El segundo es `ScrapSAE.Web`, destinado a la Landing Page comercial, desarrollado con HTML, CSS y JS Vanilla, reutilizando los estilos corporativos existentes.

En cuanto a la base de datos, se ampliará el esquema actual de Supabase para soportar la gestión de usuarios y la monetización. Se implementará el flujo de autenticación utilizando Google OAuth y correo electrónico, permitiendo a los usuarios iniciar sesión directamente desde la extensión y almacenando su token de forma segura en `chrome.storage.local`.

| Tarea Principal | Acciones Específicas | Entregables |
|---|---|---|
| **Estructura del Repositorio** | Crear proyectos `ScrapSAE.Extension` y `ScrapSAE.Web`. Actualizar `ScrapSAE.sln`. | Proyectos integrados en la solución. |
| **Base de Datos (Supabase)** | Crear tablas `user_profiles` y `user_layouts`. Actualizar `execution_reports`. Configurar RLS. | Esquema de BD actualizado y seguro. |
| **Autenticación Base** | Configurar OAuth y Email/Password. Implementar login en la extensión. | Flujo de login funcional en la extensión. |

---

## Fase 2: Desarrollo de la Extensión de Chrome (Semanas 3-5)

Esta fase constituye el núcleo del desarrollo técnico, donde se construirá la interfaz de usuario de la extensión y el motor de extracción de datos que operará directamente en el navegador del usuario.

La interfaz de usuario constará de un Popup principal para iniciar escaneos y seleccionar layouts, y un SidePanel más amplio para la gestión detallada de configuraciones y la visualización del historial. El motor de extracción, implementado como un Content Script, inyectará el script de evasión existente (`stealth_script.js`) y simulará el comportamiento humano mediante movimientos de ratón con curvas de Bézier y pausas aleatorias.

El Background Service Worker actuará como el orquestador central. Recibirá los datos crudos extraídos por el Content Script, los enviará al backend (`ScrapSAE.Api`) para su procesamiento con IA, y finalmente utilizará la biblioteca SheetJS para generar y descargar el archivo Excel final aplicando el layout configurado por el usuario.

| Componente | Responsabilidades | Tecnología |
|---|---|---|
| **Popup y SidePanel** | Interfaz para iniciar scraping, seleccionar layouts y ver historial. | HTML, CSS, TypeScript |
| **Content Script** | Leer el DOM, simular comportamiento humano, manejar paginación. | JavaScript (Inyectado) |
| **Service Worker** | Orquestar comunicación, llamar al API, generar y descargar Excel. | TypeScript, SheetJS |

---

## Fase 3: Desarrollo de la Plataforma Web y Monetización (Semanas 6-7)

El objetivo de esta fase es construir la presencia pública del producto y establecer el sistema de cobros recurrentes que permitirá la monetización de la herramienta.

Se desarrollará una Landing Page comercial optimizada para la conversión. Esta página incluirá una sección principal con un video demostrativo, una tabla comparativa de precios (Free, Pro a $19/mes, Enterprise a $79/mes), testimonios de usuarios y las secciones legales obligatorias como la Política de Privacidad y los Términos de Servicio.

La integración con Stripe gestionará todo el ciclo de vida de las suscripciones. Se configurarán los productos en el panel de Stripe y se crearán endpoints en el backend para generar sesiones de pago. El backend escuchará los webhooks de Stripe para actualizar automáticamente el estado de suscripción del usuario en Supabase, lo que a su vez desbloqueará las funciones premium dentro de la extensión.

| Área de Desarrollo | Elementos Clave | Objetivo |
|---|---|---|
| **Landing Page** | Hero section, Video Demo, Tabla de Precios, FAQ, Legal. | Captación de usuarios y conversión. |
| **Integración Stripe** | Sesiones de Checkout, Webhooks, Gestión de Suscripciones. | Procesamiento seguro de pagos. |
| **Control de Acceso** | Verificación de plan en Service Worker, límites de uso (Free vs Pro). | Aplicación de las reglas de negocio. |

---

## Fase 4: Automatización CI/CD (Semana 8)

Para garantizar un ciclo de desarrollo ágil y despliegues seguros, se implementarán flujos de trabajo automatizados utilizando GitHub Actions.

Se configurarán tres flujos de trabajo principales. El primero compilará y desplegará automáticamente el backend (`ScrapSAE.Api`) en el servidor de producción cada vez que se integren cambios en la rama principal. El segundo flujo se encargará de publicar la Landing Page estática. El tercer flujo empaquetará el código de la extensión en un archivo `.zip` optimizado, listo para ser subido a las tiendas de navegadores.

| Workflow (GitHub Actions) | Disparador | Acción Resultante |
|---|---|---|
| `deploy-api.yml` | Push a `main` (cambios en backend) | Despliegue de `ScrapSAE.Api` en producción. |
| `deploy-web.yml` | Push a `main` (cambios en web) | Publicación de la Landing Page. |
| `build-extension.yml` | Creación de Release o Tag | Generación del archivo `.zip` de la extensión. |

---

## Fase 5: Preparación para Publicación (Semana 9)

Antes de enviar la extensión a revisión, es estrictamente necesario preparar todos los activos gráficos y la documentación legal requerida por las políticas de Google y Mozilla.

Se diseñarán los iconos en múltiples resoluciones (16x16 hasta 128x128), capturas de pantalla de alta calidad que muestren la extensión en uso, y las imágenes promocionales obligatorias (440x280 para Chrome Web Store). Además, se redactará una Política de Privacidad clara que detalle el manejo de datos, un requisito indispensable para extensiones que manejan autenticación de usuarios.

Finalmente, se documentará la justificación de cada permiso solicitado en el archivo `manifest.json`. Es crucial explicar detalladamente por qué se requiere acceso a la pestaña activa (`activeTab`), al almacenamiento local (`storage`) y al sistema de descargas (`downloads`), para evitar rechazos durante el proceso de revisión.

| Requisito | Especificaciones | Destino |
|---|---|---|
| **Assets Gráficos** | Iconos (16 a 128px), Capturas (1280x800), Promo (440x280). | Chrome Web Store y AMO |
| **Política de Privacidad** | Declaración de uso de datos, no venta a terceros, URL pública. | Landing Page y Tiendas |
| **Justificación Permisos** | Explicación detallada para `activeTab`, `storage`, `downloads`. | Formulario de revisión |

---

## Fase 6: Proceso de Publicación (Semana 10)

La fase final consiste en el envío formal de la extensión a las respectivas tiendas de complementos para su revisión y publicación pública.

Para la Chrome Web Store, se creará una cuenta de desarrollador (que requiere un pago único de $5 USD). Se subirá el paquete `.zip` generado por el sistema CI/CD, se completará la ficha de la tienda con descripciones optimizadas para SEO y se enviará a revisión. Este proceso suele tomar entre 1 y 3 días hábiles.

Simultáneamente, se realizará el envío a Mozilla Add-ons (AMO) para Firefox. Dado que Firefox soporta extensiones Manifest V3 con mínimas diferencias, el mismo paquete base puede ser utilizado. La revisión preliminar en AMO suele ser automatizada y significativamente más rápida, completándose a menudo en menos de 24 horas.

| Tienda | Pasos Principales | Tiempo Estimado de Revisión |
|---|---|---|
| **Chrome Web Store** | Registro ($5), Subida de ZIP, Ficha de tienda, Pestaña Privacidad. | 1 a 3 días hábiles |
| **Firefox Add-ons (AMO)** | Registro gratuito, Subida de ZIP, Ficha de tienda. | < 24 horas (Revisión preliminar) |
