# Impacto en Frontend por Cambios en Backend

**Fecha:** 02 de febrero de 2026  
**Objetivo:** Detallar los cambios necesarios en el frontend (Desktop App) para reflejar las mejoras en la estructura de datos del backend (ScrapSAE.Core).

---

## 📋 Resumen Ejecutivo

El backend ha sido actualizado para capturar una estructura de datos de productos mucho más rica, alineada con los requisitos de la plataforma de e-commerce Flashly. Estos cambios, centrados en el DTO `ProcessedProduct`, impactan directamente la forma en que la aplicación de escritorio (`ScrapSAE.Desktop`) visualiza los productos en la sección de "Staging".

El objetivo de este documento es guiar las modificaciones necesarias en el frontend para que pueda **mostrar y gestionar la nueva información**, como múltiples imágenes, stock, moneda y archivos adjuntos.

---

## 🔄 Flujo de Datos: Backend a Frontend

El flujo de datos no ha cambiado en su esencia, pero el **contenido del payload** sí. La aplicación de escritorio sigue recibiendo una lista de `StagingProduct`, pero el campo `AIProcessedJson` ahora contiene una estructura mucho más completa.

```mermaid
graph TD
    A[Backend: Scraping] --> B(Backend: ProcessedProduct ✨);
    B --> C{Backend: StagingProduct};
    C --> |AIProcessedJson (string)| D[API Endpoint: /api/staging-products];
    D --> E[Frontend: ApiClient];
    E --> F[Frontend: MainViewModel];
    F --> G[Frontend: StagingProductUi ✨];
    G --> H[Frontend: Vista (UI)];

    subgraph Backend
        A
        B
        C
    end

    subgraph Frontend (Desktop App)
        E
        F
        G
        H
    end

    style B fill:#d4edda,stroke:#155724
    style G fill:#f8d7da,stroke:#721c24
```

**✨ Leyenda:**
- **`ProcessedProduct`:** Estructura de datos enriquecida en el backend.
- **`StagingProductUi`:** Componente del frontend que necesita las modificaciones más significativas.

---

## 💥 Impacto Principal: `StagingProductUi.cs`

El archivo `src/ScrapSAE.Desktop/Models/StagingProductUi.cs` es el principal afectado. Actualmente, solo puede interpretar campos simples como `Name`, `Description`, `Price` y una única `ImageUrl`. Debe ser modificado para manejar la nueva estructura de `ProcessedProduct`.

### 1. Nuevas Propiedades en `StagingProductUi`

Se deben agregar nuevas propiedades que expongan los datos enriquecidos del `ProcessedProduct` subyacente.

```csharp
// En: src/ScrapSAE.Desktop/Models/StagingProductUi.cs

// ... propiedades existentes

// ✨ NUEVAS PROPIEDADES

public string Currency => GetProcessed()?.Currency ?? "MXN";

public int? Stock => GetProcessed()?.Stock;

public List<string> Images => GetProcessed()?.Images ?? new List<string>();

public List<ProductAttachment> Attachments => GetProcessed()?.Attachments ?? new List<ProductAttachment>();

public List<string> Categories => GetProcessed()?.Categories ?? new List<string>();

// ✨ PROPIEDAD MODIFICADA

// Modificar ImageUrl para que sea la primera de la lista o una imagen por defecto
public string PrimaryImageUrl => Images.FirstOrDefault() ?? "/Assets/default-image.png";

```

### 2. Modificación de la Lógica de Deserialización

La lógica en el método `GetProcessed()` es correcta y ya utiliza `JsonSerializer` para deserializar `AIProcessedJson` a `ProcessedProduct`. No se necesitan cambios en esa parte, ya que los nuevos campos serán poblados automáticamente si existen en el JSON.

---

## 🎨 Cambios Recomendados en la Interfaz de Usuario (UI)

La interfaz de usuario, definida probablemente en archivos `.axaml` (Avalonia UI), debe ser actualizada para mostrar la nueva información. A continuación se presentan sugerencias de diseño.

### 1. Galería de Imágenes

En lugar de una sola imagen, se debe mostrar una galería o un carrusel.

**Sugerencia de Diseño:**
- Una imagen principal grande.
- Una fila de miniaturas (thumbnails) debajo, que al hacer clic cambian la imagen principal.
- Indicadores de navegación (flechas) si hay más de 3-4 imágenes.

```xml
<!-- Sugerencia de XAML/AXAML para la galería -->
<StackPanel>
    <!-- Imagen Principal -->
    <Image Source="{Binding PrimaryImageUrl}" Height="300" />

    <!-- Galería de Miniaturas -->
    <ItemsControl ItemsSource="{Binding Images}">
        <ItemsControl.ItemsPanel>
            <ItemsPanelTemplate>
                <StackPanel Orientation="Horizontal" Spacing="5" />
            </ItemsPanelTemplate>
        </ItemsControl.ItemsPanel>
        <ItemsControl.ItemTemplate>
            <DataTemplate>
                <Button Command="{Binding $parent[ItemsControl].DataContext.ChangeImageCommand}" CommandParameter="{Binding}">
                    <Image Source="{Binding}" Width="60" Height="60" />
                </Button>
            </DataTemplate>
        </ItemsControl.ItemTemplate>
    </ItemsControl>
</StackPanel>
```

### 2. Información de Precio y Stock

Mostrar la moneda junto al precio y la información de stock de forma clara.

**Sugerencia de Diseño:**
- **Precio:** "$99.99 MXN"
- **Stock:** "50 unidades en stock" o "Agotado"

```xml
<!-- Sugerencia de XAML/AXAML para precio y stock -->
<StackPanel Orientation="Horizontal" Spacing="20">
    <TextBlock Text="{Binding Price, StringFormat=\"{0:C}\"} {Binding Currency}" FontSize="24" FontWeight="Bold" />
    <TextBlock Text="{Binding Stock, StringFormat=\"{0} en stock\"}" Foreground="Green" IsVisible="{Binding Stock > 0}" />
    <TextBlock Text="Agotado" Foreground="Red" IsVisible="{Binding Stock == 0}" />
</StackPanel>
```

### 3. Lista de Archivos Adjuntos

Mostrar una lista de los archivos adjuntos disponibles para descargar.

**Sugerencia de Diseño:**
- Un encabezado "Archivos Adjuntos".
- Una lista de enlaces, cada uno con un icono de archivo (ej. 📄 para PDF) y el nombre del archivo.
- Al hacer clic, se debe abrir el `FileUrl` en el navegador.

```xml
<!-- Sugerencia de XAML/AXAML para archivos adjuntos -->
<StackPanel>
    <TextBlock Text="Archivos Adjuntos" FontWeight="Bold" />
    <ItemsControl ItemsSource="{Binding Attachments}">
        <ItemsControl.ItemTemplate>
            <DataTemplate>
                <Button Command="{Binding $parent[ItemsControl].DataContext.OpenFileCommand}" CommandParameter="{Binding FileUrl}">
                    <StackPanel Orientation="Horizontal" Spacing="5">
                        <TextBlock Text="📄" /> <!-- Icono -->
                        <TextBlock Text="{Binding FileName}" TextDecorations="Underline" />
                    </StackPanel>
                </Button>
            </DataTemplate>
        </ItemsControl.ItemTemplate>
    </ItemsControl>
</StackPanel>
```

### 4. Lista de Categorías

Mostrar las categorías como etiquetas (tags).

**Sugerencia de Diseño:**
- Un `ItemsControl` que renderice cada categoría como un `Border` con texto.

```xml
<!-- Sugerencia de XAML/AXAML para categorías -->
<ItemsControl ItemsSource="{Binding Categories}">
    <ItemsControl.ItemsPanel>
        <ItemsPanelTemplate>
            <WrapPanel Orientation="Horizontal" />
        </ItemsPanelTemplate>
    </ItemsControl.ItemsPanel>
    <ItemsControl.ItemTemplate>
        <DataTemplate>
            <Border Background="LightGray" CornerRadius="10" Padding="8,4">
                <TextBlock Text="{Binding}" />
            </Border>
        </DataTemplate>
    </ItemsControl.ItemTemplate>
</ItemsControl>
```

---

## 📝 Resumen de Tareas para el Frontend

1.  **Actualizar `StagingProductUi.cs`:**
    - [ ] Agregar las nuevas propiedades: `Currency`, `Stock`, `Images`, `Attachments`, `Categories`.
    - [ ] Modificar `ImageUrl` para que sea `PrimaryImageUrl` y apunte a la primera imagen de la lista `Images`.

2.  **Implementar `Commands` en `MainViewModel.cs` (o similar):**
    - [ ] Crear `ChangeImageCommand` para manejar la selección de miniaturas en la galería.
    - [ ] Crear `OpenFileCommand` para abrir los `FileUrl` de los archivos adjuntos.

3.  **Modificar la Vista de Detalles del Producto (`.axaml`):**
    - [ ] Reemplazar el `Image` único por una galería de imágenes (imagen principal + miniaturas).
    - [ ] Actualizar el `TextBlock` del precio para incluir la moneda (`Currency`).
    - [ ] Agregar un `TextBlock` o `Badge` para mostrar el `Stock`.
    - [ ] Agregar una sección para listar los `Attachments` como enlaces descargables.
    - [ ] Agregar una sección para mostrar las `Categories` como etiquetas.

4.  **Actualizar el Endpoint de la API (si es necesario):**
    - El endpoint `/api/staging-products/upsert` que recibe `StagingProduct` no necesita cambios en su firma.
    - Cualquier cliente que consuma este endpoint debe estar preparado para interpretar el nuevo formato del campo `AIProcessedJson`.

---

## ✅ Conclusión

Los cambios en el backend habilitan una experiencia de usuario mucho más rica y completa en la aplicación de escritorio. Al implementar las modificaciones sugeridas en el frontend, la aplicación podrá visualizar y gestionar toda la información de productos que ahora se captura, cerrando el ciclo de homologación de datos con la plataforma de e-commerce.
