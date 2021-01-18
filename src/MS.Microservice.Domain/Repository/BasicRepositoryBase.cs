﻿using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MS.Microservice.Domain
{
    public abstract class BasicRepositoryBase<TEntity>
        : IBasicRepository<TEntity>
        where TEntity : class, IEntity<int>
    {
        /// <summary>
        /// 根据表达式删除实体
        /// </summary>
        /// <param name="predicate"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public abstract Task<bool> DeleteAsync([NotNull] Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);
        /// <summary>
        /// 删除指定实体
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public abstract Task<bool> DeleteAsync([NotNull] TEntity entity, CancellationToken cancellationToken = default);
        /// <summary>
        /// 根据表达式查询实体
        /// </summary>
        /// <param name="predicate"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public abstract Task<TEntity> FindAsync([NotNull] Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);
        /// <summary>
        /// 插入实体
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public abstract Task<TEntity> InsertAsync([NotNull] TEntity entity, CancellationToken cancellationToken = default);
        /// <summary>
        /// 更新实体
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public abstract Task<TEntity> UpdateAsync([NotNull] TEntity entity, CancellationToken cancellationToken = default);
    }

    public abstract class BasicRepositoryBase<TEntity, TKey>
        : IBasicRepository<TEntity>, IBasicRepository<TEntity, TKey>
        where TEntity : class, IEntity<TKey>
    {
        public abstract Task<bool> DeleteAsync([NotNull] TKey id, CancellationToken cancellationToken = default);

        public abstract Task<bool> DeleteAsync([NotNull] Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

        public abstract Task<TEntity> FindAsync([NotNull] Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

        public abstract Task<TEntity> GetAsync([NotNull] TKey id, CancellationToken cancellationToken = default);

        public abstract Task<TEntity> InsertAsync([NotNull] TEntity entity, CancellationToken cancellationToken = default);

        public abstract Task<TEntity> UpdateAsync([NotNull] TEntity entity, CancellationToken cancellationToken = default);
    }
}
