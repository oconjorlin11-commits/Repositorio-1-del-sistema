using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using Microsoft.EntityFrameworkCore;
using Nuevo_Proyecto.Services;
using Nuevo_Proyecto.Services.Interfaz_service;
using Nuevo_Proyecto.Data;
using Nuevo_Proyecto.Models.DTOs;

namespace Nuevo_Proyecto.Views.FormularioUI
{
    public partial class Verificacion_de_usuario : Form
    {
        private readonly IAuthService _auth;
        public string AdminNombreVerificado { get; private set; } = string.Empty;

        public Verificacion_de_usuario()
        {
            InitializeComponent();
            _auth = new AuthService();

            // Ocultar todos los labels y lineas inicialmente
            OcultarResultados();

            // Conectar evento del botón Verificar del Designer
            BtnVerificar.Click += BtnVerificar_Click;
        }

        private void OcultarResultados()
        {
            // Ocultar label de verificacion
            LblVerificacion.Visible = false;

            // Ocultar label de guia
            LblverUsuario.Visible = false;

            // Ocultar label de respuesta
            LblRespUsuario.Visible = false;

            // Ocultar lineas
            LblLineas1.Visible = false;
            LblLineas3.Visible = false;
            LblLineas4.Visible = false;
            label2.Visible = false;
            label4.Visible = false;
            label6.Visible = false;
        }

        private void MostrarResultadosExitosos()
        {
            // Mostrar label de verificacion en verde
            LblVerificacion.Visible = true;
            LblVerificacion.Text = "Verificacion Exitosa";
            LblVerificacion.ForeColor = Color.Green;

            // Mostrar label de guia en verde
            LblverUsuario.Visible = true;
            LblverUsuario.ForeColor = Color.Green;

            // Mostrar label de respuesta
            LblRespUsuario.Visible = true;
            LblRespUsuario.ForeColor = Color.Green;

            // Mostrar lineas en verde
            LblLineas1.Visible = true;
            LblLineas1.ForeColor = Color.Green;
            LblLineas3.Visible = true;
            LblLineas3.ForeColor = Color.Green;
            LblLineas4.Visible = true;
            LblLineas4.ForeColor = Color.Green;
            label2.Visible = true;
            label2.ForeColor = Color.Green;
            label4.Visible = true;
            label4.ForeColor = Color.Green;
            label6.Visible = true;
            label6.ForeColor = Color.Green;
        }

        private void MostrarErrorBusqueda(string mensaje)
        {
            // Ocultar todo excepto el label de verificacion
            OcultarResultados();

            // Mostrar solo el label de verificacion en rojo
            LblVerificacion.Visible = true;
            LblVerificacion.Text = mensaje;
            LblVerificacion.ForeColor = Color.Red;
        }

        private async void BtnVerificar_Click(object? sender, EventArgs e)
        {
            await BuscarUsuarioAsync();
        }

        private async Task BuscarUsuarioAsync()
        {
            try
            {
                // Validar campos
                if (string.IsNullOrWhiteSpace(TxtUsuarioEspecial.Text))
                {
                    MessageBox.Show("Por favor, ingrese el Usuario.", "Campo Obligatorio", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    TxtUsuarioEspecial.Focus();
                    return;
                }

                if (string.IsNullOrWhiteSpace(TxtContraseñaEspecial.Text))
                {
                    MessageBox.Show("Por favor, ingrese la Contraseña.", "Campo Obligatorio", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    TxtContraseñaEspecial.Focus();
                    return;
                }

                // Validar credenciales del usuario
                var resultadoAuth = _auth.Autenticar(TxtUsuarioEspecial.Text, TxtContraseñaEspecial.Text);

                if (!resultadoAuth.Exitoso || resultadoAuth.Sesion == null)
                {
                    MostrarErrorBusqueda("No se encontró el usuario o credenciales incorrectas");
                    return;
                }

                // ✅ VERIFICACIÓN CRÍTICA: El usuario debe ser ADMINISTRADOR
                if (!EsUsuarioAdministrador(resultadoAuth.Sesion))
                {
                    MostrarErrorBusqueda($"Error: {resultadoAuth.Sesion.NombreUsuario} no tiene permisos de administrador.\nSolo administradores pueden autorizar acceso temporal.");
                    return;
                }

                // ✅ Usuario encontrado, autenticado y es ADMINISTRADOR
                LblRespUsuario.Text = resultadoAuth.Sesion.NombreUsuario ?? "N/A";
                LblverUsuario.Text = $"Usuario Verificado: {resultadoAuth.Sesion.NombreEmpleado}";

                // Guardar el nombre del admin verificado (incluyendo su Cargo)
                AdminNombreVerificado = $"{resultadoAuth.Sesion.NombreEmpleado} ({resultadoAuth.Sesion.Cargo})";

                // Mostrar resultados en verde
                MostrarResultadosExitosos();

                // Cerrar el diálogo con resultado OK después de un breve tiempo
                await Task.Delay(1500);
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message, "Error del Sistema", MessageBoxButtons.OK, MessageBoxIcon.Error);
                OcultarResultados();
            }
        }

        /// <summary>
        /// Verifica si un usuario tiene permisos de administrador.
        /// Un usuario es admin si su Rol o Cargo contienen "admin" (case-insensitive).
        /// </summary>
        private static bool EsUsuarioAdministrador(SesionUsuarioDto usuario)
        {
            if (usuario == null)
                return false;

            // Verificar si es administrador por Rol o por Cargo
            bool esAdminPorRol = !string.IsNullOrEmpty(usuario.Rol) && 
                                 usuario.Rol.Contains("admin", StringComparison.OrdinalIgnoreCase);

            bool esAdminPorCargo = !string.IsNullOrEmpty(usuario.Cargo) && 
                                   usuario.Cargo.Contains("admin", StringComparison.OrdinalIgnoreCase);

            return esAdminPorRol || esAdminPorCargo;
        }

        private void label2_Click(object sender, EventArgs e)
        {

        }

        private void GbVerificacion_Enter(object sender, EventArgs e)
        {

        }

        private void Verificacion_de_usuario_Load(object sender, EventArgs e)
        {

        }
    }
}
