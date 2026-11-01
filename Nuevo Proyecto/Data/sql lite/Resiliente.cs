using System.Reflection;

namespace Nuevo_Proyecto.Data.SqlLite
{
    /// <summary>
    /// Envuelve un repositorio: si SQL Server falla durante cualquier método (búsquedas incluidas),
    /// pasa a SQLite y repite la llamada una vez.
    /// </summary>
    public class Resiliente<T> : DispatchProxy where T : class
    {
        private T _destino = null!;

        public static T Envolver(T destino)
        {
            var proxy = Create<T, Resiliente<T>>();
            ((Resiliente<T>)(object)proxy)._destino = destino;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? metodo, object?[]? args)
        {
            try
            {
                return metodo!.Invoke(_destino, args);
            }
            catch (TargetInvocationException ex) when (ConexionMonitor.EsFalloDeConexion(ex.InnerException))
            {
                ConexionMonitor.ReportarFallo(ex.InnerException);
                try { return metodo!.Invoke(_destino, args); }
                catch (TargetInvocationException ex2) when (ex2.InnerException != null)
                {
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex2.InnerException).Throw();
                    throw;
                }
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }
    }

    public static class Resiliente
    {
        public static T Envolver<T>(T destino) where T : class => Resiliente<T>.Envolver(destino);
    }
}
