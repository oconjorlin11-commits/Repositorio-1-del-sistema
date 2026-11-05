namespace Nuevo_Proyecto.Data.DbSqlite.Entities;

public partial class MapeoClavesSincronizacion
{
    public string NombreEntidad { get; set; } = null!;

    public string ClaveLocal { get; set; } = null!;

    public string? ClaveServidor { get; set; }

    public string OperacionId { get; set; } = null!;

    public string Estado { get; set; } = "PENDIENTE";

    public string CreadaUtc { get; set; } = null!;

    public string? ConfirmadaUtc { get; set; }

    public virtual ColaOperacione? Operacion { get; set; }
}
