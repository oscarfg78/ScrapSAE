# Hallazgos visuales de arquitectura

El diagrama del flujo actual confirma tres cortes de control: el descubrimiento publica estado global que puede activar un retorno temprano; el orquestador se detiene en el primer resultado no vacío; y la demo solo obtiene sus datos después de persistir y consultar staging. La rama de post-análisis vuelve además a mutar el perfil temporal, de modo que la configuración probada no permanece inmutable.

El diagrama objetivo elimina esos cortes ocultos: el wizard produce una configuración versionada, un `ExecutionPlanner` decide contributors explícitos, todos emiten observaciones sin persistir, la reconciliación ocurre una sola vez y el reporte autocontenido alimenta tanto el preview como la evidencia de paridad. La persistencia queda tras un gate de calidad y una promoción explícita; en modo demo se prohíbe.
