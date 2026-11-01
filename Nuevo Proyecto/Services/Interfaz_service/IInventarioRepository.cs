using Nuevo_Proyecto.Models.DTOs;
using Nuevo_Proyecto.Models.Entities;

namespace Nuevo_Proyecto.Services.Interfaz_service
{
    public interface IInventarioRepository
    {
        IReadOnlyList<ProductoInventarioDto> GetTodos();
        IReadOnlyList<ProductoInventarioDto> GetActivos();
        IReadOnlyList<ProductoInventarioDto> GetActivosPorCategoria(int categoriaId);
        IReadOnlyList<ProductoInventarioDto> Buscar(string texto);
        IReadOnlyList<ProductoInventarioDto> BuscarPorCodigoExacto(string codigo);
        IReadOnlyList<ProductoInventarioDto> BuscarPorCodigoONombreProducto(string termino);
        IReadOnlyList<MovimientoInventarioDto> GetMovimientos(DateTime? desde, DateTime? hasta);

        /// <summary>
        /// Obtiene las notificaciones de stock crítico desde la tabla NotificacionesStockCritico.
        /// Retorna una lista de productos que tienen stock por debajo del mínimo.
        /// </summary>
        IReadOnlyList<NotificacionesStockCritico> GetNotificacionesStockCritico();

        int Reactivar(string codigo, int stock, int minimo, int empleadoId, string motivo);

        /// <param name="descripcion">null = conservar la descripción actual.</param>
        int Actualizar(int productoId, string nombre, int categoriaId, int unidadId, string? descripcion,
                       decimal precioVenta, bool activo, int stock, int minimo, int empleadoId, string observacion);

        int Inhabilitar(int productoId, int empleadoId, string motivo);
    }
}
