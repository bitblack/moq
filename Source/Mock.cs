﻿//Copyright (c) 2007, Moq Team 
//http://code.google.com/p/moq/
//All rights reserved.

//Redistribution and use in source and binary forms, 
//with or without modification, are permitted provided 
//that the following conditions are met:

//    * Redistributions of source code must retain the 
//    above copyright notice, this list of conditions and 
//    the following disclaimer.

//    * Redistributions in binary form must reproduce 
//    the above copyright notice, this list of conditions 
//    and the following disclaimer in the documentation 
//    and/or other materials provided with the distribution.

//    * Neither the name of the Moq Team nor the 
//    names of its contributors may be used to endorse 
//    or promote products derived from this software 
//    without specific prior written permission.

//THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND 
//CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, 
//INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF 
//MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE 
//DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR 
//CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, 
//SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, 
//BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR 
//SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS 
//INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, 
//WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING 
//NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE 
//OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF 
//SUCH DAMAGE.

//[This is the BSD license, see
// http://www.opensource.org/licenses/bsd-license.php]

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Linq.Expressions;
using Moq.Language.Flow;
using Moq.Language;

namespace Moq
{
	/// <summary>
	/// Base class for mocks and static helper class with methods that 
	/// apply to mocked objects, such as <see cref="Get"/> to 
	/// retrieve a <see cref="Mock{T}"/> from an object instance.
	/// </summary>
	public abstract class Mock : IMock, IHideObjectMembers
	{
		/// <summary>
		/// Retrieves the mock object for the given object instance.
		/// </summary>
		/// <typeparam name="T">Type of the mock to retrieve. Can be omitted as it's inferred 
		/// from the object instance passed in as the <paramref name="mocked"/> instance.</typeparam>
		/// <param name="mocked">The instance of the mocked object.</param>
		/// <returns>The mock associated with the mocked object.</returns>
		/// <exception cref="ArgumentException">The received <paramref name="mocked"/> instance 
		/// was not created by Moq.</exception>
		/// <example group="advanced">
		/// The following example shows how to add a new expectation to an object 
		/// instance which is not the original <see cref="Mock{T}"/> but rather 
		/// the object associated with it:
		/// <code>
		/// // Typed instance, not the mock, is retrieved from some test API.
		/// HttpContextBase context = GetMockContext();
		/// 
		/// // context.Request is the typed object from the "real" API
		/// // so in order to add an expectation to it, we need to get 
		/// // the mock that "owns" it
		/// Mock&lt;HttpRequestBase&gt; request = Mock.Get(context.Request);
		/// mock.Expect(req => req.AppRelativeCurrentExecutionFilePath)
		///     .Returns(tempUrl);
		/// </code>
		/// </example>
		public static IMock<T> Get<T>(T mocked)
			where T : class
		{
			if (mocked is IMocked<T>)
			{
				// This would be the fastest check.
				return (mocked as IMocked<T>).Mock;
			}
			else if (mocked is IMocked)
			{
				// We may have received a T of an implemented 
				// interface in the mock.
				var mock = ((IMocked)mocked).Mock;
				var imockedType = mocked.GetType().GetInterface("IMocked`1");
				var mockedType = imockedType.GetGenericArguments()[0];

				if (mock.ImplementedInterfaces.Contains(typeof(T)))
				{
					var asMethod = mock.GetType().GetMethod("As");
					var asInterface = asMethod.MakeGenericMethod(typeof(T));
					var asMock = asInterface.Invoke(mock, null);

					return (IMock<T>)asMock;
				}
				else
				{
					// Alternatively, we may have been asked 
					// for a type that is assignable to the 
					// one for the mock.
					// This is not valid as generic types 
					// do not support covariance on 
					// the generic parameters.
					var types = String.Join(", ",
							new[] { mockedType }
						// Skip first interface which is always our internal IMocked<T>
							.Concat(mock.ImplementedInterfaces.Skip(1))
							.Select(t => t.Name)
							.ToArray());

					throw new ArgumentException(String.Format(Properties.Resources.InvalidMockGetType,
						typeof(T).Name, types));
				}
			}
			else
			{
				throw new ArgumentException(Properties.Resources.ObjectInstanceNotMock, "mocked");
			}
		}

		Dictionary<EventInfo, List<Delegate>> invocationLists = new Dictionary<EventInfo, List<Delegate>>();
		Dictionary<PropertyInfo, Mock> innerMocks = new Dictionary<PropertyInfo, Mock>();

		//static MethodInfo genericSetupExpectVoid;
		//static MethodInfo genericSetupExpectReturn;
		//static MethodInfo genericSetupExpectGet;
		//static MethodInfo genericSetupExpectSet;

		//static Mock()
		//{
		//    genericSetupExpectVoid = typeof(Mock).GetMethod("SetupExpect
		//}

		/// <summary>
		/// Initializes the mock
		/// </summary>
		protected Mock()
		{
			this.CallBase = false;
			ImplementedInterfaces = new List<Type>();
		}

		internal Interceptor Interceptor { get; set; }

		/// <summary>
		/// Exposes the list of extra interfaces implemented by the mock.
		/// </summary>
		protected internal List<Type> ImplementedInterfaces { get; private set; }

		/// <summary>
		/// Implements <see cref="IMock.CallBase"/>.
		/// </summary>
		public bool CallBase { get; set; }

		/// <summary>
		/// The mocked object instance. Implements <see cref="IMock.Object"/>.
		/// </summary>
		public object Object { get { return GetObject(); } }

		/// <summary>
		/// Returns the mocked object value.
		/// </summary>
		protected abstract object GetObject();

		/// <summary>
		/// Retrieves the type of the mocked object, its generic type argument.
		/// This is used in the auto-mocking of hierarchy access.
		/// </summary>
		internal abstract Type MockedType { get; }

		/// <summary>
		/// Implements <see cref="IMock.Verify"/>.
		/// </summary>
		public abstract void Verify();

		/// <summary>
		/// Implements <see cref="IMock.VerifyAll"/>.
		/// </summary>
		public abstract void VerifyAll();

		internal static void Verify(Interceptor interceptor)
		{
			// Made static so it can be called from As<TInterface>
			try
			{
				interceptor.Verify();
				foreach (var mock in interceptor.Mock.innerMocks.Values)
				{
					mock.Verify();
				}
			}
			catch (Exception ex)
			{
				// Rethrow resetting the call-stack so that 
				// callers see the exception as happening at 
				// this call site.
				// TODO: see how to mangle the stacktrace so 
				// that the mock doesn't even show up there.
				throw ex;
			}
		}

		internal static void VerifyAll(Interceptor interceptor)
		{
			// Made static so it can be called from As<TInterface>
			try
			{
				interceptor.VerifyAll();
				foreach (var mock in interceptor.Mock.innerMocks.Values)
				{
					mock.VerifyAll();
				}
			}
			catch (Exception ex)
			{
				// Rethrow resetting the call-stack so that 
				// callers see the exception as happening at 
				// this call site.
				throw ex;
			}
		}

		#region Expect

		internal static MethodCall SetUpExpect<T1>(Expression<Action<T1>> expression, Interceptor interceptor)
		{
			Guard.ArgumentNotNull(interceptor, "interceptor");

			// Made static so that it can be called from the AsInterface private 
			// class when adding interfaces via As<TInterface>

			var methodCall = expression.ToLambda().ToMethodCall();
			MethodInfo method = methodCall.Method;
			Expression[] args = methodCall.Arguments.ToArray();

			ThrowIfCantOverride(expression, method);
			var call = new MethodCall(expression, method, args);
			interceptor.AddCall(call, ExpectKind.Other);

			return call;
		}

		internal static MethodCallReturn<TResult> SetUpExpect<T1, TResult>(Expression<Func<T1, TResult>> expression, Interceptor interceptor)
		{
			Guard.ArgumentNotNull(interceptor, "interceptor");

			// Made static so that it can be called from the AsInterface private 
			// class when adding interfaces via As<TInterface>

			var lambda = expression.ToLambda();

			if (lambda.IsProperty())
				return SetUpExpectGet(expression, interceptor);

			var methodCall = lambda.ToMethodCall();
			MethodInfo method = methodCall.Method;
			Expression[] args = methodCall.Arguments.ToArray();

			ThrowIfCantOverride(expression, method);
			var call = new MethodCallReturn<TResult>(expression, method, args);

			// Build intermediate hierarchy if necessary
			var visitor = new AutoMockPropertiesVisitor(interceptor.Mock);
			var target = visitor.SetupMocks(lambda.Body);
			interceptor = target.Interceptor;

			interceptor.AddCall(call, ExpectKind.Other);

			return call;
		}

		internal static MethodCallReturn<TProperty> SetUpExpectGet<T1, TProperty>(Expression<Func<T1, TProperty>> expression, Interceptor interceptor)
		{
			// Made static so that it can be called from the AsInterface private 
			// class when adding interfaces via As<TInterface>

			Guard.ArgumentNotNull(interceptor, "interceptor");
			LambdaExpression lambda = expression.ToLambda();

			if (lambda.IsPropertyIndexer())
			{
				// Treat indexers as regular method invocations.
				return SetUpExpect<T1, TProperty>(expression, interceptor);
			}
			else
			{
				var prop = lambda.ToPropertyInfo();
				ThrowIfPropertyNotReadable(prop);

				var propGet = prop.GetGetMethod(true);
				ThrowIfCantOverride(expression, propGet);

				var call = new MethodCallReturn<TProperty>(expression, propGet, new Expression[0]);

				// Build intermediate hierarchy if necessary
				var visitor = new AutoMockPropertiesVisitor(interceptor.Mock);
				var target = visitor.SetupMocks(lambda.Body);
				interceptor = target.Interceptor;

				interceptor.AddCall(call, ExpectKind.Other);

				return call;
			}
		}

		internal static SetterMethodCall<TProperty> SetUpExpectSet<T1, TProperty>(Expression<Func<T1, TProperty>> expression, Interceptor interceptor)
		{
			Guard.ArgumentNotNull(interceptor, "interceptor");

			// Made static so that it can be called from the AsInterface private 
			// class when adding interfaces via As<TInterface>

			var prop = expression.ToLambda().ToPropertyInfo();
			ThrowIfPropertyNotWritable(prop);

			var propSet = prop.GetSetMethod(true);
			ThrowIfCantOverride(expression, propSet);

			var call = new SetterMethodCall<TProperty>(expression, propSet);
			interceptor.AddCall(call, ExpectKind.PropertySet);

			return call;
		}

		internal static SetterMethodCall<TProperty> SetUpExpectSet<T1, TProperty>(Expression<Func<T1, TProperty>> expression, TProperty value, Interceptor interceptor)
		{
			// Thanks to the ASP.NET MVC team for this "suggestion"!
			var lambda = expression.ToLambda();
			var prop = lambda.ToPropertyInfo();
			ThrowIfPropertyNotWritable(prop);

			// We generate an invocation to the setter method with the given value
			var setter = prop.GetSetMethod();
			ThrowIfCantOverride(expression, setter);

			var call = new SetterMethodCall<TProperty>(expression, setter, value);
			interceptor.AddCall(call, ExpectKind.Other);

			return call;
		}

		private static void ThrowIfPropertyNotWritable(PropertyInfo prop)
		{
			if (!prop.CanWrite)
			{
				throw new ArgumentException(String.Format(
					Properties.Resources.PropertyNotWritable,
					prop.DeclaringType.Name,
					prop.Name), "expression");
			}
		}

		private static void ThrowIfPropertyNotReadable(PropertyInfo prop)
		{
			// If property is not readable, the compiler won't let 
			// the user to specify it in the lambda :)
			// This is just reassuring that in case they build the 
			// expression tree manually?
			if (!prop.CanRead)
			{
				throw new ArgumentException(String.Format(
					Properties.Resources.PropertyNotReadable,
					prop.DeclaringType.Name,
					prop.Name));
			}
		}

		private static void ThrowIfCantOverride(Expression expectation, MethodInfo methodInfo)
		{
			if (!methodInfo.IsVirtual || methodInfo.IsFinal || methodInfo.IsPrivate)
				throw new ArgumentException(
					String.Format(Properties.Resources.ExpectationOnNonOverridableMember,
					expectation.ToString()));
		}

		class AutoMockPropertiesVisitor : ExpressionVisitor
		{
			Mock ownerMock;
			List<PropertyInfo> properties = new List<PropertyInfo>();
			bool first = true;

			public AutoMockPropertiesVisitor(Mock ownerMock)
			{
				this.ownerMock = ownerMock;
			}

			public Mock SetupMocks(Expression expression)
			{
				var withoutLast = Visit(expression);
				var targetMock = ownerMock;
				var props = properties.AsEnumerable();

				foreach (var prop in props.Reverse())
				{
					Mock mock;
					if (!ownerMock.innerMocks.TryGetValue(prop, out mock))
					{
						// TODO: this may throw TargetInvocationException, 
						// cleanup stacktrace.
						ValidateTypeToMock(prop, expression);

						var mockType = typeof(Mock<>).MakeGenericType(prop.PropertyType);

						mock = (Mock)Activator.CreateInstance(mockType);
						ownerMock.innerMocks.Add(prop, mock);

						var targetType = targetMock.MockedType;

						// TODO: cache method
						var setupGet = typeof(Mock).GetMethod("SetUpExpectGet", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.InvokeMethod);
						setupGet = setupGet.MakeGenericMethod(targetType, prop.PropertyType);
						var param = Expression.Parameter(targetType, "mock");
						var expr = Expression.Lambda(Expression.MakeMemberAccess(param, prop), param);
						var result = setupGet.Invoke(targetMock, new object[] { expr, targetMock.Interceptor });
						var returns = result.GetType().GetMethod("Returns", new[] { prop.PropertyType });
						returns.Invoke(result, new[] { mock.Object });
					}

					targetMock = mock;
				}

				return targetMock;
			}

			private void ValidateTypeToMock(PropertyInfo prop, Expression expr)
			{
				if (prop.PropertyType.IsValueType || prop.PropertyType.IsSealed)
					throw new NotSupportedException(String.Format(
						Properties.Resources.UnsupportedIntermediateType,
						prop.DeclaringType.Name, prop.Name, prop.PropertyType, expr));
			}

			protected override Expression VisitMethodCall(MethodCallExpression m)
			{
				if (first)
				{
					first = false;
					return base.Visit(m.Object);
				}
				else
				{
					throw new NotSupportedException(String.Format(
						Properties.Resources.UnsupportedIntermediateExpression, m));
				}
			}

			protected override Expression VisitMemberAccess(MemberExpression m)
			{
				if (first)
				{
					first = false;
					return base.Visit(m.Expression);
				}
	
				if (m.Member is FieldInfo)
					throw new NotSupportedException(String.Format(
						Properties.Resources.FieldsNotSupported, m));

				if (m.Expression.NodeType != ExpressionType.MemberAccess &&
					m.Expression.NodeType != ExpressionType.Parameter)
					throw new NotSupportedException(String.Format(
						Properties.Resources.UnsupportedIntermediateExpression, m));

				var prop = (PropertyInfo)m.Member;
				//var targetType = ((MemberExpression)m.Expression).Type;

				properties.Add(prop);

				return base.VisitMemberAccess(m);
			}
		}

		#endregion

		#region Events

		internal void AddEventHandler(EventInfo ev, Delegate handler)
		{
			List<Delegate> handlers;
			if (!invocationLists.TryGetValue(ev, out handlers))
			{
				handlers = new List<Delegate>();
				invocationLists.Add(ev, handlers);
			}

			handlers.Add(handler);
		}

		internal void RemoveEventHandler(EventInfo ev, Delegate handler)
		{
			List<Delegate> handlers;
			if (invocationLists.TryGetValue(ev, out handlers))
			{
				handlers.Remove(handler);
			}
		}

		internal IEnumerable<Delegate> GetInvocationList(EventInfo ev)
		{
			List<Delegate> handlers;
			if (!invocationLists.TryGetValue(ev, out handlers))
				return new Delegate[0];
			else
				return handlers;
		}

		/// <summary>
		/// Implements <see cref="IMock.CreateEventHandler{TEventArgs}()"/>.
		/// </summary>
		/// <typeparam name="TEventArgs">Type of event argument class.</typeparam>
		public MockedEvent<TEventArgs> CreateEventHandler<TEventArgs>() where TEventArgs : EventArgs
		{
			return new MockedEvent<TEventArgs>(this);
		}

		/// <summary>
		/// Implements <see cref="IMock.CreateEventHandler()"/>
		/// </summary>
		public MockedEvent<EventArgs> CreateEventHandler()
		{
			return new MockedEvent<EventArgs>(this);
		}

		class NullMockedEvent : MockedEvent
		{
			public NullMockedEvent(Mock mock)
				: base(mock)
			{
			}
		}

		#endregion
	}
}
