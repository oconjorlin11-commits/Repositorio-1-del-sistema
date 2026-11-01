using Microsoft.Data.SqlClient;
using Nuevo_Proyecto.Services.Helpers;

namespace Nuevo_Proyecto.Data.SqlLite
{
    /// <summary>
    /// Cada 10 segundos comprueba SQL Server. Mientras hay conexión mantiene la caché SQLite al día;
    /// si se cae avisa y el sistema sigue con la caché; al volver avisa y borra la caché.
    /// </summary>
    public static class ConexionMonitor
    {
        private static readonly TimeSpan Intervalo = TimeSpan.FromSeconds(10);
        private static volatile bool _enLinea = true;
        private static CancellationTokenSource? _cts;

        public static bool EnLinea => _enLinea;

        private static readonly object _cambio = new();

        public static bool EsFalloDeConexion(Exception? ex)
        {
            for (var e = ex; e != null; e = e.InnerException)
                if (e is SqlException s && (s.Number is -1 or 2 or 53 or 26 or 40 or 233 or 10053 or 10054 or 10060 or 10061 or 4060 or 18456 or 258 or 0
                                           || s.Message.Contains("red", StringComparison.OrdinalIgnoreCase)))
                    return true;
            return false;
        }

        /// <summary>Se llama apenas una operación falla por conexión: pasa a modo sin conexión sin esperar los 10 s.</summary>
        public static void ReportarFallo(Exception? ex)
        {
            if (!EsFalloDeConexion(ex)) return;
            lock (_cambio)
            {
                if (!_enLinea) return;
                _enLinea = false;
            }
            AvisarPerdida();
        }

        /// <summary>Comprueba la conexión antes de crear un contexto; si SQL Server no responde pasa a modo sin conexión.</summary>
        public static bool VerificarEnLinea()
        {
            if (!_enLinea) return false;
            if (Ping()) return true;
            bool cambio;
            lock (_cambio) { cambio = _enLinea; _enLinea = false; }
            if (cambio) AvisarPerdida();
            return false;
        }

        /// <summary>Ejecuta la operación; si SQL Server falla a mitad, pasa a SQLite y la repite una vez.</summary>
        public static T Ejecutar<T>(Func<T> operacion)
        {
            try { return operacion(); }
            catch (Exception ex) when (EsFalloDeConexion(ex))
            {
                ReportarFallo(ex);
                return operacion();
            }
        }

        public static void Iniciar()
        {
            if (_cts != null) return;

            PendientesSync.AsegurarTabla();
            _enLinea = Ping();
            if (!_enLinea) AvisarPerdida();

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _ = Task.Run(() => Bucle(token));
        }

        public static void Detener()
        {
            _cts?.Cancel();
            _cts = null;
        }

        private static async Task Bucle(CancellationToken ct)
        {
            using var timer = new PeriodicTimer(Intervalo);
            try
            {
                do { Ciclo(); }
                while (await timer.WaitForNextTickAsync(ct));
            }
            catch (OperationCanceledException) { }
        }

        private static void Ciclo()
        {
            try
            {
                if (!Ping())
                {
                    bool cambio;
                    lock (_cambio) { cambio = _enLinea; _enLinea = false; }
                    if (cambio) AvisarPerdida();
                    return;
                }

                int enviados = 0, fallidos = 0;
                if (!_enLinea)
                {
                    // Primero se envían a SQL Server todos los cambios hechos sin conexión
                    if (!PendientesSync.Reenviar(AppConfig.ConnectionString, out enviados, out fallidos))
                        return;

                    _enLinea = true;
                    try { CacheLocal.Limpiar(); }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Error limpiando caché: {ex.Message}"); }
                    AvisarRestablecida(enviados, fallidos);
                }
                else if (PendientesSync.HayPendientes())
                {
                    PendientesSync.Reenviar(AppConfig.ConnectionString, out _, out _);
                }

                CacheLocal.Sincronizar(AppConfig.ConnectionString);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error en ciclo de conexión/sincronización: {ex.GetBaseException().Message}");
            }
        }

        private static bool Ping()
        {
            try
            {
                var b = new SqlConnectionStringBuilder(AppConfig.ConnectionString) { ConnectTimeout = 3 };
                using var c = new SqlConnection(b.ConnectionString);
                c.Open();
                using var cmd = new SqlCommand("SELECT 1", c) { CommandTimeout = 3 };
                cmd.ExecuteScalar();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void AvisarPerdida() => Mostrar(
            "Conexión perdida",
            "Se perdió la conexión con el servidor.\n\n" +
            "Puede seguir operando temporalmente con los datos guardados en este equipo.\n" +
            "Le avisaremos cuando la conexión se recupere.",
            MessageBoxIcon.Warning);

        private static void AvisarRestablecida(int enviados, int fallidos)
        {
            var texto = "Conexión restablecida.\n\nYa puede seguir usando el sistema con normalidad.";
            if (enviados > 0)
                texto += $"\n\nSe enviaron {enviados} cambio(s) realizados sin conexión.";
            if (fallidos > 0)
                texto += $"\n\n{fallidos} cambio(s) no pudieron aplicarse (ver sync_errores.log en la carpeta de la caché).";
            Mostrar("Conexión restablecida", texto, fallidos > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }

        // En otro hilo para no detener el ciclo mientras el mensaje está abierto
        private static void Mostrar(string titulo, string texto, MessageBoxIcon icono) =>
            Task.Run(() => MessageBox.Show(texto, titulo, MessageBoxButtons.OK, icono));
    }
}

namespace Nuevo_Proyecto.Data.SqlLite
{
    public sealed class FalloConexionInterceptor : Microsoft.EntityFrameworkCore.Diagnostics.DbConnectionInterceptor
    {
        public override void ConnectionFailed(System.Data.Common.DbConnection connection, Microsoft.EntityFrameworkCore.Diagnostics.ConnectionErrorEventData eventData) => ConexionMonitor.ReportarFallo(eventData.Exception);

        public override Task ConnectionFailedAsync(System.Data.Common.DbConnection connection, Microsoft.EntityFrameworkCore.Diagnostics.ConnectionErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            ConexionMonitor.ReportarFallo(eventData.Exception);
            return Task.CompletedTask;
        }
    }
}

