using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nuevo_Proyecto.Models.Entities
{
    public partial class TriggerLogStockCritico
    {
        public int LogId { get; set; }
        public int ProductoId { get; set; }
        public int Stock { get; set; }
        public int StockMinimo { get; set; }
        public string SourceTable { get; set; } = null!;
        public DateTime FechaRegistro { get; set; }

        public virtual Productos Productos { get; set; } = null!;
    }
}
