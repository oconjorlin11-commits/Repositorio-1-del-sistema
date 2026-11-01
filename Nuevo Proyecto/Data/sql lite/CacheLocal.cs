using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Nuevo_Proyecto.Data;
using Nuevo_Proyecto.Services.Helpers;

namespace Nuevo_Proyecto.Data.SqlLite
{
    /// <summary>
    /// Copia local (SQLite) de SQL Server para poder operar si se cae la conexión.
    /// - Tablas del modelo EF: copia completa.
    /// - Usuarios: solo administradores + el último usuario no administrador que inició sesión.
    /// </summary>
    public static class CacheLocal
    {
        private const string TablaMeta = "CacheMeta";
        private const string ClaveUltimoNoAdmin = "UltimoNoAdmin";
        private static readonly object _lock = new();

        public static string Ruta { get; }
        public static string CadenaConexion => $"Data Source={Ruta}";

        static CacheLocal()
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "Data", "sql lite");
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            {
                var candidato = Path.Combine(d.FullName, "Data", "sql lite");
                if (Directory.Exists(candidato) && File.Exists(Path.Combine(d.FullName, "Nuevo Proyecto.csproj")))
                {
                    dir = candidato;
                    break;
                }
            }
            Directory.CreateDirectory(dir);
            Ruta = Path.Combine(dir, "cache_local.db");
        }

        private sealed record Columna(string Nombre, string TipoSqlite, bool EsPkEntera, bool DefaultFecha);
        private sealed record TablaModelo(string Esquema, string Nombre, List<Columna> Columnas, List<string> Pk);

        private static readonly Lazy<List<TablaModelo>> _tablas = new(CargarTablasModelo);

        private static List<TablaModelo> CargarTablasModelo()
        {
            var opciones = new DbContextOptionsBuilder<Dev_ComideriaDbContext>()
                .UseSqlServer(AppConfig.ConnectionString).Options;
            using var ctx = new Dev_ComideriaDbContext(opciones);
            var modelo = ctx.GetService<IDesignTimeModel>().Model;

            var lista = new List<TablaModelo>();
            foreach (var et in modelo.GetEntityTypes())
            {
                var nombre = et.GetTableName();
                var pk = et.FindPrimaryKey();
                if (nombre == null || pk == null || nombre == "TriggerLogStockCritico") continue;

                var esquema = et.GetSchema() ?? "dbo";
                var id = StoreObjectIdentifier.Table(nombre, et.GetSchema());
                var pkProps = pk.Properties;
                bool pkSimpleEntera = pkProps.Count == 1 &&
                    (Nullable.GetUnderlyingType(pkProps[0].ClrType) ?? pkProps[0].ClrType) is var t && (t == typeof(int) || t == typeof(long));

                var columnas = new List<Columna>();
                foreach (var p in et.GetProperties())
                {
                    var col = p.GetColumnName(id);
                    if (col == null) continue;
                    var clr = Nullable.GetUnderlyingType(p.ClrType) ?? p.ClrType;
                    string tipo = (clr == typeof(int) || clr == typeof(long) || clr == typeof(short) || clr == typeof(byte) || clr == typeof(bool))
                        ? "INTEGER" : clr == typeof(byte[]) ? "BLOB" : "TEXT";
                    bool esPk = pkSimpleEntera && p == pkProps[0];
                    bool defFecha = clr == typeof(DateTime) && p.GetDefaultValueSql(id) != null;
                    columnas.Add(new Columna(col, tipo, esPk, defFecha));
                }

                var pkCols = pkProps.Select(p => p.GetColumnName(id)!).ToList();
                lista.Add(new TablaModelo(esquema, nombre, columnas, pkSimpleEntera ? new List<string>() : pkCols));
            }
            return lista;
        }

        private static SqliteConnection Abrir()
        {
            var c = new SqliteConnection(CadenaConexion);
            c.Open();
            return c;
        }

        private static void Ejecutar(SqliteConnection c, SqliteTransaction? tx, string sql)
        {
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        private static void AsegurarMeta(SqliteConnection c, SqliteTransaction? tx) =>
            Ejecutar(c, tx, $"CREATE TABLE IF NOT EXISTS \"{TablaMeta}\" (Clave TEXT PRIMARY KEY, Valor TEXT)");

        // ------------------------------------------------------------ último usuario no administrador

        public static void GuardarUltimoNoAdmin(string usuario)
        {
            lock (_lock)
            {
                using var c = Abrir();
                AsegurarMeta(c, null);
                using var cmd = c.CreateCommand();
                cmd.CommandText = $"INSERT OR REPLACE INTO \"{TablaMeta}\" (Clave, Valor) VALUES ($k, $v)";
                cmd.Parameters.AddWithValue("$k", ClaveUltimoNoAdmin);
                cmd.Parameters.AddWithValue("$v", usuario);
                cmd.ExecuteNonQuery();
            }
        }

        public static string? ObtenerUltimoNoAdmin()
        {
            lock (_lock)
            {
                using var c = Abrir();
                AsegurarMeta(c, null);
                using var cmd = c.CreateCommand();
                cmd.CommandText = $"SELECT Valor FROM \"{TablaMeta}\" WHERE Clave = $k";
                cmd.Parameters.AddWithValue("$k", ClaveUltimoNoAdmin);
                return cmd.ExecuteScalar() as string;
            }
        }

        // ------------------------------------------------------------ borrado al recuperar conexión

        /// <summary>Borra todos los datos cacheados (conserva solo la tabla de metadatos).</summary>
        public static void Limpiar()
        {
            lock (_lock)
            {
                using var c = Abrir();
                var nombres = new List<string>();
                using (var cmd = c.CreateCommand())
                {
                    cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'";
                    using var r = cmd.ExecuteReader();
                    while (r.Read()) nombres.Add(r.GetString(0));
                }

                using var tx = c.BeginTransaction();
                foreach (var n in nombres.Where(n => n != TablaMeta && n != PendientesSync.Tabla))
                    Ejecutar(c, tx, $"DROP TABLE IF EXISTS \"{n}\"");
                tx.Commit();
            }
        }

        // ------------------------------------------------------------ sincronización SQL Server -> SQLite

        public static void Sincronizar(string cadenaSqlServer)
        {
            lock (_lock)
            {
                using var origen = new SqlConnection(cadenaSqlServer);
                origen.Open();
                using var destino = Abrir();
                using var tx = destino.BeginTransaction();

                AsegurarMeta(destino, tx);

                foreach (var t in _tablas.Value)
                    CopiarTabla(origen, destino, tx, t);

                CopiarUsuarios(origen, destino, tx);

                tx.Commit();
            }
        }

        private static string Q(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";

        private static void CopiarTabla(SqlConnection origen, SqliteConnection destino, SqliteTransaction tx, TablaModelo t)
        {
            var cols = string.Join(", ", t.Columnas.Select(c => $"[{c.Nombre}]"));
            using var sel = new SqlCommand($"SELECT {cols} FROM [{t.Esquema}].[{t.Nombre}]", origen);

            SqlDataReader reader;
            try { reader = sel.ExecuteReader(); }
            catch (SqlException ex) when (ex.Number is 207 or 208) { return; } // tabla/columna inexistente: se omite

            using (reader)
            {
                var defs = t.Columnas.Select(c =>
                    $"{Q(c.Nombre)} {c.TipoSqlite}" +
                    (c.EsPkEntera ? " PRIMARY KEY" : "") +
                    (c.DefaultFecha ? " DEFAULT CURRENT_TIMESTAMP" : "")).ToList();
                if (t.Pk.Count > 0)
                    defs.Add($"PRIMARY KEY ({string.Join(", ", t.Pk.Select(Q))})");

                Ejecutar(destino, tx, $"DROP TABLE IF EXISTS {Q(t.Nombre)}");
                Ejecutar(destino, tx, $"CREATE TABLE {Q(t.Nombre)} ({string.Join(", ", defs)})");
                Insertar(reader, destino, tx, t.Nombre, t.Columnas.Select(c => c.Nombre).ToList());
            }
        }

        private static void CopiarUsuarios(SqlConnection origen, SqliteConnection destino, SqliteTransaction tx)
        {
            // Todos los administradores + el último usuario no administrador que inició sesión
            using var sel = new SqlCommand(@"
                SELECT u.*
                FROM Usuarios u
                INNER JOIN Empleados e ON u.EmpleadoID = e.EmpleadoID
                WHERE u.RolSistema LIKE '%admin%'
                   OR e.Cargo LIKE '%admin%'
                   OR (@ultimo IS NOT NULL AND u.NombreUsuario = @ultimo)", origen);

            var ultimo = LeerUltimoNoAdminEn(destino, tx);
            sel.Parameters.AddWithValue("@ultimo", (object?)ultimo ?? DBNull.Value);

            SqlDataReader reader;
            try { reader = sel.ExecuteReader(); }
            catch (SqlException ex) when (ex.Number is 207 or 208) { return; }

            using (reader)
            {
                var nombres = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
                Ejecutar(destino, tx, "DROP TABLE IF EXISTS \"Usuarios\"");
                Ejecutar(destino, tx, $"CREATE TABLE \"Usuarios\" ({string.Join(", ", nombres.Select(Q))})");
                Insertar(reader, destino, tx, "Usuarios", nombres);
            }
        }

        private static string? LeerUltimoNoAdminEn(SqliteConnection c, SqliteTransaction tx)
        {
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"SELECT Valor FROM \"{TablaMeta}\" WHERE Clave = $k";
            cmd.Parameters.AddWithValue("$k", ClaveUltimoNoAdmin);
            return cmd.ExecuteScalar() as string;
        }

        private static void Insertar(SqlDataReader reader, SqliteConnection destino, SqliteTransaction tx,
                                     string tabla, List<string> columnas)
        {
            using var ins = destino.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = $"INSERT INTO {Q(tabla)} ({string.Join(", ", columnas.Select(Q))}) " +
                              $"VALUES ({string.Join(", ", columnas.Select((_, i) => "$p" + i))})";

            var ps = new SqliteParameter[columnas.Count];
            for (int i = 0; i < ps.Length; i++)
            {
                ps[i] = new SqliteParameter("$p" + i, DBNull.Value);
                ins.Parameters.Add(ps[i]);
            }

            while (reader.Read())
            {
                for (int i = 0; i < ps.Length; i++)
                    ps[i].Value = reader.IsDBNull(i) ? DBNull.Value : reader.GetValue(i);
                ins.ExecuteNonQuery();
            }
        }
    }
}

