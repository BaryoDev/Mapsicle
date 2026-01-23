using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Mapsicle.Fluent;

namespace Mapsicle.Dapper
{
    /// <summary>
    /// Dapper integration extensions for Mapsicle.
    /// Bridges Dapper query results to mapped DTOs with fluent extensions.
    /// </summary>
    public static class DapperExtensions
    {
        #region IEnumerable Extensions

        /// <summary>
        /// Maps a collection of Dapper query results to the destination type.
        /// </summary>
        /// <typeparam name="TDest">The destination type.</typeparam>
        /// <param name="source">The Dapper query results.</param>
        /// <returns>A list of mapped objects.</returns>
        public static List<TDest> MapTo<TDest>(this IEnumerable<object> source)
        {
            if (source is null) return new List<TDest>();
            return source.Select(item => item.MapTo<TDest>()!).ToList();
        }

        /// <summary>
        /// Maps a collection of Dapper query results to the destination type using an IMapper.
        /// </summary>
        /// <typeparam name="TSource">The source type.</typeparam>
        /// <typeparam name="TDest">The destination type.</typeparam>
        /// <param name="source">The Dapper query results.</param>
        /// <param name="mapper">The mapper instance.</param>
        /// <returns>A list of mapped objects.</returns>
        public static List<TDest> MapTo<TSource, TDest>(this IEnumerable<TSource> source, IMapper mapper)
        {
            if (source is null) return new List<TDest>();
            return source.Select(item => mapper.Map<TSource, TDest>(item)!).ToList();
        }

        #endregion

        #region IDbConnection Query Extensions

        /// <summary>
        /// Queries and maps results to the destination type in a single operation.
        /// </summary>
        /// <typeparam name="TSource">The source/database entity type.</typeparam>
        /// <typeparam name="TDest">The destination DTO type.</typeparam>
        /// <param name="connection">The database connection.</param>
        /// <param name="sql">The SQL query.</param>
        /// <param name="param">Optional query parameters.</param>
        /// <param name="transaction">Optional transaction.</param>
        /// <param name="buffered">Whether to buffer results.</param>
        /// <param name="commandTimeout">Optional command timeout.</param>
        /// <param name="commandType">Optional command type.</param>
        /// <returns>A list of mapped DTOs.</returns>
        public static IEnumerable<TDest> QueryAndMap<TSource, TDest>(
            this IDbConnection connection,
            string sql,
            object? param = null,
            IDbTransaction? transaction = null,
            bool buffered = true,
            int? commandTimeout = null,
            CommandType? commandType = null)
        {
            var results = connection.Query<TSource>(sql, param, transaction, buffered, commandTimeout, commandType);
            return results.Select(item => item!.MapTo<TDest>()!);
        }

        /// <summary>
        /// Queries and maps results to the destination type using the provided mapper configuration.
        /// </summary>
        /// <typeparam name="TSource">The source/database entity type.</typeparam>
        /// <typeparam name="TDest">The destination DTO type.</typeparam>
        /// <param name="connection">The database connection.</param>
        /// <param name="sql">The SQL query.</param>
        /// <param name="config">The mapper configuration.</param>
        /// <param name="param">Optional query parameters.</param>
        /// <param name="transaction">Optional transaction.</param>
        /// <param name="buffered">Whether to buffer results.</param>
        /// <param name="commandTimeout">Optional command timeout.</param>
        /// <param name="commandType">Optional command type.</param>
        /// <returns>A list of mapped DTOs.</returns>
        public static IEnumerable<TDest> QueryAndMap<TSource, TDest>(
            this IDbConnection connection,
            string sql,
            MapperConfiguration config,
            object? param = null,
            IDbTransaction? transaction = null,
            bool buffered = true,
            int? commandTimeout = null,
            CommandType? commandType = null)
        {
            var mapper = config.CreateMapper();
            var results = connection.Query<TSource>(sql, param, transaction, buffered, commandTimeout, commandType);
            return results.Select(item => mapper.Map<TSource, TDest>(item)!);
        }

        /// <summary>
        /// Queries and maps results to the destination type using the provided IMapper.
        /// </summary>
        /// <typeparam name="TSource">The source/database entity type.</typeparam>
        /// <typeparam name="TDest">The destination DTO type.</typeparam>
        /// <param name="connection">The database connection.</param>
        /// <param name="sql">The SQL query.</param>
        /// <param name="mapper">The mapper instance.</param>
        /// <param name="param">Optional query parameters.</param>
        /// <param name="transaction">Optional transaction.</param>
        /// <param name="buffered">Whether to buffer results.</param>
        /// <param name="commandTimeout">Optional command timeout.</param>
        /// <param name="commandType">Optional command type.</param>
        /// <returns>A list of mapped DTOs.</returns>
        public static IEnumerable<TDest> QueryAndMap<TSource, TDest>(
            this IDbConnection connection,
            string sql,
            IMapper mapper,
            object? param = null,
            IDbTransaction? transaction = null,
            bool buffered = true,
            int? commandTimeout = null,
            CommandType? commandType = null)
        {
            var results = connection.Query<TSource>(sql, param, transaction, buffered, commandTimeout, commandType);
            return results.Select(item => mapper.Map<TSource, TDest>(item)!);
        }

        #endregion

        #region Async Query Extensions

        /// <summary>
        /// Asynchronously queries and maps results to the destination type.
        /// </summary>
        /// <typeparam name="TSource">The source/database entity type.</typeparam>
        /// <typeparam name="TDest">The destination DTO type.</typeparam>
        /// <param name="connection">The database connection.</param>
        /// <param name="sql">The SQL query.</param>
        /// <param name="param">Optional query parameters.</param>
        /// <param name="transaction">Optional transaction.</param>
        /// <param name="commandTimeout">Optional command timeout.</param>
        /// <param name="commandType">Optional command type.</param>
        /// <returns>A list of mapped DTOs.</returns>
        public static async Task<IEnumerable<TDest>> QueryAndMapAsync<TSource, TDest>(
            this IDbConnection connection,
            string sql,
            object? param = null,
            IDbTransaction? transaction = null,
            int? commandTimeout = null,
            CommandType? commandType = null)
        {
            var results = await connection.QueryAsync<TSource>(sql, param, transaction, commandTimeout, commandType);
            return results.Select(item => item!.MapTo<TDest>()!);
        }

        /// <summary>
        /// Asynchronously queries and maps results to the destination type using the provided mapper.
        /// </summary>
        /// <typeparam name="TSource">The source/database entity type.</typeparam>
        /// <typeparam name="TDest">The destination DTO type.</typeparam>
        /// <param name="connection">The database connection.</param>
        /// <param name="sql">The SQL query.</param>
        /// <param name="mapper">The mapper instance.</param>
        /// <param name="param">Optional query parameters.</param>
        /// <param name="transaction">Optional transaction.</param>
        /// <param name="commandTimeout">Optional command timeout.</param>
        /// <param name="commandType">Optional command type.</param>
        /// <returns>A list of mapped DTOs.</returns>
        public static async Task<IEnumerable<TDest>> QueryAndMapAsync<TSource, TDest>(
            this IDbConnection connection,
            string sql,
            IMapper mapper,
            object? param = null,
            IDbTransaction? transaction = null,
            int? commandTimeout = null,
            CommandType? commandType = null)
        {
            var results = await connection.QueryAsync<TSource>(sql, param, transaction, commandTimeout, commandType);
            return results.Select(item => mapper.Map<TSource, TDest>(item)!);
        }

        #endregion

        #region Single Result Extensions

        /// <summary>
        /// Queries a single result and maps to the destination type.
        /// </summary>
        /// <typeparam name="TSource">The source/database entity type.</typeparam>
        /// <typeparam name="TDest">The destination DTO type.</typeparam>
        /// <param name="connection">The database connection.</param>
        /// <param name="sql">The SQL query.</param>
        /// <param name="param">Optional query parameters.</param>
        /// <param name="transaction">Optional transaction.</param>
        /// <param name="commandTimeout">Optional command timeout.</param>
        /// <param name="commandType">Optional command type.</param>
        /// <returns>The mapped DTO or default if not found.</returns>
        public static TDest? QuerySingleAndMap<TSource, TDest>(
            this IDbConnection connection,
            string sql,
            object? param = null,
            IDbTransaction? transaction = null,
            int? commandTimeout = null,
            CommandType? commandType = null)
        {
            var result = connection.QuerySingleOrDefault<TSource>(sql, param, transaction, commandTimeout, commandType);
            return result is null ? default : result.MapTo<TDest>();
        }

        /// <summary>
        /// Queries a single result and maps to the destination type using the provided mapper.
        /// </summary>
        /// <typeparam name="TSource">The source/database entity type.</typeparam>
        /// <typeparam name="TDest">The destination DTO type.</typeparam>
        /// <param name="connection">The database connection.</param>
        /// <param name="sql">The SQL query.</param>
        /// <param name="mapper">The mapper instance.</param>
        /// <param name="param">Optional query parameters.</param>
        /// <param name="transaction">Optional transaction.</param>
        /// <param name="commandTimeout">Optional command timeout.</param>
        /// <param name="commandType">Optional command type.</param>
        /// <returns>The mapped DTO or default if not found.</returns>
        public static TDest? QuerySingleAndMap<TSource, TDest>(
            this IDbConnection connection,
            string sql,
            IMapper mapper,
            object? param = null,
            IDbTransaction? transaction = null,
            int? commandTimeout = null,
            CommandType? commandType = null)
        {
            var result = connection.QuerySingleOrDefault<TSource>(sql, param, transaction, commandTimeout, commandType);
            return result is null ? default : mapper.Map<TSource, TDest>(result);
        }

        /// <summary>
        /// Queries the first result and maps to the destination type.
        /// </summary>
        /// <typeparam name="TSource">The source/database entity type.</typeparam>
        /// <typeparam name="TDest">The destination DTO type.</typeparam>
        /// <param name="connection">The database connection.</param>
        /// <param name="sql">The SQL query.</param>
        /// <param name="param">Optional query parameters.</param>
        /// <param name="transaction">Optional transaction.</param>
        /// <param name="commandTimeout">Optional command timeout.</param>
        /// <param name="commandType">Optional command type.</param>
        /// <returns>The mapped DTO or default if not found.</returns>
        public static TDest? QueryFirstAndMap<TSource, TDest>(
            this IDbConnection connection,
            string sql,
            object? param = null,
            IDbTransaction? transaction = null,
            int? commandTimeout = null,
            CommandType? commandType = null)
        {
            var result = connection.QueryFirstOrDefault<TSource>(sql, param, transaction, commandTimeout, commandType);
            return result is null ? default : result.MapTo<TDest>();
        }

        #endregion

        #region Async Single Result Extensions

        /// <summary>
        /// Asynchronously queries a single result and maps to the destination type.
        /// </summary>
        /// <typeparam name="TSource">The source/database entity type.</typeparam>
        /// <typeparam name="TDest">The destination DTO type.</typeparam>
        /// <param name="connection">The database connection.</param>
        /// <param name="sql">The SQL query.</param>
        /// <param name="param">Optional query parameters.</param>
        /// <param name="transaction">Optional transaction.</param>
        /// <param name="commandTimeout">Optional command timeout.</param>
        /// <param name="commandType">Optional command type.</param>
        /// <returns>The mapped DTO or default if not found.</returns>
        public static async Task<TDest?> QuerySingleAndMapAsync<TSource, TDest>(
            this IDbConnection connection,
            string sql,
            object? param = null,
            IDbTransaction? transaction = null,
            int? commandTimeout = null,
            CommandType? commandType = null)
        {
            var result = await connection.QuerySingleOrDefaultAsync<TSource>(sql, param, transaction, commandTimeout, commandType);
            return result is null ? default : result.MapTo<TDest>();
        }

        /// <summary>
        /// Asynchronously queries a single result and maps to the destination type using the provided mapper.
        /// </summary>
        /// <typeparam name="TSource">The source/database entity type.</typeparam>
        /// <typeparam name="TDest">The destination DTO type.</typeparam>
        /// <param name="connection">The database connection.</param>
        /// <param name="sql">The SQL query.</param>
        /// <param name="mapper">The mapper instance.</param>
        /// <param name="param">Optional query parameters.</param>
        /// <param name="transaction">Optional transaction.</param>
        /// <param name="commandTimeout">Optional command timeout.</param>
        /// <param name="commandType">Optional command type.</param>
        /// <returns>The mapped DTO or default if not found.</returns>
        public static async Task<TDest?> QuerySingleAndMapAsync<TSource, TDest>(
            this IDbConnection connection,
            string sql,
            IMapper mapper,
            object? param = null,
            IDbTransaction? transaction = null,
            int? commandTimeout = null,
            CommandType? commandType = null)
        {
            var result = await connection.QuerySingleOrDefaultAsync<TSource>(sql, param, transaction, commandTimeout, commandType);
            return result is null ? default : mapper.Map<TSource, TDest>(result);
        }

        /// <summary>
        /// Asynchronously queries the first result and maps to the destination type.
        /// </summary>
        /// <typeparam name="TSource">The source/database entity type.</typeparam>
        /// <typeparam name="TDest">The destination DTO type.</typeparam>
        /// <param name="connection">The database connection.</param>
        /// <param name="sql">The SQL query.</param>
        /// <param name="param">Optional query parameters.</param>
        /// <param name="transaction">Optional transaction.</param>
        /// <param name="commandTimeout">Optional command timeout.</param>
        /// <param name="commandType">Optional command type.</param>
        /// <returns>The mapped DTO or default if not found.</returns>
        public static async Task<TDest?> QueryFirstAndMapAsync<TSource, TDest>(
            this IDbConnection connection,
            string sql,
            object? param = null,
            IDbTransaction? transaction = null,
            int? commandTimeout = null,
            CommandType? commandType = null)
        {
            var result = await connection.QueryFirstOrDefaultAsync<TSource>(sql, param, transaction, commandTimeout, commandType);
            return result is null ? default : result.MapTo<TDest>();
        }

        #endregion

        #region Stored Procedure Extensions

        /// <summary>
        /// Executes a stored procedure and maps results to the destination type.
        /// </summary>
        /// <typeparam name="TSource">The source/database entity type.</typeparam>
        /// <typeparam name="TDest">The destination DTO type.</typeparam>
        /// <param name="connection">The database connection.</param>
        /// <param name="procedureName">The stored procedure name.</param>
        /// <param name="param">Optional procedure parameters.</param>
        /// <param name="transaction">Optional transaction.</param>
        /// <param name="commandTimeout">Optional command timeout.</param>
        /// <returns>A list of mapped DTOs.</returns>
        public static IEnumerable<TDest> ExecuteAndMap<TSource, TDest>(
            this IDbConnection connection,
            string procedureName,
            object? param = null,
            IDbTransaction? transaction = null,
            int? commandTimeout = null)
        {
            var results = connection.Query<TSource>(
                procedureName, param, transaction, true, commandTimeout, CommandType.StoredProcedure);
            return results.Select(item => item!.MapTo<TDest>()!);
        }

        /// <summary>
        /// Asynchronously executes a stored procedure and maps results to the destination type.
        /// </summary>
        /// <typeparam name="TSource">The source/database entity type.</typeparam>
        /// <typeparam name="TDest">The destination DTO type.</typeparam>
        /// <param name="connection">The database connection.</param>
        /// <param name="procedureName">The stored procedure name.</param>
        /// <param name="param">Optional procedure parameters.</param>
        /// <param name="transaction">Optional transaction.</param>
        /// <param name="commandTimeout">Optional command timeout.</param>
        /// <returns>A list of mapped DTOs.</returns>
        public static async Task<IEnumerable<TDest>> ExecuteAndMapAsync<TSource, TDest>(
            this IDbConnection connection,
            string procedureName,
            object? param = null,
            IDbTransaction? transaction = null,
            int? commandTimeout = null)
        {
            var results = await connection.QueryAsync<TSource>(
                procedureName, param, transaction, commandTimeout, CommandType.StoredProcedure);
            return results.Select(item => item!.MapTo<TDest>()!);
        }

        #endregion
    }
}
