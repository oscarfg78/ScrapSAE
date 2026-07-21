# ScrapSAE

ScrapSAE es una solución integral diseñada para automatizar la extracción de datos (scraping) desde orígenes externos y su integración mediante procesos en segundo plano e interfaces de programación.

## 🏗️ Arquitectura y Módulos

La solución (`ScrapSAE.sln`) está compuesta por diferentes capas y aplicaciones organizadas en la carpeta `src/`:

- **ScrapSAE.Core**: Capa central del modelo de Dominio (Domain Driven Design). Contiene Entidades, DTOs, Enumeradores y las Interfaces base.
- **ScrapSAE.Infrastructure**: Implementación de los servicios y dependencias externas. Incluye la integración de Scraping (con Playwright), acceso a datos (mediante Supabase), integraciones con AI y el SAE.
- **ScrapSAE.Worker**: Servicio en segundo plano (Background Worker) responsable de orquestar y ejecutar los trabajos de scraping de forma periódica o desencadenada.
- **ScrapSAE.Api**: API RESTful que expone los servicios y datos del sistema hacia aplicaciones clientes.
- **ScrapSAE.Desktop**: Cliente de escritorio para la aplicación.
- **ScrapSAE.Web / ScrapSAE.Extension**: Proyectos Front-end.

## 🚀 Requisitos Previos

- [.NET 8 SDK](https://dotnet.microsoft.com/download) (o la versión correspondiente definida en los proyectos).
- Configuración de dependencias externas (Supabase, Playwright).

## 🛠️ Configuración y Ejecución Local

Para ejecutar los proyectos principales (`Api` y `Worker`), es necesario configurar los secretos o las variables de entorno locales (típicamente en `appsettings.json` o `appsettings.Development.json`).

### Ejecutar ScrapSAE.Worker

El Worker requiere una correcta configuración de las credenciales de Supabase y cualquier otro servicio externo.

Desde la raíz del proyecto, ejecuta:
```bash
dotnet run --project src/ScrapSAE.Worker/ScrapSAE.Worker.csproj
```

### Ejecutar ScrapSAE.Api

Para lanzar la API RESTful y visualizar su documentación (por ejemplo, vía Swagger):

```bash
dotnet run --project src/ScrapSAE.Api/ScrapSAE.Api.csproj
```

### Ejecutar ScrapSAE.Desktop (En Desarrollo)

Para ejecutar la aplicación de escritorio (WPF):

```bash
dotnet run --project src/ScrapSAE.Desktop/ScrapSAE.Desktop.csproj
```

### Ejecutar ScrapSAE.Extension (Obsoleto/Incompleto)

Este proyecto está basado en Node.js y utiliza Vite. Se ejecuta con pnpm:

```bash
cd src/ScrapSAE.Extension
pnpm install
pnpm dev
```

### Ejecutar ScrapSAE.Web (Obsoleto/Incompleto)

Este es un proyecto web estático. Puedes abrir directamente `index.html` en el navegador o servirlo localmente:

```bash
cd src/ScrapSAE.Web
npx serve .
```

## 📊 Estado de los Módulos

- 🟢 **Core**: Estable / Listo
- 🟢 **Infrastructure**: Estable / Listo
- 🟢 **Worker**: Estable / Listo
- 🟢 **Api**: Estable / Listo
- 🟡 **Desktop**: En Desarrollo / Pruebas E2E
- 🔴 **Web**: Obsoleto / Incompleto
- 🔴 **Extension**: Obsoleto / Incompleto

## 📁 Estructura del Proyecto

En la raíz encontrarás:
- `src/`: Código fuente de todos los proyectos de la solución.
- `tests/`: Proyectos de pruebas unitarias y E2E.
- `docs/`: Documentación del proyecto y notas de análisis.
- `scripts/`: Scripts utilitarios (PowerShell y Python) para operaciones y consultas manuales.
- `configs/`: Archivos base de configuración `.json` y `.txt`.
- `temp/`: Archivos generados dinámicamente y volcados temporales (logs, HTML dumps).
- `tools/`: Ejecutables de terceros.
