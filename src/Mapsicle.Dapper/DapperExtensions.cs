using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Runtime.CompilerServices;
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
            return MapRows<object, TDest>(source);
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
            return MapRows<TSource, TDest>(source, mapper);
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
        public static List<TDest> QueryAndMap<TSource, TDest>(
            this IDbConnection connection,
            string sql,
            object? param = null,
            IDbTransaction? transaction = null,
            bool buffered = true,
            int? commandTimeout = null,
            CommandType? commandType = null)
        {
            var results = connection.Query<TSource>(sql, param, transaction, buffered, commandTimeout, commandType);
            return MapRows<TSource, TDest>(results);
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
        public static List<TDest> QueryAndMap<TSource, TDest>(
            this IDbConnection connection,
            string sql,
            MapperConfiguration config,
            object? param = null,
            IDbTransaction? transaction = null,
            bool buffered = true,
            int? commandTimeout = null,
            CommandType? commandType = null)
        {
            var mapper = MapperFor(config);
            var results = connection.Query<TSource>(sql, param, transaction, buffered, commandTimeout, commandType);
            return MapRows<TSource, TDest>(results, mapper);
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
        public static List<TDest> QueryAndMap<TSource, TDest>(
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
            return MapRows<TSource, TDest>(results, mapper);
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
        public static async Task<List<TDest>> QueryAndMapAsync<TSource, TDest>(
            this IDbConnection connection,
            string sql,
            object? param = null,
            IDbTransaction? transaction = null,
            int? commandTimeout = null,
            CommandType? commandType = null)
        {
            var results = await connection.QueryAsync<TSource>(sql, param, transaction, commandTimeout, commandType);
            return MapRows<TSource, TDest>(results);
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
        public static async Task<List<TDest>> QueryAndMapAsync<TSource, TDest>(
            this IDbConnection connection,
            string sql,
            IMapper mapper,
            object? param = null,
            IDbTransaction? transaction = null,
            int? commandTimeout = null,
            CommandType? commandType = null)
        {
            var results = await connection.QueryAsync<TSource>(sql, param, transaction, commandTimeout, commandType);
            return MapRows<TSource, TDest>(results, mapper);
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
            return result is null ? default : MapRow<TDest>(result);
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
            return result is null ? default : MapRow<TDest>(result);
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
            return result is null ? default : MapRow<TDest>(result);
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
            return result is null ? default : MapRow<TDest>(result);
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
        public static List<TDest> ExecuteAndMap<TSource, TDest>(
            this IDbConnection connection,
            string procedureName,
            object? param = null,
            IDbTransaction? transaction = null,
            int? commandTimeout = null)
        {
            var results = connection.Query<TSource>(
                procedureName, param, transaction, true, commandTimeout, CommandType.StoredProcedure);
            return MapRows<TSource, TDest>(results);
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
        public static async Task<List<TDest>> ExecuteAndMapAsync<TSource, TDest>(
            this IDbConnection connection,
            string procedureName,
            object? param = null,
            IDbTransaction? transaction = null,
            int? commandTimeout = null)
        {
            var results = await connection.QueryAsync<TSource>(
                procedureName, param, transaction, commandTimeout, CommandType.StoredProcedure);
            return MapRows<TSource, TDest>(results);
        }

        #endregion

        #region Row Mapping

        // CreateMapper returns a mapper with its own empty plan cache, so calling it per query
        // rebuilt every plan on every call. The configuration version still invalidates a plan held
        // by the shared mapper, so a change made after the first query is honoured.
        private static readonly ConditionalWeakTable<MapperConfiguration, IMapper> _mappers = new();

        private static IMapper MapperFor(MapperConfiguration config) =>
            _mappers.GetValue(config, c => c.CreateMapper());

        // Buffered Dapper results are a List, walked by index into a list sized up front. A LINQ
        // Select and ToList cost an iterator and a delegate per call, plus a closure when the
        // mapper was captured.
        private static List<TDest> MapRows<TSource, TDest>(IEnumerable<TSource> rows)
        {
            if (rows is List<TSource> list)
            {
                var target = new List<TDest>(list.Count);
                for (var i = 0; i < list.Count; i++) target.Add(MapRow<TDest>(list[i]));
                return target;
            }

            var result = rows is ICollection<TSource> collection ? new List<TDest>(collection.Count) : new List<TDest>();
            foreach (var row in rows) result.Add(MapRow<TDest>(row));
            return result;
        }

        private static List<TDest> MapRows<TSource, TDest>(IEnumerable<TSource> rows, IMapper mapper)
        {
            if (rows is List<TSource> list)
            {
                var target = new List<TDest>(list.Count);
                for (var i = 0; i < list.Count; i++) target.Add(mapper.Map<TSource, TDest>(list[i])!);
                return target;
            }

            var result = rows is ICollection<TSource> collection ? new List<TDest>(collection.Count) : new List<TDest>();
            foreach (var row in rows) result.Add(mapper.Map<TSource, TDest>(row)!);
            return result;
        }

        // An untyped Dapper row is a DapperRow, whose columns live behind IDictionary<string, object>
        // and not in properties. The object path found no properties on it, so every column was
        // dropped and the DTO came back empty. Rows go through the dictionary overload instead.
        private static TDest MapRow<TDest>(object? item)
        {
            if (item is IDictionary<string, object?> row && RowMapper<TDest>.Map is { } map)
            {
                return map(row)!;
            }
            return item.MapTo<TDest>()!;
        }

        private static class RowMapper<TDest>
        {
            internal static readonly Func<IDictionary<string, object?>, TDest?>? Map = Create();

            private static Func<IDictionary<string, object?>, TDest?>? Create()
            {
                var type = typeof(TDest);
                if (type.IsAbstract || (!type.IsValueType && type.GetConstructor(Type.EmptyTypes) is null))
                {
                    return null;
                }

                var method = typeof(Mapper).GetMethods()
                    .Single(m => m.Name == nameof(Mapper.MapTo)
                        && m.IsGenericMethodDefinition
                        && m.GetParameters()[0].ParameterType == typeof(IDictionary<string, object?>))
                    .MakeGenericMethod(type);
                return (Func<IDictionary<string, object?>, TDest?>)method.CreateDelegate(
                    typeof(Func<IDictionary<string, object?>, TDest?>));
            }
        }

        #endregion
    }
}
