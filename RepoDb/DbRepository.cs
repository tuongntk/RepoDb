﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using RepoDb.Extensions;
using RepoDb.Interfaces;
using System.Threading.Tasks;
using System.Linq;
using RepoDb.Exceptions;
using RepoDb.EventArguments;
using System.Text;

namespace RepoDb
{
    public class DbRepository<TDbConnection> : IDbRepository<TDbConnection>
        where TDbConnection : DbConnection
    {
        private readonly string _connectionString;
        private readonly int? _commandTimeout;

        public DbRepository(string connectionString)
            : this(connectionString, null, null)
        {
        }

        public DbRepository(string connectionString, int? commandTimeout)
            : this(connectionString, commandTimeout, null)
        {
        }

        public DbRepository(string connectionString, int? commandTimeout, ICache cache = null)
        {
            // Fields
            _connectionString = connectionString;
            _commandTimeout = commandTimeout;

            // Properties
            Cache = (cache ?? new DbCache());
        }

        // CreateConnection

        public TDbConnection CreateConnection()
        {
            var connection = Activator.CreateInstance<TDbConnection>();
            connection.ConnectionString = _connectionString;
            return connection;
        }

        // DbCache

        public ICache Cache { get; }

        // GuardQueryable

        private void GuardQueryable<TEntity>()
            where TEntity : IDataEntity
        {
            if (!DataEntityExtension.IsQueryable<TEntity>())
            {
                throw new EntityNotQueryableException(DataEntityExtension.GetMappedName<TEntity>());
            }
        }

        // Query

        public IEnumerable<TEntity> Query<TEntity>(IDbTransaction transaction = null, string cacheKey = null)
            where TEntity : DataEntity
        {
            return Query<TEntity>(where: (IQueryGroup)null,
                transaction: transaction,
                cacheKey: cacheKey);
        }

        public IEnumerable<TEntity> Query<TEntity>(object where, IDbTransaction transaction = null, string cacheKey = null)
            where TEntity : DataEntity
        {
            if (where is QueryField)
            {
                return Query<TEntity>(where: where.ToQueryFields(),
                    transaction: transaction);
            }
            else if (where is IQueryGroup)
            {
                return Query<TEntity>(where: (IQueryGroup)where,
                    transaction: transaction);
            }
            else
            {
                return Query<TEntity>(where: where?.ToQueryFields(),
                    transaction: transaction);
            }
        }

        public IEnumerable<TEntity> Query<TEntity>(IEnumerable<IQueryField> where, IDbTransaction transaction = null, string cacheKey = null)
            where TEntity : DataEntity
        {
            return Query<TEntity>(where: where != null ? new QueryGroup(where) : (IQueryGroup)null,
                transaction: transaction);
        }

        public IEnumerable<TEntity> Query<TEntity>(IQueryGroup where, IDbTransaction transaction = null, string cacheKey = null)
            where TEntity : DataEntity
        {
            // Get Cache
            if (cacheKey != null)
            {
                var item = Cache?.Get(cacheKey);
                if (item != null)
                {
                    return (IEnumerable<TEntity>)item;
                }
            }

            // Check
            GuardQueryable<TEntity>();

            // Variables
            var commandText = DataEntityExtension.GetSelectStatement<TEntity>(where);
            var param = where?.AsObject();
            var eventArgs = new CancellableExecutionEventArgs(commandText, param);

            // Before Execution
            EventNotifier.OnBeforeQueryExecution(this, eventArgs);

            // Cancel Execution
            if (eventArgs.IsCancelled)
            {
                EventNotifier.OnCancelledExecution(this, new CancelledExecutionEventArgs(commandText, param));
                return null;
            }

            // Actual Execution
            var result = ExecuteReader<TEntity>(commandText: commandText,
                param: where?.AsObject(),
                commandType: DataEntityExtension.GetCommandType<TEntity>(),
                commandTimeout: _commandTimeout,
                transaction: transaction);

            // Set Cache
            if (cacheKey != null)
            {
                Cache?.Set(cacheKey, result);
            }

            // After Execution
            EventNotifier.OnAfterQueryExecution(this, new ExecutionEventArgs(commandText, result));

            // Result
            return result;
        }
        
        // QueryAsync

        public Task<IEnumerable<TEntity>> QueryAsync<TEntity>(IDbTransaction transaction = null, string cacheKey = null)
            where TEntity : DataEntity
        {
            return Task.Factory.StartNew<IEnumerable<TEntity>>(() =>
                Query<TEntity>(transaction: transaction,
                    cacheKey: cacheKey));
        }

        public Task<IEnumerable<TEntity>> QueryAsync<TEntity>(object where, IDbTransaction transaction = null, string cacheKey = null)
            where TEntity : DataEntity
        {
            return Task.Factory.StartNew<IEnumerable<TEntity>>(() =>
                Query<TEntity>(where: where,
                    transaction: transaction,
                    cacheKey: cacheKey));
        }

        public Task<IEnumerable<TEntity>> QueryAsync<TEntity>(IEnumerable<IQueryField> where, IDbTransaction transaction = null, string cacheKey = null)
            where TEntity : DataEntity
        {
            return Task.Factory.StartNew<IEnumerable<TEntity>>(() =>
                Query<TEntity>(where: where,
                    transaction: transaction,
                    cacheKey: cacheKey));
        }

        public Task<IEnumerable<TEntity>> QueryAsync<TEntity>(IQueryGroup where, IDbTransaction transaction = null, string cacheKey = null)
            where TEntity : DataEntity
        {
            return Task.Factory.StartNew<IEnumerable<TEntity>>(() =>
                Query<TEntity>(where: where,
                    transaction: transaction,
                    cacheKey: cacheKey));
        }

        // GuardInsertable

        private void GuardInsertable<TEntity>()
            where TEntity : IDataEntity
        {
            if (!DataEntityExtension.IsInsertable<TEntity>())
            {
                throw new EntityNotInsertableException(DataEntityExtension.GetMappedName<TEntity>());
            }
        }

        // GuardBulkInsert

        private void GuardBulkInsert<TEntity>()
            where TEntity : IDataEntity
        {
            if (typeof(TDbConnection) != typeof(System.Data.SqlClient.SqlConnection))
            {
                throw new EntityNotBulkInsertableException(DataEntityExtension.GetMappedName<TEntity>());
            }
        }

        // Insert

        public object Insert<TEntity>(TEntity entity, IDbTransaction transaction = null)
            where TEntity : DataEntity
        {
            // Check
            GuardInsertable<TEntity>();

            // Variables
            var commandText = DataEntityExtension.GetInsertStatement<TEntity>();
            var eventArgs = new CancellableExecutionEventArgs(commandText, entity);

            // Before Execution
            EventNotifier.OnBeforeInsertExecution(this, eventArgs);

            // Cancel Execution
            if (eventArgs.IsCancelled)
            {
                EventNotifier.OnCancelledExecution(this, new CancelledExecutionEventArgs(commandText, entity));
                return null;
            }

            // Actual Execution
            var result = ExecuteScalar(commandText: commandText,
                param: entity,
                commandType: DataEntityExtension.GetCommandType<TEntity>(),
                commandTimeout: _commandTimeout,
                transaction: transaction);

            // After Execution
            EventNotifier.OnAfterInsertExecution(this, new ExecutionEventArgs(commandText, result));

            // Result
            return result;
        }

        // InsertAsync
        public Task<object> InsertAsync<TEntity>(TEntity entity, IDbTransaction transaction = null)
            where TEntity : DataEntity
        {
            return Task.Factory.StartNew<object>(() =>
                Insert<TEntity>(entity: entity,
                    transaction: transaction));
        }

        // GuardUpdateable

        private void GuardUpdateable<TEntity>()
            where TEntity : IDataEntity
        {
            if (!DataEntityExtension.IsUpdateable<TEntity>())
            {
                throw new EntityNotUpdateableException(DataEntityExtension.GetMappedName<TEntity>());
            }
        }

        // Update

        public int Update<TEntity>(TEntity entity, IDbTransaction transaction = null)
            where TEntity : DataEntity
        {
            var primary = DataEntityExtension.GetPrimaryProperty<TEntity>();
            return Update<TEntity>(entity: entity,
                where: primary?.AsQueryField(entity).AsEnumerable(),
                transaction: transaction);
        }

        public int Update<TEntity>(TEntity entity, object where, IDbTransaction transaction = null)
            where TEntity : DataEntity
        {
            var primary = DataEntityExtension.GetPrimaryProperty<TEntity>();
            return Update<TEntity>(entity: entity,
                where: where is QueryField ? ((QueryField)where).AsEnumerable() : where?.ToQueryFields(),
                transaction: transaction);
        }

        public int Update<TEntity>(TEntity entity, IEnumerable<IQueryField> where, IDbTransaction transaction = null)
            where TEntity : DataEntity
        {
            return Update(entity: entity,
                where: where != null ? new QueryGroup(where) : (IQueryGroup)null,
                transaction: transaction);
        }

        public int Update<TEntity>(TEntity entity, IQueryGroup where, IDbTransaction transaction = null)
            where TEntity : DataEntity
        {
            // Check
            GuardUpdateable<TEntity>();

            // Variables
            var commandText = DataEntityExtension.GetUpdateStatement<TEntity>(where);
            var param = entity?.AsObject(where);
            var eventArgs = new CancellableExecutionEventArgs(commandText, param);

            // Before Execution
            EventNotifier.OnBeforeUpdateExecution(this, eventArgs);

            // Cancel Execution
            if (eventArgs.IsCancelled)
            {
                EventNotifier.OnCancelledExecution(this, new CancelledExecutionEventArgs(commandText, param));
                return 0;
            }

            // Actual Execution
            var result = ExecuteNonQuery(commandText: commandText,
                param: param,
                commandType: DataEntityExtension.GetCommandType<TEntity>(),
                commandTimeout: _commandTimeout,
                transaction: transaction);

            // After Execution
            EventNotifier.OnAfterUpdateExecution(this, new ExecutionEventArgs(commandText, result));

            // Result
            return result;
        }

        // UpdateAsync
        public Task<int> UpdateAsync<TEntity>(TEntity entity, IDbTransaction transaction = null)
            where TEntity : DataEntity
        {
            return Task.Factory.StartNew<int>(() =>
                Update(entity: entity,
                    transaction: transaction));
        }

        public Task<int> UpdateAsync<TEntity>(TEntity entity, object where, IDbTransaction transaction = null)
            where TEntity : DataEntity
        {
            return Task.Factory.StartNew<int>(() =>
                Update(entity: entity,
                    where: where,
                    transaction: transaction));
        }

        public Task<int> UpdateAsync<TEntity>(TEntity entity, IEnumerable<IQueryField> where, IDbTransaction transaction = null)
            where TEntity : DataEntity
        {
            return Task.Factory.StartNew<int>(() =>
                Update(entity: entity,
                    where: where,
                    transaction: transaction));
        }

        public Task<int> UpdateAsync<TEntity>(TEntity entity, IQueryGroup where, IDbTransaction transaction = null)
            where TEntity : DataEntity
        {
            return Task.Factory.StartNew<int>(() =>
                Update(entity: entity,
                    where: where,
                    transaction: transaction));
        }

        // GuardDeletable

        private void GuardDeletable<TEntity>()
            where TEntity : IDataEntity
        {
            if (!DataEntityExtension.IsDeletable<TEntity>())
            {
                throw new EntityNotDeletableException(DataEntityExtension.GetMappedName<TEntity>());
            }
        }

        // Delete

        public int Delete<TEntity>(object where, IDbTransaction transaction = null)
            where TEntity : DataEntity
        {
            return Delete<TEntity>(where: where is QueryField ? ((QueryField)where).AsEnumerable() : where?.ToQueryFields(),
                transaction: transaction);
        }

        public int Delete<TEntity>(IEnumerable<IQueryField> where, IDbTransaction transaction = null)
            where TEntity : DataEntity
        {
            return Delete<TEntity>(where: where != null ? new QueryGroup(where) : (IQueryGroup)null,
                transaction: transaction);
        }

        public int Delete<TEntity>(IQueryGroup where, IDbTransaction transaction = null)
            where TEntity : DataEntity
        {
            // Check
            GuardDeletable<TEntity>();

            // Variables
            var commandText = DataEntityExtension.GetDeleteStatement<TEntity>(where);
            var param = where?.AsObject();
            var eventArgs = new CancellableExecutionEventArgs(commandText, param);

            // Before Execution
            EventNotifier.OnBeforeDeleteExecution(this, eventArgs);

            // Cancel Execution
            if (eventArgs.IsCancelled)
            {
                EventNotifier.OnCancelledExecution(this, new CancelledExecutionEventArgs(commandText, param));
                return 0;
            }

            // Actual Execution
            var result = ExecuteNonQuery(commandText: commandText,
                param: param,
                commandType: DataEntityExtension.GetCommandType<TEntity>(),
                commandTimeout: _commandTimeout);

            // After Execution
            EventNotifier.OnAfterDeleteExecution(this, new ExecutionEventArgs(commandText, result));

            // Result
            return result;
        }

        // DeleteAsync
        public Task<int> DeleteAsync<TEntity>(object where, IDbTransaction transaction = null)
            where TEntity : DataEntity
        {
            return Task.Factory.StartNew<int>(() =>
                Delete<TEntity>(where: where,
                    transaction: transaction));
        }

        public Task<int> DeleteAsync<TEntity>(IEnumerable<IQueryField> where, IDbTransaction transaction = null)
            where TEntity : DataEntity
        {
            return Task.Factory.StartNew<int>(() =>
                Delete<TEntity>(where: where,
                    transaction: transaction));
        }

        public Task<int> DeleteAsync<TEntity>(IQueryGroup where, IDbTransaction transaction = null)
            where TEntity : DataEntity
        {
            return Task.Factory.StartNew<int>(() =>
                Delete<TEntity>(where: where,
                    transaction: transaction));
        }

        // GuardMergeable

        private void GuardMergeable<TEntity>()
            where TEntity : IDataEntity
        {
            if (!DataEntityExtension.IsMergeable<TEntity>())
            {
                throw new EntityNotMergeableException(DataEntityExtension.GetMappedName<TEntity>());
            }
        }

        // Merge

        public int Merge<TEntity>(TEntity entity, IDbTransaction transaction = null)
            where TEntity : DataEntity
        {
            return Merge<TEntity>(entity: entity,
                qualifiers: null,
                    transaction: transaction);
        }

        public int Merge<TEntity>(TEntity entity, IEnumerable<IField> qualifiers, IDbTransaction transaction = null)
            where TEntity : DataEntity
        {
            // Check
            GuardMergeable<TEntity>();

            // Variables
            var commandText = DataEntityExtension.GetMergeStatement<TEntity>(qualifiers);
            var eventArgs = new CancellableExecutionEventArgs(commandText, entity);

            // Before Execution
            EventNotifier.OnBeforeMergeExecution(this, eventArgs);

            // Cancel Execution
            if (eventArgs.IsCancelled)
            {
                EventNotifier.OnCancelledExecution(this, new CancelledExecutionEventArgs(commandText, entity));
                return 0;
            }

            // Actual Execution
            var result = ExecuteNonQuery(commandText: commandText,
                param: entity,
                commandType: DataEntityExtension.GetCommandType<TEntity>(),
                commandTimeout: _commandTimeout,
                transaction: transaction);

            // After Execution
            EventNotifier.OnAfterMergeExecution(this, new ExecutionEventArgs(commandText, result));

            // Result
            return result;
        }

        // MergeAsync
        public Task<int> MergeAsync<TEntity>(TEntity entity, IDbTransaction transaction = null)
            where TEntity : DataEntity
        {
            return Task.Factory.StartNew<int>(() =>
                Merge<TEntity>(entity: entity,
                    transaction: transaction));
        }

        public Task<int> MergeAsync<TEntity>(TEntity entity, IEnumerable<IField> qualifiers, IDbTransaction transaction = null)
            where TEntity : DataEntity
        {
            return Task.Factory.StartNew<int>(() =>
                Merge<TEntity>(entity: entity,
                    qualifiers: qualifiers,
                    transaction: transaction));
        }

        // BulkInsert

        public int BulkInsert<TEntity>(IEnumerable<TEntity> entities, IDbTransaction transaction = null)
            where TEntity : DataEntity
        {
            // Check
            GuardInsertable<TEntity>();
            GuardBulkInsert<TEntity>();

            // Variables
            using (var connection = (transaction?.Connection ?? CreateConnection()).EnsureOpen())
            {
                var table = entities.AsDataTable<TEntity>(connection);
                var eventArgs = new CancellableExecutionEventArgs(null, table);

                // Before Execution
                EventNotifier.OnBeforeQueryExecution(this, eventArgs);

                // Cancel Execution
                if (eventArgs.IsCancelled)
                {
                    EventNotifier.OnCancelledExecution(this, new CancelledExecutionEventArgs("BulkInsert", entities));
                    return 0;
                }

                // Actual Execution
                var sqlBulkCopy = new System.Data.SqlClient.SqlBulkCopy((System.Data.SqlClient.SqlConnection)connection);
                var result = entities.Count();
                sqlBulkCopy.DestinationTableName = table.TableName;
                sqlBulkCopy.WriteToServer(table);

                // After Execution
                EventNotifier.OnAfterBulkInsertExecution(this, new ExecutionEventArgs(null, result));

                // Result
                return result;
            }
        }

        // BulkInsertAsync
        public Task<int> BulkInsertAsync<TEntity>(IEnumerable<TEntity> entities, IDbTransaction transaction = null)
            where TEntity : DataEntity
        {
            return Task.Factory.StartNew<int>(() =>
                BulkInsert<TEntity>(entities: entities,
                    transaction: transaction));
        }

        // ExecuteReader

        public IEnumerable<TEntity> ExecuteReader<TEntity>(string commandText, object param = null, CommandType? commandType = null,
            int? commandTimeout = null, IDbTransaction transaction = null)
            where TEntity : DataEntity
        {
            var connection = (transaction?.Connection ?? CreateConnection());
            var result = connection.ExecuteReader<TEntity>(commandText: commandText,
                param: param,
                commandType: commandType,
                commandTimeout: _commandTimeout,
                transaction: transaction);
            if (transaction == null)
            {
                connection.Dispose();
            }
            return result;
        }

        // ExecuteReaderAsync

        public Task<IEnumerable<TEntity>> ExecuteReaderAsync<TEntity>(string commandText, object param = null, CommandType? commandType = null,
            int? commandTimeout = null, IDbTransaction transaction = null)
            where TEntity : DataEntity
        {
            return Task.Factory.StartNew<IEnumerable<TEntity>>(() =>
                ExecuteReader<TEntity>(commandText: commandText,
                    param: param,
                    commandType: commandType,
                    commandTimeout: commandTimeout,
                    transaction: transaction));
        }

        // ExecuteNonQuery

        public int ExecuteNonQuery(string commandText, object param = null, CommandType? commandType = null,
            int? commandTimeout = null, IDbTransaction transaction = null)
        {
            var connection = (transaction?.Connection ?? CreateConnection());
            var result = connection.ExecuteNonQuery(commandText: commandText,
                param: param,
                commandType: commandType,
                commandTimeout: _commandTimeout,
                transaction: transaction);
            if (transaction == null)
            {
                connection.Dispose();
            }
            return result;
        }

        // ExecuteNonQueryAsync

        public Task<int> ExecuteNonQueryAsync(string commandText, object param = null, CommandType? commandType = null,
            int? commandTimeout = null, IDbTransaction transaction = null)
        {
            return Task.Factory.StartNew<int>(() =>
                ExecuteNonQuery(commandText: commandText,
                    param: param,
                    commandType: commandType,
                    commandTimeout: commandTimeout,
                    transaction: transaction));
        }

        // ExecuteScalar

        public object ExecuteScalar(string commandText, object param = null, CommandType? commandType = null,
            int? commandTimeout = null, IDbTransaction transaction = null)
        {
            var connection = (transaction?.Connection ?? CreateConnection());
            var result = connection.ExecuteScalar(commandText: commandText,
                param: param,
                commandType: commandType,
                commandTimeout: _commandTimeout,
                transaction: transaction);
            if (transaction == null)
            {
                connection.Dispose();
            }
            return result;
        }

        // ExecuteScalarAsync

        public Task<object> ExecuteScalarAsync(string commandText, object param = null, CommandType? commandType = null,
            int? commandTimeout = null, IDbTransaction transaction = null)
        {
            return Task.Factory.StartNew<object>(() =>
                ExecuteScalar(commandText: commandText,
                    param: param,
                    commandType: commandType,
                    commandTimeout: commandTimeout,
                    transaction: transaction));
        }
    }
}