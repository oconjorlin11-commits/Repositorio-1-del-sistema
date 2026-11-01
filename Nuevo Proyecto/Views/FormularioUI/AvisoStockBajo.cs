using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Nuevo_Proyecto.Models.Entities;
using Nuevo_Proyecto.Services.Interfaz_service;
using Nuevo_Proyecto.Services;

namespace Nuevo_Proyecto.Views.FormularioUI
{
    public partial class AvisoStockBajo : Form
    {
        private readonly IInventarioRepository _inventarioRepo;
        private List<NotificacionesStockCritico> _notificaciones;

        public AvisoStockBajo(IInventarioRepository? inventarioRepo = null)
        {
            InitializeComponent();
            _inventarioRepo = inventarioRepo ?? new InventarioRepository();
            _notificaciones = new List<NotificacionesStockCritico>();
        }

        private void AvisoStockBajo_Load(object sender, EventArgs e)
        {
            CargarNotificacionesStockCritico();
        }

        private void CargarNotificacionesStockCritico()
        {
            try
            {
                _notificaciones = _inventarioRepo.GetNotificacionesStockCritico().ToList();

                if (_notificaciones.Count == 0)
                {
                    // Si no hay notificaciones, cerrar el formulario inmediatamente
                    this.DialogResult = DialogResult.OK;
                    this.Close();
                    return;
                }

                // Mostrar notificaciones en el DataGridView
                MostrarNotificacionesEnGrid();

                // Mostrar mensaje informativo
                lblTitulo.Text = $"⚠️ ALERTA: {_notificaciones.Count} Producto(s) con Stock Crítico";
                lblDescripcion.Text = "Los siguientes productos tienen stock por debajo del mínimo. Por favor, considere realizar una compra.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al cargar notificaciones de stock: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
        }

        private void MostrarNotificacionesEnGrid()
        {
            DataTable dt = new DataTable();
            dt.Columns.Add("Código Producto", typeof(string));
            dt.Columns.Add("Nombre Producto", typeof(string));
            dt.Columns.Add("Stock Actual", typeof(int));
            dt.Columns.Add("Stock Mínimo", typeof(int));
            dt.Columns.Add("Fecha Registro", typeof(DateTime));

            foreach (var notif in _notificaciones)
            {
                dt.Rows.Add(
                    notif.Productos?.Codigo ?? "N/A",
                    notif.Productos?.Nombre ?? "N/A",
                    notif.StockActual,
                    notif.StockMinimo,
                    notif.FechaRegistro
                );
            }

            dgvNotificaciones.DataSource = dt;

            // Ajustar ancho de columnas
            dgvNotificaciones.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.AllCells);
        }

        private void btnContinuar_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}
