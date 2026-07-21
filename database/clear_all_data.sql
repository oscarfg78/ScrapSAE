-- ==============================================================================
-- SCRIPT PARA LIMPIAR DATOS DE LA BASE DE DATOS (SUPABASE / POSTGRESQL)
-- Proyecto: ScrapSAE
-- ==============================================================================
-- Este script permite limpiar la información de la base de datos en diferentes
-- niveles, desde borrar únicamente los logs y productos extraídos hasta un
-- restablecimiento total (hard reset).
--
-- INSTRUCCIONES DE USO:
-- 1. Copia el contenido de la sección que desees ejecutar.
-- 2. Ve a tu panel de Supabase -> SQL Editor.
-- 3. Crea una nueva consulta ("New Query"), pega el script y haz clic en "Run".
-- ==============================================================================

-- ==============================================================================
-- OPCIÓN A: LIMPIEZA DE TRANSACCIONES Y LOGS (RECOMENDADO)
-- Borra productos extraídos, logs y reportes, pero MANTIENE los proveedores
-- configurados, mapeos de categorías, perfiles y layouts.
-- ==============================================================================
BEGIN;

-- 1. Eliminar logs de sincronización
DELETE FROM public.sync_logs;

-- 2. Eliminar reportes de ejecución de scraping
DELETE FROM public.execution_reports;

-- 3. Eliminar todos los productos en staging (productos extraídos)
DELETE FROM public.staging_products;

-- 4. Opcional: Eliminar historial de ejecuciones de la extensión de Chrome
DELETE FROM public.extension_executions;

COMMIT;

-- ==============================================================================
-- OPCIÓN B: LIMPIEZA TOTAL DE CONFIGURACIÓN Y DATOS (HARD RESET DE SCRAPING)
-- Borra absolutamente todo lo relacionado con scraping, incluyendo los sitios
-- proveedores configurados y los mapeos de categorías creados.
-- ==============================================================================
-- IMPORTANTE: Ejecutar con precaución. Perderás la configuración de los sitios.
/*
BEGIN;

-- 1. Eliminar logs de sincronización
DELETE FROM public.sync_logs;

-- 2. Eliminar reportes de ejecución
DELETE FROM public.execution_reports;

-- 3. Eliminar productos en staging
DELETE FROM public.staging_products;

-- 4. Eliminar sitios configurados (proveedores)
-- Nota: Al tener restricción ON DELETE CASCADE, esto también limpiará registros dependientes si existieran.
DELETE FROM public.config_sites;

-- 5. Eliminar mapeos de categorías
DELETE FROM public.category_mapping;

COMMIT;
*/

-- ==============================================================================
-- OPCIÓN C: LIMPIEZA DE DATOS DE LA EXTENSIÓN DE CHROME
-- Borra los layouts creados por los usuarios y su historial de ejecuciones.
-- Mantiene los perfiles de usuario.
-- ==============================================================================
/*
BEGIN;

-- 1. Eliminar historial de ejecuciones de la extensión
DELETE FROM public.extension_executions;

-- 2. Eliminar layouts configurados por los usuarios
DELETE FROM public.user_layouts;

COMMIT;
*/

-- ==============================================================================
-- OPCIÓN D: PURGA ABSOLUTA (ELIMINAR TODO EN EL ESQUEMA PÚBLICO)
-- Borra todos los datos de todas las tablas de ScrapSAE en el esquema public.
-- ==============================================================================
-- NOTA: Se usa TRUNCATE con CASCADE para limpiar todas las tablas respetando las
-- claves foráneas de manera eficiente y rápida.
/*
BEGIN;

TRUNCATE TABLE 
    public.sync_logs,
    public.execution_reports,
    public.staging_products,
    public.config_sites,
    public.category_mapping,
    public.extension_executions,
    public.user_layouts,
    public.user_profiles
CASCADE;

COMMIT;
*/
