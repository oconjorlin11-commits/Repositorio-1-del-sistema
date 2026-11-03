using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using Nuevo_Proyecto.Services.Helpers;

namespace Nuevo_Proyecto.Views.FormularioUI
{
    public partial class Salir : Form
    {
        public DialogResult ResultadoSalida { get; private set; }

        public Salir()
        {
            InitializeComponent();
            // Conectar eventos de los botones
            BtnCerrarSession.Click += BtnCerrarSession_Click;
            BtnSalir.Click += BtnSalir_Click;
            BtnCancelar.Click += BtnCancelar_Click;
        }

        private void BtnCerrarSession_Click(object sender, EventArgs e)
        {
            ResultadoSalida = DialogResult.Yes;  // Cerrar sesión
            this.DialogResult = DialogResult.Yes;
            this.Close();
        }

        private void BtnSalir_Click(object sender, EventArgs e)
        {
            ResultadoSalida = DialogResult.No;   // Salir del sistema
            this.DialogResult = DialogResult.No;
            this.Close();
        }

        private void BtnCancelar_Click(object sender, EventArgs e)
        {
            ResultadoSalida = DialogResult.Cancel;  // Cancelar
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }
    }
}
