using Microsoft.EntityFrameworkCore;
using Nuevo_Proyecto.Data.DbSqlite.Entities;

namespace Nuevo_Proyecto.Data.DbSqlite;

/// <summary>Contexto EF de la base SQLite persistente (independiente de cache_local.db).</summary>
public partial class ComideriaSqliteContext : DbContext
{
    public ComideriaSqliteContext()
    {
    }

    public ComideriaSqliteContext(DbContextOptions<ComideriaSqliteContext> options)
        : base(options)
    {
    }

    public virtual DbSet<ColaOperacione> ColaOperaciones { get; set; }

    public virtual DbSet<ConfiguracionSincronizacion> ConfiguracionSincronizacions { get; set; }

    public virtual DbSet<MapeoClavesSincronizacion> MapeoClavesSincronizacions { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
            optionsBuilder.UseSqlite(SqliteSchemaInitializer.CadenaConexion);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ColaOperacione>(entity =>
        {
            entity.HasKey(e => e.Secuencia);
            entity.ToTable("ColaOperaciones");

            entity.HasIndex(e => e.OperacionId).IsUnique();
            entity.HasIndex(e => e.ClaveIdempotencia).IsUnique();
            entity.HasIndex(e => new { e.Estado, e.ProximoIntentoUtc });

            entity.Property(e => e.Secuencia).ValueGeneratedOnAdd();
            entity.Property(e => e.Estado).HasDefaultValue("PENDIENTE");
            entity.Property(e => e.Intentos).HasDefaultValue(0);

            entity.HasOne(d => d.DependenciaSecuenciaNavigation)
                .WithMany(p => p.InverseDependenciaSecuenciaNavigation)
                .HasForeignKey(d => d.DependenciaSecuencia)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ConfiguracionSincronizacion>(entity =>
        {
            entity.HasKey(e => e.Clave);
            entity.ToTable("ConfiguracionSincronizacion");
        });

        modelBuilder.Entity<MapeoClavesSincronizacion>(entity =>
        {
            entity.HasKey(e => new { e.NombreEntidad, e.ClaveLocal });
            entity.ToTable("MapeoClavesSincronizacion");

            entity.HasIndex(e => e.OperacionId).IsUnique();
            entity.Property(e => e.Estado).HasDefaultValue("PENDIENTE");

            entity.HasOne(d => d.Operacion)
                .WithOne(p => p.MapeoClavesSincronizacion)
                .HasPrincipalKey<ColaOperacione>(p => p.OperacionId)
                .HasForeignKey<MapeoClavesSincronizacion>(d => d.OperacionId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
