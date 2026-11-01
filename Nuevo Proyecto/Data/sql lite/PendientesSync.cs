using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Nuevo_Proyecto.Data;
using Nuevo_Proyecto.Services.Helpers;

namespace Nuevo_Proyecto.Data.SqlLite
{
    /// <summary>Operación realizada sin conexión, pendiente de enviar a SQL Server.</summary>
    public sealed class OpPendiente
    {
        public string Tipo { get; set; } = "ENTIDAD";           // ENTIDAD | SQL
        public string? Tabla { get; set; }
        public string? Operacion { get; set; }                  // INSERT | UPDATE | DELETE
        public Dictionary<string, object?>? Clave { get; set; }
        public Dictionary<string, object?>? Valores { get; set; }
        public string? Sql { get; set; }
        public List<ParametroPendiente>? Parametros { get; set; }
    }

    public sealed class ParametroPendiente
    {
        public string Nombre { get; set; } = string.Empty;
        public string? Tipo { get; set; }
        public object? Valor { get; set; }
    }

    internal sealed class ColInfo
    {
        public string Nombre = string.Empty;
        public Type Clr = typeof(object);
        public bool Computada;
    }

    internal sealed class TablaInfo
    {
        public string Esquema = "dbo";
        public string Nombre = string.Empty;
        public Dictionary<string, ColInfo> Cols = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Pk = new();
        public bool PkIdentity;
        public List<(string Col, string Tabla)> Fks = new();
        public int Profundidad;
    }

    /// <summary>
    /// Cola de cambios hechos sin conexión: se guardan en SQLite (tabla PendientesSync) y se reenvían
    /// en el mismo orden a SQL Server cuando la conexión se restablece.
    /// </summary>
    public static class PendientesSync
    {
        public const string Tabla = "PendientesSync";

        public static readonly PendientesSaveChangesInterceptor Guardado = new();
        public static readonly PendientesCommandInterceptor Comandos = new();

        private static readonly Lazy<Dictionary<string, TablaInfo>> _meta = new(CargarMeta);

        // ------------------------------------------------------------ metadatos del modelo

        private static Dictionary<string, TablaInfo> CargarMeta()
        {
            var opciones = new DbContextOptionsBuilder<Dev_ComideriaDbContext>()
                .UseSqlServer(AppConfig.ConnectionString).Options;
            using var ctx = new Dev_ComideriaDbContext(opciones);
            var modelo = ctx.GetService<IDesignTimeModel>().Model;

            var res = new Dictionary<string, TablaInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var et in modelo.GetEntityTypes())
            {
                var nombre = et.GetTableName();
                var pk = et.FindPrimaryKey();
                if (nombre == null || pk == null) continue;

                var id = StoreObjectIdentifier.Table(nombre, et.GetSchema());
                var info = new TablaInfo { Nombre = nombre, Esquema = et.GetSchema() ?? "dbo" };

                foreach (var p in et.GetProperties())
                {
                    var col = p.GetColumnName(id);
                    if (col == null) continue;
                    bool computada = p.GetComputedColumnSql(id) != null
                                     || p.ValueGenerated == ValueGenerated.OnAddOrUpdate
                                     || p.ValueGenerated == ValueGenerated.OnUpdate;
                    info.Cols[col] = new ColInfo { Nombre = col, Clr = p.ClrType, Computada = computada };
                }

                info.Pk = pk.Properties.Select(p => p.GetColumnName(id)!).ToList();
                if (pk.Properties.Count == 1)
                {
                    var p0 = pk.Properties[0];
                    var t = Nullable.GetUnderlyingType(p0.ClrType) ?? p0.ClrType;
                    info.PkIdentity = p0.ValueGenerated == ValueGenerated.OnAdd && (t == typeof(int) || t == typeof(long));
                }

                foreach (var fk in et.GetForeignKeys())
                {
                    if (fk.Properties.Count != 1) continue;
                    var principal = fk.PrincipalEntityType.GetTableName();
                    var col = fk.Properties[0].GetColumnName(id);
                    if (principal != null && col != null && principal != nombre)
                        info.Fks.Add((col, principal));
                }

                res[nombre] = info;
            }

            foreach (var t in res.Values)
                t.Profundidad = Profundidad(t, res, new HashSet<string>());

            return res;
        }

        private static int Profundidad(TablaInfo t, Dictionary<string, TablaInfo> todas, HashSet<string> visitadas)
        {
            if (!visitadas.Add(t.Nombre)) return 0;
            int max = 0;
            foreach (var fk in t.Fks)
                if (todas.TryGetValue(fk.Tabla, out var p))
                    max = Math.Max(max, Profundidad(p, todas, visitadas) + 1);
            visitadas.Remove(t.Nombre);
            return max;
        }

        internal static TablaInfo? Info(string? tabla) =>
            tabla != null && _meta.Value.TryGetValue(tabla, out var i) ? i : null;

        // ------------------------------------------------------------ cola en SQLite

        public static void AsegurarTabla()
        {
            using var c = new SqliteConnection(CacheLocal.CadenaConexion);
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = $"CREATE TABLE IF NOT EXISTS \"{Tabla}\" (Id INTEGER PRIMARY KEY AUTOINCREMENT, Datos TEXT NOT NULL)";
            cmd.ExecuteNonQuery();
        }

        public static bool HayPendientes()
        {
            AsegurarTabla();
            using var c = new SqliteConnection(CacheLocal.CadenaConexion);
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(1) FROM \"{Tabla}\"";
            return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
        }

        /// <summary>Guarda la operación usando la misma conexión/transacción de EF (si hay rollback, se descarta).</summary>
        internal static void Encolar(DbConnection con, DbTransaction? tx, OpPendiente op)
        {
            bool cerrar = con.State != ConnectionState.Open;
            if (cerrar) con.Open();
            try
            {
                using var cmd = con.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = $"INSERT INTO \"{Tabla}\" (Datos) VALUES ($d)";
                var p = cmd.CreateParameter();
                p.ParameterName = "$d";
                p.Value = JsonSerializer.Serialize(op);
                cmd.Parameters.Add(p);
                cmd.ExecuteNonQuery();
            }
            finally
            {
                if (cerrar) con.Close();
            }
        }

        internal static Dictionary<string, object?> Extraer(EntityEntry en, TablaInfo info, bool soloModificadas)
        {
            var d = new Dictionary<string, object?>();
            foreach (var p in en.Properties)
            {
                var col = p.Metadata.GetColumnName();
                if (col == null || !info.Cols.TryGetValue(col, out var ci) || ci.Computada) continue;
                if (soloModificadas && !p.IsModified) continue;
                d[ci.Nombre] = p.CurrentValue;
            }
            return d;
        }

        internal static Dictionary<string, object?> ExtraerClave(EntityEntry en)
        {
            var d = new Dictionary<string, object?>();
            foreach (var pk in en.Metadata.FindPrimaryKey()!.Properties)
                d[pk.GetColumnName() ?? pk.Name] = en.Property(pk.Name).CurrentValue;
            return d;
        }

        // ------------------------------------------------------------ reenvío a SQL Server

        /// <returns>false si la conexión se cayó a mitad del envío (quedan pendientes).</returns>
        public static bool Reenviar(string cadenaSqlServer, out int enviados, out int fallidos)
        {
            enviados = 0;
            fallidos = 0;
            AsegurarTabla();

            var lista = new List<(long Id, string Datos)>();
            using (var c = new SqliteConnection(CacheLocal.CadenaConexion))
            {
                c.Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = $"SELECT Id, Datos FROM \"{Tabla}\" ORDER BY Id";
                using var r = cmd.ExecuteReader();
                while (r.Read()) lista.Add((r.GetInt64(0), r.GetString(1)));
            }
            if (lista.Count == 0) return true;

            using var origen = new SqlConnection(cadenaSqlServer);
            origen.Open();
            var mapa = new Dictionary<(string, long), long>();

            foreach (var (id, datos) in lista)
            {
                try
                {
                    var op = JsonSerializer.Deserialize<OpPendiente>(datos)!;
                    Aplicar(origen, op, mapa);
                    enviados++;
                }
                catch (Exception ex)
                {
                    if (origen.State != ConnectionState.Open) return false;
                    fallidos++;
                    RegistrarError(datos, ex);
                }
                Borrar(id);
            }
            return true;
        }

        private static void Borrar(long id)
        {
            using var c = new SqliteConnection(CacheLocal.CadenaConexion);
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = $"DELETE FROM \"{Tabla}\" WHERE Id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }

        private static void RegistrarError(string datos, Exception ex)
        {
            try
            {
                var ruta = Path.Combine(Path.GetDirectoryName(CacheLocal.Ruta)!, "sync_errores.log");
                File.AppendAllText(ruta, $"{DateTime.Now:s} | {ex.GetBaseException().Message}{Environment.NewLine}{datos}{Environment.NewLine}{Environment.NewLine}");
            }
            catch { }
        }

        private static void Aplicar(SqlConnection con, OpPendiente op, Dictionary<(string, long), long> mapa)
        {
            if (op.Tipo == "SQL")
            {
                using var cmd = new SqlCommand(op.Sql, con);
                foreach (var p in op.Parametros ?? new())
                {
                    var tipo = (p.Tipo != null ? Type.GetType(p.Tipo) : null) ?? typeof(string);
                    cmd.Parameters.AddWithValue(p.Nombre, Convertir(p.Valor, tipo) ?? DBNull.Value);
                }
                cmd.ExecuteNonQuery();
                return;
            }

            var info = Info(op.Tabla) ?? throw new InvalidOperationException($"Tabla desconocida: {op.Tabla}");
            string T = $"[{info.Esquema}].[{info.Nombre}]";

            switch (op.Operacion)
            {
                case "INSERT": Insertar(con, info, T, ValoresRemapeados(info, op.Valores, mapa), mapa); break;
                case "UPDATE": Actualizar(con, info, T, op, mapa); break;
                case "DELETE": Eliminar(con, info, T, op, mapa); break;
            }
        }

        private static Dictionary<string, object?> ValoresRemapeados(TablaInfo info, Dictionary<string, object?>? crudos,
                                                                      Dictionary<(string, long), long> mapa)
        {
            var vals = new Dictionary<string, object?>();
            foreach (var kv in crudos ?? new())
            {
                if (!info.Cols.TryGetValue(kv.Key, out var ci) || ci.Computada) continue;
                vals[ci.Nombre] = RemapearFk(info, ci, Convertir(kv.Value, ci.Clr), mapa);
            }
            return vals;
        }

        private static void Insertar(SqlConnection con, TablaInfo info, string T, Dictionary<string, object?> vals,
                                     Dictionary<(string, long), long> mapa)
        {
            if (!info.PkIdentity)
            {
                EjecutarInsert(con, T, vals, false, false);
                return;
            }

            var pk = info.Pk[0];
            long viejo = Convert.ToInt64(vals[pk], CultureInfo.InvariantCulture);

            bool existe;
            using (var q = new SqlCommand($"SELECT COUNT(1) FROM {T} WHERE [{pk}] = @id", con))
            {
                q.Parameters.AddWithValue("@id", vals[pk]!);
                existe = Convert.ToInt32(q.ExecuteScalar()) > 0;
            }

            // Se conserva el mismo Id (así los SQL directos que lo referencian siguen siendo válidos)
            if (!existe)
            {
                try
                {
                    EjecutarInsert(con, T, vals, true, false);
                    return;
                }
                catch (SqlException) when (con.State == ConnectionState.Open) { }
            }

            // Id ocupado en el servidor (o sin permiso IDENTITY_INSERT): se asigna uno nuevo y se remapea
            var sinId = vals.Where(k => !string.Equals(k.Key, pk, StringComparison.OrdinalIgnoreCase))
                            .ToDictionary(k => k.Key, k => k.Value);
            var nuevo = EjecutarInsert(con, T, sinId, false, true);
            if (nuevo.HasValue && nuevo.Value != viejo)
                mapa[(info.Nombre, viejo)] = nuevo.Value;
        }

        private static long? EjecutarInsert(SqlConnection con, string T, Dictionary<string, object?> vals,
                                            bool identityOn, bool devolverId)
        {
            var cols = vals.Keys.ToList();
            var sql = (identityOn ? $"SET IDENTITY_INSERT {T} ON; " : "") +
                      $"INSERT INTO {T} ({string.Join(", ", cols.Select(c => $"[{c}]"))}) " +
                      $"VALUES ({string.Join(", ", cols.Select((_, i) => "@p" + i))});" +
                      (identityOn ? $" SET IDENTITY_INSERT {T} OFF;" : "") +
                      (devolverId ? " SELECT CAST(SCOPE_IDENTITY() AS bigint);" : "");

            using var cmd = new SqlCommand(sql, con);
            for (int i = 0; i < cols.Count; i++)
                cmd.Parameters.AddWithValue("@p" + i, vals[cols[i]] ?? DBNull.Value);

            if (!devolverId) { cmd.ExecuteNonQuery(); return null; }
            var r = cmd.ExecuteScalar();
            return r == null || r is DBNull ? null : Convert.ToInt64(r);
        }

        private static void Actualizar(SqlConnection con, TablaInfo info, string T, OpPendiente op,
                                       Dictionary<(string, long), long> mapa)
        {
            var vals = ValoresRemapeados(info, op.Valores, mapa)
                .Where(k => !info.Pk.Contains(k.Key, StringComparer.OrdinalIgnoreCase)).ToList();
            if (vals.Count == 0) return;

            var clave = ClaveRemapeada(info, op.Clave, mapa);
            using var cmd = new SqlCommand(
                $"UPDATE {T} SET {string.Join(", ", vals.Select((v, i) => $"[{v.Key}] = @v{i}"))} " +
                $"WHERE {string.Join(" AND ", clave.Select((k, i) => $"[{k.Key}] = @k{i}"))}", con);
            for (int i = 0; i < vals.Count; i++) cmd.Parameters.AddWithValue("@v" + i, vals[i].Value ?? DBNull.Value);
            for (int i = 0; i < clave.Count; i++) cmd.Parameters.AddWithValue("@k" + i, clave[i].Value ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }

        private static void Eliminar(SqlConnection con, TablaInfo info, string T, OpPendiente op,
                                     Dictionary<(string, long), long> mapa)
        {
            var clave = ClaveRemapeada(info, op.Clave, mapa);
            using var cmd = new SqlCommand(
                $"DELETE FROM {T} WHERE {string.Join(" AND ", clave.Select((k, i) => $"[{k.Key}] = @k{i}"))}", con);
            for (int i = 0; i < clave.Count; i++) cmd.Parameters.AddWithValue("@k" + i, clave[i].Value ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }

        private static List<KeyValuePair<string, object?>> ClaveRemapeada(TablaInfo info, Dictionary<string, object?>? clave,
                                                                          Dictionary<(string, long), long> mapa)
        {
            var res = new List<KeyValuePair<string, object?>>();
            foreach (var kv in clave ?? new())
            {
                if (!info.Cols.TryGetValue(kv.Key, out var ci)) continue;
                var v = Convertir(kv.Value, ci.Clr);
                if (v != null && info.Pk.Count == 1 && EsEntero(v) &&
                    mapa.TryGetValue((info.Nombre, Convert.ToInt64(v, CultureInfo.InvariantCulture)), out var nuevo))
                    v = Convert.ChangeType(nuevo, Nullable.GetUnderlyingType(ci.Clr) ?? ci.Clr, CultureInfo.InvariantCulture);
                res.Add(new(ci.Nombre, v));
            }
            return res;
        }

        private static object? RemapearFk(TablaInfo info, ColInfo ci, object? v, Dictionary<(string, long), long> mapa)
        {
            if (v == null || !EsEntero(v)) return v;
            foreach (var fk in info.Fks)
            {
                if (!string.Equals(fk.Col, ci.Nombre, StringComparison.OrdinalIgnoreCase)) continue;
                if (mapa.TryGetValue((fk.Tabla, Convert.ToInt64(v, CultureInfo.InvariantCulture)), out var nuevo))
                    return Convert.ChangeType(nuevo, Nullable.GetUnderlyingType(ci.Clr) ?? ci.Clr, CultureInfo.InvariantCulture);
            }
            return v;
        }

        private static bool EsEntero(object v) => v is int or long or short or byte;

        private static object? Convertir(object? v, Type clr)
        {
            if (v == null) return null;
            var t = Nullable.GetUnderlyingType(clr) ?? clr;
            if (v is JsonElement el)
            {
                if (el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
                return el.Deserialize(t);
            }
            return Convert.ChangeType(v, t, CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Registra los cambios de entidades (SaveChanges) hechos sobre SQLite.</summary>
    public sealed class PendientesSaveChangesInterceptor : SaveChangesInterceptor
    {
        private sealed class Snap
        {
            public EntityEntry Entry = null!;
            public TablaInfo Info = null!;
            public EntityState Estado;
            public Dictionary<string, object?> Clave = new();
            public Dictionary<string, object?> Valores = new();
        }

        private readonly ConditionalWeakTable<DbContext, List<Snap>> _snaps = new();

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            Antes(eventData.Context);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Antes(eventData.Context);
            return new ValueTask<InterceptionResult<int>>(result);
        }

        public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
        {
            Despues(eventData.Context);
            return result;
        }

        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            Despues(eventData.Context);
            return new ValueTask<int>(result);
        }

        public override void SaveChangesFailed(DbContextErrorEventData eventData)
        {
            Descartar(eventData.Context);
        }

        public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            Descartar(eventData.Context);
            return Task.CompletedTask;
        }

        private void Descartar(DbContext? ctx)
        {
            PendientesCommandInterceptor.EnGuardado = false;
            if (ctx != null) _snaps.Remove(ctx);
        }

        private void Antes(DbContext? ctx)
        {
            if (ctx == null) return;
            PendientesCommandInterceptor.EnGuardado = true;
            ctx.ChangeTracker.DetectChanges();

            var lista = new List<Snap>();
            foreach (var en in ctx.ChangeTracker.Entries())
            {
                if (en.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;
                var info = PendientesSync.Info(en.Metadata.GetTableName());
                if (info == null || en.Metadata.FindPrimaryKey() == null) continue;

                var s = new Snap { Entry = en, Info = info, Estado = en.State };
                if (s.Estado == EntityState.Modified)
                {
                    s.Clave = PendientesSync.ExtraerClave(en);
                    s.Valores = PendientesSync.Extraer(en, info, true);
                }
                else if (s.Estado == EntityState.Deleted)
                {
                    s.Clave = PendientesSync.ExtraerClave(en);
                }
                lista.Add(s);
            }

            _snaps.Remove(ctx);
            _snaps.Add(ctx, lista);
        }

        private void Despues(DbContext? ctx)
        {
            PendientesCommandInterceptor.EnGuardado = false;
            if (ctx == null || !_snaps.TryGetValue(ctx, out var lista)) return;
            _snaps.Remove(ctx);

            foreach (var s in lista.Where(s => s.Estado == EntityState.Added))
                s.Valores = PendientesSync.Extraer(s.Entry, s.Info, false);

            // Padres antes que hijos al insertar/modificar; al borrar, al revés
            var ordenadas = lista.Where(s => s.Estado == EntityState.Added).OrderBy(s => s.Info.Profundidad)
                .Concat(lista.Where(s => s.Estado == EntityState.Modified).OrderBy(s => s.Info.Profundidad))
                .Concat(lista.Where(s => s.Estado == EntityState.Deleted).OrderByDescending(s => s.Info.Profundidad));

            var con = ctx.Database.GetDbConnection();
            var tx = ctx.Database.CurrentTransaction?.GetDbTransaction();

            foreach (var s in ordenadas)
            {
                PendientesSync.Encolar(con, tx, new OpPendiente
                {
                    Tipo = "ENTIDAD",
                    Tabla = s.Info.Nombre,
                    Operacion = s.Estado switch
                    {
                        EntityState.Added => "INSERT",
                        EntityState.Modified => "UPDATE",
                        _ => "DELETE"
                    },
                    Clave = s.Estado == EntityState.Added ? null : s.Clave,
                    Valores = s.Estado == EntityState.Deleted ? null : s.Valores
                });
            }
        }
    }

    /// <summary>
    /// Registra el SQL directo (INSERT/UPDATE/DELETE) ejecutado sobre SQLite fuera de SaveChanges,
    /// y los procedimientos almacenados (EXEC), que SQLite no puede ejecutar: se encolan y no se ejecutan.
    /// </summary>
    public sealed class PendientesCommandInterceptor : DbCommandInterceptor
    {
        [ThreadStatic] public static bool EnGuardado;

        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result)
        {
            if (EsExec(command))
            {
                Registrar(command);
                return InterceptionResult<int>.SuppressWithResult(1);
            }
            return result;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (EsExec(command))
            {
                Registrar(command);
                return new ValueTask<InterceptionResult<int>>(InterceptionResult<int>.SuppressWithResult(1));
            }
            return new ValueTask<InterceptionResult<int>>(result);
        }

        public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
        {
            if (!EnGuardado && !EsExec(command)) Registrar(command);
            return result;
        }

        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (!EnGuardado && !EsExec(command)) Registrar(command);
            return new ValueTask<int>(result);
        }

        private static bool EsExec(DbCommand c) =>
            c.CommandText.TrimStart().StartsWith("EXEC", StringComparison.OrdinalIgnoreCase);

        private static void Registrar(DbCommand command)
        {
            var sql = command.CommandText.TrimStart();
            bool valido = EsExec(command)
                          || sql.StartsWith("INSERT", StringComparison.OrdinalIgnoreCase)
                          || sql.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
                          || sql.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase);
            if (!valido || sql.Contains("UltimoAcceso", StringComparison.OrdinalIgnoreCase)) return;

            var ps = new List<ParametroPendiente>();
            foreach (DbParameter p in command.Parameters)
            {
                object? v = p.Value is DBNull ? null : p.Value;
                ps.Add(new ParametroPendiente { Nombre = p.ParameterName, Tipo = v?.GetType().FullName, Valor = v });
            }

            PendientesSync.Encolar(command.Connection!, command.Transaction,
                new OpPendiente { Tipo = "SQL", Sql = command.CommandText, Parametros = ps });
        }
    }
}

