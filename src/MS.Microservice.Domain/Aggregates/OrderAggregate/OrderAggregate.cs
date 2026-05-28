using MS.Microservice.Core.Functional;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using static MS.Microservice.Core.Functional.F;

namespace MS.Microservice.Domain.Aggregates.OrderAggregate
{
    public static class OrderAggregate
    {
        public const string StreamType = "order";

        public static string GetStreamId(Guid orderId) => $"order-{orderId:N}";

        public static OrderState Fold(IEnumerable<OrderEvent> history, OrderState? seed = null)
            => history.Aggregate(seed ?? OrderState.Initial, Evolve);

        public static OrderState Evolve(OrderState state, OrderEvent @event)
        {
            return @event switch
            {
                OrderCreated created => state with
                {
                    Exists = true,
                    CustomerId = created.CustomerId,
                    Currency = created.Currency,
                    IsConfirmed = false,
                    IsCancelled = false,
                    Version = state.Version + 1,
                },
                OrderItemAdded added => ApplyItemAdded(state, added),
                OrderItemRemoved removed => ApplyItemRemoved(state, removed),
                OrderConfirmed => state with
                {
                    IsConfirmed = true,
                    Version = state.Version + 1,
                },
                OrderCancelled => state with
                {
                    IsCancelled = true,
                    Version = state.Version + 1,
                },
                _ => state,
            };
        }

        public static Either<Error, IReadOnlyList<OrderEvent>> Decide(OrderState state, OrderCommand command)
        {
            return command switch
            {
                CreateOrder create => DecideCreate(state, create),
                AddOrderItem addItem => DecideAddItem(state, addItem),
                RemoveOrderItem removeItem => DecideRemoveItem(state, removeItem),
                ConfirmOrder confirm => DecideConfirm(state, confirm),
                CancelOrder cancel => DecideCancel(state, cancel),
                _ => Left(Error.Unexpected($"未知订单命令类型：{command.GetType().Name}")),
            };
        }

        private static Either<Error, IReadOnlyList<OrderEvent>> DecideCreate(OrderState state, CreateOrder command)
        {
            if (state.Exists)
            {
                return Left(Error.Conflict("订单已存在。"));
            }

            var details = new List<string>();
            if (command.OrderId == Guid.Empty)
            {
                details.Add("订单标识不能为空。");
            }

            if (string.IsNullOrWhiteSpace(command.CustomerId))
            {
                details.Add("客户标识不能为空。");
            }

            if (string.IsNullOrWhiteSpace(command.Currency))
            {
                details.Add("币种不能为空。");
            }

            return details.Count > 0
                ? Left(Error.Validation("创建订单命令不合法。", details))
                : Right<IReadOnlyList<OrderEvent>>([new OrderCreated(command.OrderId, command.CustomerId, command.Currency)]);
        }

        private static Either<Error, IReadOnlyList<OrderEvent>> DecideAddItem(OrderState state, AddOrderItem command)
        {
            var guard = GuardMutable(state);
            if (guard is not null)
            {
                return Left(guard);
            }

            var details = new List<string>();
            if (string.IsNullOrWhiteSpace(command.ProductId))
            {
                details.Add("商品标识不能为空。");
            }

            if (command.UnitPrice <= 0)
            {
                details.Add("商品单价必须大于 0。");
            }

            if (command.Quantity <= 0)
            {
                details.Add("商品数量必须大于 0。");
            }

            return details.Count > 0
                ? Left(Error.Validation("添加商品命令不合法。", details))
                : Right<IReadOnlyList<OrderEvent>>([new OrderItemAdded(command.OrderId, command.ProductId, command.UnitPrice, command.Quantity)]);
        }

        private static Either<Error, IReadOnlyList<OrderEvent>> DecideRemoveItem(OrderState state, RemoveOrderItem command)
        {
            var guard = GuardMutable(state);
            if (guard is not null)
            {
                return Left(guard);
            }

            if (command.Quantity <= 0)
            {
                return Left(Error.Validation("移除商品数量必须大于 0。"));
            }

            if (!state.Lines.TryGetValue(command.ProductId, out var line))
            {
                return Left(Error.Validation("待移除的商品不存在于订单中。"));
            }

            if (command.Quantity > line.Quantity)
            {
                return Left(Error.Validation("移除商品数量不能超过当前订单中的数量。"));
            }

            return Right<IReadOnlyList<OrderEvent>>([new OrderItemRemoved(command.OrderId, command.ProductId, line.UnitPrice, command.Quantity)]);
        }

        private static Either<Error, IReadOnlyList<OrderEvent>> DecideConfirm(OrderState state, ConfirmOrder command)
        {
            var guard = GuardMutable(state);
            if (guard is not null)
            {
                return Left(guard);
            }

            if (state.Lines.Count == 0)
            {
                return Left(Error.Validation("订单至少包含一条商品后才能确认。"));
            }

            return Right<IReadOnlyList<OrderEvent>>([new OrderConfirmed(command.OrderId)]);
        }

        private static Either<Error, IReadOnlyList<OrderEvent>> DecideCancel(OrderState state, CancelOrder command)
        {
            var guard = GuardMutable(state);
            if (guard is not null)
            {
                return Left(guard);
            }

            return string.IsNullOrWhiteSpace(command.Reason)
                ? Left(Error.Validation("取消订单时必须提供原因。"))
                : Right<IReadOnlyList<OrderEvent>>([new OrderCancelled(command.OrderId, command.Reason)]);
        }

        private static Error? GuardMutable(OrderState state)
        {
            if (!state.Exists)
            {
                return Error.Validation("订单不存在。", ["请先创建订单再执行后续命令。"]);
            }

            if (state.IsConfirmed)
            {
                return Error.Conflict("订单已确认，不能再修改。");
            }

            if (state.IsCancelled)
            {
                return Error.Conflict("订单已取消，不能再修改。");
            }

            return null;
        }

        private static OrderState ApplyItemAdded(OrderState state, OrderItemAdded added)
        {
            var nextLine = state.Lines.TryGetValue(added.ProductId, out var currentLine)
                ? currentLine with
                {
                    UnitPrice = added.UnitPrice,
                    Quantity = currentLine.Quantity + added.Quantity,
                }
                : new OrderLineState(added.ProductId, added.UnitPrice, added.Quantity);

            var lines = state.Lines.SetItem(added.ProductId, nextLine);
            return state with
            {
                Lines = lines,
                TotalAmount = CalculateTotal(lines),
                Version = state.Version + 1,
            };
        }

        private static OrderState ApplyItemRemoved(OrderState state, OrderItemRemoved removed)
        {
            if (!state.Lines.TryGetValue(removed.ProductId, out var currentLine))
            {
                return state with { Version = state.Version + 1 };
            }

            var remainingQuantity = currentLine.Quantity - removed.Quantity;
            var lines = remainingQuantity > 0
                ? state.Lines.SetItem(removed.ProductId, currentLine with { Quantity = remainingQuantity })
                : state.Lines.Remove(removed.ProductId);

            return state with
            {
                Lines = lines,
                TotalAmount = CalculateTotal(lines),
                Version = state.Version + 1,
            };
        }

        private static decimal CalculateTotal(ImmutableDictionary<string, OrderLineState> lines)
            => lines.Values.Sum(line => line.Amount);
    }
}
