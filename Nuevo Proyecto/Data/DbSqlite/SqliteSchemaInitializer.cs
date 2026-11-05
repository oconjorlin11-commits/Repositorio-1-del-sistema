using Microsoft.Data.Sqlite;

namespace Nuevo_Proyecto.Data.DbSqlite
{
    /// <summary>
    /// Crea (si no existe) la base SQLite persistente propia del sistema y aplica los PRAGMA de robustez.
    /// Es independiente de CacheLocal (espejo de SQL Server), que se limpia al sincronizar.
    /// </summary>
    public static class SqliteSchemaInitializer
    {
        public static string Ruta { get; } = Path.Combine(AppContext.BaseDirectory, "Data", "DbSqlite", "persistente.db");

        public static string CadenaConexion => $"Data Source={Ruta}";

        private const string Pragmas = @"
PRAGMA foreign_keys = ON;
PRAGMA journal_mode = WAL;
PRAGMA synchronous = FULL;
PRAGMA busy_timeout = 5000;";

        private const string Esquema = @"
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
);";

        /// <summary>Crea la carpeta, la base y las tablas. Seguro de llamar varias veces.</summary>
        public static void Inicializar()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Ruta)!);

            using var con = AbrirConexion();
            using var cmd = con.CreateCommand();
            cmd.CommandText = Esquema;
            cmd.ExecuteNonQuery();
        }

        /// <summary>Abre una conexión con los PRAGMA aplicados (foreign_keys y busy_timeout son por conexión).</summary>
        public static SqliteConnection AbrirConexion()
        {
            var con = new SqliteConnection(CadenaConexion);
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = Pragmas;
            cmd.ExecuteNonQuery();
            return con;
        }
    }
}
