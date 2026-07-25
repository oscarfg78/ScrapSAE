# Especificaciones de Funcionalidad

1. **AI Dual Extraction**: El prompt de la IA se actualiza para pedir {"css": "...", "xpath": "..."} en cada campo.
2. **Estrategia Resiliente**: GetSelector() evalúa si el string es un JSON parseable a DualSelector (o evalúa ambos). Si es un string simple mantiene retrocompatibilidad.
3. **Wizard Demo Mode**:
   - ProviderWizardViewModel.ExecuteRunTestScrapeAsync configurará MaxProductsPerScrape = 5 temporalmente (antes era 2).
   - Se extraerá un listado base.
   - En la interfaz ProviderWizardView.xaml se añadirá un indicador "Demo Mode" cuando se visualiza la tabla final del Test.
