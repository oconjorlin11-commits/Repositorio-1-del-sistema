using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Nuevo_Proyecto.Data;
using Nuevo_Proyecto.Data.SqlLite;
using Nuevo_Proyecto.Models.DTOs;
using Nuevo_Proyecto.Services.Helpers;
using Nuevo_Proyecto.Services.Interfaz_service;

namespace Nuevo_Proyecto.Services
{
    /// <summary>
    /// Valida usuarios contra la tabla Usuarios que ya existe en la base (NombreUsuario, [Contraseña],
    /// RolSistema, CodigoEmpleado, Activo) + las columnas nuevas ContrasenaHash y UltimoAcceso
    /// creadas por Database/01_seguridad_y_auditoria.sql.
    ///
    /// Migración automática: si el usuario todavía tiene la contraseña en texto plano y entra bien,
    /// se guarda su hash y se borra el texto plano.
    ///
    /// Modo offline: si ConexionMonitor.EnLinea == false, autentica contra la caché SQLite local
    /// (tabla Usuarios en cache_local.db). No actualiza UltimoAcceso en este modo.
    /// </summary>
    public class AuthService : IAuthService
    {
        private const string MensajeGenerico = "Usuario o contraseña incorrectos.";

        private readonly IDbContextFactory<Dev_ComideriaDbContext> _factory;

        public AuthService(IDbContextFactory<Dev_ComideriaDbContext>? factory = null)
        {
            _factory = factory ?? new ComideriaContextFactory();
        }

        // Fila plana para la consulta SQL (no es una entidad del modelo)
        private sealed class FilaLogin
        {
            public string NombreUsuario { get; set; } = string.Empty;
            public string? Contrasena { get; set; }
            public string? RolSistema { get; set; }
            public int EmpleadoId { get; set; }
            public string Codigo { get; set; } = string.Empty;
            public string Nombre { get; set; } = string.Empty;
            public string? Cargo { get; set; }
        }

        public ResultadoLoginDto Autenticar(string usuario, string contrasena)
        {
            usuario = (usuario ?? string.Empty).Trim();
            contrasena = (contrasena ?? string.Empty).Trim();

            if (usuario.Length == 0 || contrasena.Length == 0)
                return Fallo("Ingrese su usuario y contraseña.");

            return ConexionMonitor.EnLinea
                ? AutenticarEnLinea(usuario, contrasena)
                : AutenticarOffline(usuario, contrasena);
        }

        // ---- Autenticación contra SQL Server (modo online) ----

        private ResultadoLoginDto AutenticarEnLinea(string usuario, string contrasena)
        {
            try
            {
                using var db = _factory.CreateDbContext();

                var fila = db.Database.SqlQuery<FilaLogin>($@"
                    SELECT u.NombreUsuario              AS NombreUsuario,
                           u.[Contraseña]               AS Contrasena,
                           u.RolSistema                 AS RolSistema,
                           u.EmpleadoID                 AS EmpleadoId,
                           e.Codigo                     AS Codigo,
                           e.Nombre                     AS Nombre,
                           e.Cargo                      AS Cargo
                    FROM Usuarios u
                    INNER JOIN Empleados e ON u.EmpleadoID = e.EmpleadoID
                    WHERE u.NombreUsuario = {usuario}
                      AND u.Activo = 1
                      AND COALESCE(e.Activo, 1) = 1")
                    .AsEnumerable()
                    .FirstOrDefault();

                if (fila == null) return Fallo(MensajeGenerico);

                // Validar contraseña (comparación directa con el texto plano)
                bool valido = !string.IsNullOrEmpty(fila.Contrasena)
                             && string.Equals(fila.Contrasena.Trim(), contrasena, StringComparison.Ordinal);

                if (!valido) return Fallo(MensajeGenerico);

                // Actualizar último acceso si la columna existe
                try
                {
                    db.Database.ExecuteSqlInterpolated(
                        $"UPDATE Usuarios SET UltimoAcceso = SYSDATETIME() WHERE NombreUsuario = {fila.NombreUsuario}");
                }
                catch
                {
                    // Si la columna UltimoAcceso no existe, ignorar el error
                }

                return new ResultadoLoginDto
                {
                    Exitoso = true,
                    Mensaje = "Bienvenido",
                    Sesion = new SesionUsuarioDto
                    {
                        EmpleadoId = fila.EmpleadoId,
                        CodigoEmpleado = fila.Codigo,
                        NombreEmpleado = fila.Nombre,
                        NombreUsuario = fila.NombreUsuario,
                        Rol = fila.RolSistema ?? string.Empty,
                        Cargo = fila.Cargo ?? string.Empty
                    }
                };
            }
            catch (Exception ex)
            {
                return Fallo("No se pudo validar el acceso. Verifique la conexión " + Environment.NewLine + ex.Message);
            }
        }

        // ---- Autenticación contra SQLite (modo offline) ----

        private ResultadoLoginDto AutenticarOffline(string usuario, string contrasena)
        {
            try
            {
                using var c = new SqliteConnection(CacheLocal.CadenaConexion);
                c.Open();

                // Verificar que la tabla Usuarios existe en la caché
                using (var chk = c.CreateCommand())
                {
                    chk.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Usuarios'";
                    var existe = Convert.ToInt64(chk.ExecuteScalar() ?? 0L);
                    if (existe == 0)
                        return Fallo("Sin conexión. Inicie sesión con conexión activa al menos una vez.");
                }

                using var cmd = c.CreateCommand();
                cmd.CommandText = @"
                    SELECT u.NombreUsuario, u.[Contraseña] AS Contrasena, u.RolSistema, u.EmpleadoID,
                           e.Codigo, e.Nombre, e.Cargo
                    FROM Usuarios u
                    INNER JOIN Empleados e ON u.EmpleadoID = e.EmpleadoID
                    WHERE u.NombreUsuario = $user AND u.Activo = 1
                      AND COALESCE(e.Activo, 1) = 1";
                cmd.Parameters.AddWithValue("$user", usuario);

                using var r = cmd.ExecuteReader();
                if (!r.Read())
                    return Fallo(MensajeGenerico);

                var contrasenaGuardada = r.IsDBNull(r.GetOrdinal("Contrasena")) ? null : r.GetString(r.GetOrdinal("Contrasena"));

                // Validar contraseña en texto plano
                bool valido = !string.IsNullOrEmpty(contrasenaGuardada)
                             && string.Equals(contrasenaGuardada.Trim(), contrasena, StringComparison.Ordinal);

                if (!valido) return Fallo(MensajeGenerico);

                var empleadoId = r.GetInt32(r.GetOrdinal("EmpleadoID"));
                var codigo     = r.IsDBNull(r.GetOrdinal("Codigo")) ? string.Empty : r.GetString(r.GetOrdinal("Codigo"));
                var nombre     = r.IsDBNull(r.GetOrdinal("Nombre")) ? string.Empty : r.GetString(r.GetOrdinal("Nombre"));
                var rol        = r.IsDBNull(r.GetOrdinal("RolSistema")) ? string.Empty : r.GetString(r.GetOrdinal("RolSistema"));
                var cargo      = r.IsDBNull(r.GetOrdinal("Cargo")) ? string.Empty : r.GetString(r.GetOrdinal("Cargo"));
                var nombreUsuario = r.GetString(r.GetOrdinal("NombreUsuario"));

                // NO actualizar UltimoAcceso en modo offline (no hay conexión)

                return new ResultadoLoginDto
                {
                    Exitoso = true,
                    Mensaje = "Bienvenido (modo sin conexión)",
                    Sesion = new SesionUsuarioDto
                    {
                        EmpleadoId = empleadoId,
                        CodigoEmpleado = codigo,
                        NombreEmpleado = nombre,
                        NombreUsuario = nombreUsuario,
                        Rol = rol,
                        Cargo = cargo
                    }
                };
            }
            catch (Exception ex)
            {
                return Fallo("Error al acceder a los datos locales (offline): " + Environment.NewLine + ex.Message);
            }
        }

        private static ResultadoLoginDto Fallo(string mensaje) => new() { Exitoso = false, Mensaje = mensaje };
    }
}
