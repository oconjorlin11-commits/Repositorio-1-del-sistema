PRAGMA foreign_keys = ON;
PRAGMA journal_mode = WAL;
PRAGMA synchronous = FULL;
PRAGMA busy_timeout = 5000;

CREATE TABLE IF NOT EXISTS ColaOperaciones (
	Secuencia INTEGER PRIMARY KEY AUTOINCREMENT,
	OperacionId TEXT NOT NULL UNIQUE,
	ClaveIdempotencia TEXT NOT NULL UNIQUE,
	NombreEntidad TEXT NOT NULL,
	Accion TEXT NOT NULL,
	IdentificadorEntidad TEXT NOT NULL,
	PayloadJson TEXT NOT NULL,
	Estado TEXT NOT NULL DEFAULT 'PENDIENTE',
	Intentos INTEGER NOT NULL DEFAULT 0,
	CreadaUtc TEXT NOT NULL,
	UltimoIntentoUtc TEXT,
	ConfirmadaUtc TEXT,
	ProximoIntentoUtc TEXT,
	UltimoError TEXT,
	DependenciaSecuencia INTEGER,
	FOREIGN KEY (DependenciaSecuencia) REFERENCES ColaOperaciones(Secuencia)
);

CREATE INDEX IF NOT EXISTS IX_ColaOperaciones_Estado_ProximoIntentoUtc
	ON ColaOperaciones (Estado, ProximoIntentoUtc);

CREATE TABLE IF NOT EXISTS MapeoClavesSincronizacion (
	NombreEntidad TEXT NOT NULL,
	ClaveLocal TEXT NOT NULL,
	ClaveServidor TEXT,
	OperacionId TEXT NOT NULL UNIQUE,
	Estado TEXT NOT NULL DEFAULT 'PENDIENTE',
	CreadaUtc TEXT NOT NULL,
	ConfirmadaUtc TEXT,
	PRIMARY KEY (NombreEntidad, ClaveLocal),
	FOREIGN KEY (OperacionId) REFERENCES ColaOperaciones(OperacionId) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS ConfiguracionSincronizacion (
	Clave TEXT PRIMARY KEY,
	Valor TEXT NOT NULL,
	ActualizadaUtc TEXT NOT NULL
);
