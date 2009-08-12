﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel;
using Moq;
using IQToolkit;
using System.Linq.Expressions;
using System.Reflection;
using Moq.Language.Flow;
using Moq.Language;
using System.Collections;

namespace Moq.Linq
{
	public static class Mocks
	{
		public static IQueryable<T> Query<T>()
		{
			return new MockQueryable<T>();
		}

		class MockQueryable<T> : Query<T>
		{
			public MockQueryable()
				: base(new MockQueryProvider())
			{
			}
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		public static IEnumerable<T> CreateReal<T>()
			where T : class
		{
			while (true)
			{
				yield return new Mock<T>().Object;
			}
		}

		public static bool Mock<T>(T source, Action<Mock<T>> setup)
			where T : class
		{
			setup(Moq.Mock.Get(source));
			return true;
		}

		class MockQueryProvider : QueryProvider
		{
			static readonly MethodInfo createMockMethod = typeof(Mocks).GetMethod("Create");
			static readonly MethodInfo createRealMockMethod = typeof(Mocks).GetMethod("CreateReal");

			public override object Execute(Expression expression)
			{
				var collector = new ExpressionCollector();
				var createCalls = collector
					.Collect(expression)
					.OfType<MethodCallExpression>()
					.Where(call => call.Method.IsGenericMethod &&
						call.Method.GetGenericMethodDefinition() == createMockMethod)
					.ToArray();
				var replaceWith = createCalls
					.Select(call => Expression.Call(
						call.Object,
						createRealMockMethod.MakeGenericMethod(
							call.Method.GetGenericArguments()),
						call.Arguments.ToArray()))
					.ToArray();

				var replaced = ExpressionReplacer.ReplaceAll(expression, createCalls, replaceWith);
				replaced = MockSetupsReplacer.Accept(replaced);
				replaced = QueryableToEnumerableReplacer.ReplaceAll(replaced);

				var lambda = Expression.Lambda(typeof(Func<>).MakeGenericType(expression.Type), replaced);
				return lambda.Compile().DynamicInvoke(null);
			}

			public override string GetQueryText(Expression expression)
			{
				throw new NotImplementedException();
			}
		}

		class ExpressionCollector : ExpressionVisitor
		{
			List<Expression> expressions = new List<Expression>();

			public IEnumerable<Expression> Collect(Expression exp)
			{
				Visit(exp);
				return expressions;
			}

			protected override Expression Visit(Expression exp)
			{
				expressions.Add(exp);
				return base.Visit(exp);
			}
		}

		class MockSetupsReplacer : ExpressionVisitor
		{
			Expression expression;
			Stack<MethodCallExpression> whereCalls = new Stack<MethodCallExpression>();

			public MockSetupsReplacer(Expression expression)
			{
				this.expression = expression;
			}

			public static Expression Accept(Expression expression)
			{
				return new MockSetupsReplacer(expression).Accept();
			}

			public Expression Accept()
			{
				return Visit(expression);
			}

			protected override Expression VisitMethodCall(MethodCallExpression m)
			{
				// We only translate Where for now.
				if (m.Method.DeclaringType == typeof(Queryable) && m.Method.Name == "Where")
				{
					whereCalls.Push(m);
					var result = base.VisitMethodCall(m);
					whereCalls.Pop();

					return result;
				}
				else
				{
					return base.VisitMethodCall(m);
				}
			}

			protected override Expression VisitBinary(BinaryExpression b)
			{
				if (whereCalls.Count != 0 && b.NodeType == ExpressionType.Equal &&
					(b.Left.NodeType == ExpressionType.MemberAccess || b.Left.NodeType == ExpressionType.Call))
				{
					var isMember = b.Left.NodeType == ExpressionType.MemberAccess;
					var methodCall = b.Left as MethodCallExpression;
					var memberAccess = b.Left as MemberExpression;

					// TODO: throw if target is a static class?
					var targetObject = isMember ? memberAccess.Expression : methodCall.Object;
					var sourceType = isMember ? memberAccess.Expression.Type : methodCall.Object.Type;
					var returnType = isMember ? memberAccess.Type : methodCall.Method.ReturnType;

					var mockType = typeof(Mock<>).MakeGenericType(sourceType);
					var actionType = typeof(Action<>).MakeGenericType(mockType);
					var funcType = typeof(Func<,>).MakeGenericType(sourceType, returnType);

					// where dte.Solution == solution
					// becomes:	
					// where Mock.Get(dte).Setup(mock => mock.Solution).Returns(solution) != null

					var returnsMethod = typeof(IReturns<,>)
						.MakeGenericType(sourceType, returnType)
						.GetMethod("Returns", new[] { returnType });

					var notNullBinary = Expression.NotEqual(
						Expression.Call(
							FluentMockVisitor.Accept(b.Left),
							// .Returns(solution)
							returnsMethod,
							b.Right
						),
						// != null
						Expression.Constant(null)
					);

					return notNullBinary;
				}

				return base.VisitBinary(b);
			}
		}

		class FluentMockVisitor : ExpressionVisitor
		{
			static readonly MethodInfo FluentMockGenericMethod = ((Func<Mock<string>, Expression<Func<string, string>>, Mock<string>>)
				MockExtensions.FluentMock<string, string>).Method.GetGenericMethodDefinition();
			static readonly MethodInfo MockGetGenericMethod = ((Func<string, Mock<string>>)Moq.Mock.Get<string>)
				.Method.GetGenericMethodDefinition();

			Expression expression;

			/// <summary>
			/// The first method call or member access will be the 
			/// last segment of the expression (depth-first traversal), 
			/// which is the one we have to Setup rather than FluentMock.
			/// And the last one is the one we have to Mock.Get rather 
			/// than FluentMock.
			/// </summary>
			bool isFirst = true;

			public FluentMockVisitor(Expression expression)
			{
				this.expression = expression;
			}

			public static Expression Accept(Expression expression)
			{
				return new FluentMockVisitor(expression).Accept();
			}

			public Expression Accept()
			{
				return Visit(expression);
			}

			protected override Expression VisitParameter(ParameterExpression p)
			{
				// the actual first object being used in a fluent expression.
				return Expression.Call(null,
					MockGetGenericMethod.MakeGenericMethod(p.Type),
					p);
			}

			protected override Expression VisitMethodCall(MethodCallExpression m)
			{
				var lambdaParam = Expression.Parameter(m.Object.Type, "mock");
				Expression lambdaBody = Expression.Call(lambdaParam, m.Method, m.Arguments);
				var targetMethod = GetTargetMethod(m.Object.Type, m.Method.ReturnType);
				if (isFirst) isFirst = false;

				return TranslateFluent(m.Object.Type, m.Method.ReturnType, targetMethod, Visit(m.Object), lambdaParam, lambdaBody);
			}

			protected override Expression VisitMemberAccess(MemberExpression m)
			{
				// If member is not mock-able, actually, including being a sealed class, etc.?
				if (m.Member is FieldInfo)
					throw new NotSupportedException();

				var lambdaParam = Expression.Parameter(m.Expression.Type, "mock");
				Expression lambdaBody = Expression.MakeMemberAccess(lambdaParam, m.Member);
				var targetMethod = GetTargetMethod(m.Expression.Type, ((PropertyInfo)m.Member).PropertyType);
				if (isFirst) isFirst = false;

				return TranslateFluent(m.Expression.Type, ((PropertyInfo)m.Member).PropertyType, targetMethod, Visit(m.Expression), lambdaParam, lambdaBody);
			}

			// Args like: string IFoo (mock => mock.Value)
			private Expression TranslateFluent(Type objectType, Type returnType, MethodInfo targetMethod, Expression instance, ParameterExpression lambdaParam, Expression lambdaBody)
			{
				var funcType = typeof(Func<,>).MakeGenericType(objectType, returnType);

				// This is the fluent extension method one, so pass the instance as one more arg.
				if (targetMethod.IsStatic)
					return Expression.Call(
						targetMethod,
						instance,
						Expression.Lambda(
							funcType,
							lambdaBody,
							lambdaParam
						)
					);
				else
					return Expression.Call(
						instance,
						targetMethod,
						Expression.Lambda(
							funcType,
							lambdaBody,
							lambdaParam
						)
					);
			}

			private MethodInfo GetTargetMethod(Type objectType, Type returnType)
			{
				MethodInfo targetMethod;
				// dte.Solution =>
				if (isFirst)
				{
					//.Setup(mock => mock.Solution)
					targetMethod = GetSetupMethod(objectType, returnType);
				}
				else
				{
					//.FluentMock(mock => mock.Solution)
					targetMethod = FluentMockGenericMethod.MakeGenericMethod(objectType, returnType);
				}
				return targetMethod;
			}

			private MethodInfo GetSetupMethod(Type objectType, Type returnType)
			{
				return typeof(Mock<>)
					.MakeGenericType(objectType)
					.GetMethods()
					.First(mi => mi.Name == "Setup" && mi.IsGenericMethod)
					.MakeGenericMethod(returnType);
			}
		}

		class QueryableToEnumerableReplacer : ExpressionVisitor
		{
			Expression expression;

			public QueryableToEnumerableReplacer(Expression expression)
			{
				this.expression = expression;
			}
			public static Expression ReplaceAll(Expression expression)
			{
				return new QueryableToEnumerableReplacer(expression).ReplaceAll();
			}

			public Expression ReplaceAll()
			{
				return this.Visit(expression);
			}

			protected override Expression VisitConstant(ConstantExpression c)
			{
				if (c.Type.IsGenericType &&
					c.Type.GetGenericTypeDefinition() == (typeof(MockQueryable<>)))
				{
					var targetType = c.Type.GetGenericArguments()[0];
					var createRealMethod = typeof(Mocks).GetMethod("CreateReal").MakeGenericMethod(targetType);
					var createRealExpr = Expression.Call(null, createRealMethod);
					var asQueryableMethod = typeof(Queryable).GetMethods()
						.Where(mi => mi.Name == "AsQueryable" && mi.IsGenericMethodDefinition)
						.Single()
						.MakeGenericMethod(targetType);
					var asQueryable = Expression.Call(null, asQueryableMethod, createRealExpr);

					return asQueryable;
				}

				return base.VisitConstant(c);
			}

			protected override Expression VisitMethodCall(MethodCallExpression m)
			{
				//if (m.Method.DeclaringType == typeof(Queryable))
				//{
				//    var queryMethod = m.Method.GetGenericMethodDefinition();
				//    var enumerableMethod = typeof(Enumerable).GetMethods()
				//        .Where(mi => mi.IsGenericMethod == queryMethod.IsGenericMethod &&
				//            mi.Name == queryMethod.Name &&
				//            mi.GetGenericArguments().Length == queryMethod.GetGenericArguments().Length &&
				//            // Yes, this is not precise either :)
				//            mi.GetParameters().Length == queryMethod.GetParameters().Length).First();

				//    enumerableMethod = enumerableMethod.MakeGenericMethod(m.Method.GetGenericArguments());

				//    return Expression.Call(m.Object, enumerableMethod,
				//        m.Arguments.Select(e => this.Visit(e)).ToArray());
				//}

				return base.VisitMethodCall(m);
			}

			private bool AreEqual<T>(IEnumerable<T> first, IEnumerable<T> second)
			{
				return AreEqual(first, second, (obj1, obj2) => Object.Equals(obj1, obj2));
			}

			private bool AreEqual<T>(IEnumerable<T> first, IEnumerable<T> second, Func<T, T, bool> equalityComparer)
			{
				var firstEnum = first.GetEnumerator();
				var secondEnum = second.GetEnumerator();

				while (firstEnum.MoveNext() == secondEnum.MoveNext() == true)
				{
					if (!equalityComparer(firstEnum.Current, secondEnum.Current))
						return false;
				}

				// Yes, this is not 100% precise. That's why it's a POC
				return true;
			}
		}
	}
}
