using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nuevo_Proyecto.Models.Entities
{
    public partial class NotificacionesStockCritico
    {
        public int NotificacionId { get; set; }
        public int ProductoId { get; set; }
        public int StockActual { get; set; }
        public int StockMinimo { get; set; }
        public DateTime FechaRegistro { get; set; }

        public virtual Productos Productos { get; set; } = null!;
    }
}
