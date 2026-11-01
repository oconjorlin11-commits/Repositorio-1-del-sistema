using Nuevo_Proyecto.Models.Views;
using Nuevo_Proyecto.Services.Helpers;
using Nuevo_Proyecto.Services;
using Nuevo_Proyecto.Data.SqlLite;
using Nuevo_Proyecto.Views.FormularioUI;

namespace Nuevo_Proyecto
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) =>
            {
                if (ConexionMonitor.EsFalloDeConexion(e.Exception))
                {
                    ConexionMonitor.ReportarFallo(e.Exception);
                    MessageBox.Show("No se pudo completar la acción porque se perdió la conexión.\nEl sistema ya trabaja sin conexión; repita la operación.",
                        "Sin conexión", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else
                {
                    MessageBox.Show(e.Exception.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
            ConexionMonitor.Iniciar();

            // Mostrar InicioSesion como diálogo modal
            using (var inicioSesion = new InicioSesion())
            {
                if (inicioSesion.ShowDialog() == DialogResult.OK && SesionActual.HaySesion)
                {
                    // Si login fue exitoso y hay sesión, verificar stock crítico
                    VerificarYMostrarAvisoStockCritico();

                    // Abrir MenuPrincipal
                    Application.Run(new MenuPrincipal());
                }
                // Si no hubo login exitoso, la aplicación se cierra
            }
        }

        /// <summary>
        /// Verifica si hay notificaciones de stock crítico y muestra el aviso si es necesario.
        /// Si no hay stock bajo, cierra automáticamente y continúa con el menú.
        /// </summary>
        private static void VerificarYMostrarAvisoStockCritico()
        {
            try
            {
                var inventarioRepo = new InventarioRepository();
                var notificaciones = inventarioRepo.GetNotificacionesStockCritico();

                if (notificaciones.Count > 0)
                {
                    // Si hay stock crítico, mostrar el aviso
                    using (var avisoStockBajo = new AvisoStockBajo(inventarioRepo))
                    {
                        avisoStockBajo.ShowDialog();
                    }
                }
                // Si no hay notificaciones, continúa directamente sin mostrar nada
            }
            catch (Exception ex)
            {
                // En caso de error, solo registrar y continuar (no interrumpir flujo de login)
                System.Diagnostics.Debug.WriteLine($"Error verificando stock crítico: {ex.Message}");
            }
        }
    }
}
