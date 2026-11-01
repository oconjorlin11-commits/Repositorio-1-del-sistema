using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Nuevo_Proyecto.Data;
using Nuevo_Proyecto.Data.SqlLite;
using Nuevo_Proyecto.Models.DTOs;
using Nuevo_Proyecto.Models.Entities;
using Nuevo_Proyecto.Services.Helpers;
using Nuevo_Proyecto.Services.Interfaz_service;

namespace Nuevo_Proyecto.Services
{
    public class ClienteRepository : IClienteRepository
    {
        public const string CodigoConsumidorFinal = "CLI-CF";
        public const string NombreConsumidorFinal = "Consumidor Final";

        private readonly IDbContextFactory<Dev_ComideriaDbContext> _factory;
        private readonly AuditoriaService _auditoria;

        public ClienteRepository(IDbContextFactory<Dev_ComideriaDbContext>? factory = null)
        {
            _factory = factory ?? new ComideriaContextFactory();
            _auditoria = new AuditoriaService(_factory);
        }

        private static readonly Expression<Func<Cliente, ClienteDto>> ADto = c => new ClienteDto
        {
            ClienteId = c.ClienteId,
            Codigo = c.Codigo,
            Nombre = c.Nombre,
            Telefono = c.Telefono,
            Direccion = c.Direccion,
            Nota = c.Nota,
            Activo = c.Activo ?? false
        };

        public IReadOnlyList<ClienteDto> GetActivos() => ConexionMonitor.Ejecutar(GetActivosInterno);
        public IReadOnlyList<ClienteDto> BuscarPorCodigo(string codigoExacto) => ConexionMonitor.Ejecutar(() => BuscarPorCodigoInterno(codigoExacto));
        public IReadOnlyList<ClienteDto> BuscarPorCodigoONombre(string termino) => ConexionMonitor.Ejecutar(() => BuscarPorCodigoONombreInterno(termino));
        public IReadOnlyList<string> GetNotas() => ConexionMonitor.Ejecutar(GetNotasInterno);
        public string GetSiguienteCodigo() => ConexionMonitor.Ejecutar(GetSiguienteCodigoInterno);
        public bool CodigoExiste(string codigo) => ConexionMonitor.Ejecutar(() => CodigoExisteInterno(codigo));
        public bool TelefonoExiste(string telefono) => ConexionMonitor.Ejecutar(() => TelefonoExisteInterno(telefono));
        public int Crear(ClienteDto dto) => ConexionMonitor.Ejecutar(() => CrearInterno(dto));
        public bool Actualizar(string codigo, string nombre, string? telefono, string? direccion, string? nota, string? motivo = null) =>
            ConexionMonitor.Ejecutar(() => ActualizarInterno(codigo, nombre, telefono, direccion, nota, motivo));
        public ClienteDto ObtenerOCrearConsumidorFinal() => ConexionMonitor.Ejecutar(ObtenerOCrearConsumidorFinalInterno);

        private IReadOnlyList<ClienteDto> GetActivosInterno()
        {
            using var db = _factory.CreateDbContext();
            return db.Clientes.AsNoTracking()
                .Where(c => c.Activo == true && c.Codigo != CodigoConsumidorFinal)
                .OrderBy(c => c.Codigo)
                .Select(ADto)
                .ToList();
        }

        private IReadOnlyList<ClienteDto> BuscarPorCodigoInterno(string codigoExacto)
        {
            using var db = _factory.CreateDbContext();
            return db.Clientes.AsNoTracking()
                .Where(c => c.Codigo == codigoExacto)
                .OrderBy(c => c.Codigo)
                .Select(ADto)
                .ToList();
        }

        private IReadOnlyList<ClienteDto> BuscarPorCodigoONombreInterno(string termino)
        {
            using var db = _factory.CreateDbContext();
            string t = termino.Trim().ToUpper();
            return db.Clientes.AsNoTracking()
                .Where(c => c.Codigo != CodigoConsumidorFinal &&
                            (c.Codigo.ToUpper().Contains(t) || c.Nombre.ToUpper().Contains(t)))
                .OrderBy(c => c.Codigo)
                .Select(ADto)
                .ToList();
        }

        private IReadOnlyList<string> GetNotasInterno()
        {
            using var db = _factory.CreateDbContext();
            return db.Clientes.AsNoTracking()
                .Select(c => c.Nota)
                .Where(n => n != null && n != "")
                .Distinct()
                .OrderBy(n => n)
                .ToList()!;
        }

        private string GetSiguienteCodigoInterno()
        {
            using var db = _factory.CreateDbContext();
            var codigos = db.Clientes.AsNoTracking().Select(c => c.Codigo).ToList();
            return CodigoGenerator.SiguienteAgrupado("CLI", codigos, "CLI-001");
        }

        private bool CodigoExisteInterno(string codigo)
        {
            using var db = _factory.CreateDbContext();
            return db.Clientes.AsNoTracking().Any(c => c.Codigo == codigo);
        }

        private bool TelefonoExisteInterno(string telefono)
        {
            using var db = _factory.CreateDbContext();
            return db.Clientes.AsNoTracking().Any(c => c.Telefono == telefono);
        }

        /// <summary>
        /// Valida las reglas de negocio para crear un nuevo cliente.
        /// Devuelve null si es válido, o un mensaje de error si no.
        /// </summary>
        public string? ValidarNuevoCliente(string codigo, string nombre, string? telefono, string? direccion, string? nota)
        {
            // Validar nombre: debe ser "Nombre Apellido" (exactamente un espacio)
            if (string.IsNullOrWhiteSpace(nombre))
                return "El nombre es obligatorio.";

            nombre = nombre.Trim();
            var partes = nombre.Split(' ');

            // Debe tener exactamente 2 partes (nombre y apellido)
            if (partes.Length != 2)
                return "El nombre debe contener exactamente un nombre y un apellido separados por espacio (ej: Jose Ordoz).";

            // Cada parte debe tener al menos 1 carácter
            if (partes[0].Length == 0 || partes[1].Length == 0)
                return "El nombre y el apellido no pueden estar vacíos.";

            // Validar teléfono: formato XXXX-XXXX (4 dígitos, guion, 4 dígitos)
            if (string.IsNullOrWhiteSpace(telefono))
                return "El teléfono es obligatorio.";

            telefono = telefono.Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(telefono, @"^\d{4}-\d{4}$"))
                return "El teléfono debe tener formato XXXX-XXXX (ej: 7777-1111).";

            // Validar que el teléfono no sea duplicado
            if (TelefonoExiste(telefono))
                return $"El teléfono '{telefono}' ya está registrado en el sistema.";

            // Validar dirección: no más de 50 caracteres
            if (string.IsNullOrWhiteSpace(direccion))
                return "La dirección es obligatoria.";

            direccion = direccion.Trim();
            if (direccion.Length > 50)
                return "La dirección no puede exceder 50 caracteres.";

            // Validar nota: obligatoria
            if (string.IsNullOrWhiteSpace(nota))
                return "Debe seleccionar una nota.";

            return null;  // Todas las validaciones pasaron
        }

        private int CrearInterno(ClienteDto dto)
        {
            using var db = _factory.CreateDbContext();

            var cliente = new Cliente
            {
                Codigo = dto.Codigo,
                Nombre = dto.Nombre,
                Telefono = dto.Telefono,
                Direccion = dto.Direccion,
                Nota = dto.Nota ?? string.Empty,   // la entidad no admite null
                Activo = dto.Activo
            };

            db.Clientes.Add(cliente);
            db.SaveChanges();

            // Registrar auditoría
            _auditoria.RegistrarAccion(
                seccion: "Clientes",
                accion: "CREAR",
                registroId: cliente.ClienteId,
                usuario: SesionActual.NombreEmpleado,
                rolUsuario: SesionActual.Rol,
                detalle: $"Cliente: {dto.Codigo} - {dto.Nombre}, Teléfono: {dto.Telefono}",
                descripcion: "Nuevo cliente creado"
            );

            return cliente.ClienteId;  // Retorna el ID del cliente recién creado
        }

        private bool ActualizarInterno(string codigo, string nombre, string? telefono, string? direccion, string? nota, string? motivo)
        {
            using var db = _factory.CreateDbContext();
            var cliente = db.Clientes.FirstOrDefault(c => c.Codigo == codigo);
            if (cliente == null) return false;

            var antes = new { cliente.Nombre, cliente.Telefono, cliente.Direccion, cliente.Nota };

            cliente.Nombre = nombre.Trim();
            cliente.Telefono = string.IsNullOrWhiteSpace(telefono) ? null : telefono.Trim();
            cliente.Direccion = direccion?.Trim() ?? string.Empty;
            cliente.Nota = nota?.Trim() ?? string.Empty;

            db.SaveChanges();

            // Registrar auditoría
            _auditoria.RegistrarAccion(
                seccion: "Clientes",
                accion: "MODIFICAR",
                registroId: cliente.ClienteId,
                usuario: SesionActual.NombreEmpleado,
                rolUsuario: SesionActual.Rol,
                usuarioAutorizador: SesionActual.UsuarioAutorizador,
                detalle: $"Cliente: {codigo} - {nombre}, Teléfono: {telefono}",
                descripcion: motivo ?? "Cliente actualizado"
            );

            return true;
        }

        public bool Desactivar(string codigo, string? motivo = null) => ConexionMonitor.Ejecutar(() => CambiarEstado(codigo, false, motivo));
        public bool Reactivar(string codigo, string? motivo = null) => ConexionMonitor.Ejecutar(() => CambiarEstado(codigo, true, motivo));

        private bool CambiarEstado(string codigo, bool activo, string? motivo = null)
        {
            using var db = _factory.CreateDbContext();
            var cliente = db.Clientes.FirstOrDefault(c => c.Codigo == codigo);
            if (cliente == null) return false;

            string accion = activo ? "REACTIVAR" : "DESACTIVAR";
            cliente.Activo = activo;
            db.SaveChanges();

            // Registrar auditoría
            _auditoria.RegistrarAccion(
                seccion: "Clientes",
                accion: accion,
                registroId: cliente.ClienteId,
                usuario: SesionActual.NombreEmpleado,
                rolUsuario: SesionActual.Rol,
                usuarioAutorizador: SesionActual.UsuarioAutorizador,
                detalle: $"Cliente: {codigo} - {cliente.Nombre}",
                descripcion: motivo ?? $"Cliente {(activo ? "reactivado" : "desactivado")}"
            );

            return true;
        }

        private ClienteDto ObtenerOCrearConsumidorFinalInterno()
        {
            using var db = _factory.CreateDbContext();
            var cf = BuscarOCrearConsumidorFinal(db);
            db.SaveChanges();
            return new ClienteDto
            {
                ClienteId = cf.ClienteId,
                Codigo = cf.Codigo,
                Nombre = cf.Nombre,
                Direccion = cf.Direccion,
                Nota = cf.Nota,
                Activo = true
            };
        }

        /// <summary>
        /// Busca (o agrega al contexto, sin guardar) el cliente genérico. Lo usa también la facturación
        /// para que la venta y el cliente se guarden en la misma transacción.
        /// </summary>
        internal static Cliente BuscarOCrearConsumidorFinal(Dev_ComideriaDbContext db)
        {
            var cf = db.Clientes.FirstOrDefault(c => c.Codigo == CodigoConsumidorFinal || c.Nombre == NombreConsumidorFinal);
            if (cf != null)
            {
                if (cf.Activo != true) cf.Activo = true;
                return cf;
            }

            cf = new Cliente
            {
                Codigo = CodigoConsumidorFinal,
                Nombre = NombreConsumidorFinal,
                Telefono = null,
                Direccion = "N/A",
                Nota = "Cliente genérico para ventas sin cliente",
                Activo = true
            };
            db.Clientes.Add(cf);
            return cf;
        }
    }
}
